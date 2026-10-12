using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>通常 translate から利用者所有ケースを取り込み、固定 MSTest を生成します。</summary>
internal static class ReferenceTestGeneration
{
    /// <summary>入力の来歴と既存 API を使う、元 COBOL や IR ファイルに依存しない実行入口を作ります。</summary>
    /// <param name="ir">解析済みプログラムと設定。</param>
    /// <param name="contracts">レビュー済み業務観測境界。</param>
    /// <returns>C# の固定実行入口。</returns>
    internal static string Api(JsonNode ir, JsonNode contracts)
    {
        var code = new StringBuilder("""
            using System.Text.Json.Nodes;
            using CobolCS.Runtime;

            namespace CobolCS.Generated;

            /// <summary>固定された宣言と設定を持つ生成プログラムの観測入口です。</summary>
            public static class GeneratedApi
            {
                /// <summary>固定入力と任意の経路記録を使って生成された命令列を実行します。</summary>
                /// <param name="programId">PROGRAM-ID。</param>
                /// <param name="input">fields で与える初期状態。業務入力 API の代用ではありません。</param>
                /// <param name="trace">条件を再評価しない実行記録の保存先。</param>
                /// <returns>論理値 display、fields、signal の完全観測。</returns>
                public static JsonObject Run(string programId, JsonNode? input = null, JsonArray? trace = null)
                {
                    return programId switch
                    {
            """);
        code.AppendLine();
        var index = 0;
        foreach (var program in ir.A("programs"))
        {
            code.AppendLine($"            {JsonSerializer.Serialize(program.S("id"))} => Program{index:D4}.Run(J.Parse({JsonSerializer.Serialize(program.ToJsonString())}), J.Parse({JsonSerializer.Serialize(ir["config"]!.ToJsonString())}), input, trace),");
            index++;
        }

        code.AppendLine("""
                        _ => throw new ArgumentException("Unknown PROGRAM-ID " + programId),
                    };
                }
            }
            """);
        var close = code.ToString().LastIndexOf('}');
        code.Remove(close, code.Length - close);
        code.AppendLine($$"""

                /// <summary>業務入力と観測を、内部状態スナップショットから分離して実行します。</summary>
                /// <param name="contractId">レビュー済み業務観測境界。</param>
                /// <param name="input">型付き入力。</param>
                /// <param name="trace">任意の計測保存先。</param>
                /// <returns>業務観測と full-state の別々の結果。</returns>
                public static BusinessResult RunBusiness(string contractId, BusinessInput input, JsonArray? trace = null)
                {
                    var contracts = J.Parse({{JsonSerializer.Serialize(contracts.ToJsonString())}});
                    var contract = contracts[contractId] ?? throw new ArgumentException("Unknown business contract " + contractId);
                    BusinessObservation.ValidateContract(contract);
                    var snapshot = Run(contract.S("programId"), BusinessObservation.Input(input.ToJson(), contract), trace);
                    return new BusinessResult(BusinessObservation.Observe(snapshot, contract), snapshot);
                }
            }
            """);
        return code.ToString();
    }

