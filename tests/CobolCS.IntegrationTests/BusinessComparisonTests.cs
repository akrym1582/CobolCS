using System.Text.Json.Nodes;
using CobolCS.Cli;
using CobolCS.Runtime;
using Xunit;

namespace CobolCS.IntegrationTests;

/// <summary>業務型契約の正負を生成や観測出力に依存せず検証します。</summary>
public sealed class BusinessComparisonTests
{
    /// <summary>未知関数や間接段落の効果を内部処理と推測しません。</summary>
    [Fact]
    public void UnknownFunctionsAndIndirectBlocksRemainConservative()
    {
        Assert.Equal("unknown", StubRoles.Classify(BusinessComparison.Parse("{\"kind\":\"move\",\"value\":{\"kind\":\"function\",\"name\":\"UNKNOWN-SERVICE\"}}"), false));
        Assert.Equal("unknown", StubRoles.Classify(BusinessComparison.Parse("{\"kind\":\"controlStub\",\"perform\":{\"targetKind\":\"paragraph\"}}"), false));
        Assert.Equal("external-unsupported", StubRoles.Classify(BusinessComparison.Parse("{\"kind\":\"controlStub\",\"cases\":[{\"body\":[{\"kind\":\"external\",\"dependencyId\":\"d1\"}]}]}"), false));
    }

    /// <summary>精密小数の同値と、文字・配列・null・欠落・型の厳密性を確認します。</summary>
    [Fact]
    public void ExactTypesPreserveDecimalWhitespaceOrderAndPresence()
    {
        var type = BusinessComparison.Parse("""
            {"type":"object","fields":{"amount":{"type":"number","pic":"S9(20)V99"},"code":{"type":"string"},"values":{"type":"array","items":{"type":"number"}},"optional":{"type":"string","nullable":true}}}
            """);
        var expected = BusinessComparison.Parse("""
            {"amount":"12345678901234567890.01","code":"A ","values":["1","2"],"optional":null}
            """);
        var same = expected.DeepClone();
        same["values"]![0] = "1.0";
        BusinessComparison.Equal("Exact", type, expected, same);
        JsonNode Changed(string key, JsonNode? value)
        {
            var actual = expected.DeepClone();
            actual[key] = value;
            return actual;
        }

        foreach (var actual in new[] { Changed("amount", JsonValue.Create("12345678901234567890.02")), Changed("code", JsonValue.Create("A")), Changed("values", new JsonArray("2", "1")), Changed("values", new JsonArray("1")), Changed("optional", JsonValue.Create(string.Empty)), Changed("amount", JsonValue.Create(1)) })
        {
            var error = Assert.Throws<InvalidOperationException>(() => BusinessComparison.Equal("Exact", type, expected, actual));
            Assert.Contains("Exact /", error.Message);
            Assert.True(error.Message.Length < 1000);
        }

        var spaceType = BusinessComparison.Parse("{\"type\":\"object\",\"fields\":{\"fee (value) / key\":{\"type\":\"number\"}}}");
        var spaceExpected = BusinessComparison.Parse("{\"fee (value) / key\":\"1\"}");
        var spaceActual = BusinessComparison.Parse("{\"fee (value) / key\":1}");
        var spaceFailure = Assert.Throws<InvalidOperationException>(() => BusinessComparison.Equal("Space", spaceType, spaceExpected, spaceActual, "/result"));
        Assert.Equal("/result/fee (value) ~1 key", spaceFailure.Data["BusinessPointer"]);
        var missing = expected.DeepClone().AsObject();
        missing.Remove("optional");
        Assert.Throws<InvalidOperationException>(() => BusinessComparison.Equal("Missing", type, expected, missing));
        var unknown = expected.DeepClone();
        unknown["extra"] = "X";
        Assert.Throws<InvalidOperationException>(() => BusinessComparison.Equal("Unknown", type, expected, unknown));
        Assert.Throws<ArgumentException>(() => BusinessComparison.Parse("{\"x\":1,\"x\":2}"));
        Assert.Throws<ArgumentException>(() => BusinessComparison.ValidateType(BusinessComparison.Parse("{\"type\":\"number\",\"pic\":\"9(0)\"}")));
        Assert.Throws<ArgumentException>(() => BusinessComparison.ValidateType(BusinessComparison.Parse("{\"type\":\"double\"}")));
    }

