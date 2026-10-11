using System.Numerics;
using System.Text.Json.Nodes;

namespace CobolCS.Runtime;

public sealed class SizeError(string message) : Exception(message);

public sealed class Storage
{
    public Dictionary<string, JsonNode> Symbols { get; }
    private readonly Dictionary<string, object[]> cells = [];
    public Storage(JsonNode program)
    {
        Symbols = program.A("symbols").ToDictionary(s => s.S("id"));
        foreach (var s in Symbols.Values.Where(s => s["pic"] != null && s.I("level") != 88))
        {
            var count = Dimensions(s).Aggregate(1, (a, b) => checked(a * b));
            if (count > 1000000) throw new InvalidOperationException("Table allocation limit");
            cells[s.S("id")] = Enumerable.Repeat<object>(s["pic"]!.S("category") == "numeric" ? new Number(0) : "", count).ToArray();
        }
    }
    public int[] Dimensions(JsonNode symbol)
    {
        var dimensions = new List<int>();
        JsonNode? s = symbol;
        while (s != null)
        {
            if (s.I("occurs", 1) > 1 || s["occursClause"] != null) dimensions.Insert(0, s.I("occurs", 1));
            s = s["parentId"] == null ? null : Symbols[s.S("parentId")];
        }
        return dimensions.ToArray();
    }
    private int Index(JsonNode reference, Func<JsonNode, object> eval)
    {
        var dims = Dimensions(Symbols[reference.S("symbolId")]);
        var indices = reference.A("subscripts").Select(e => Machine.Integer(eval(e))).ToArray();
        if (indices.Length != dims.Length) throw new IndexOutOfRangeException("Subscript count: " + reference.S("symbolId"));
        var index = 0;
        for (var i = 0; i < dims.Length; i++)
        {
            if (indices[i] < 1 || indices[i] > dims[i]) throw new IndexOutOfRangeException("COBOL subscript is outside 1.." + dims[i]);
            index = checked(index * dims[i] + indices[i] - 1);
        }
        return index;
    }
    public object Get(JsonNode reference, Func<JsonNode, object> eval)
    {
        var value = cells[reference.S("symbolId")][Index(reference, eval)];
        if (reference["modification"] is JsonNode m)
        {
            var text = Machine.Text(value); var start = Machine.Integer(eval(m["start"]!)); var length = Machine.Integer(eval(m["length"]!));
            if (start < 1 || length < 1 || start - 1 + length > text.Length) throw new IndexOutOfRangeException("Reference modification");
            return text.Substring(start - 1, length);
        }
        return value;
    }
    public void Set(JsonNode reference, object value, Func<JsonNode, object> eval, bool rounded = false, bool sizeError = false)
        => Prepare(reference, value, eval, rounded, sizeError)();
    public Action Prepare(JsonNode reference, object value, Func<JsonNode, object> eval, bool rounded = false, bool sizeError = false)
    {
        var id = reference.S("symbolId"); var s = Symbols[id]; var index = Index(reference, eval);
        if (reference["modification"] is JsonNode m)
        {
            var old = Machine.Text(cells[id][index]); var start = Machine.Integer(eval(m["start"]!)); var length = Machine.Integer(eval(m["length"]!));
            if (start < 1 || length < 1 || start - 1 + length > old.Length) throw new IndexOutOfRangeException("Reference modification");
            var modified = old[..(start - 1)] + Fit(Machine.Text(value), length, false) + old[(start - 1 + length)..];
            return () => cells[id][index] = modified;
        }
        var converted = Convert(s, value, rounded, sizeError);
        return () => cells[id][index] = converted;
    }
    public static string Fit(string value, int length, bool right) => value.Length >= length ? right ? value[^length..] : value[..length] : right ? value.PadLeft(length) : value.PadRight(length);
    private static object Convert(JsonNode s, object value, bool rounded, bool sizeError)
    {
        var pic = s["pic"] ?? throw new NotSupportedException("Group assignment");
        if (pic.S("category") != "numeric")
        {
            var text = Machine.Text(value);
            if (pic.S("category") == "national" && text.Any(char.IsSurrogate)) throw new NotSupportedException("PIC N supports BMP characters only");
            if (pic.S("category") != "national" && text.Any(c => c > 127)) throw new NotSupportedException("Portable PIC X/A supports ASCII only; use PIC N");
            return Fit(text, pic.I("digits"), s.B("justified"));
        }
        var units = Machine.Numeric(value).Units(pic.I("scale"), rounded);
        var limit = BigInteger.Pow(10, pic.I("digits"));
        if (sizeError && (BigInteger.Abs(units) >= limit || (!pic.B("signed") && units.Sign < 0))) throw new SizeError(s.S("qualifiedName"));
        units %= limit;
        if (!pic.B("signed")) units = BigInteger.Abs(units);
        return new Number(units, BigInteger.Pow(10, pic.I("scale")));
    }
    public void Initialize(Func<JsonNode, object> eval)
    {
        foreach (var s in Symbols.Values.Where(s => cells.ContainsKey(s.S("id"))))
        {
            var value = s["initial"] is JsonNode initial ? eval(initial) : s["pic"]!.S("category") == "numeric" ? new Number(0) : "";
            for (var i = 0; i < cells[s.S("id")].Length; i++) cells[s.S("id")][i] = Convert(s, value, false, false);
        }
    }
    public string Display(JsonNode reference, Func<JsonNode, object> eval)
    {
        var s = Symbols[reference.S("symbolId")]; var pic = s["pic"]!; var value = Get(reference, eval);
        if (value is not Number n || reference["modification"] != null) return Machine.Text(value);
        return (pic.B("signed") ? n.Numerator.Sign < 0 ? "-" : "+" : "") + BigInteger.Abs(n.Units(pic.I("scale"))).ToString().PadLeft(pic.I("digits"), '0');
    }
    public JsonObject Snapshot()
    {
        var result = new JsonObject();
        foreach (var s in Symbols.Values.Where(s => cells.ContainsKey(s.S("id")) && s.S("name") != "FILLER"))
        {
            string Format(object v) => v is Number n ? n.Decimal(s["pic"]!.I("scale")) : Machine.Text(v);
            var values = cells[s.S("id")];
            result[s.S("qualifiedName")] = Dimensions(s).Length == 0 ? JsonValue.Create(Format(values[0])) : new JsonArray(values.Select(v => (JsonNode?)JsonValue.Create(Format(v))).ToArray());
        }
        return result;
    }
}
