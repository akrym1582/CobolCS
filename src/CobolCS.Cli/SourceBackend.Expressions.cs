using System.Text.Json.Nodes;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>式を型付き C# として出力します。</summary>
internal sealed partial class SourceBackend
{
    /// <summary>参照式を状態プロパティへ変換します。</summary>
    /// <param name="reference">解決済み参照。</param>
    /// <param name="modification">参照変更を含めるかどうか。</param>
    /// <returns>C# の読み取り式。</returns>
    private string Reference(JsonNode reference, bool modification = true)
    {
        if (!symbols.TryGetValue(reference.S("symbolId"), out var symbol) || !Known(symbol))
        {
            throw new NotSupportedException("Unknown or unsupported storage: " + reference.S("symbolId"));
        }

        var dims = Dimensions(symbol);
        var subscripts = reference.A("subscripts").ToArray();
        if (subscripts.Length != dims.Length)
        {
            throw new NotSupportedException("Subscript count: " + symbol.S("qualifiedName"));
        }

        var result = Access(symbol, dims.Select((d, i) => $"SourceSupport.Index({Expression(subscripts[i])}, {d})").ToArray());
        if (modification && reference["modification"] is JsonNode slice)
        {
            if (symbol["pic"]!.S("category") == "numeric")
            {
                throw new NotSupportedException("Numeric reference modification");
            }

            result = $"SourceSupport.Slice({result}, CobolValue.Integer({Expression(slice["start"]!)}), {(slice["length"] is JsonNode length ? "CobolValue.Integer(" + Expression(length) + ")" : "null")})";
        }

        return result;
    }

    /// <summary>式の種類に従って通常の C# 式を生成します。</summary>
    /// <param name="expression">解析済み式。</param>
    /// <returns>型付き C# 式。</returns>
    private string Expression(JsonNode expression)
    {
        string E(JsonNode? node) => Expression(node ?? throw new NotSupportedException("Missing expression"));
        switch (expression.S("kind"))
        {
            case "literal":
                return expression.S("valueType") switch
                {
                    "numeric" => $"Number.Parse({Quote(expression.S("value"))})",
                    "boolean" => Bool(expression.S("value").Equals("true", StringComparison.OrdinalIgnoreCase)),
                    _ => Quote(expression.S("value")),
                };
            case "reference":
                return Reference(expression);
            case "figurative":
                return expression.S("value") switch
                {
                    "zero" => "new Number(0)",
                    "space" => "\" \"",
                    _ => throw new NotSupportedException("Contextual figurative constant " + expression.S("value")),
                };
            case "unary":
                return expression.S("op") == "NOT" ? $"(!CobolValue.Truth({E(expression["operand"])}))" : $"CobolValue.Numeric({E(expression["operand"])}).Negate()";
            case "binary":
                var left = E(expression["left"]);
                var right = E(expression["right"]);
                var op = expression.S("op");
                return op switch
                {
                    "+" or "-" or "*" or "/" => $"(CobolValue.Numeric({left}) {op} CobolValue.Numeric({right}))",
                    "**" => $"CobolValue.Pow(CobolValue.Numeric({left}), CobolValue.Integer({right}))",
                    "AND" or "OR" => $"(CobolValue.Truth({left}) {(op == "AND" ? "&&" : "||")} CobolValue.Truth({right}))",
                    "=" or "<>" or "<" or ">" or "<=" or ">=" => $"(CobolValue.Compare({left}, {right}) {(op == "=" ? "==" : op == "<>" ? "!=" : op)} 0)",
                    _ => throw new NotSupportedException("Operator " + op),
                };
            case "classCondition":
                return $"({(expression.B("negated") ? "!" : string.Empty)}SourceSupport.IsClass({E(expression["operand"])}, {Quote(expression.S("category"))}))";
            case "signCondition":
                var comparison = expression.S("sign") == "positive" ? ">" : expression.S("sign") == "negative" ? "<" : "==";
                return $"({(expression.B("negated") ? "!" : string.Empty)}(CobolValue.Numeric({E(expression["operand"])}).Numerator.Sign {comparison} 0))";
            case "condition88":
                var condition = symbols[expression.S("symbolId")];
                var reference = new JsonObject { ["kind"] = "reference", ["symbolId"] = condition.S("parentId"), ["subscripts"] = expression["subscripts"]?.DeepClone() ?? new JsonArray() };
                var parent = Reference(reference);
                return "(" + string.Join(" || ", condition.A("conditions").Select(c => $"(CobolValue.Compare({parent}, {E(c["low"])}) >= 0 && CobolValue.Compare({parent}, {E(c["high"])}) <= 0)")) + ")";
            case "function":
                var function = expression.S("name");
                var arguments = expression.A("arguments").ToArray();
                if (!ExpressionTypes.Functions.TryGetValue(function, out var definition) || arguments.Length < definition.Min || arguments.Length > definition.Max)
                {
                    throw new NotSupportedException("Unknown FUNCTION or arity: " + function);
                }

                var args = arguments.Select(Expression).ToArray();
                if (!definition.Native)
                {
                    return ExpressionStub(expression, definition.Type, args);
                }

                if (function == "LENGTH" && ExpressionTypes.TypeOf(arguments[0], symbols) != "text")
                {
                    throw new NotSupportedException("FUNCTION LENGTH requires text");
                }

                var a = args[0];
                var numeric = $"CobolValue.Numeric({a})";
                return function switch
                {
                    "ABS" => numeric + ".Abs()",
                    "INTEGER" => $"CobolValue.Floor({numeric})",
                    "INTEGER-PART" => $"new Number({numeric}.Units(0), 1)",
                    "LENGTH" => $"new Number(CobolValue.Text({a}).Length)",
                    "TRIM" => $"CobolValue.Text({a}).Trim()",
                    "UPPER-CASE" => $"CobolValue.Text({a}).ToUpperInvariant()",
                    "LOWER-CASE" => $"CobolValue.Text({a}).ToLowerInvariant()",
                    "REVERSE" => $"new string(CobolValue.Text({a}).Reverse().ToArray())",
                    "NUMVAL" => $"Number.Parse(CobolValue.Text({a}))",
                    "MIN" or "MAX" => $"new Number[] {{ {string.Join(", ", args.Select(arg => $"CobolValue.Numeric({arg})"))} }}.{(function == "MIN" ? "Min" : "Max")}()",
                    "MOD" or "REM" => $"CobolValue.Numeric(CobolValue.Function({Quote(function)}, [{string.Join(", ", args)}]))",
                    _ => throw new NotSupportedException("FUNCTION " + function),
                };
            default:
                throw new NotSupportedException("Expression " + expression.S("kind"));
        }
    }

    /// <summary>型と PIC を保持して代入文を生成します。</summary>
    /// <param name="reference">受信参照。</param>
    /// <param name="value">評価済み送信式。</param>
    /// <param name="rounded">丸め指定。</param>
    /// <param name="sizeError">桁あふれ通知。</param>
    /// <returns>C# 代入文。</returns>
    private string Assign(JsonNode reference, string value, bool rounded = false, bool sizeError = false)
    {
        var target = Reference(reference, modification: false);
        if (reference["modification"] is JsonNode slice)
        {
            value = $"SourceSupport.Replace({target}, CobolValue.Integer({Expression(slice["start"]!)}), {(slice["length"] is JsonNode length ? "CobolValue.Integer(" + Expression(length) + ")" : "null")}, {value})";
        }

        return target + " = " + ConvertValue(symbols[reference.S("symbolId")], value, rounded, sizeError) + ";\n";
    }
}
