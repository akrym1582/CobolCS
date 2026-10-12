using System.Text.Json.Nodes;
using CobolCS.Runtime;

namespace CobolCS.Tools;

/// <summary>同じ固定lockの全分割を照合し、全体の検証下限を維持します。</summary>
public static partial class Corpus
{
    /// <summary>ケース欠落・重複・異なる測定条件を拒否して、元lockの順に全結果を統合します。</summary>
    /// <param name="lockPath">変更しない固定lock。</param>
    /// <param name="summaries">全分割の完了レポート。</param>
    /// <returns>通常のVerifyへ渡せる全体レポート。</returns>
    public static JsonObject MergeEvaluations(string lockPath, IEnumerable<JsonNode> summaries)
    {
        var parts = summaries.ToArray();
        if (parts.Length == 0)
        {
            throw new ArgumentException("No corpus shard summaries");
        }

        var first = parts[0];
        var count = first.I("shardCount", 1);
        if (count is < 1 or > 16 || parts.Length != count || !parts.Select(p => p.I("shardIndex", -1)).Order().SequenceEqual(Enumerable.Range(0, count)))
        {
            throw new ArgumentException("Missing or duplicate corpus shard");
        }

        var locked = Workspace.Read(lockPath);
        if (locked.I("schemaVersion") != 1 || first.I("schemaVersion") != 1)
        {
            throw new ArgumentException("Unsupported corpus summary/lock schema");
        }

        var expected = locked.A("cases").Where(c => first.B("includeGpl") || (!c.B("optionalGpl") && !c.S("license").Contains("GPL", StringComparison.OrdinalIgnoreCase))).ToArray();
        var ids = new JsonArray(expected.Select(c => (JsonNode?)JsonValue.Create(c.S("id"))).ToArray());
        if (expected.Select(c => c.S("id")).Distinct(StringComparer.Ordinal).Count() != expected.Length)
        {
            throw new ArgumentException("Duplicate case ID in corpus lock");
        }

        var rows = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
        foreach (var part in parts)
        {
            if (part.S("lockSha256") != Workspace.Digest(lockPath) || part.I("shardCount") != count || part.I("expectedCases") != expected.Length
                || part.I("minimumGenerated") != locked.I("minimumGenerated") || !JsonNode.DeepEquals(part["expectedCaseIds"], ids)
                || new[] { "schemaVersion", "scope", "upstream", "includeGpl", "compilerCommit", "compilerSourceSha256", "dotnetVersion", "backend", "originalCobolComparison", "expectedExecutionIds" }.Any(key => !JsonNode.DeepEquals(part[key], first[key])))
            {
                throw new ArgumentException("Mismatched corpus shard identity or configuration");
            }

            var selected = expected.Where((_, index) => index % count == part.I("shardIndex")).ToArray();
            if (part["counts"] == null || part["counts"]!.I("selected") != selected.Length || part.A("cases").Count() != selected.Length)
            {
                throw new ArgumentException("Incomplete corpus shard");
            }

            foreach (var item in selected)
            {
                var matches = part.A("cases").Where(c => c.S("id") == item.S("id")).ToArray();
                if (matches.Length != 1 || matches[0].S("source") != item.S("source") || matches[0].S("sha256") != item.S("sha256") || !rows.TryAdd(item.S("id"), matches[0]))
                {
                    throw new ArgumentException("Missing, duplicate or mismatched corpus case");
                }
            }
        }

        var merged = (JsonObject)first.DeepClone();
        merged["shardIndex"] = 0;
        merged["shardCount"] = 1;
        merged["mergedShards"] = count;
        merged["cases"] = new JsonArray(expected.Select(c => rows[c.S("id")].DeepClone()).ToArray());
        merged["expectedExecutionIds"] = new JsonArray(expected.Where(c => c["execution"] != null).Select(c => (JsonNode?)JsonValue.Create(c.S("id"))).ToArray());
        var counts = new JsonObject { ["selected"] = expected.Length };
        foreach (var key in new[] { "analyzed", "scaffoldAnalyzed", "strictGenerated", "scaffoldGenerated", "built", "fixtureExecuted", "stubFixtureExecuted", "matchedIndependentExpectation", "matchedOriginalCobol" })
        {
            counts[key] = rows.Values.Count(r => r.B(key));
        }

        merged["counts"] = counts;
        merged["internalStubSummary"] = InternalStubs(merged);
        return merged;
    }
}
