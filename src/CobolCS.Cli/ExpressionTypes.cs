using System.Text.Json.Nodes;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>検証と直接生成で共用する式の型と組み込み関数の定義です。</summary>
internal static class ExpressionTypes
{
    /// <summary>関数名から引数数、戻り型、実行対応状態への対応です。</summary>
    internal static readonly Dictionary<string, (int Min, int Max, string Type, bool Native)> Functions = new()
    {
        ["ABS"] = (1, 1, "numeric", true),
        ["LENGTH"] = (1, 1, "numeric", true),
        ["NUMVAL"] = (1, 1, "numeric", true),
        ["INTEGER"] = (1, 1, "numeric", true),
        ["INTEGER-PART"] = (1, 1, "numeric", true),
        ["MIN"] = (1, 100, "numeric", true),
        ["MAX"] = (1, 100, "numeric", true),
        ["MOD"] = (2, 2, "numeric", true),
        ["REM"] = (2, 2, "numeric", true),
        ["TRIM"] = (1, 1, "text", true),
        ["UPPER-CASE"] = (1, 1, "text", true),
        ["LOWER-CASE"] = (1, 1, "text", true),
        ["REVERSE"] = (1, 1, "text", true),
        ["CURRENT-DATE"] = (0, 0, "text", false),
        ["INTEGER-OF-DATE"] = (1, 1, "numeric", false),
        ["DATE-OF-INTEGER"] = (1, 1, "numeric", false),
        ["INTEGER-OF-DAY"] = (1, 1, "numeric", false),
        ["DAY-OF-INTEGER"] = (1, 1, "numeric", false),
        ["NUMVAL-C"] = (1, 2, "numeric", false),
        ["SUM"] = (1, 100, "numeric", false),
    };

    /// <summary>未知を文字列とみなさず、解析済みの式からカテゴリを求めます。</summary>
    /// <param name="expression">型を求める式。</param>
    /// <param name="symbols">解決済みの宣言。</param>
    /// <returns>numeric、text、boolean、unknown のいずれか。</returns>
    internal static string TypeOf(JsonNode expression, IReadOnlyDictionary<string, JsonNode> symbols)
    {
        string Type(JsonNode? node) => node == null ? "unknown" : TypeOf(node, symbols);
        return expression.S("kind") switch
        {
            "literal" => expression.S("valueType") is "numeric" or "boolean" ? expression.S("valueType") : "text",
            "reference" => symbols.TryGetValue(expression.S("symbolId"), out var symbol) && !symbol.B("stub") && symbol["pic"] != null ? symbol["pic"]!.S("category") == "numeric" ? "numeric" : "text" : "unknown",
            "figurative" => expression.S("value") == "zero" ? "numeric" : "text",
            "unary" => expression.S("op") == "NOT" ? "boolean" : Type(expression["operand"]) == "unknown" ? "unknown" : "numeric",
            "binary" => Type(expression["left"]) == "unknown" || Type(expression["right"]) == "unknown" ? "unknown" : expression.S("op") is "+" or "-" or "*" or "/" or "**" ? "numeric" : "boolean",
            "function" => Functions.TryGetValue(expression.S("name"), out var definition) ? definition.Type : "unknown",
            "condition88" or "signCondition" or "classCondition" => "boolean",
            _ => "unknown",
        };
    }
}
