using System.Text.Json.Nodes;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>相互排他的な主理由と重複を許す理由件数を区別します。</summary>
public static partial class InternalStubMetrics
{
    /// <summary>旧レポートの分類欠落を未知のまま保持し、候補を文単位で数えます。</summary>
    /// <param name="statements">重複排除済みの展開文。</param>
    /// <returns>原文や値を含まない MOVE 専用の測定。</returns>
    private static JsonObject MoveSummary(JsonNode[] statements)
    {
        var moves = statements.Where(s => s.S("statementKind") == "move").ToArray();
        string Primary(JsonNode s) => s["moveClassification"]?.S("primaryReason") is { Length: > 0 } p ? p : "classification-unavailable";
        JsonObject Counts(IEnumerable<string> keys) => new(keys.GroupBy(k => k, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => KeyValuePair.Create<string, JsonNode?>(g.Key, JsonValue.Create(g.Count()))));
        var candidates = moves.Where(s => s["moveClassification"]?.S("candidate") == "numeric-display-to-pic-x").ToArray();
        JsonArray Rows(IEnumerable<IGrouping<string, JsonNode>> groups) => new(groups.OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => (JsonNode?)new JsonObject { ["condition"] = g.Key, ["statements"] = g.Count(), ["programs"] = g.Select(s => (s.S("caseId"), s.S("programId"))).Distinct().Count() }).ToArray());
        var reasons = moves.SelectMany(s => Primary(s) == "classification-unavailable" ? new[] { (Reason: "classification-unavailable", Statement: s) } : s["moveClassification"]!.A("reasons").Select(r => (Reason: r.GetValue<string>(), Statement: s)).DistinctBy(p => p.Reason)).ToArray();
        return new JsonObject
        {
            ["statements"] = moves.Length,
            ["classifiedStatements"] = moves.Count(s => Primary(s) != "classification-unavailable"),
            ["classificationUnavailable"] = moves.Count(s => Primary(s) == "classification-unavailable"),
            ["primaryReasons"] = Counts(moves.Select(Primary)),
            ["primaryReasonGroups"] = Rows(moves.GroupBy(Primary, StringComparer.Ordinal)),
            ["reasons"] = Counts(reasons.Select(p => p.Reason)),
            ["reasonGroups"] = Rows(reasons.GroupBy(p => p.Reason, p => p.Statement, StringComparer.Ordinal)),
            ["reasonCombinations"] = Counts(moves.Select(s => Primary(s) == "classification-unavailable" ? "classification-unavailable" : string.Join(" + ", s["moveClassification"]!.A("reasons").Select(r => r.GetValue<string>())))),
            ["candidateStatements"] = candidates.Length,
            ["candidatePrograms"] = candidates.Select(s => (s.S("caseId"), s.S("programId"))).Distinct().Count(),
            ["candidateStatus"] = "requires-semantic-review",
            ["signedScaleGroups"] = Rows(candidates.GroupBy(s => "signed=" + s["moveClassification"]!["candidateDetails"]!["sending"]!.B("signed") + ",scale=" + s["moveClassification"]!["candidateDetails"]!["sending"]!.I("scale"), StringComparer.Ordinal)),
            ["receiverLengthGroups"] = Rows(candidates.SelectMany(s => s["moveClassification"]!["candidateDetails"]!.A("receivers").Select(r => r.I("length")).Distinct().Select(length => (Length: length, Statement: s))).GroupBy(x => x.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), x => x.Statement, StringComparer.Ordinal)),
            ["candidates"] = new JsonArray(candidates.Select(s => (JsonNode?)new JsonObject { ["caseId"] = s["caseId"]?.DeepClone(), ["programId"] = s.S("programId"), ["statementId"] = s.S("statementId"), ["details"] = s["moveClassification"]!["candidateDetails"]?.DeepClone() }).ToArray()),
        };
    }

    /// <summary>主理由・重複理由・型条件を別表にし、旧形式の欠落も表示します。</summary>
    /// <param name="summary">集計済みのレポート。</param>
    /// <returns>MOVE 専用の Markdown。</returns>
    private static string MoveMarkdown(JsonNode summary)
    {
        var move = summary["moveSummary"] ?? MoveSummary(summary.A("statements").ToArray());
        string Table(string key, string label) => "\n\n| " + label + " | Statements |\n|---|---:|\n" + string.Join("\n", move[key]!.AsObject().Select(p => $"| {p.Key} | {p.Value} |"));
        string Groups(string key, string label) => "\n\n| " + label + " | Statements | Programs |\n|---|---:|---:|\n" + string.Join("\n", move.A(key).Select(g => $"| {g.S("condition")} | {g.I("statements")} | {g.I("programs")} |"));
        return "\n## MOVE classification\n\nExpanded MOVE statements: " + move.I("statements") + "; classification-unavailable: " + move.I("classificationUnavailable") + ". Primary reasons are exclusive; reasons overlap.\n\nCandidates: " + move.I("candidateStatements") + " statements / " + move.I("candidatePrograms") + " programs (requires-semantic-review). No native capability change."
            + Groups("primaryReasonGroups", "Primary reason") + Groups("reasonGroups", "Reason (overlapping)") + Table("reasonCombinations", "Reason combination") + Groups("signedScaleGroups", "Sending signed/scale") + Groups("receiverLengthGroups", "Receiver length (overlapping)") + "\n";
    }
}
