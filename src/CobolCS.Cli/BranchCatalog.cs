using System.Text.Json.Nodes;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>原文 IR と実装能力を結び、dispatcher の内部移動を除いた分母を作ります。</summary>
internal static class BranchCatalog
{
    /// <summary>未対応ブロック内の枝は計測不能として保持します。</summary>
    /// <param name="ir">解析結果。</param>
    /// <param name="files">生成 C# と対応表。</param>
    /// <returns>原文・条件・outcome・能力の一覧。</returns>
    internal static JsonArray Create(JsonNode ir, SortedDictionary<string, string> files)
    {
        var map = files.TryGetValue("reports/source-map.json", out var text) ? J.Parse(text).AsArray() : [];
        var result = new JsonArray();
        foreach (var program in ir.A("programs"))
        {
            foreach (var statement in Validation.Walk(program["paragraphs"]).Where(n => n.S("id").StartsWith('s') && n["source"] != null).DistinctBy(n => n.S("id")))
            {
                var kind = statement.S("kind");
                string[] outcomes = kind switch
                {
                    "if" => ["true", "false"],
                    "evaluate" => [.. statement.A("cases").Select((_, i) => "when-" + i), statement.A("other").Any() ? "other" : "none"],
                    "loop" => ["continue", "end", "zero", "one", "multiple"],
                    "arithmetic" when statement.B("hasSizeError") || statement.A("notError").Any() => ["normal", "error"],
                    "stringTransfer" => ["normal", "overflow"],
                    "external" => program.A("dependencies").FirstOrDefault(d => d.S("id") == statement.S("dependencyId"))?.S("kind") switch
                    {
                        "call" => ["Normal", "Exception"],
                        "file" when statement.S("operation").StartsWith("read", StringComparison.Ordinal) => ["Normal", "End", "Exception"],
                        "file" => ["Normal"],
                        "sql" => ["Normal", "End", "Exception"],
                        _ => ["unknown"],
                    },
                    _ => [],
                };
                var capability = map.FirstOrDefault(m => m!.S("programId") == program.S("id") && m!.S("statementId") == statement.S("id"))?.S("capability") ?? "unsupported";
                foreach (var outcome in outcomes)
                {
                    result.Add(new JsonObject
                    {
                        ["branchId"] = program.S("id") + ":" + statement.S("id") + ":" + kind,
                        ["programId"] = program.S("id"),
                        ["statementId"] = statement.S("id"),
                        ["role"] = kind,
                        ["outcome"] = outcome,
                        ["capability"] = capability,
                        ["measurable"] = capability != "unsupported" && files.ContainsKey($"Programs/{SourceBackend.Identifier(program.S("id"))}Program.g.cs"),
                        ["source"] = statement["source"]?.DeepClone(),
                        ["condition"] = (statement["condition"] ?? statement["until"] ?? statement["times"] ?? statement["subject"])?.DeepClone(),
                        ["conditionReference"] = "reports/ir.json#" + program.S("id") + "/" + statement.S("id"),
                        ["sourceHashes"] = new JsonObject(ir.A("sources").Select(s => KeyValuePair.Create<string, JsonNode?>(s.S("path"), JsonValue.Create(s.S("sha256"))))),
                    });
                }
            }
        }

        return result;
    }
}
