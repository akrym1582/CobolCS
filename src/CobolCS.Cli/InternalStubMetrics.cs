using System.Text;
using System.Text.Json.Nodes;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>原文と値を保存せず、位置・安定理由・現在の実測だけで内部不足を集計します。</summary>
public static class InternalStubMetrics
{
    /// <summary>同じ固定範囲の位置別集計を統合します。外部操作は内部不足と分けます。</summary>
    /// <param name="summaries">範囲を明記した生成別集計。</param>
    /// <param name="provenance">lock・入力・コンパイラー・取得範囲。</param>
    /// <returns>展開件数、物理件数、プログラム数と実測停止件数。</returns>
    public static JsonObject Aggregate(IEnumerable<JsonNode> summaries, JsonNode provenance)
    {
        var statements = summaries.SelectMany(s => s.A("statements")).Select(s => s.DeepClone()).ToArray();
        var groups = statements.GroupBy(s => (s.S("statementKind"), s.S("reasonCode"), s.S("executionRole"), s.B("layoutUnknown"))).OrderBy(g => g.Key.Item1, StringComparer.Ordinal).ThenBy(g => g.Key.Item2, StringComparer.Ordinal).ThenBy(g => g.Key.Item3, StringComparer.Ordinal).ThenBy(g => g.Key.Item4);
        return new JsonObject
        {
            ["schemaVersion"] = 1,
            ["scope"] = provenance.DeepClone(),
            ["counting"] = "One expanded statement instance per program/statement ID; physical statements deduplicate source hash/file/start/kind within a generation or locked source case. COPY expansion instances and physical source positions are distinct. Cross-case physical files are not deduplicated. Static counts do not imply reachability.",
            ["statements"] = new JsonArray(statements),
            ["groups"] = new JsonArray(groups.Select(g => (JsonNode?)new JsonObject { ["statementKind"] = g.Key.Item1, ["reasonCode"] = g.Key.Item2, ["executionRole"] = g.Key.Item3, ["layoutUnknown"] = g.Key.Item4, ["statements"] = g.Count(), ["physicalStatements"] = g.Select(s => s.S("physicalId")).Distinct(StringComparer.Ordinal).Count(), ["programs"] = g.Select(s => s.S("programId")).Distinct(StringComparer.Ordinal).Count(), ["firstStopPrograms"] = g.Where(s => s.B("firstStop")).Select(s => s.S("programId")).Distinct(StringComparer.Ordinal).Count(), ["reachedStatements"] = g.Count(s => s.S("observation") == "reached"), ["unreachedStatements"] = g.Count(s => s.S("observation") == "unreached"), ["unexecutedStatements"] = g.Count(s => s.S("observation") == "unexecuted") }).ToArray()),
        };
    }

    /// <summary>優先順位を推測スコアで自動確定しません。</summary>
    /// <param name="summary">構造化集計。</param>
    /// <returns>件数と解釈を分離した表。</returns>
    public static string Markdown(JsonNode summary) => "# Internal stub summary\n\n" + summary.S("counting") + "\n\nPrioritize actual first stops, affected programs and clear semantics after review; no guessed ranking. Missing COPY/layout diagnostics remain failures. External responses are separate. Unacquired corpus input is not successful coverage.\n\n| Kind | Reason | Role | Unknown layout | Expanded | Physical | Programs | First stops | Reached | Unreached | Unexecuted |\n|---|---|---|---|---:|---:|---:|---:|---:|---:|---:|\n" + string.Join("\n", summary.A("groups").Select(g => $"| {g.S("statementKind")} | {g.S("reasonCode")} | {g.S("executionRole")} | {g.B("layoutUnknown")} | {g.I("statements")} | {g.I("physicalStatements")} | {g.I("programs")} | {g.I("firstStopPrograms")} | {g.I("reachedStatements")} | {g.I("unreachedStatements")} | {g.I("unexecutedStatements")} |")) + "\n";

    /// <summary>完全なエラーメッセージを集計キーにしません。</summary>
    /// <param name="statement">スタブ化した文。</param>
    /// <param name="reason">生成側の理由。</param>
    /// <returns>安定した分類コード。</returns>
    internal static string ReasonCode(JsonNode statement, string reason) => reason.StartsWith("CHAR_", StringComparison.Ordinal) ? reason : reason.StartsWith("CS_MOVE", StringComparison.Ordinal) ? "MOVE_TYPE" : "UNSUPPORTED_" + statement.S("kind").ToUpperInvariant();

