using System.Text.Json.Nodes;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>検査と生成で共用する、固定 ASCII 文字処理の文全体の対応判定です。</summary>
internal static class CharacterCapabilities
{
    /// <summary>対象・句・配置を一括検査し、部分実行を防ぎます。</summary>
    /// <param name="statement">文字処理文。</param>
    /// <param name="symbols">同じプログラムの宣言。</param>
    /// <returns>対応する場合は空文字列、それ以外は安定した理由コード。</returns>
    internal static string Reason(JsonNode statement, IReadOnlyDictionary<string, JsonNode> symbols)
    {
        IEnumerable<JsonNode> Ancestors(JsonNode s) => symbols.TryGetValue(s.S("parentId"), out var p) ? Ancestors(p).Append(s) : [s];
        bool Reference(JsonNode? n, bool numeric = false)
        {
            if (n == null || n.S("kind") != "reference" || n["modification"] != null || !symbols.TryGetValue(n.S("symbolId"), out var s))
            {
                return false;
            }

            var ancestors = Ancestors(s).ToArray();
            var pic = s["pic"];
            return pic != null && pic.I("digits") is > 0 and <= 1000 && pic.I("scale") is >= 0 and <= 1000
                && (numeric ? pic.S("category") == "numeric" : pic.S("category") is "text" or "alphabetic")
                && !symbols.Values.Any(a => a.I("level") == 66)
                && ancestors.All(a => !a.S("layoutStatus").Contains("unknown", StringComparison.Ordinal) && !a.B("stub") && a["redefines"] == null && a["dependingOn"] == null && a["occursClause"]?["dependingOn"] == null && !a.B("synchronized") && !a.B("blankWhenZero") && a.S("usage") is not ("COMP-1" or "COMP-2") && (a["signClause"] == null || (a["signClause"]!.S("position") == "leading" && a["signClause"]!.B("separate"))))
                && !symbols.Values.Any(a => a["redefines"] != null && Ancestors(a).First().S("id") == ancestors[0].S("id"))
                && ancestors.Count(a => a.I("occurs", 1) > 1 || a["occursClause"] != null) == n.A("subscripts").Count()
                && ancestors.Aggregate(1L, (v, a) => Math.Min(1000001, v * a.I("occurs", 1))) <= 1000000;
        }

        bool Text(JsonNode? n) => n != null && (n.S("kind") == "literal" ? n.S("valueType") == "text" && n.S("value").All(c => c <= 127) : Reference(n));
        int Length(JsonNode? n) => n?.S("kind") == "literal" ? n.S("value").Length : n != null && symbols.TryGetValue(n.S("symbolId"), out var s) ? s["pic"]?.I("digits") ?? 0 : 0;
        bool Alias(JsonNode? a, JsonNode? b) => a?.S("kind") == "reference" && b?.S("kind") == "reference" && a.S("symbolId") == b.S("symbolId");
        if (statement.S("kind") == "inspectOperations")
        {
            var operations = statement.A("operations").ToArray();
            if (operations.Length != 1 || operations.Any(o => o["before"] != null || o["after"] != null || o.S("verb") is not ("tally" or "replace")))
            {
                return "CHAR_INSPECT_CLAUSES";
            }

            var o = operations[0];
            var subject = statement["value"];
            if (!Reference(subject) || (o.S("verb") == "tally" && !Reference(o["target"], true)))
            {
                return "CHAR_LAYOUT";
            }

            if (o.S("mode") is not ("characters" or "all" or "leading" or "first") || (o.S("verb") == "tally" && o.S("mode") == "first"))
            {
                return "CHAR_INSPECT_MODE";
            }

            if (o.S("mode") != "characters" && (!Text(o["pattern"]) || Length(o["pattern"]) == 0))
            {
                return "CHAR_PATTERN";
            }

            if (o.S("verb") == "replace" && (!Text(o["replacement"]) || Length(o["replacement"]) != (o.S("mode") == "characters" ? 1 : Length(o["pattern"]))))
            {
                return "CHAR_REPLACEMENT_LENGTH";
            }

            return Alias(subject, o["pattern"]) || Alias(subject, o["replacement"]) ? "CHAR_ALIAS" : string.Empty;
        }

        var target = statement["target"];
        if (!Reference(target) || symbols[target!.S("symbolId")].B("justified"))
        {
            return "CHAR_LAYOUT";
        }

        var pointer = statement["pointer"];
        if (pointer != null && (!Reference(pointer, true) || symbols[pointer.S("symbolId")]["pic"]!.I("scale") != 0 || symbols[pointer.S("symbolId")]["pic"]!.S("raw").Contains('P') || symbols[pointer.S("symbolId")]["pic"]!.I("digits") < (Length(target) + 1).ToString().Length))
        {
            return "CHAR_POINTER";
        }

        foreach (var sending in statement.A("sendings"))
        {
            if (!Text(sending["value"]) || (sending["delimiter"] != null && (!Text(sending["delimiter"]) || Length(sending["delimiter"]) == 0)))
            {
                return "CHAR_OPERAND";
            }

            if (Alias(target, sending["value"]) || Alias(target, sending["delimiter"]))
            {
                return "CHAR_ALIAS";
            }
        }

        return statement.A("sendings").Any() ? string.Empty : "CHAR_OPERAND";
    }
}
