using System.Text.Json.Nodes;
using CobolCS.Cli;
using CobolCS.Runtime;
using static CobolCS.Tools.Workspace;

namespace CobolCS.Tools;

/// <summary>固定 corpus の測定範囲と内部不足を、原文・値なしで保存します。</summary>
public static partial class Corpus
{
    /// <summary>取得・生成していない入力を測定成功に含めません。</summary>
    /// <param name="summary">既存の評価台帳。</param>
    /// <returns>同じ source/config/compiler/lock 範囲の集計。</returns>
    public static JsonObject InternalStubs(JsonNode summary)
    {
        var measured = summary.A("cases").Where(r => r["internalStubs"] != null).ToArray();
        var reports = measured.Select(r =>
        {
            var report = r["internalStubs"]!.DeepClone();
            foreach (var statement in report.A("statements"))
            {
                statement["programId"] = r.S("id") + "/" + statement.S("programId");
                statement["physicalId"] = r.S("id") + "/" + statement.S("physicalId");
            }

            return report;
        });
        return InternalStubMetrics.Aggregate(reports, new JsonObject { ["lockSha256"] = summary.S("lockSha256"), ["compilerSourceSha256"] = summary.S("compilerSourceSha256"), ["compilerCommit"] = summary.S("compilerCommit"), ["backend"] = summary.S("backend"), ["expectedCases"] = summary.I("expectedCases"), ["selectedCases"] = summary.A("cases").Count(), ["measuredCases"] = measured.Length, ["unmeasuredOrUnacquired"] = summary.I("expectedCases") - measured.Length, ["sourceHashes"] = new JsonObject(summary.A("cases").Select(r => KeyValuePair.Create<string, JsonNode?>(r.S("id"), JsonValue.Create(r.S("sha256"))))), ["configHashes"] = new JsonObject(measured.Select(r => KeyValuePair.Create<string, JsonNode?>(r.S("id"), r["internalStubs"]!["scope"]?["configHash"]?.DeepClone()))), ["diagnosticCounts"] = new JsonArray(summary.A("cases").Select(r => r["diagnosticCounts"]?.DeepClone()).ToArray()), ["originalCobolAgreement"] = "not-performed" });
    }

    /// <summary>評価の構造化結果と同じ出力先へ保存します。</summary>
    /// <param name="summary">現在までの評価。</param>
    /// <param name="output">評価先。</param>
    private static void WriteInternalStubs(JsonNode summary, string output)
    {
        var report = InternalStubs(summary);
        Write(Path.Combine(output, "internal-stub-summary.json"), report);
        File.WriteAllText(Path.Combine(output, "internal-stub-summary.md"), InternalStubMetrics.Markdown(report));
    }
}