    /// <summary>展開文を1件と数え、同じ COPY 原位置の物理件数も別に返します。</summary>
    /// <param name="ir">静的入力。</param>
    /// <param name="stubs">今回のスタブ。</param>
    /// <returns>原文なしの位置別記録。</returns>
    internal static JsonObject Create(JsonNode ir, JsonArray stubs)
    {
        var statements = new JsonArray();
        foreach (var stub in stubs)
        {
            var source = stub!["source"];
            statements.Add(new JsonObject
            {
                ["programId"] = stub.S("programId"),
                ["statementId"] = stub.S("statementId"),
                ["statementKind"] = stub["statement"]!.S("kind"),
                ["reasonCode"] = stub.S("reasonCode") is { Length: > 0 } code ? code : ReasonCode(stub["statement"]!, stub.S("reason")),
                ["executionRole"] = stub.S("executionRole"),
                ["layoutUnknown"] = stub.A("referencedLayouts").Any(s => s.B("stub") || s.S("layoutStatus").Contains("unknown", StringComparison.Ordinal)),
                ["file"] = source?.S("file"),
                ["start"] = source?["start"]?.DeepClone(),
                ["end"] = source?["end"]?.DeepClone(),
                ["physicalId"] = Generator.Hash(Encoding.UTF8.GetBytes(ir.A("sources").FirstOrDefault(s => s.S("path") == source?.S("file"))?.S("sha256") + source?.S("file") + source?["start"]?.ToJsonString() + stub["statement"]!.S("kind"))),
                ["observation"] = "unexecuted",
                ["firstStop"] = false,
            });
        }

        return Aggregate([new JsonObject { ["statements"] = statements }], new JsonObject { ["configHash"] = Generator.Hash(Encoding.UTF8.GetBytes(Generator.Json(ir["config"]!))), ["targetPrograms"] = ir.A("programs").Count(), ["originalCobolAgreement"] = "not-performed" });
    }

    /// <summary>現在の指紋と入力 hash に一致する記録だけを到達証跡にします。</summary>
    /// <param name="root">生成先。</param>
    /// <param name="manifest">現在の入力条件。</param>
    internal static void Update(string root, JsonNode manifest)
    {
        var ir = J.Parse(File.ReadAllText(Path.Combine(root, "reports/ir.json")));
        var stubPath = Path.Combine(root, "reports/stubs.json");
        var report = Create(ir, File.Exists(stubPath) ? J.Parse(File.ReadAllText(stubPath)).AsArray() : new JsonArray());
        var fingerprint = CompletionWorkflow.Fingerprint(root, manifest);
        var records = Generator.Inventory(root).Keys.Where(CompletionWorkflow.IsRecord).OrderDescending(StringComparer.Ordinal).Select(p => J.Parse(File.ReadAllText(Path.Combine(root, p)))).Where(r => r.S("fingerprint") == fingerprint && CompletionWorklist.InputCurrent(root, r)).ToArray();
        foreach (var statement in report.A("statements"))
        {
            var record = records.FirstOrDefault(r => r.S("programId") == statement.S("programId") || r.S("programId") == "all");
            if (record == null)
            {
                continue;
            }

            var trials = record.A("trials").Where(t => t.S("programId") == statement.S("programId")).ToArray();
            var traces = trials.SelectMany(t => t.A("internalTrace")).Concat(record.A("internalTrace")).ToArray();
            var stopped = trials.Any(t => t["failure"]?["source"]?.S("StatementId") == statement.S("statementId")) || record["failure"]?["source"]?.S("StatementId") == statement.S("statementId");
            statement["observation"] = traces.Any(t => t.S("StatementId") == statement.S("statementId")) || stopped ? "reached" : record.S("status") == "completed" || traces.Length > 0 ? "unreached" : "unexecuted";
            statement["firstStop"] = stopped;
        }

        report = Aggregate([report], new JsonObject { ["sourceHashes"] = manifest["inputHashes"]!.DeepClone(), ["configHash"] = manifest.S("configHash"), ["executionFingerprint"] = fingerprint, ["compilerAssemblyHashes"] = new JsonObject(new[] { typeof(Generator).Assembly, typeof(CobolCS.Frontend.FrontendAnalyzer).Assembly, typeof(Number).Assembly }.Where(a => a.Location.Length > 0 && File.Exists(a.Location)).Select(a => KeyValuePair.Create<string, JsonNode?>(a.GetName().Name!, JsonValue.Create(Generator.Hash(File.ReadAllBytes(a.Location)))))), ["diagnosticCounts"] = new JsonObject(ir.A("diagnostics").GroupBy(d => d.S("code"), StringComparer.Ordinal).Select(g => KeyValuePair.Create<string, JsonNode?>(g.Key, JsonValue.Create(g.Count())))), ["targetPrograms"] = manifest.A("programs").Count(), ["originalCobolAgreement"] = "not-performed" });
        CompletionWorkflow.Write(root, "reports/internal-stub-summary.json", Generator.Json(report));
        CompletionWorkflow.Write(root, "reports/internal-stub-summary.md", Markdown(report));
    }
}