    /// <summary>登録不備もゲートに残し、期待値を変更せずテスト・テンプレート・状態を生成します。</summary>
    /// <param name="ir">ソース、設定とプログラム。</param>
    /// <param name="output">利用者ケースのある出力先。</param>
    /// <param name="files">生成ファイル。</param>
    /// <param name="generated">実行コードがある場合はテストプロジェクトも生成します。</param>
    internal static void Add(JsonNode ir, string output, IDictionary<string, string> files, bool generated)
    {
        var context = new JsonObject { ["externalAdapter"] = ir["config"]!.S("externalAdapter"), ["configHash"] = ir.S("configHash"), ["inputHashes"] = new JsonObject(ir.A("sources").Select(s => KeyValuePair.Create(s.S("path"), (JsonNode?)JsonValue.Create(s.S("sha256"))))), ["programs"] = new JsonArray(ir.A("programs").Select(p => (JsonNode?)JsonValue.Create(p.S("id"))).ToArray()) };
        var catalog = new JsonArray();
        var unmeasurable = new JsonArray();
        foreach (var program in ir.A("programs"))
        {
            try
            {
                var flow = FlowBuilder.Build(program);
                for (var pc = 0; pc < flow.Length; pc++)
                {
                    if (flow[pc].Op is "branch" or "sizeBranch" or "loopCheck" or "externalBranch" or "externalGoto")
                    {
                        catalog.Add(new JsonObject { ["programId"] = program.S("id"), ["statementId"] = flow[pc].StatementId, ["instruction"] = pc, ["op"] = flow[pc].Op });
                    }
                    else if (flow[pc].Op == "gotoDepending")
                    {
                        unmeasurable.Add(new JsonObject { ["programId"] = program.S("id"), ["statementId"] = flow[pc].StatementId, ["instruction"] = pc, ["reason"] = "Multi-target GO TO is outside the boolean branch catalog" });
                    }
                }
            }
            catch (Exception error) when (error is KeyNotFoundException or NotSupportedException)
            {
                unmeasurable.Add(new JsonObject { ["programId"] = program.S("id"), ["reason"] = error.Message });
            }
        }

        context["branchCatalog"] = catalog;
        context["unmeasurableBranches"] = unmeasurable;
        var cases = new JsonArray();
        var errors = new JsonArray();
        var contracts = new JsonObject();
        var contractFiles = new JsonObject();
        var contractsRoot = Path.Combine(output, "Tests/Contracts");
        if (Directory.Exists(contractsRoot))
        {
            foreach (var path in Directory.GetFiles(contractsRoot, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(contractsRoot, path).Replace('\\', '/');
                if (relative == "full-state-v1.json")
                {
                    continue;
                }

                Generator.SafePath(path);
                var bytes = File.ReadAllBytes(path);
                contractFiles[relative] = Generator.Hash(bytes);
                try
                {
                    var contract = ReferenceCase.Parse(Encoding.UTF8.GetString(bytes));
                    BusinessObservation.ValidateContract(contract);
                    if (!context.A("programs").Any(p => p.GetValue<string>() == contract.S("programId")) || contracts.ContainsKey(contract.S("id")))
                    {
                        throw new InvalidDataException("Unknown program or duplicate business contract ID");
                    }

                    var program = ir.A("programs").Single(p => p.S("id") == contract.S("programId"));
                    var storage = new Storage(program);
                    var statements = Validation.Walk(program["paragraphs"]).Where(n => n.S("id").StartsWith('s')).Select(n => n.S("id")).ToHashSet(StringComparer.Ordinal);
                    var inputTargets = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var key in new[] { "inputs", "arguments", "observations" })
                    {
                        foreach (var binding in contract.A(key))
                        {
                            var symbols = program.A("symbols").Where(s => s.S("qualifiedName") == binding.S("field")).ToArray();
                            if (symbols.Length != 1 || symbols[0]["pic"] == null || (binding.S("type").StartsWith("number", StringComparison.Ordinal) != (symbols[0]["pic"]!.S("category") == "numeric")) || (binding.S("type").EndsWith("-array", StringComparison.Ordinal) != (storage.Dimensions(symbols[0]).Length != 0)) || (binding["statementId"] != null && !statements.Contains(binding.S("statementId"))))
                            {
                                throw new InvalidDataException("Unknown/mistyped field or source statement in business contract");
                            }

                            if (key != "observations" && !inputTargets.Add(binding.S("field")))
                            {
                                throw new InvalidDataException("Duplicate business input/argument target");
                            }
                        }
                    }

                    contracts.Add(contract.S("id"), contract);
                }
                catch (Exception error) when (error is InvalidDataException or InvalidOperationException or System.Text.Json.JsonException or FormatException or IOException)
                {
                    errors.Add(relative + ": " + error.Message);
                }
            }
        }

        context["businessContracts"] = contracts;
        context["contractFiles"] = contractFiles;
        var required = new JsonObject();
        var previous = Path.Combine(output, "reports/quality/reference-inventory.json");
        if (File.Exists(previous))
        {
            required = J.Parse(File.ReadAllText(previous))["requiredCases"]?.DeepClone().AsObject() ?? new JsonObject();
        }

        var root = Path.Combine(output, "Tests/ReferenceCases");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (Directory.Exists(root))
        {
            foreach (var file in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var path = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (path.StartsWith("Data/", StringComparison.Ordinal))
                {
                    continue;
                }

                Generator.SafePath(file);
                var bytes = File.ReadAllBytes(file);
                var entry = new JsonObject { ["path"] = path, ["hash"] = Generator.Hash(bytes), ["testMethod"] = $"Reference{cases.Count:D4}" };
                cases.Add(entry);
                try
                {
                    var value = ReferenceCase.Parse(Encoding.UTF8.GetString(bytes));
                    entry["caseId"] = value.S("caseId");
                    entry["origin"] = value.S("origin");
                    entry["required"] = value.B("required");
                    if (!ids.Add(value.S("caseId")))
                    {
                        throw new InvalidDataException("Duplicate caseId " + value.S("caseId"));
                    }

                    if (value.B("required"))
                    {
                        required[value.S("caseId")] = path;
                    }

                    ReferenceCase.Validate(value, context, root);
                    entry["status"] = "ready";
                }
                catch (Exception error) when (error is InvalidDataException or InvalidOperationException or JsonException or FormatException or IOException)
                {
                    entry["status"] = "blocked";
                    entry["reason"] = error.Message;
                    errors.Add(path + ": " + error.Message);
                }
            }
        }

        foreach (var (id, _) in required)
        {
            if (!cases.Any(c => c!.S("caseId") == id && c!.B("required")))
            {
                errors.Add("Missing required reference case: " + id);
            }
        }

        var characterization = new JsonArray();
        if (generated && cases.Count == 0 && required.Count == 0)
        {
            foreach (var program in ir.A("programs"))
            {
                var entry = new JsonObject { ["programId"] = program.S("id"), ["testMethod"] = $"Characterization{characterization.Count:D4}" };
                try
                {
                    var executionConfig = ir["config"]!.DeepClone();
                    executionConfig["maxSteps"] = Math.Min(10000, executionConfig.I("maxSteps"));
                    executionConfig["timeoutMs"] = Math.Min(1000, executionConfig.I("timeoutMs"));
                    entry["expected"] = new Machine(program, executionConfig).Run(FlowBuilder.Build(program), program);
                }
                catch (Exception error)
                {
                    entry["error"] = "Default characterization cannot complete: " + error.Message;
                }

                characterization.Add(entry);
            }
        }

        var inventory = new JsonObject { ["schemaVersion"] = "1.0", ["cases"] = cases, ["errors"] = errors, ["requiredCases"] = required, ["characterization"] = characterization };
        files["reports/quality/reference-inventory.json"] = Generator.Json(inventory);
        var summary = J.Parse(files["reports/quality/summary.json"]);
        summary["independentVerification"] = errors.Count != 0 ? "blocked" : cases.Count == 0 ? "not-configured" : "ready-not-run";
        summary["referenceCases"] = cases.Count;
        summary["referenceCaseErrors"] = errors.DeepClone();
        summary["context"] = context.DeepClone();
        files["reports/quality/summary.json"] = Generator.Json(summary);
        files["reports/quality/summary.md"] = "# Translation quality (generation only)\n\n" + "Source correspondence does not establish behavioral equivalence.\n\n```json\n" + Generator.Json(summary) + "```\n\nExecution reports are separate: TestResults/quality/<executionId>/. Only completed results with matching source/config/contract/case/code hashes apply.\n";
        var work = J.Parse(files["reports/quality/work-items.json"]).AsArray();
        foreach (var item in cases.Where(c => c!.S("status") == "blocked"))
        {
            work.Add(new JsonObject { ["caseId"] = item!.S("caseId"), ["ruleId"] = "reference-case-invalid", ["reason"] = item!.S("reason"), ["status"] = "open" });
        }

        files["reports/quality/work-items.json"] = Generator.Json(work);
        files["Tests/Contracts/full-state-v1.json"] = Generator.Json(new JsonObject { ["id"] = ReferenceCase.ContractId, ["version"] = "1.0", ["hash"] = ReferenceCase.ContractHash, ["observation"] = "display, fields, signal; exact logical values; ordered arrays; no trim; no double", ["inputBoundary"] = "internal initial-state injection (fields); not a business input API", ["physicalRecords"] = "unsupported" });
        context["contractFileHash"] = Generator.Hash(Encoding.UTF8.GetBytes(files["Tests/Contracts/full-state-v1.json"]));

        files["Tests/Templates/ReferenceCases/README.md"] = "# Independent reference cases\n\nPlace reviewed JSON cases in Tests/ReferenceCases/, then run normal translate again. Examples here are templates and are never discovered as cases. No real COBOL execution is claimed.\n\nSchemas and a complete synthetic example: see the repository docs/translation-quality.md. The case must contain schemaVersion 1.0, caseId, programId, origin, inputHashes, configHash, contract, input.fields, expected (display/fields/signal), required, evidence and review. Use full-state-v1 or a reviewed user-owned business contract in Tests/Contracts/. Fixed external responses require externalAdapter=fixed. All numeric values are strings. Review contentHash is ReferenceCase.ReviewHash(case), excluding only review. Source/config hashes above must not be updated to legitimize an old expected value.\n\nUse dotnet test Tests/CobolCS.ReferenceTests.csproj --logger trx. Required deleted cases remain missing; templates do not restore them.\n";
        const string sampleSource = "IDENTIFICATION DIVISION.\nPROGRAM-ID. QUALITY.\nDATA DIVISION.\nWORKING-STORAGE SECTION.\n01 N PIC 9 VALUE 0.\nPROCEDURE DIVISION.\nADD 2 TO N GOBACK.\n";
        var sample = new JsonObject
        {
            ["schemaVersion"] = "1.0",
            ["caseId"] = "synthetic-add-two",
            ["programId"] = "QUALITY",
            ["origin"] = "specification",
            ["inputHashes"] = new JsonObject { ["QUALITY.cbl"] = Generator.Hash(Encoding.UTF8.GetBytes(sampleSource)) },
            ["configHash"] = ir.S("configHash"),
            ["contract"] = new JsonObject { ["id"] = ReferenceCase.ContractId, ["version"] = "1.0", ["hash"] = ReferenceCase.ContractHash },
            ["input"] = new JsonObject { ["fields"] = new JsonObject() },
            ["expected"] = new JsonObject { ["display"] = new JsonArray(), ["fields"] = new JsonObject { ["N"] = "2" }, ["signal"] = "goback" },
            ["required"] = true,
            ["evidence"] = new JsonObject { ["rationale"] = "Hand calculated synthetic portable example: initial zero, ADD two, result two. No actual COBOL execution.", ["syntheticDependencies"] = false, ["conditions"] = new JsonObject() },
        };
        sample["review"] = new JsonObject { ["approved"] = false, ["reviewer"] = string.Empty, ["contentHash"] = ReferenceCase.ReviewHash(sample) };
        files["Tests/Templates/ReferenceCases/QUALITY.cbl"] = sampleSource;
        files["Tests/Templates/ReferenceCases/example.specification.json"] = Generator.Json(sample);
        files["Tests/Templates/ReferenceCases/schema.json"] = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "templates/reference-case.schema.json"));
        files["Tests/Templates/ReferenceCases/business-observation.schema.json"] = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "templates/business-observation.schema.json"));
        if (!generated)
        {
            files["Tests/Templates/ReferenceCases/context.json"] = Generator.Json(context);
            return;
        }

        files["GeneratedApi.cs"] = Api(ir, contracts);
        files["README.md"] = "# Generated CobolCS project\n\nBuild: `dotnet build CobolCS.Generated.csproj`. Independent verification: place reviewed cases in `Tests/ReferenceCases/`, run the existing CobolCS `translate` again, then `dotnet test Tests/CobolCS.ReferenceTests.csproj --logger trx`. The templates are not registered cases. Initial package restoration needs access to NuGet; subsequent builds and tests can use the local package cache offline.\n\nReview `reports/quality/summary.md`, translation-ledger.json and work-items.json. Completed runtime evidence is in `TestResults/quality/<executionId>/summary.json`. Generation never updates expected values. No references means not-configured, not COBOL equivalence. CALL/FILE/SQL can run only under the explicitly configured ordered fixed-response adapter; real services are unverified. Opaque adapter bytes can be compared without a PIC decoder. Adopt through the existing --config quality.adopt settings (destination, trx, review), after successful tests and review of Tests/Templates/ReferenceCases/adoption-review.json.\n\nCopy this project with Tests and runtime sources to retain independent tests. The executable and tests do not read reports/ir.json or the original COBOL source.\n";
        files["Tests/CobolCS.ReferenceTests.csproj"] = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <IsTestProject>true</IsTestProject>
                <IsPackable>false</IsPackable>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                <GenerateDocumentationFile>true</GenerateDocumentationFile>
                <NoWarn>$(NoWarn);SA1101;SA1200;SA1309;SA1629;SA1633</NoWarn>
              </PropertyGroup>
              <ItemGroup>
                <Compile Remove="ReferenceCases/**;Templates/**" />
                <ProjectReference Include="../CobolCS.Generated.csproj" />
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
                <PackageReference Include="MSTest.TestFramework" Version="3.6.4" />
                <PackageReference Include="MSTest.TestAdapter" Version="3.6.4" />
                <PackageReference Include="StyleCop.Analyzers" Version="1.2.0-beta.556" PrivateAssets="all" />
                <None Include="ReferenceCases/**" CopyToOutputDirectory="PreserveNewest" />
              </ItemGroup>
            </Project>
            """ + "\n";
        var codeArtifacts = new JsonObject();
        foreach (var (path, content) in files.Where(p => p.Key.EndsWith(".cs", StringComparison.Ordinal) && !p.Key.StartsWith("Tests/", StringComparison.Ordinal)))
        {
            codeArtifacts[path] = Generator.Hash(Encoding.UTF8.GetBytes(content));
        }

        context["codeArtifacts"] = codeArtifacts;
        context["compiledSourceFingerprint"] = ReferenceCase.ReviewHash(codeArtifacts);
        files["GeneratedSourceMetadata.cs"] = "using System.Reflection;\n\n[assembly: AssemblyMetadata(\"CobolCS.SourceFingerprint\", " + JsonSerializer.Serialize(context.S("compiledSourceFingerprint")) + ")]\n";
        files["Tests/Templates/ReferenceCases/context.json"] = Generator.Json(context);
        var history = new JsonArray();
        JsonNode? latestCurrentResult = null;
        var historyRoot = Path.Combine(output, "TestResults/quality");
        if (Directory.Exists(historyRoot))
        {
            foreach (var report in Directory.GetFiles(historyRoot, "summary.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                Generator.SafePath(report);
                try
                {
                    var result = J.Parse(File.ReadAllText(report));
                    var current = result.B("completed") && result.S("compiledSourceFingerprint") == context.S("compiledSourceFingerprint") && JsonNode.DeepEquals(result["context"], context) && JsonNode.DeepEquals(result["caseInventory"], inventory) && JsonNode.DeepEquals(result["executedCodeArtifacts"], codeArtifacts);
                    if (current && (latestCurrentResult == null || string.CompareOrdinal(result.S("completedAt"), latestCurrentResult.S("completedAt")) > 0))
                    {
                        latestCurrentResult = result;
                    }

                    history.Add(new JsonObject { ["path"] = Path.GetRelativePath(output, report).Replace('\\', '/'), ["status"] = current ? "current" : "stale", ["result"] = result.S("status") });
                    if (!current)
                    {
                        work.Add(new JsonObject { ["ruleId"] = "stale-quality-evidence", ["executionId"] = Path.GetFileName(Path.GetDirectoryName(report)), ["status"] = "open" });
                    }
                }
                catch (Exception error) when (error is JsonException or InvalidOperationException or IOException)
                {
                    history.Add(new JsonObject { ["path"] = Path.GetRelativePath(output, report).Replace('\\', '/'), ["status"] = "invalid", ["reason"] = error.Message });
                }
            }
        }

        if (Directory.Exists(historyRoot))
        {
            foreach (var started in Directory.GetFiles(historyRoot, "started.json", SearchOption.AllDirectories).Where(p => !File.Exists(Path.Combine(Path.GetDirectoryName(p)!, "summary.json"))))
            {
                Generator.SafePath(started);
                history.Add(new JsonObject { ["path"] = Path.GetRelativePath(output, started).Replace('\\', '/'), ["status"] = "incomplete" });
                work.Add(new JsonObject { ["ruleId"] = "incomplete-quality-evidence", ["executionId"] = Path.GetFileName(Path.GetDirectoryName(started)), ["status"] = "open" });
            }
        }

        if (latestCurrentResult != null)
        {
            foreach (var record in latestCurrentResult.A("cases").Where(c => c.S("status") != "matched"))
            {
                var differences = record["comparison"]?.A("differences").ToArray() ?? [];
                if (differences.Length == 0)
                {
                    work.Add(new JsonObject { ["caseId"] = record.S("caseId"), ["ruleId"] = "verification-infrastructure-failure", ["reason"] = record.S("error"), ["status"] = "open" });
                }

                foreach (var difference in differences)
                {
                    work.Add(new JsonObject { ["caseId"] = record.S("caseId"), ["programId"] = record.S("programId"), ["statementId"] = difference["statementId"]?.DeepClone(), ["businessKey"] = difference["businessKey"]?.DeepClone(), ["path"] = difference.S("path"), ["ruleId"] = "reference-comparison-mismatch", ["status"] = "open" });
                }
            }
        }

        summary["executionEvidence"] = history;
        summary["context"] = context.DeepClone();
        files["reports/quality/summary.json"] = Generator.Json(summary);
        files["reports/quality/summary.md"] = "# Translation quality (generation only)\n\nCorrespondence is not equivalence. Execution results must be reviewed separately.\n\n```json\n" + Generator.Json(summary) + "```\n";
        files["reports/quality/work-items.json"] = Generator.Json(work);
        var code = new StringBuilder($$"""
            using System.Text.Json.Nodes;
            using CobolCS.Generated;
            using CobolCS.Runtime;
            using Microsoft.VisualStudio.TestTools.UnitTesting;

            namespace CobolCS.ReferenceTests;

            /// <summary>利用者の独立期待値を固定入力で比較する生成 MSTest です。</summary>
            [TestClass]
            public sealed class ReferenceGenerated
            {
                private static readonly ReferenceVerification Verification = new(
                    J.Parse({{JsonSerializer.Serialize(context.ToJsonString())}}),
                    J.Parse({{JsonSerializer.Serialize(inventory.ToJsonString())}}),
                    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../ReferenceCases")),
                    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../TestResults/quality")));

                private static TestContext testContext = null!;

                /// <summary>Gets or sets MSTest の実行コンテキスト。</summary>
                public TestContext TestContext { get; set; } = null!;

                /// <summary>TRX に完了レポートを関連付けるコンテキストを記録します。</summary>
                /// <param name="context">MSTest のコンテキスト。</param>
                [ClassInitialize]
                public static void Initialize(TestContext context) => testContext = context;

                /// <summary>試験終了時の完了証跡を保存します。</summary>
                [ClassCleanup]
                public static void Complete()
                {
                    Verification.Complete(typeof(GeneratedApi).Assembly.Location);
                    testContext.AddResultFile(Verification.SummaryPath);
                }

                /// <summary>取込不備や必須ケースの欠落を成功にしないゲートです。</summary>
                [TestMethod]
                public void ReferenceCaseGate()
                {
                    TestContext.WriteLine("COBOLCS_QUALITY_EXECUTION=" + Verification.ExecutionId);
                    Assert.AreEqual(string.Empty, Verification.CheckGate());
                }
            """);
        code.AppendLine();
        var number = 0;
        foreach (var item in cases)
        {
            code.AppendLine($$"""

                    /// <summary>固定ケース {{number}} を独立期待値と比較します。</summary>
                    [TestMethod]
                    [TestProperty("caseId", {{JsonSerializer.Serialize(item!.S("caseId").Length == 0 ? "invalid:" + item!.S("path") : item!.S("caseId"))}})]
                    public void Reference{{number++:D4}}()
                    {
                        TestContext.WriteLine("COBOLCS_QUALITY_EXECUTION=" + Verification.ExecutionId);
                        Assert.AreEqual(string.Empty, Verification.Execute({{JsonSerializer.Serialize(item!.S("path"))}}, (id, input, trace) => GeneratedApi.Run(id, input, trace)));
                    }
                """);
        }

        foreach (var item in characterization)
        {
            code.AppendLine($$"""

                    /// <summary>C# 自己観測の回帰。独立 COBOL 一致とは別に記録します。</summary>
                    [TestMethod]
                    public void {{item!.S("testMethod")}}()
                    {
                        TestContext.WriteLine("COBOLCS_QUALITY_EXECUTION=" + Verification.ExecutionId);
                        Assert.AreEqual(string.Empty, Verification.Characterize({{JsonSerializer.Serialize(item!.S("programId"))}}, (id, input, trace) => GeneratedApi.Run(id, input, trace)));
                    }
                """);
        }

        var adoptionPayload = new JsonObject { ["context"] = context.DeepClone(), ["inventory"] = inventory.DeepClone() };
        files["Tests/Templates/ReferenceCases/adoption-review.json"] = Generator.Json(new JsonObject { ["approved"] = false, ["reviewer"] = string.Empty, ["contentHash"] = ReferenceCase.ReviewHash(adoptionPayload) });
        code.AppendLine("}");
        files["Tests/Cases/ReferenceGenerated/ReferenceGenerated.cs"] = code.ToString();
    }
}
