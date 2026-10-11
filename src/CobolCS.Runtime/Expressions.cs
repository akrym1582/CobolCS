using System.Text.Json.Nodes;

namespace CobolCS.Runtime;

public sealed partial class Machine
{
    public static Number Numeric(object value) => value is Number n ? n : Number.Parse(Text(value));
    public static int Integer(object value)
    {
        var n = Numeric(value);
        if (n.Denominator != 1) throw new InvalidOperationException("Expected an integer");
        return checked((int)n.Numerator);
    }
    public static string Text(object value) => value switch { string s => s, Number n => n.ExactDecimal(), bool b => b ? "true" : "false", _ => throw new InvalidOperationException("Invalid value") };
    public static bool Truth(object value) => value is bool b ? b : throw new InvalidOperationException("Expected a condition");
    private static int Compare(object a, object b) => a is Number || b is Number ? Numeric(a).CompareTo(Numeric(b)) : string.CompareOrdinal(Text(a).PadRight(Math.Max(Text(a).Length, Text(b).Length)), Text(b).PadRight(Math.Max(Text(a).Length, Text(b).Length)));
    public object Eval(JsonNode e)
    {
        switch (e.S("kind"))
        {
            case "literal": return e.S("valueType") switch { "numeric" => Number.Parse(e.S("value")), "boolean" => e.S("value").Equals("true", StringComparison.OrdinalIgnoreCase), _ => e.S("value") };
            case "figurative": return e.S("value") switch { "zero" => new Number(0), "space" => " ", "quote" => "\"", "low" => "\0", "high" => "\u007f", _ => throw new NotSupportedException("Figurative") };
            case "reference": return storage.Get(e, Eval);
            case "unary": return e.S("op") == "NOT" ? !Truth(Eval(e["operand"]!)) : Numeric(Eval(e["operand"]!)).Negate();
            case "binary":
                var op = e.S("op"); var left = Eval(e["left"]!);
                if (op == "AND") return Truth(left) && Truth(Eval(e["right"]!));
                if (op == "OR") return Truth(left) || Truth(Eval(e["right"]!));
                var right = Eval(e["right"]!);
                return op switch
                {
                    "+" => Numeric(left) + Numeric(right), "-" => Numeric(left) - Numeric(right), "*" => Numeric(left) * Numeric(right), "/" => Numeric(left) / Numeric(right),
                    "**" => Pow(Numeric(left), Integer(right)), "=" => Compare(left, right) == 0, "<>" => Compare(left, right) != 0,
                    "<" => Compare(left, right) < 0, ">" => Compare(left, right) > 0, "<=" => Compare(left, right) <= 0, ">=" => Compare(left, right) >= 0,
                    _ => throw new NotSupportedException("Operator " + op)
                };
            case "condition88":
                var s = storage.Symbols[e.S("symbolId")];
                var reference = new JsonObject { ["symbolId"] = s.S("parentId"), ["subscripts"] = e["subscripts"]!.DeepClone() };
                var value = storage.Get(reference, Eval);
                return s.A("conditions").Any(c => Compare(value, Eval(c["low"]!)) >= 0 && Compare(value, Eval(c["high"]!)) <= 0);
            case "signCondition":
                var sign = Numeric(Eval(e["operand"]!)).Numerator.Sign;
                var matched = e.S("sign") switch { "positive" => sign > 0, "negative" => sign < 0, _ => sign == 0 };
                return e.B("negated") ? !matched : matched;
            case "classCondition":
                var text = Text(Eval(e["operand"]!));
                var match = text.Length > 0 && e.S("category") switch
                {
                    "numeric" => text.All(c => c is >= '0' and <= '9'),
                    "alphabetic" => text.All(c => c == ' ' || c is >= 'A' and <= 'Z' || c is >= 'a' and <= 'z'),
                    "upper" => text.All(c => c == ' ' || c is >= 'A' and <= 'Z'),
                    "lower" => text.All(c => c == ' ' || c is >= 'a' and <= 'z'), _ => false
                };
                return e.B("negated") ? !match : match;
            case "function": return Function(e.S("name"), e.A("arguments").Select(Eval).ToArray());
            default: throw new NotSupportedException("Expression " + e.S("kind"));
        }
    }
    private static Number Pow(Number value, int exponent)
    {
        if (Math.Abs((long)exponent) > 1000) throw new InvalidOperationException("Exponent limit");
        return value.Pow(exponent);
    }
    private static object Function(string name, object[] args) => name switch
    {
        "ABS" => Numeric(args[0]).Abs(), "LENGTH" => new Number(Text(args[0]).Length),
        "UPPER-CASE" => Text(args[0]).ToUpperInvariant(), "LOWER-CASE" => Text(args[0]).ToLowerInvariant(),
        "TRIM" => Text(args[0]).Trim(), "REVERSE" => new string(Text(args[0]).Reverse().ToArray()),
        "NUMVAL" => Number.Parse(Text(args[0])), "MAX" => args.Select(Numeric).Max(), "MIN" => args.Select(Numeric).Min(),
        "INTEGER-PART" => new Number(Numeric(args[0]).Units(0), 1),
        "INTEGER" => Floor(Numeric(args[0])), "MOD" => Numeric(args[0]) - Floor(Numeric(args[0]) / Numeric(args[1])) * Numeric(args[1]),
        "REM" => Numeric(args[0]) - new Number((Numeric(args[0]) / Numeric(args[1])).Units(0), 1) * Numeric(args[1]),
        _ => throw new NotSupportedException("FUNCTION " + name)
    };
    private static Number Floor(Number n) => new(n.Units(0) - (n.Numerator.Sign < 0 && n.Denominator != 1 ? 1 : 0), 1);
}
