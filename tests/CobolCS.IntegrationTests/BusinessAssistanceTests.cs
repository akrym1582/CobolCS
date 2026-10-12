using System.Text.Json.Nodes;
using CobolCS.Cli;
using CobolCS.Frontend;
using CobolCS.Runtime;
using CobolCS.Tools;
using Xunit;

namespace CobolCS.IntegrationTests;

/// <summary>明示境界・実 runner・利用者バイト保持・採用後の置換を検証します。</summary>
public sealed class BusinessAssistanceTests
{
    /// <summary>既存 payment の native INSPECT と合成 CALL を独立の料金期待値で確認します。</summary>
    /// <returns>生成、実テスト、採用、サービス置換。</returns>
    [Fact]
    public async Task PaymentScaffoldToAdoptionAndServiceReplacement()
    {
        using var folder = new TestDirectory();
        var root = Generate(folder.Path, "completion/payment.cbl");
        Assert.DoesNotContain(Workspace.Read(Path.Combine(root, "reports/stubs.json")).AsArray(), s => s!.S("executionRole") == "internal-logic");
        var mapping = Workspace.Read(Path.Combine(Workspace.Root, "tests/fixtures/business-assistance/payment.mapping.json"));
        mapping["candidateHash"] = Candidates(root).S("candidateHash");
        var path = Path.Combine(folder.Path, "mapping.json");
        Workspace.Write(path, mapping);
        Assert.Equal(0, BusinessScaffolding.Run(root, "PAYMENT", path));
        Assert.Empty(BusinessCases.Load(root));
        _ = await ProcessRunner.Run("dotnet", ["build", "CobolCS.Generated.sln", "-m:1"], root);
        var scaffold = Scaffold(root);
        var adapter = scaffold.A("files").Select(f => f.GetValue<string>()).Single(p => p.EndsWith("Adapter.cs", StringComparison.Ordinal));
        var template = scaffold.A("files").Select(f => f.GetValue<string>()).Single(p => p.EndsWith("case.json.txt", StringComparison.Ordinal));
        var registrationExample = scaffold.A("files").Select(f => f.GetValue<string>()).Single(p => p.EndsWith("registration.cs.txt", StringComparison.Ordinal));
        var registration = Path.Combine(root, "UserCode/Business/BusinessRegistration.cs");
        File.WriteAllText(registration, File.ReadAllText(registration).Replace("// adapters.Add(\"payment-v1\", () => new PaymentAdapter());", File.ReadAllText(Path.Combine(root, registrationExample)).Trim(), StringComparison.Ordinal));
        foreach (var external in Workspace.Read(Path.Combine(root, "reports/stubs.json")).AsArray())
        {
            var file = Path.Combine(root, external!.S("file"));
            File.WriteAllText(file, File.ReadAllText(file).Replace("throw new NotImplementedException", "throw new InvalidOperationException", StringComparison.Ordinal));
        }

        var item = Workspace.Read(Path.Combine(root, template));
        item["caseId"] = "IndependentFee";
        item["provenance"] = new JsonObject { ["calculation"] = "Independent hand calculation of synthetic fee, not COBOL capture" };
        item["reviewReason"] = "1000.00 * 0.015 = 15.00; AB contains one A; synthetic response is OK";
        item["input"] = new JsonObject { ["balance"] = "1000.00", ["testState"] = new JsonObject { ["seedText"] = "AB" } };
        item["responses"] = new JsonArray(new JsonObject { ["operation"] = "customer.lookup", ["request"] = new JsonObject { ["service"] = "SYNTHETIC", ["code"] = "  " }, ["response"] = new JsonObject { ["status"] = "OK", ["outcome"] = "Normal", ["code"] = "OK" } });
        item["expected"] = new JsonObject { ["result"] = new JsonObject { ["fee"] = "15.00", ["display"] = new JsonArray("OK", "0001500") }, ["effects"] = new JsonArray(new JsonObject { ["operation"] = "customer.lookup", ["request"] = new JsonObject { ["service"] = "SYNTHETIC", ["code"] = "  " } }), ["termination"] = "returned" };
        var caseFile = Path.Combine(root, "Tests/BusinessCases/payment.json");
        Directory.CreateDirectory(Path.GetDirectoryName(caseFile)!);
        Workspace.Write(caseFile, item);
        Assert.Equal(0, CompletionWorkflow.Prepare(root));
        _ = await ProcessRunner.Run("dotnet", ["test", "Tests/CobolCS.Generated.Tests.csproj", "--filter", "Category=Business", "--logger", "trx;LogFileName=business-assistance.trx"], root);
        var unchanged = File.ReadAllBytes(caseFile);
        var programFile = Directory.GetFiles(Path.Combine(root, "Programs")).Single();
        var source = File.ReadAllText(programFile);
        File.WriteAllText(programFile, source.Replace("0.015", "0.025", StringComparison.Ordinal));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ProcessRunner.Run("dotnet", ["test", "Tests/CobolCS.Generated.Tests.csproj", "--filter", "Category=Business"], root));
        Assert.Equal(unchanged, File.ReadAllBytes(caseFile));
        File.WriteAllText(programFile, source);
        var wrong = item.DeepClone();
        wrong["responses"]![0]!["request"]!["code"] = "XX";
        Workspace.Write(caseFile, wrong);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ProcessRunner.Run("dotnet", ["test", "Tests/CobolCS.Generated.Tests.csproj", "--filter", "Category=Business"], root));
        Workspace.Write(caseFile, item);
        Review(root);
        var adopted = Path.Combine(folder.Path, "adopted");
        Assert.Equal(0, await Adoption.Run(root, adopted));
        Assert.Throws<IOException>(() => BusinessScaffolding.Run(adopted, "PAYMENT", path));
        var className = Path.GetFileNameWithoutExtension(adapter);
        var replacement = """
            using System.Text.Json.Nodes;
            using CobolCS.Runtime;

            namespace CobolCS.Generated;

            /// <summary>独立料金仕様を実装した採用後のサービス。</summary>
            public sealed class CLASSNAME : IBusinessCaseAdapter
            {
                /// <summary>Gets 安定した業務契約。</summary>
                public string ContractId => "payment-assistance-v1";

                /// <summary>同じ固定入力・応答・期待値を使用します。</summary>
                /// <param name="input">業務入力。</param>
                /// <returns>選択結果と効果。</returns>
                public BusinessObservation Execute(BusinessCaseInput input)
                {
                    _ = input.External.Invoke<JsonObject, JsonObject>("customer.lookup", new JsonObject { ["service"] = "SYNTHETIC", ["code"] = "  " }, request => request, response => response);
                    input.External.VerifyComplete();
                    return new(new JsonObject { ["fee"] = BusinessValues.Decimal(CobolValue.Numeric(input.Values["balance"]!.GetValue<string>()) * CobolValue.Numeric("0.015"), 2), ["display"] = new JsonArray("OK", "0001500") }, input.External.Effects, "returned");
                }
            }
            """;
        File.WriteAllText(Path.Combine(adopted, adapter), replacement.Replace("CLASSNAME", className, StringComparison.Ordinal) + "\n");
        _ = await ProcessRunner.Run("dotnet", ["test", "Tests/CobolCS.Generated.Tests.csproj", "--filter", "Category=Business"], adopted);
        Assert.Equal(unchanged, File.ReadAllBytes(Path.Combine(adopted, "Tests/BusinessCases/payment.json")));
    }

    /// <summary>不正 mapping、再実行、削除、変更提案、公開競合を確認します。</summary>
    [Fact]
    public void MappingValidationAndUserBytesArePreserved()
    {
        using var folder = new TestDirectory();
        var root = Generate(folder.Path, "completion/payment.cbl");
        var mapping = Workspace.Read(Path.Combine(Workspace.Root, "tests/fixtures/business-assistance/payment.mapping.json"));
        mapping["candidateHash"] = Candidates(root).S("candidateHash");
        var path = Path.Combine(folder.Path, "mapping.json");
        foreach (var mutation in new Action<JsonNode>[]
        {
            m => m["candidateHash"] = new string('0', 64),
            m => m["programId"] = "OTHER",
            m => m["results"]![0]!["symbolId"] = "unknown",
            m => m["inputs"]![0]!["source"] = "initialState",
            m => m["results"]![0]!["type"] = new JsonObject { ["type"] = "string" },
            m => m["results"]!.AsArray().Add(m["results"]![0]!.DeepClone()),
            m => m["operations"]![0]!["response"]!["code"] = "Missing",
            m => m["expression"] = "arbitrary C# is not supported",
        })
        {
            var before = Generator.Inventory(root);
            var bad = mapping.DeepClone();
            mutation(bad);
            Workspace.Write(path, bad);
            Assert.Throws<ArgumentException>(() => BusinessScaffolding.Run(root, "PAYMENT", path));
            Assert.Equal(before, Generator.Inventory(root));
        }

        Workspace.Write(path, mapping);
        Assert.Equal(0, BusinessScaffolding.Run(root, "PAYMENT", path));
        var scaffold = Scaffold(root);
        var files = scaffold.A("files").Select(f => f.GetValue<string>()).ToArray();
        var adapter = files.Single(p => p.EndsWith("Adapter.cs", StringComparison.Ordinal));
        File.AppendAllText(Path.Combine(root, adapter), "\n// preserved user edit\n");
        var deleted = files.Single(p => p.EndsWith("case.json.txt", StringComparison.Ordinal));
        File.Delete(Path.Combine(root, deleted));
        var bytes = File.ReadAllBytes(Path.Combine(root, adapter));
        var review = File.ReadAllBytes(Path.Combine(root, "Tests/review.json"));
        Assert.Equal(0, BusinessScaffolding.Run(root, "PAYMENT", path));
        mapping["selectionReason"] = "Explicitly changed selection rationale; propose without patching user code";
        Workspace.Write(path, mapping);
        Assert.Equal(0, BusinessScaffolding.Run(root, "PAYMENT", path));
        Assert.NotNull(Scaffold(root)["proposal"]);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(root, adapter)));
        Assert.Equal(review, File.ReadAllBytes(Path.Combine(root, "Tests/review.json")));
        Assert.False(File.Exists(Path.Combine(root, deleted)));
        Assert.Throws<IOException>(() => CompletionWorkflow.Stage(root, (_, _) =>
        {
            File.AppendAllText(Path.Combine(root, adapter), "// concurrent edit\n");
            return 0;
        }));
        Assert.EndsWith("// concurrent edit\n", File.ReadAllText(Path.Combine(root, adapter)), StringComparison.Ordinal);
    }

    /// <summary>正式 LINKAGE グループと固定表を既存 DTO に配線します。</summary>
    /// <returns>生成 Adapter の実行。</returns>
    [Fact]
    public async Task FixedLinkageGroupAndTableKeepOrderAndPic()
    {
        using var folder = new TestDirectory();
        var root = Generate(folder.Path, "business-assistance/linkage.cbl");
        var program = Candidates(root);
        var symbol = program.A("candidates").Single(c => c.S("role") == "formal-input");
        var mapping = new JsonObject { ["schemaVersion"] = 1, ["programId"] = "BUSINESS-LINKAGE", ["candidateHash"] = program.S("candidateHash"), ["contractId"] = "group-v1", ["initialState"] = new JsonArray(new JsonObject { ["key"] = "seedTable", ["source"] = "initialState", ["symbolId"] = "f000001" }), ["version"] = 1, ["selectionReason"] = "Explicit whole fixed group and its ordered table", ["inputs"] = new JsonArray(new JsonObject { ["key"] = "request", ["source"] = "arguments", ["symbolId"] = symbol.S("symbolId") }), ["results"] = new JsonArray(new JsonObject { ["key"] = "updated", ["source"] = "arguments", ["symbolId"] = symbol.S("symbolId") }, new JsonObject { ["key"] = "table", ["source"] = "snapshot", ["symbolId"] = "f000001" }), ["operations"] = new JsonArray(), ["termination"] = new JsonObject { ["Goback"] = "returned" } };
        var path = Path.Combine(folder.Path, "mapping.json");
        Workspace.Write(path, mapping);
        Assert.Equal(0, BusinessScaffolding.Run(root, "BUSINESS-LINKAGE", path));
        var scaffold = Scaffold(root);
        var template = scaffold.A("files").Select(f => f.GetValue<string>()).Single(p => p.EndsWith("case.json.txt", StringComparison.Ordinal));
        var item = Workspace.Read(Path.Combine(root, template));
        item["caseId"] = "GroupCalculation";
        item["provenance"] = new JsonObject { ["calculation"] = "Independent fixed additions and full table order" };
        item["reviewReason"] = "10.00+1=11.00; second element 2+2=4; first element stays 1";
        item["input"] = new JsonObject { ["request"] = new JsonObject { ["field_f000003"] = "10.00", ["field_f000004"] = new JsonArray("1", "2") }, ["testState"] = new JsonObject { ["seedTable"] = new JsonArray("3", "4") } };
        item["expected"] = new JsonObject { ["result"] = new JsonObject { ["updated"] = new JsonObject { ["field_f000003"] = "11.00", ["field_f000004"] = new JsonArray("1", "4") }, ["table"] = new JsonArray("3", "4") }, ["effects"] = new JsonArray(), ["termination"] = "returned" };
        Directory.CreateDirectory(Path.Combine(root, "Tests/BusinessCases"));
        Workspace.Write(Path.Combine(root, "Tests/BusinessCases/group.json"), item);
        var registrationExample = scaffold.A("files").Select(f => f.GetValue<string>()).Single(p => p.EndsWith("registration.cs.txt", StringComparison.Ordinal));
        var registration = Path.Combine(root, "UserCode/Business/BusinessRegistration.cs");
        File.WriteAllText(registration, File.ReadAllText(registration).Replace("// adapters.Add(\"payment-v1\", () => new PaymentAdapter());", File.ReadAllText(Path.Combine(root, registrationExample)).Trim(), StringComparison.Ordinal));
        _ = await ProcessRunner.Run("dotnet", ["test", "Tests/CobolCS.Generated.Tests.csproj", "--filter", "Category=Business"], root);
        item["input"]!["request"]!["field_f000004"] = new JsonArray("1");
        Workspace.Write(Path.Combine(root, "Tests/BusinessCases/group.json"), item);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ProcessRunner.Run("dotnet", ["test", "Tests/CobolCS.Generated.Tests.csproj", "--filter", "Category=Business"], root));
    }

    /// <summary>合成入力の正式 API と独立期待値を作るための小さな探索予算です。</summary>
    private static string Generate(string folder, string fixture)
    {
        var root = Path.Combine(folder, "generated");
        Generator.Generate(FrontendAnalyzer.Analyze(Path.Combine(Workspace.Root, "tests/fixtures", fixture), FrontendConfiguration.Parse("{\"testGeneration\":{\"maxAttempts\":4}}")), root, "scaffold");
        return root;
    }

    /// <summary>プログラムを明示した管理候補。</summary>
    private static JsonNode Candidates(string root) => Workspace.Read(Path.Combine(root, "reports/business-test-candidates.json")).A("programs").Single();

    /// <summary>単一契約の管理来歴。</summary>
    private static JsonNode Scaffold(string root) => Workspace.Read(Directory.GetFiles(Path.Combine(root, "reports/business-scaffolds"), "*.json").Single(p => !p.EndsWith("-review-files.json", StringComparison.Ordinal)));

    /// <summary>合成 characterization と独立業務期待値を明示承認します。</summary>
    private static void Review(string root)
    {
        var manifest = Generator.ReadManifest(root);
        var path = Path.Combine(root, "Tests/review.json");
        var review = Workspace.Read(path);
        foreach (var entry in review.A("cases"))
        {
            foreach (var (file, _) in entry["files"]!.AsObject().ToArray())
            {
                if (file.StartsWith("Tests/Baselines/", StringComparison.Ordinal))
                {
                    var value = Workspace.Read(Path.Combine(root, file));
                    value["reviewReason"] = "Reviewed synthetic C# characterization, not original COBOL";
                    Workspace.Write(Path.Combine(root, file), value);
                }

                entry["files"]![file] = BusinessCases.FileHash(Path.Combine(root, file));
            }

            entry["reviewed"] = true;
        }

        foreach (var item in BusinessCases.Load(root))
        {
            var contracts = Generator.Inventory(root).Where(p => p.Key.StartsWith("Tests/BusinessContracts/", StringComparison.Ordinal));
            review["cases"]!.AsArray().Add(new JsonObject { ["programId"] = item.Case.S("programId"), ["caseId"] = item.Case.S("caseId"), ["caseKind"] = "business", ["baselineKind"] = "specification", ["reviewed"] = true, ["reviewReason"] = "Independent arithmetic and fixed synthetic external payload", ["hasNoOpCalls"] = false, ["callStubMode"] = "Throw", ["sourceHashes"] = manifest["inputHashes"]!.DeepClone(), ["configHash"] = manifest.S("configHash"), ["contractHashes"] = manifest["contractHashes"]!.DeepClone(), ["implementationHashes"] = BusinessReview.ImplementationHashes(root), ["files"] = new JsonObject(contracts.Append(KeyValuePair.Create(item.Path, BusinessCases.FileHash(Path.Combine(root, item.Path)))).Select(p => KeyValuePair.Create<string, JsonNode?>(p.Key, JsonValue.Create(p.Value)))) });
        }

        Workspace.Write(path, review);
    }
}
