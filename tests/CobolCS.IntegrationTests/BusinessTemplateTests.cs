using System.Text.Json.Nodes;
using CobolCS.Cli;
using CobolCS.Runtime;
using CobolCS.Tools;
using Xunit;

namespace CobolCS.IntegrationTests;

/// <summary>業務雛形が全構造と不足を示し、未記入値や大きな表を成功値で補わないことを検証します。</summary>
public sealed class BusinessTemplateTests
{
    /// <summary>ネスト、固定表、大きい表、複数操作と nullable 応答の参考構造を確認します。</summary>
    [Fact]
    public void TemplatesDescribeCompleteStructuresAndPreserveEditsAndDeletion()
    {
        using var folder = new TestDirectory();
        var scalar = BusinessComparison.Parse("""{"kind":"number","csharpType":"Number","scale":2,"pic":"999V99"}""");
        var wire = new JsonObject { ["kind"] = "object", ["fields"] = new JsonArray(new JsonObject { ["key"] = "nested", ["kind"] = "object", ["fields"] = new JsonArray(new JsonObject { ["key"] = "small", ["kind"] = "array", ["count"] = 2, ["items"] = scalar.DeepClone() }, new JsonObject { ["key"] = "large", ["kind"] = "array", ["count"] = 101, ["items"] = scalar.DeepClone() }) }) };
        var selected = new JsonObject { ["key"] = "request", ["source"] = "arguments", ["symbolId"] = "formal" };
        var mapping = new JsonObject { ["programId"] = "FORMAL", ["contractId"] = "formal-v1", ["selectionReason"] = "Independent full group selection", ["inputs"] = new JsonArray(selected.DeepClone()), ["results"] = new JsonArray(selected.DeepClone()), ["operations"] = new JsonArray() };
        JsonNode Candidate(string role) => new JsonObject { ["source"] = "arguments", ["role"] = role, ["symbolId"] = "formal", ["wire"] = wire.DeepClone(), ["originalName"] = "REQUEST", ["reason"] = "Required formal group", ["location"] = new JsonObject { ["file"] = "formal.cbl", ["line"] = 3 } };
        var program = new JsonObject { ["candidates"] = new JsonArray(Candidate("formal-input"), Candidate("result"), new JsonObject { ["source"] = "arguments", ["role"] = "formal-input", ["symbolId"] = "missing", ["originalName"] = "MANDATORY" }), ["operations"] = new JsonArray() };
        var contract = new JsonObject { ["input"] = new JsonObject { ["type"] = "object", ["fields"] = new JsonObject { ["request"] = BusinessProjection.Descriptor(wire) } }, ["result"] = new JsonObject { ["type"] = "object", ["fields"] = new JsonObject { ["request"] = BusinessProjection.Descriptor(wire) } }, ["operations"] = new JsonObject() };
        foreach (var key in new[] { "lookup", "store" })
        {
            var selection = new JsonObject { ["key"] = key, ["statementId"] = key, ["request"] = new JsonObject { ["amount"] = "Amount" }, ["response"] = new JsonObject { ["status"] = "StatusCode", ["outcome"] = "Outcome", ["updated"] = "Updated" } };
            mapping["operations"]!.AsArray().Add(selection);
            program["operations"]!.AsArray().Add(new JsonObject { ["statementId"] = key, ["requestFields"] = new JsonArray(new JsonObject { ["member"] = "Amount", ["kind"] = "number", ["scale"] = 2, ["pic"] = "999V99" }), ["responseFields"] = new JsonArray(new JsonObject { ["member"] = "Updated", ["kind"] = "string", ["pic"] = "XX" }) });
            contract["operations"]![key] = BusinessComparison.Parse("""{"request":{"type":"object","fields":{"amount":{"type":"number","pic":"999V99"}}},"response":{"type":"object","fields":{"status":{"type":"string"},"outcome":{"type":"string"},"updated":{"type":"string","pic":"XX","nullable":true}}}}""");
        }

        BusinessCaseTemplates.Write(folder.Path, "test", mapping, program, contract, true);
        var template = Path.Combine(folder.Path, "Tests/Templates/Business/test/case.json.txt");
        var item = Workspace.Read(template);
        Assert.Equal(2, item["input"]!["request"]!["nested"]!["small"]!.AsArray().Count);
        Assert.All(item["input"]!["request"]!["nested"]!["small"]!.AsArray(), Assert.Null);
        Assert.Null(item["input"]!["request"]!["nested"]!["large"]);
        Assert.Null(item["expected"]!["termination"]);
        Assert.Null(item["expected"]!["result"]!["request"]!["nested"]!["large"]);
        var guidePath = Path.Combine(folder.Path, "Tests/Templates/Business/test/case-guide.md");
        var guide = File.ReadAllText(guidePath);
        Assert.Contains("count=101", guide, StringComparison.Ordinal);
        Assert.Contains("missing-formal-argument: MANDATORY", guide, StringComparison.Ordinal);
        Assert.Contains("null intentionally means no update", guide, StringComparison.Ordinal);
        Assert.Contains("/input/request/nested/small", guide, StringComparison.Ordinal);
        var examples = Workspace.Read(Path.Combine(folder.Path, "Tests/Templates/Business/test/response-examples.json.txt"));
        Assert.Equal(2, examples.A("operations").Count());
        Assert.All(examples.A("operations"), o => Assert.Null(o["response"]!["status"]));
        File.WriteAllText(template, "user bytes\n");
        File.Delete(guidePath);
        mapping["results"]![0]!["key"] = "testState";
        contract["result"]!["fields"]!.AsObject().Remove("request");
        contract["result"]!["fields"]!["testState"] = BusinessProjection.Descriptor(wire);
        BusinessCaseTemplates.Write(folder.Path, "test", mapping, program, contract, false);
        var proposal = Workspace.Read(Path.Combine(folder.Path, "reports/business-scaffolds/test-case.json"));
        Assert.Equal(2, proposal["expected"]!["result"]!["testState"]!["nested"]!["small"]!.AsArray().Count);
        Assert.Equal("user bytes\n", File.ReadAllText(template));
        Assert.False(File.Exists(guidePath));
        Assert.True(File.Exists(Path.Combine(folder.Path, "reports/business-scaffolds/test-case-guide.md")));
    }
}
