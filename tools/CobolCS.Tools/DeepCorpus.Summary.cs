using System.Text.Json.Nodes;
using CobolCS.Runtime;
using static CobolCS.Tools.Workspace;

namespace CobolCS.Tools;

/// <summary>統括用の原文を含まない測定集計を生成します。</summary>
public static partial class DeepCorpus
{
    /// <summary>欠落工程とケースを隠さず、同一版だけを集計します。</summary>
    /// <param name="lockPath">固定台帳。</param>
    /// <param name="output">工程checkpointのルート。</param>
    /// <returns>決定的な集計。</returns>
    public static JsonObject Summarize(string lockPath, string output)
    {
        var locked = Read(lockPath);
        var identity = Read(Path.Combine(output, "identity.json"));
        if (Digest(lockPath) != identity.S("lockSha256"))
        {
            throw new ArgumentException("Summary lock identity mismatch");
        }

        var rows = new JsonArray();
        foreach (var item in locked.A("cases"))
        {
            var row = new JsonObject { ["id"] = item.S("id"), ["source"] = item.S("source"), ["inputKind"] = item.S("inputKind"), ["encoding"] = item.S("encoding"), ["sourceFormatEvidence"] = item.S("sourceFormatEvidence"), ["inputSha256"] = item.S("sha256") };
            foreach (var stage in new[] { "analyze", "generate", "build" })
            {
                var path = Path.Combine(output, item.S("id"), stage + ".json");
                if (File.Exists(path))
                {
                    var result = Read(path);
                    ValidateCheckpoint(result, item, identity);
                    result.AsObject().Remove("identity");
                    row[stage] = result;
                }
                else
                {
                    row[stage] = new JsonObject { ["status"] = "not-performed", ["attempted"] = false };
                }
            }

            rows.Add(row);
        }

        var counts = new JsonObject { ["inventory"] = rows.Count, ["distinctInputHashes"] = locked.I("distinctInputHashes") };
        foreach (var stage in new[] { "analyze", "generate", "build" })
        {
            counts[stage] = new JsonObject(rows.Select(r => r![stage]!).GroupBy(r => r.S("status")).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => KeyValuePair.Create<string, JsonNode?>(g.Key, JsonValue.Create(g.Count()))));
        }

        counts["analyzed"] = rows.Count(r => r!["analyze"]!.B("analyzed"));
        counts["generated"] = rows.Count(r => r!["generate"]!.B("generated"));
        counts["built"] = rows.Count(r => r!["build"]!.B("built"));
        var summary = new JsonObject { ["schemaVersion"] = 1, ["identity"] = identity.DeepClone(), ["counts"] = counts, ["cases"] = rows };
        AtomicWrite(Path.Combine(output, "summary.json"), summary);
        return summary;
    }
}
