using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>MOVE の実行範囲を変えず、宣言と式から未対応理由と次期候補を記録します。</summary>
internal sealed partial class SourceBackend
{
    /// <summary>全受信先の理由を固定順で保持し、部分的な候補昇格を防ぎます。</summary>
    /// <param name="statement">スタブを必要とする MOVE 文。</param>
    /// <returns>原文や実データ値を含まない分類。</returns>
    private JsonObject ClassifyMove(JsonNode statement)
    {
        var reasons = new HashSet<string>(StringComparer.Ordinal);
        var sending = statement["value"];
        var targets = statement.A("targets").ToArray();
        var operands = targets.Prepend(sending).Where(n => n != null).Cast<JsonNode>().ToArray();
        var references = operands.SelectMany(Validation.Walk).Where(n => n.S("kind") == "reference").ToArray();
        foreach (var reference in references)
        {
            if (!symbols.TryGetValue(reference.S("symbolId"), out var symbol))
            {
                reasons.Add("unknown-layout");
                continue;
            }

            var ancestors = Ancestors(symbol).ToArray();
            var layout = LayoutLeaves(symbol).Concat(ancestors).DistinctBy(s => s.S("id")).ToArray();
            if (layout.Any(s => s.B("stub") || s.S("layoutStatus").Contains("unknown", StringComparison.Ordinal) || s["dependingOn"] != null || s["occursClause"]?["dependingOn"] != null) || !ArraySafe(symbol))
            {
                reasons.Add("unknown-layout");
            }

            if (RenamedRegions.Affects(symbol, symbols) || ancestors.Any(s => s["redefines"] != null) || symbols.Values.Any(s => s["redefines"] != null && Ancestors(s).First().S("id") == ancestors[0].S("id")))
            {
                reasons.Add("alias-layout");
            }

            if (symbol["pic"] == null && !symbol.B("stub"))
            {
                reasons.Add("group-storage");
            }

            if (layout.Any(s => s["pic"]?.S("category").Contains("edited", StringComparison.Ordinal) == true || s["pic"]?.S("raw").Contains('P') == true))
            {
                reasons.Add("edited-pic");
            }

            if (symbol["pic"]?.S("category") == "unknown")
            {
                reasons.Add("unknown-layout");
            }
        }

        bool Unsupported(JsonNode n) => n.S("kind") switch
        {
            "function" => !ExpressionTypes.Functions.TryGetValue(n.S("name"), out var f) || !f.Native || n.A("arguments").Count() < f.Min || n.A("arguments").Count() > f.Max || (n.S("name") == "LENGTH" && n.A("arguments").Any(a => ExpressionTypes.TypeOf(a, symbols) != "text")),
            "figurative" => n.S("value") is not ("zero" or "space"),
            "all" or "allLiteral" or "unknown" => true,
            _ => false,
        };
        if (sending == null || targets.Length == 0)
        {
            reasons.Add("unknown-layout");
        }

        if (operands.SelectMany(Validation.Walk).Any(Unsupported))
        {
            reasons.Add("unsupported-expression");
        }

        string KnownType(JsonNode n) => n.S("kind") == "reference" && (!symbols.TryGetValue(n.S("symbolId"), out var s) || !Known(s) || Ancestors(s).Any(a => a.S("layoutStatus").Contains("unknown", StringComparison.Ordinal))) ? "unknown" : ExpressionTypes.TypeOf(n, symbols);
        if (sending != null && KnownType(sending) == "numeric" && targets.Any(t => KnownType(t) == "text"))
        {
            reasons.Add("numeric-to-text");
        }

        if (reasons.Count == 0)
        {
            reasons.Add("other");
        }

        var ordered = new[] { "unknown-layout", "alias-layout", "group-storage", "edited-pic", "unsupported-expression", "numeric-to-text", "other" }.Where(reasons.Contains).ToArray();

        bool CandidateReference(JsonNode? n, bool numeric)
        {
            if (n == null || n.S("kind") != "reference" || n["modification"] != null || n.A("subscripts").Any() || !symbols.TryGetValue(n.S("symbolId"), out var s) || !Known(s) || !FixedLayout(s) || Dimensions(s).Length != 0)
            {
                return false;
            }

            var ancestors = Ancestors(s).ToArray();
            return ancestors.All(a => a["signClause"] == null && a.S("usage") is "" or "DISPLAY" && !a.B("justified"))
                && s["pic"]!.S("category") == (numeric ? "numeric" : "text")
                && Regex.IsMatch(s["pic"]!.S("raw"), numeric ? @"\AS?(?:9(?:\([0-9]+\))?)+(?:V(?:9(?:\([0-9]+\))?)+)?\z" : @"\A(?:X(?:\([0-9]+\))?)+\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        var candidate = ordered.SequenceEqual(new[] { "numeric-to-text" }) && CandidateReference(sending, true) && targets.Length > 0 && targets.All(t => CandidateReference(t, false));
        JsonObject Pic(JsonNode n, bool numeric)
        {
            var s = symbols[n.S("symbolId")];
            var p = s["pic"]!;
            var result = new JsonObject { ["pic"] = p.S("raw"), ["usage"] = s.S("usage") is "" ? "DISPLAY" : s.S("usage") };
            if (numeric)
            {
                result["digits"] = p.I("digits");
                result["scale"] = p.I("scale");
                result["signed"] = p.B("signed");
            }
            else
            {
                result["length"] = p.I("digits");
            }

            return result;
        }

        return new JsonObject
        {
            ["primaryReason"] = ordered[0],
            ["reasons"] = new JsonArray(ordered.Select(r => (JsonNode?)JsonValue.Create(r)).ToArray()),
            ["candidate"] = candidate ? "numeric-display-to-pic-x" : null,
            ["candidateStatus"] = candidate ? "requires-semantic-review" : null,
            ["candidateDetails"] = candidate ? new JsonObject { ["programId"] = program.S("id"), ["statementId"] = statement.S("id"), ["source"] = new JsonObject { ["file"] = statement["source"]?.S("file"), ["start"] = statement["source"]?["start"]?.DeepClone(), ["end"] = statement["source"]?["end"]?.DeepClone() }, ["sending"] = Pic(sending!, true), ["receivers"] = new JsonArray(targets.Select(t => (JsonNode?)Pic(t, false)).ToArray()) } : null,
        };
    }
}
