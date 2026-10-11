using System.Text.Json.Nodes;
using CobolCS.Runtime;

namespace CobolCS.Cli;

public static class Validation
{
    private static readonly HashSet<string> Statements = new("move display arithmetic if evaluate loop sequence perform goto gotoDepending halt continue sentenceBoundary set88 setIndex exitPerform".Split());
    private static readonly Dictionary<string, (int Min, int Max)> Functions = new()
    {
        ["ABS"] = (1,1), ["LENGTH"] = (1,1), ["UPPER-CASE"] = (1,1), ["LOWER-CASE"] = (1,1),
        ["TRIM"] = (1,1), ["REVERSE"] = (1,1), ["NUMVAL"] = (1,1), ["MAX"] = (1,100), ["MIN"] = (1,100),
        ["INTEGER"] = (1,1), ["INTEGER-PART"] = (1,1), ["MOD"] = (2,2), ["REM"] = (2,2)
    };
    public static IEnumerable<JsonNode> Walk(JsonNode? node)
    {
        if (node == null) yield break;
        if (node is JsonObject obj)
        {
            yield return node;
            foreach (var pair in obj) foreach (var child in Walk(pair.Value)) yield return child;
        }
        if (node is JsonArray array) foreach (var value in array) foreach (var child in Walk(value)) yield return child;
    }
    public static JsonArray Check(JsonNode ir)
    {
        var diagnostics = (JsonArray)ir["diagnostics"]!.DeepClone();
        void Error(string code, string message, JsonNode? source = null) => diagnostics.Add(new JsonObject
        { ["code"] = code, ["message"] = message, ["severity"] = "error", ["stage"] = "csharp", ["supported"] = false, ["source"] = source?.DeepClone() });
        var config = ir["config"]!;
        if (config.S("profile") != "portable-test" || config.S("dataEncoding") != "ascii" || config.S("collation") != "ascii" || config.S("signStorage") != "leading-separate" || config.S("arithmeticProfile") != "exact-rational")
            Error("CS_PROFILE", "C# backend supports only portable-test / ASCII / leading-separate / exact-rational");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in ir.A("programs"))
        {
            if (!ids.Add(p.S("id"))) Error("CS_DUPLICATE", "Duplicate PROGRAM-ID " + p.S("id"));
            var symbols = p.A("symbols").ToDictionary(s => s.S("id"));
            foreach (var s in symbols.Values)
            {
                if (s.B("stub") || s["redefines"] != null || s["dependingOn"] != null || s["occursClause"]?["dependingOn"] != null || s.B("synchronized") || s.B("blankWhenZero") || (s["signClause"] is JsonNode sign && (sign.S("position") != "leading" || !sign.B("separate"))))
                    Error("CS_STORAGE", "Unimplemented storage attribute: " + s.S("qualifiedName"), s["source"]);
                if (s["pic"] is JsonNode pic && (!new[] { "numeric", "text", "alphabetic", "national" }.Contains(pic.S("category")) || pic.I("digits") > 1000 || pic.I("scale") < 0 || pic.I("scale") > 1000))
                    Error("CS_PIC", "Unsupported PIC: " + pic.S("raw"), s["source"]);
                if (s.S("usage") is "COMP-1" or "COMP-2") Error("CS_FLOAT", "Floating point storage is not implemented", s["source"]);
            }
            foreach (var dependency in p.A("dependencies")) Error("CS_EXTERNAL", "Adapter required: " + dependency.S("kind") + " " + dependency.S("name"), dependency["source"]);
            foreach (var n in Walk(p["paragraphs"]).Concat(symbols.Values.SelectMany(s => Walk(s["initial"])) ))
            {
                var kind = n.S("kind");
                if (kind != "" && n["source"] != null && n["id"] != null && !Statements.Contains(kind)) Error("CS_STATEMENT", "Unsupported statement: " + kind, n["source"]);
                if (kind == "loop" && (n.A("varyingAfter").Any() || (n.B("testAfter") && n["varying"] != null))) Error("CS_LOOP", "PERFORM AFTER / TEST AFTER VARYING requires review", n["source"]);
                if (kind == "arithmetic" && n["remainder"] != null && (n.A("targets").Count() != 1 || n["value"]?.S("kind") != "binary" || n["value"]?.S("op") != "/")) Error("CS_REMAINDER", "Unsupported remainder form", n["source"]);
                if (kind == "reference")
                {
                    var s = symbols[n.S("symbolId")];
                    if (s["pic"] == null) Error("CS_GROUP", "Group assignment/reference requires byte storage: " + s.S("qualifiedName"), s["source"]);
                    if (n["modification"] != null && s["pic"]?.S("category") == "numeric") Error("CS_MODIFICATION", "Numeric reference modification is not implemented", s["source"]);
                }
                if (kind == "figurative" && n.S("value") is "high" or "low" or "quote") Error("CS_FIGURATIVE", "Unsupported contextual figurative constant " + n.S("value"));
                if (kind == "allLiteral") Error("CS_ALL", "MOVE ALL requires contextual expansion");
                if (kind == "function" && (!Functions.TryGetValue(n.S("name"), out var arity) || n.A("arguments").Count() < arity.Min || n.A("arguments").Count() > arity.Max)) Error("CS_FUNCTION", "Unsupported FUNCTION or arity: " + n.S("name"));
                if (kind == "function" && n.S("name") == "LENGTH")
                {
                    var argument = n.A("arguments").FirstOrDefault();
                    if (argument != null && (argument.S("kind") == "reference" ? symbols[argument.S("symbolId")]["pic"]?.S("category") == "numeric" : argument.S("kind") != "literal" || argument.S("valueType") is not ("text" or "national")))
                        Error("CS_LENGTH", "FUNCTION LENGTH supports text/national arguments only");
                }
                if (kind == "move")
                {
                    var sending = n["value"]!;
                    bool NumericExpression(JsonNode e) => e.S("kind") switch
                    {
                        "literal" => e.S("valueType") == "numeric", "reference" => symbols[e.S("symbolId")]["pic"]?.S("category") == "numeric",
                        "binary" or "unary" => true, "figurative" => e.S("value") == "zero", _ => false
                    };
                    foreach (var target in n.A("targets"))
                        if (NumericExpression(sending) && symbols[target.S("symbolId")]["pic"]?.S("category") != "numeric") Error("CS_MOVE", "Numeric-to-text MOVE is outside the supported subset", n["source"]);
                }
            }
            try { FlowBuilder.Build(p); }
            catch (Exception e) when (e is KeyNotFoundException or NotSupportedException) { Error("CS_CONTROL", e.Message); }
        }
        if (!ir.A("programs").Any()) Error("CS_NO_PROGRAMS", "No parsed programs");
        return diagnostics;
    }
}