    /// <summary>固定外部要求・順序・余剰・重複・payload破壊は業務 fake で必ず失敗します。</summary>
    [Fact]
    public void StableExternalOperationsRejectOrderPayloadAndMissingCalls()
    {
        var operations = BusinessComparison.Parse("""
            {"customer.lookup":{"request":{"type":"object","fields":{"id":{"type":"string"}}},"response":{"type":"object","fields":{"found":{"type":"boolean"}}}},"payment.write":{"request":{"type":"object","fields":{"fee":{"type":"number"}}},"response":{"type":"object","fields":{"status":{"type":"string"}}}}}
            """).AsObject();
        var responses = BusinessComparison.Parse("""
            [{"operation":"customer.lookup","request":{"id":"A "},"response":{"found":true}},{"operation":"payment.write","request":{"fee":"1.50"},"response":{"status":"ok"}}]
            """).AsArray();
        BusinessExternalScript Script() => new("External", operations, responses);
        static JsonObject Map(JsonObject value) => value;
        var script = Script();
        _ = script.Invoke("customer.lookup", new JsonObject { ["id"] = "A " }, Map, Map);
        _ = script.Invoke("payment.write", new JsonObject { ["fee"] = "1.5" }, Map, Map);
        script.VerifyComplete();
        Assert.Equal(2, script.Effects.Count());
        Assert.Throws<InvalidOperationException>(() => script.Invoke("payment.write", new JsonObject { ["fee"] = "1.5" }, Map, Map));
        var orderFailure = Assert.Throws<InvalidOperationException>(() => Script().Invoke("payment.write", new JsonObject { ["fee"] = "1.5" }, Map, Map));
        Assert.Equal("/effects", orderFailure.Data["BusinessPointer"]);
        Assert.Equal("external-response-failed", orderFailure.Data["BusinessReason"]);
        Assert.Throws<InvalidOperationException>(() => Script().Invoke("customer.lookup", new JsonObject { ["id"] = "B " }, Map, Map));
        Assert.Throws<InvalidOperationException>(() => Script().VerifyComplete());
        var payload = Script();
        _ = payload.Invoke("customer.lookup", new JsonObject { ["id"] = "A " }, Map, Map);
        Assert.Throws<InvalidOperationException>(() => payload.Invoke("payment.write", new JsonObject { ["fee"] = "1.51" }, Map, Map));
    }

    /// <summary>未設定境界・期待値・重複ケースを成功件数へ算入しません。</summary>
    [Fact]
    public void EmptyContractsAndDuplicateCaseIdsFail()
    {
        using var folder = new TestDirectory();
        Directory.CreateDirectory(Path.Combine(folder.Path, "Tests/BusinessContracts"));
        var contractPath = Path.Combine(folder.Path, "Tests/BusinessContracts/test.json");
        var contractText = """
            {"schemaVersion":1,"contractId":"test-v1","version":1,"reason":"Hand calculation","input":{"type":"object","fields":{}},"result":{"type":"object","fields":{}},"termination":{"type":"string"},"operations":{}}
            """;
        File.WriteAllText(contractPath, contractText);
        Assert.Throws<ArgumentException>(() => BusinessCases.Load(folder.Path));
        var contract = BusinessComparison.Parse(File.ReadAllText(contractPath));
        contract["result"]!["fields"]!["fee"] = new JsonObject { ["type"] = "number" };
        File.WriteAllText(contractPath, contract.ToJsonString());
        Directory.CreateDirectory(Path.Combine(folder.Path, "Tests/BusinessCases"));
        var item = """
            {"schemaVersion":1,"caseId":"Fee","programId":"TEST","contractId":"test-v1","baselineKind":"specification","reviewReason":"1.5 is independently calculated","provenance":{"calculation":"100 * .015"},"input":{},"responses":[],"expected":{"result":{"fee":"1.50"},"effects":[],"termination":"ok"}}
            """;
        File.WriteAllText(Path.Combine(folder.Path, "Tests/BusinessCases/one.json"), item);
        Assert.Single(BusinessCases.Load(folder.Path));
        var reference = BusinessComparison.Parse(item);
        reference["baselineKind"] = "cobol-reference";
        reference["provenance"] = new JsonObject { ["processor"] = null, ["version"] = string.Empty, ["method"] = string.Empty, ["conditions"] = null, ["fileHashes"] = new JsonObject { ["capture.json"] = string.Empty } };
        File.WriteAllText(Path.Combine(folder.Path, "Tests/BusinessCases/one.json"), reference.ToJsonString());
        Assert.Throws<ArgumentException>(() => BusinessCases.Load(folder.Path));
        File.WriteAllText(Path.Combine(folder.Path, "Tests/BusinessCases/one.json"), item);
        File.WriteAllText(Path.Combine(folder.Path, "Tests/BusinessCases/two.json"), item);
        Assert.Throws<ArgumentException>(() => BusinessCases.Load(folder.Path));
    }
}
