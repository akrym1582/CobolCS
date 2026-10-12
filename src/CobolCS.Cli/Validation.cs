using System.Text.Json.Nodes;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>
/// 解析できた COBOL と、C# ランタイムで実行できる COBOL の範囲を区別します。
/// 生成前に未対応の格納方式や外部依存を報告し、ビルド成功だけを対応済みと誤認させないための検査です。
/// </summary>
public static class Validation
{
    private static readonly HashSet<string> Statements = new("move display arithmetic if evaluate loop sequence perform goto gotoDepending halt continue sentenceBoundary set88 setIndex exitPerform".Split());
    private static readonly Dictionary<string, (int Min, int Max)> Functions = new()
    {
        ["ABS"] = (1, 1),
        ["LENGTH"] = (1, 1),
        ["UPPER-CASE"] = (1, 1),
        ["LOWER-CASE"] = (1, 1),
        ["TRIM"] = (1, 1),
        ["REVERSE"] = (1, 1),
        ["NUMVAL"] = (1, 1),
        ["MAX"] = (1, 100),
        ["MIN"] = (1, 100),
        ["INTEGER"] = (1, 1),
        ["INTEGER-PART"] = (1, 1),
        ["MOD"] = (2, 2),
        ["REM"] = (2, 2),
    };

    /// <summary>
    /// 入れ子の IF や式の中も調べられるよう、JSON ツリー内のオブジェクトを列挙します。
    /// 最上位の文だけを調べて、内側の未対応関数や項目参照を見落とすことを防ぎます。
    /// </summary>
    /// <param name="node">調べ始める JSON ノード。省略された子要素を表す null も受け付けます。</param>
    /// <returns>親から子への順に並ぶオブジェクト。単なる文字列や数値の値は列挙しません。</returns>
    public static IEnumerable<JsonNode> Walk(JsonNode? node)
    {
        if (node == null)
        {
            yield break;
        }

        if (node is JsonObject obj)
        {
            yield return node;
            foreach (var pair in obj)
            {
                foreach (var child in Walk(pair.Value))
                {
                    yield return child;
                }
            }
        }

        if (node is JsonArray array)
        {
            foreach (var value in array)
            {
                foreach (var child in Walk(value))
                {
                    yield return child;
                }
            }
        }
    }

