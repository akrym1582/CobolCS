using System.Text.Json.Nodes;
using CobolCS.Cli;
using CobolCS.Frontend;
using CobolCS.Runtime;
using CobolCS.Tools;
using Xunit;

namespace CobolCS.IntegrationTests;

/// <summary>反復・分岐の正式入力を業務ケースごとに固定し、採用後も同じ仕様を検証します。</summary>
public sealed class BusinessAcceptTests
{
    /// <summary>同じ symbol の異なる文、反復、分岐を構造化と dispatcher で同じ意味にします。</summary>
    /// <param name="dispatcher">段落 dispatcher を生成するか。</param>
    /// <returns>生成 Adapter と実 xUnit 行の正負検証。</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CaseSequencesSurviveAdoptionAndServiceReplacement(bool dispatcher)
    {
        using var folder = new TestDirectory();
        var source = Path.Combine(folder.Path, "input.cbl");
        File.WriteAllText(source, "IDENTIFICATION DIVISION. PROGRAM-ID. ACCEPT-CASE. DATA DIVISION. WORKING-STORAGE SECTION. 01 MODE PIC 9. 01 AMOUNT PIC 999V99. 01 TOTAL PIC 9(4)V99 VALUE 0. 01 TEXT-VALUE PIC XX. PROCEDURE DIVISION. " + (dispatcher ? "MAIN. " : string.Empty) + "ACCEPT MODE IF MODE = 1 PERFORM 2 TIMES ACCEPT AMOUNT ADD AMOUNT TO TOTAL END-PERFORM ELSE ACCEPT AMOUNT ADD AMOUNT TO TOTAL END-IF ACCEPT TEXT-VALUE DISPLAY TEXT-VALUE GOBACK.");
        var root = Path.Combine(folder.Path, "generated");
        Generator.Generate(FrontendAnalyzer.Analyze(source, FrontendConfiguration.Parse("{\"testGeneration\":{\"maxAttempts\":4}}")), root, "scaffold");
        var program = Workspace.Read(Path.Combine(root, "reports/business-test-candidates.json")).A("programs").Single();
        var accepts = program.A("candidates").Where(c => c.S("source") == "accept").ToArray();
        Assert.Equal(4, accepts.Length);
        var acceptKeys = new[] { "mode", "repeatAmount", "singleAmount", "label" };
        var mapping = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["programId"] = "ACCEPT-CASE",
            ["candidateHash"] = program.S("candidateHash"),
            ["contractId"] = "accept-case-v1",
            ["version"] = 1,
            ["selectionReason"] = "Independent totals and branch-specific input counts",
            ["acceptMode"] = "case-sequence",
            ["inputs"] = new JsonArray(accepts.Select((a, i) => (JsonNode?)new JsonObject { ["key"] = acceptKeys[i], ["source"] = "accept", ["symbolId"] = a.S("symbolId"), ["statementId"] = a.S("statementId") }).ToArray()),
            ["results"] = new JsonArray(new JsonObject { ["key"] = "total", ["source"] = "snapshot", ["symbolId"] = program.A("candidates").First(c => c.S("role") == "result" && c.S("originalName") == "TOTAL").S("symbolId") }, new JsonObject { ["key"] = "display", ["source"] = "display" }),
            ["operations"] = new JsonArray(),
            ["termination"] = new JsonObject { ["Goback"] = "returned" },
        };
        var mappingPath = Path.Combine(folder.Path, "mapping.json");
        Workspace.Write(mappingPath, mapping);
        Assert.Equal(0, BusinessScaffolding.Run(root, "ACCEPT-CASE", mappingPath));
        var evidence = Workspace.Read(Directory.GetFiles(Path.Combine(root, "reports/business-scaffolds"), "*.json").Single(p => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(p), @"\ABusiness_[a-f0-9]{64}\.json\z")));
        var files = evidence.A("files").Select(f => f.GetValue<string>()).ToArray();
        var template = files.Single(p => p.EndsWith("case.json.txt", StringComparison.Ordinal));
        var item = Workspace.Read(Path.Combine(root, template));
        Assert.Empty(item["input"]!["acceptSequence"]!.AsArray());
        Assert.Null(item["expected"]!["termination"]);
        var registration = Path.Combine(root, "UserCode/Business/BusinessRegistration.cs");
        File.WriteAllText(registration, File.ReadAllText(registration).Replace("// adapters.Add(\"payment-v1\", () => new PaymentAdapter());", File.ReadAllText(Path.Combine(root, files.Single(p => p.EndsWith("registration.cs.txt", StringComparison.Ordinal)))).Trim(), StringComparison.Ordinal));
        Directory.CreateDirectory(Path.Combine(root, "Tests/BusinessCases"));
        var casePath = Path.Combine(root, "Tests/BusinessCases/repeat.json");
        Workspace.Write(casePath, item);
        Assert.Empty(BusinessCases.Inspect(root).Cases);
        item["caseId"] = "IndependentRepeat";
        item["provenance"] = new JsonObject { ["calculation"] = "Hand calculated 10.25 + 20.50 = 30.75" };
        item["reviewReason"] = "Two different amounts and preserved trailing space; no observed expectations";
        item["input"] = BusinessComparison.Parse("""{"mode":["1"],"repeatAmount":["10.25","20.50"],"singleAmount":[],"label":["A "],"acceptSequence":["mode","repeatAmount","repeatAmount","label"]}""");
        item["expected"] = BusinessComparison.Parse("""{"result":{"total":"30.75","display":["A "]},"effects":[],"termination":"returned"}""");
        Workspace.Write(casePath, item);
        var branch = item.DeepClone();
        branch["caseId"] = "IndependentSingle";
        branch["input"] = BusinessComparison.Parse("""{"mode":["2"],"repeatAmount":[],"singleAmount":["7.25"],"label":["B "],"acceptSequence":["mode","singleAmount","label"]}""");
        branch["expected"]!["result"] = BusinessComparison.Parse("""{"total":"7.25","display":["B "]}""");
        Workspace.Write(Path.Combine(root, "Tests/BusinessCases/single.json"), branch);
        Assert.Equal(0, CompletionWorkflow.Prepare(root));
        Assert.Equal(2, BusinessCases.Load(root).Count);
        _ = await ProcessRunner.Run("dotnet", ["test", "Tests/CobolCS.Generated.Tests.csproj", "--filter", "Category=Business"], root);
        var original = File.ReadAllBytes(casePath);
        var wrongOrder = item.DeepClone();
        wrongOrder["input"]!["acceptSequence"] = new JsonArray("repeatAmount", "mode", "repeatAmount", "label");
        Workspace.Write(casePath, wrongOrder);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ProcessRunner.Run("dotnet", ["test", "Tests/CobolCS.Generated.Tests.csproj", "--filter", "Category=Business"], root));
        Assert.Contains(Directory.GetFiles(Path.Combine(root, "TestResults/business"), "*.json", SearchOption.AllDirectories), p => File.ReadAllText(p).Contains("accept-input-order-mismatch", StringComparison.Ordinal));
        File.WriteAllBytes(casePath, original);
        foreach (var badNumber in new[] { "1.001", "1000.00", "10.25 ", "bad" })
        {
            var bad = item.DeepClone();
            bad["input"]!["repeatAmount"]![0] = badNumber;
            Workspace.Write(casePath, bad);
            Assert.NotEmpty(BusinessCases.Inspect(root).Diagnostics);
        }

        File.WriteAllBytes(casePath, original);
        var adapterPath = Path.Combine(root, files.Single(p => p.EndsWith("Adapter.cs", StringComparison.Ordinal)));
        var adapterSource = File.ReadAllText(adapterPath);
        var modeLine = adapterSource.Split('\n').Single(line => line.Contains("return BusinessValues.Decimal", StringComparison.Ordinal) && line.Contains("script.Read(\"mode\")", StringComparison.Ordinal));
        File.WriteAllText(adapterPath, adapterSource.Replace(modeLine, modeLine[..modeLine.IndexOf("return", StringComparison.Ordinal)] + "throw BusinessAcceptScript.Unmapped(source.ToString());", StringComparison.Ordinal));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ProcessRunner.Run("dotnet", ["test", "Tests/CobolCS.Generated.Tests.csproj", "--filter", "Category=Business"], root));
        Assert.Contains(Directory.GetFiles(Path.Combine(root, "TestResults/business"), "*.json", SearchOption.AllDirectories), p => File.ReadAllText(p).Contains("accept-input-unmapped", StringComparison.Ordinal));
        File.WriteAllText(adapterPath, adapterSource.Replace("AcceptInput = new CaseAcceptInput(accept)", "AcceptInput = new CaseAcceptInput(accept), AcceptInputs = new[] { \"1\" }", StringComparison.Ordinal));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ProcessRunner.Run("dotnet", ["test", "Tests/CobolCS.Generated.Tests.csproj", "--filter", "Category=Business"], root));
        File.WriteAllText(adapterPath, adapterSource);
        var unused = branch.DeepClone();
        unused["input"]!["repeatAmount"] = new JsonArray("1.00");
        unused["input"]!["acceptSequence"] = new JsonArray("mode", "singleAmount", "label", "repeatAmount");
        Workspace.Write(Path.Combine(root, "Tests/BusinessCases/single.json"), unused);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ProcessRunner.Run("dotnet", ["test", "Tests/CobolCS.Generated.Tests.csproj", "--filter", "Category=Business"], root));
        Workspace.Write(Path.Combine(root, "Tests/BusinessCases/single.json"), branch);
        BusinessAssistanceTests.Review(root);
        var reviewBytes = File.ReadAllBytes(Path.Combine(root, "Tests/review.json"));
        var adopted = Path.Combine(folder.Path, "adopted");
        Assert.Equal(0, await Adoption.Run(root, adopted));
        var adapter = files.Single(p => p.EndsWith("Adapter.cs", StringComparison.Ordinal));
        var replacement = """
            using System.Text.Json.Nodes;
            using CobolCS.Runtime;

            namespace CobolCS.Generated;

            /// <summary>独立仕様のサービス。COBOL の文 ID を使用しません。</summary>
            public sealed class CLASS : IBusinessCaseAdapter
            {
                /// <summary>Gets 固定業務契約。</summary>
                public string ContractId => "accept-case-v1";

                /// <summary>同じ値列と順序で独立料金を計算します。</summary>
                /// <param name="input">ケース専用の正式入力。</param>
                /// <returns>業務結果。</returns>
                public BusinessObservation Execute(BusinessCaseInput input)
                {
                    var script = new BusinessAcceptScript(input.Values, new[] { "mode", "repeatAmount", "singleAmount", "label" });
                    var mode = script.Read("mode").GetValue<string>();
                    var total = new Number(0);
                    if (mode == "1")
                    {
                        for (var i = 0; i < 2; i++)
                        {
                            total += Number.Parse(script.Read("repeatAmount").GetValue<string>());
                        }
                    }
                    else
                    {
                        total = Number.Parse(script.Read("singleAmount").GetValue<string>());
                    }

                    var label = script.Read("label").GetValue<string>();
                    script.VerifyComplete();
                    return new(new JsonObject { ["total"] = BusinessValues.Decimal(total, 2), ["display"] = new JsonArray(label) }, new JsonArray(), "returned");
                }
            }
            """;
        File.WriteAllText(Path.Combine(adopted, adapter), replacement.Replace("CLASS", Path.GetFileNameWithoutExtension(adapter), StringComparison.Ordinal) + "\n");
        _ = await ProcessRunner.Run("dotnet", ["test", "Tests/CobolCS.Generated.Tests.csproj", "--filter", "Category=Business"], adopted);
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(adopted, "Tests/BusinessCases/repeat.json")));
        Assert.Equal(reviewBytes, File.ReadAllBytes(Path.Combine(adopted, "Tests/review.json")));
        File.AppendAllText(Path.Combine(adopted, "Tests/BusinessContracts", Path.GetFileName(files.Single(p => p.StartsWith("Tests/BusinessContracts/", StringComparison.Ordinal)))), " ");
        await Assert.ThrowsAsync<InvalidOperationException>(() => ProcessRunner.Run("dotnet", ["test", "Tests/CobolCS.Generated.Tests.csproj", "--filter", "Category=Business"], adopted));
    }

    /// <summary>任意 metadata の不整合と精密数値を Strict と Inspect の同じ境界で拒否します。</summary>
    [Fact]
    public void ContractMetadataAndInvalidValuesUseCommonInspection()
    {
        using var folder = new TestDirectory();
        Directory.CreateDirectory(Path.Combine(folder.Path, "Tests/BusinessContracts"));
        Directory.CreateDirectory(Path.Combine(folder.Path, "Tests/BusinessCases"));
        var contract = BusinessComparison.Parse("""
            {"schemaVersion":1,"contractId":"accept-v1","version":1,"reason":"Independent values",
             "input":{"type":"object","fields":{"amount":{"type":"array","items":{"type":"number","pic":"999V99"}},"unused":{"type":"array","items":{"type":"string"}},"acceptSequence":{"type":"array","items":{"type":"string"}}}},
             "result":{"type":"object","fields":{"amount":{"type":"number"}}},"termination":{"type":"string"},"operations":{},
             "acceptInputs":{"sequenceField":"acceptSequence","valueFields":["amount","unused"]}}
            """);
        var item = BusinessComparison.Parse("""
            {"schemaVersion":1,"caseId":"Independent","programId":"ACCEPT-CASE","contractId":"accept-v1","baselineKind":"specification","reviewReason":"Hand value","provenance":{"calculation":"Independent"},
             "input":{"amount":["10.25"],"unused":[],"acceptSequence":["amount"]},"responses":[],"expected":{"result":{"amount":"10.25"},"effects":[],"termination":"returned"}}
            """);
        var contractPath = Path.Combine(folder.Path, "Tests/BusinessContracts/accept.json");
        var casePath = Path.Combine(folder.Path, "Tests/BusinessCases/accept.json");
        Workspace.Write(contractPath, contract);
        Workspace.Write(casePath, item);
        Assert.Single(BusinessCases.Load(folder.Path));
        foreach (var mutate in new Action<JsonNode>[]
        {
            c => c["acceptInputs"]!["valueFields"] = new JsonArray("missing"),
            c => c["acceptInputs"]!["valueFields"] = new JsonArray("amount", "amount"),
            c => c["acceptInputs"]!["valueFields"] = new JsonArray("acceptSequence"),
            c => c["acceptInputs"]!["sequenceField"] = "missing",
            c => c["input"]!["fields"]!["acceptSequence"]!["items"]!["type"] = "number",
            c => c["input"]!["fields"]!["amount"]!["items"]!["type"] = "object",
            c => c["input"]!["fields"]!["amount"]!["items"]!["nullable"] = true,
        })
        {
            var changed = contract.DeepClone();
            mutate(changed);
            Workspace.Write(contractPath, changed);
            Assert.Empty(BusinessCases.Inspect(folder.Path).Cases);
            Assert.Throws<ArgumentException>(() => BusinessCases.Load(folder.Path));
        }

        Workspace.Write(contractPath, contract);
        foreach (var value in new JsonNode?[] { JsonValue.Create("bad"), JsonValue.Create("1000.00"), JsonValue.Create("10.251"), JsonValue.Create("10.25 "), null, JsonValue.Create(10.25) })
        {
            var changed = item.DeepClone();
            changed["input"]!["amount"]![0] = value;
            Workspace.Write(casePath, changed);
            Assert.Empty(BusinessCases.Inspect(folder.Path).Cases);
            Assert.Throws<ArgumentException>(() => BusinessCases.Load(folder.Path));
        }
    }

    /// <summary>回数・未知キー・不正数値と metadata を Strict/Inspect 共通で拒否します。</summary>
    [Fact]
    public void InputValidationAndConsumerRejectAmbiguousOrIncompleteSequences()
    {
        var input = BusinessComparison.Parse("""{"a":["1","2"],"b":["X "],"acceptSequence":["a","b","a"]}""").AsObject();
        var script = new BusinessAcceptScript(input, ["a", "b"]);
        Assert.Equal("1", script.Read("a").GetValue<string>());
        Assert.Equal("X ", script.Read("b").GetValue<string>());
        Assert.Equal("2", script.Read("a").GetValue<string>());
        script.VerifyComplete();
        var sameValues = BusinessComparison.Parse("""{"a":["1"],"b":["1"],"acceptSequence":["b","a"]}""").AsObject();
        Assert.Equal("accept-input-order-mismatch", Assert.Throws<InvalidOperationException>(() => new BusinessAcceptScript(sameValues, ["a", "b"]).Read("a")).Data["BusinessReason"]);
        Assert.Equal("accept-input-missing", Assert.Throws<InvalidOperationException>(() => script.Read("a")).Data["BusinessReason"]);
        Assert.Equal("accept-input-unused", Assert.Throws<InvalidOperationException>(() => new BusinessAcceptScript(input, ["a", "b"]).VerifyComplete()).Data["BusinessReason"]);
        Assert.Equal("accept-input-unmapped", Assert.Throws<InvalidOperationException>(() => new BusinessAcceptScript(input, ["a", "b"]).Read("unknown")).Data["BusinessReason"]);
        foreach (var (key, value, code) in new (string, JsonNode?, string)[]
        {
            ("a", new JsonArray("1"), "accept-input-missing"), ("a", new JsonArray("1", "2", "3"), "accept-input-unused"),
            ("a", new JsonArray("1", null), "accept-input-missing"), ("acceptSequence", new JsonArray("a", "b", "a", "unknown"), "accept-input-unmapped"),
        })
        {
            var changed = (JsonObject)input.DeepClone();
            changed[key] = value;
            Assert.Equal(code, Assert.Throws<InvalidOperationException>(() => new BusinessAcceptScript(changed, ["a", "b"])).Data["BusinessReason"]);
        }
    }
}