    /// <summary>
    /// frontend の診断に、C# backend の対応範囲の診断を追加します。
    /// strict 生成を拒否すべき理由を集める処理であり、項目値や業務結果を実行して検証するものではありません。
    /// </summary>
    /// <param name="ir">設定、宣言、段落、外部依存を含む解析結果全体。</param>
    /// <returns>元の診断を保持した新しい配列。未対応項目には CS_ で始まる識別子と理由を付けます。</returns>
    public static JsonArray Check(JsonNode ir)
    {
        var diagnostics = (JsonArray)ir["diagnostics"]!.DeepClone();
        void Error(string code, string message, JsonNode? source = null) => diagnostics.Add(new JsonObject { ["code"] = code, ["message"] = message, ["severity"] = "error", ["stage"] = "csharp", ["supported"] = false, ["source"] = source?.DeepClone() });
        var config = ir["config"]!;
        if (config.S("profile") != "portable-test" || config.S("dataEncoding") != "ascii" || config.S("collation") != "ascii" || config.S("signStorage") != "leading-separate" || config.S("arithmeticProfile") != "exact-rational")
        {
            Error("CS_PROFILE", "C# backend supports only portable-test / ASCII / leading-separate / exact-rational");
        }

        var fixedExternal = config.S("externalAdapter") == "fixed";
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in ir.A("programs"))
        {
            if (!ids.Add(p.S("id")))
            {
                Error("CS_DUPLICATE", "Duplicate PROGRAM-ID " + p.S("id"));
            }

            var symbols = p.A("symbols").ToDictionary(s => s.S("id"));
            foreach (var s in symbols.Values)
            {
                if (s.B("stub") || s["redefines"] != null || s["dependingOn"] != null || s["occursClause"]?["dependingOn"] != null || s.B("synchronized") || s.B("blankWhenZero") || (s["signClause"] is JsonNode sign && (sign.S("position") != "leading" || !sign.B("separate"))))
                {
                    Error("CS_STORAGE", "Unimplemented storage attribute: " + s.S("qualifiedName"), s["source"]);
                }

                if (s["pic"] is JsonNode pic && (!new[]
                {
                    "numeric",
                    "text",
                    "alphabetic",
                    "national",
                }.Contains(pic.S("category")) || pic.I("digits") > 1000 || pic.I("scale") < 0 || pic.I("scale") > 1000))
                {
                    Error("CS_PIC", "Unsupported PIC: " + pic.S("raw"), s["source"]);
                }

                if (s.S("usage") is "COMP-1" or "COMP-2")
                {
                    Error("CS_FLOAT", "Floating point storage is not implemented", s["source"]);
                }
            }

            foreach (var dependency in p.A("dependencies"))
            {
                if (!fixedExternal || dependency.S("kind") is not ("call" or "file" or "sql"))
                {
                    Error("CS_EXTERNAL", "Adapter required: " + dependency.S("kind") + " " + dependency.S("name"), dependency["source"]);
                }
                else
                {
                    diagnostics.Add(new JsonObject { ["code"] = "CS_EXTERNAL_FIXED", ["message"] = "Explicit ordered fixed responses required; real service unverified", ["severity"] = "review", ["source"] = dependency["source"]?.DeepClone() });
                }
            }

            foreach (var n in Walk(p["paragraphs"]).Concat(p.A("dependencies").SelectMany(d => Walk(d))).Concat(symbols.Values.SelectMany(s => Walk(s["initial"]))))
            {
                var kind = n.S("kind");
                if (kind != string.Empty && n["source"] != null && n.S("id").StartsWith('s') && !Statements.Contains(kind) && !(fixedExternal && kind == "external"))
                {
                    Error("CS_STATEMENT", "Unsupported statement: " + kind, n["source"]);
                }

                if (fixedExternal && kind == "external" && n["transfer"] != null)
                {
                    var dependency = p.A("dependencies").Single(d => d.S("id") == n.S("dependencyId"));
                    if (dependency.S("kind") == "file")
                    {
                        var recordPic = symbols[dependency.S("recordId")]["pic"];
                        var transfer = n["transfer"]!;
                        var transferPic = transfer.S("kind") == "reference" ? symbols[transfer.S("symbolId")]["pic"] : null;
                        var transferNumeric = transfer.S("kind") is "binary" or "unary" || (transfer.S("kind") == "literal" && transfer.S("valueType") == "numeric") || transferPic?.S("category") == "numeric";
                        if (recordPic == null || (transfer.S("kind") == "reference" && transferPic == null) || (recordPic.S("category") == "numeric" != transferNumeric))
                        {
                            Error("CS_EXTERNAL_TRANSFER", "Group or cross-category FILE transfer requires physical/contextual semantics; not inferred by the logical adapter", n["source"]);
                        }
                    }
                }

                if (kind == "loop" && (n.A("varyingAfter").Any() || (n.B("testAfter") && n["varying"] != null)))
                {
                    Error("CS_LOOP", "PERFORM AFTER / TEST AFTER VARYING requires review", n["source"]);
                }

                if (kind == "arithmetic" && n["remainder"] != null && (n.A("targets").Count() != 1 || n["value"]?.S("kind") != "binary" || n["value"]?.S("op") != "/"))
                {
                    Error("CS_REMAINDER", "Unsupported remainder form", n["source"]);
                }

                if (kind == "reference")
                {
                    var s = symbols[n.S("symbolId")];
                    if (s["pic"] == null && !(fixedExternal && p.A("dependencies").Any(d => Walk(d).Any(r => ReferenceEquals(r, n)))))
                    {
                        Error("CS_GROUP", "Group assignment/reference requires byte storage: " + s.S("qualifiedName"), s["source"]);
                    }

                    if (n["modification"] != null && s["pic"]?.S("category") == "numeric")
                    {
                        Error("CS_MODIFICATION", "Numeric reference modification is not implemented", s["source"]);
                    }
                }

                if (kind == "figurative" && n.S("value") is "high" or "low" or "quote")
                {
                    Error("CS_FIGURATIVE", "Unsupported contextual figurative constant " + n.S("value"));
                }

                if (kind == "allLiteral")
                {
                    Error("CS_ALL", "MOVE ALL requires contextual expansion");
                }

                if (kind == "function" && (!Functions.TryGetValue(n.S("name"), out var arity) || n.A("arguments").Count() < arity.Min || n.A("arguments").Count() > arity.Max))
                {
                    Error("CS_FUNCTION", "Unsupported FUNCTION or arity: " + n.S("name"));
                }

                if (kind == "function" && n.S("name") == "LENGTH")
                {
                    var argument = n.A("arguments").FirstOrDefault();
                    if (argument != null && (argument.S("kind") == "reference" ? symbols[argument.S("symbolId")]["pic"]?.S("category") == "numeric" : argument.S("kind") != "literal" || argument.S("valueType") is not ("text" or "national")))
                    {
                        Error("CS_LENGTH", "FUNCTION LENGTH supports text/national arguments only");
                    }
                }

                if (kind == "move")
                {
                    var sending = n["value"]!;
                    bool NumericExpression(JsonNode e) => e.S("kind") switch
                    {
                        "literal" => e.S("valueType") == "numeric",
                        "reference" => symbols[e.S("symbolId")]["pic"]?.S("category") == "numeric",
                        "binary" or "unary" => true,
                        "figurative" => e.S("value") == "zero",
                        _ => false,
                    };
                    foreach (var target in n.A("targets"))
                    {
                        if (NumericExpression(sending) && symbols[target.S("symbolId")]["pic"]?.S("category") != "numeric")
                        {
                            Error("CS_MOVE", "Numeric-to-text MOVE is outside the supported subset", n["source"]);
                        }
                    }
                }
            }

            try
            {
                FlowBuilder.Build(p);
            }
            catch (Exception e) when (e is KeyNotFoundException or NotSupportedException)
            {
                Error("CS_CONTROL", e.Message);
            }
        }

        if (!ir.A("programs").Any())
        {
            Error("CS_NO_PROGRAMS", "No parsed programs");
        }

        return diagnostics;
    }
}
