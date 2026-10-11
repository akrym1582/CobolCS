using System.Numerics;
using System.Text.Json.Nodes;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>精密な境界と限定した代入逆算から、実行確認が必要な入力候補を作ります。</summary>
internal sealed class BranchCandidates
{
    private readonly JsonNode plan;
    private readonly Dictionary<string, JsonNode> symbols;
    private readonly Dictionary<string, Relation> relations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> values = new(StringComparer.Ordinal);
    private readonly List<JsonNode> accepts;
    private readonly JsonObject baseline;

    /// <summary>Initializes a new instance of the <see cref="BranchCandidates"/> class. 正式入力と状態注入を区別します。</summary>
    /// <param name="plan">生成器が検査した型情報と IR。</param>
    internal BranchCandidates(JsonNode plan)
    {
        this.plan = plan;
        symbols = plan.A("symbols").ToDictionary(s => s.S("id"), StringComparer.Ordinal);
        accepts = Validation.Walk(plan["paragraphs"]).Where(s => s.S("kind") == "acceptInput").ToList();
        baseline = new JsonObject { ["input"] = new JsonObject { ["AcceptInputs"] = new JsonArray(accepts.Select(s => (JsonNode?)JsonValue.Create(Default(symbols[s["target"]!.S("symbolId")]))).ToArray()), ["Arguments"] = new JsonObject(), ["InitialState"] = new JsonObject() }, ["responseMode"] = 0, ["responseValues"] = new JsonObject() };
        foreach (var argument in plan.A("using"))
        {
            var symbol = symbols[argument.S("symbolId")];
            baseline["input"]!["Arguments"]![symbol.S("member")] = LayoutValue(symbol);
        }

        foreach (var symbol in symbols.Values.Where(s => s.B("known") && s.I("level") != 88))
        {
            relations[symbol.S("id")] = new(symbol.S("scope") == "LINKAGE" ? "argument:" + symbol.S("id") : "state:" + symbol.S("id"), new Number(1), new Number(0));
        }
    }

    /// <summary>境界候補を先に置き、有限な組合せを決定的に列挙します。</summary>
    /// <param name="budget">候補上限。</param>
    /// <param name="seed">組合せの再現 seed。</param>
    /// <returns>重複排除前の探索候補。</returns>
    internal IEnumerable<JsonObject> Create(int budget, int seed)
    {
        Analyze(plan.A("paragraphs").SelectMany(p => p.A("statements")));
        foreach (var input in values.Keys.Concat(accepts.Select(a => "accept:" + a.S("id"))).Concat(symbols.Values.Where(s => s.B("known") && s.S("scope") == "LINKAGE").Select(s => "argument:" + s.S("id"))).Distinct().ToArray())
        {
            var id = input[(input.IndexOf(':') + 1)..];
            var symbol = input.StartsWith("accept:", StringComparison.Ordinal) ? symbols[accepts.Single(a => a.S("id") == id)["target"]!.S("symbolId")] : symbols.GetValueOrDefault(id);
            if (symbol == null)
            {
                continue;
            }

            if (symbol["pic"]!.S("category") == "numeric")
            {
                var scale = symbol["pic"]!.I("scale");
                var maximum = new Number(BigInteger.Pow(10, symbol["pic"]!.I("digits")) - 1, BigInteger.Pow(10, scale));
                foreach (var n in new[] { new Number(0), new Number(1), new Number(2), maximum, symbol["pic"]!.B("signed") ? maximum.Negate() : new Number(0), symbol["pic"]!.B("signed") ? new Number(-1) : new Number(0) })
                {
                    Add(input, n.Decimal(scale));
                }
            }
            else
            {
                Add(input, string.Empty);
                Add(input, "X");
            }
        }

        foreach (var (origin, candidates) in values)
        {
            foreach (var value in candidates)
            {
                var candidate = (JsonObject)baseline.DeepClone();
                Set(candidate, origin, value);
                candidate["inputPath"] = origin;
                yield return candidate;
            }
        }

        yield return (JsonObject)baseline.DeepClone();

        // 異なる外部文の結果を独立に変え、依頼順は実行で採取します。
        foreach (var statement in Validation.Walk(plan["paragraphs"]).Where(n => n.S("kind") == "external"))
        {
            for (var mode = 1; mode <= 3; mode++)
            {
                var candidate = (JsonObject)baseline.DeepClone();
                candidate["responseModes"] = new JsonObject { ["Dependency_" + statement.S("id")] = mode };
                yield return candidate;
            }
        }

        var random = new Random(seed);
        for (var i = 0; i < budget; i++)
        {
            var candidate = (JsonObject)baseline.DeepClone();
            foreach (var (origin, choices) in values)
            {
                Set(candidate, origin, choices[random.Next(choices.Count)]);
            }

            candidate["responseMode"] = i % 4;
            candidate["inputPath"] = "combined-leaf-candidates";
            yield return candidate;
        }
    }

    /// <summary>複数独立入力と複雑な乗除算を逆算対象から外します。</summary>
    /// <param name="left">左辺。</param>
    /// <param name="right">右辺。</param>
    /// <param name="op">加減算。</param>
    /// <returns>限定した関係。</returns>
    private static Relation Combine(Relation? left, Relation right, string op)
    {
        if (left == null || op is not ("+" or "-") || (left.Origin != null && right.Origin != null && left.Origin != right.Origin))
        {
            return new(null, new Number(0), new Number(0));
        }

        return new(left.Origin ?? right.Origin, op == "+" ? left.Factor + right.Factor : left.Factor - right.Factor, op == "+" ? left.Offset + right.Offset : left.Offset - right.Offset);
    }

    /// <summary>合成値は業務正解を意味しません。</summary>
    /// <param name="symbol">既知項目。</param>
    /// <returns>数値ゼロまたは空白。</returns>
    private static string Default(JsonNode symbol) => symbol["pic"]?.S("category") == "numeric" ? "0" : " ";

    /// <summary>文順の限定した関係を追跡します。複雑な式は解けたと扱いません。</summary>
    /// <param name="statements">文列。</param>
    private void Analyze(IEnumerable<JsonNode> statements)
    {
        foreach (var statement in statements)
        {
            switch (statement.S("kind"))
            {
                case "acceptInput":
                    relations[statement["target"]!.S("symbolId")] = new("accept:" + statement.S("id"), new Number(1), new Number(0));
                    break;
                case "move":
                    var moved = Resolve(statement["value"]!);
                    foreach (var target in statement.A("targets"))
                    {
                        relations[target.S("symbolId")] = moved;
                    }

                    break;
                case "arithmetic":
                    var computed = Resolve(statement["value"]!);
                    foreach (var target in statement.A("targets"))
                    {
                        var id = target["reference"]!.S("symbolId");
                        if (statement.B("update"))
                        {
                            computed = Combine(relations.GetValueOrDefault(id), computed, statement.S("verb") == "ADD" ? "+" : statement.S("verb") == "SUBTRACT" ? "-" : "unsupported");
                        }

                        relations[id] = target.B("rounded") ? new(null, new Number(0), new Number(0)) : computed;
                    }

                    break;
                case "external":
                    foreach (var reference in ExternalOutputs(statement))
                    {
                        relations[reference] = new("response:" + reference, new Number(1), new Number(0));
                    }

                    break;
                case "if":
                    Conditions(statement["condition"]!);
                    break;
                case "evaluate":
                    foreach (var value in statement.A("cases").SelectMany(c => c.A("values")))
                    {
                        Boundary(Resolve(statement["subject"]!), value);
                    }

                    break;
                case "loop":
                    if (statement["times"] is JsonNode times)
                    {
                        foreach (var n in new[] { "0", "1", "2" })
                        {
                            Boundary(Resolve(times), new JsonObject { ["kind"] = "literal", ["valueType"] = "numeric", ["value"] = n });
                        }
                    }

                    if (statement["until"] is JsonNode until)
                    {
                        Conditions(until);
                    }

                    break;
            }

            // 条件付き代入は候補生成のヒューリスティックです。到達判定は trace のみです。
            foreach (var key in new[] { "then", "else", "body", "other", "onError", "notError", "atEnd", "notEnd", "onOverflow", "notOverflow" })
            {
                Analyze(statement.A(key));
            }

            foreach (var choice in statement.A("cases"))
            {
                Analyze(choice.A("body"));
            }
        }
    }

    /// <summary>対応契約の出力項目を外部応答の入力経路として扱います。</summary>
    /// <param name="statement">外部文。</param>
    /// <returns>更新候補 ID。</returns>
    private IEnumerable<string> ExternalOutputs(JsonNode statement)
    {
        var dependency = plan.A("dependencies").Single(d => d.S("id") == statement.S("dependencyId"));
        if (dependency.S("kind") == "call")
        {
            var modes = dependency.A("modes").ToArray();
            var inputs = dependency.A("inputs").ToArray();
            for (var i = 0; i < inputs.Length; i++)
            {
                if (i < modes.Length && modes[i].GetValue<string>() == "reference")
                {
                    var root = inputs[i].S("symbolId");
                    foreach (var leaf in symbols.Values.Where(s => s.S("id") == root || Ancestors(s).Any(a => a.S("id") == root)).Where(s => s.B("known")))
                    {
                        yield return leaf.S("id");
                    }
                }
            }

            if (dependency["call"]?["returning"] is JsonNode returning)
            {
                yield return returning.S("symbolId");
            }
        }
        else if (dependency.S("kind") == "sql")
        {
            foreach (var binding in dependency["sql"]!.A("bindings").Where(b => b.S("direction") == "output"))
            {
                yield return binding["host"]!.S("symbolId");
            }
        }
        else if (dependency.S("kind") == "file")
        {
            foreach (var leaf in symbols.Values.Where(s => s.B("known") && Ancestors(s).Any(a => a.S("id") == dependency.S("recordId"))))
            {
                yield return leaf.S("id");
            }
        }
    }

    /// <summary>複合条件の葉を抽出し、全体の短絡評価は生成 C# に任せます。</summary>
    /// <param name="expression">条件。</param>
    private void Conditions(JsonNode expression)
    {
        foreach (var leaf in Validation.Walk(expression))
        {
            if (leaf.S("kind") == "binary" && leaf.S("op") is "=" or "<>" or "<" or "<=" or ">" or ">=")
            {
                Boundary(Resolve(leaf["left"]!), leaf["right"]!);
                Boundary(Resolve(leaf["right"]!), leaf["left"]!);
            }
            else if (leaf.S("kind") == "condition88" && symbols.TryGetValue(leaf.S("symbolId"), out var condition))
            {
                foreach (var range in condition.A("conditions"))
                {
                    Boundary(relations.GetValueOrDefault(condition.S("parentId")), range["low"]!);
                    Boundary(relations.GetValueOrDefault(condition.S("parentId")), range["high"]!);
                }
            }
        }
    }

    /// <summary>単一入力の加減算だけを精密に逆算します。</summary>
    /// <param name="expression">式。</param>
    /// <returns>入力・係数・定数。未知は origin が null です。</returns>
    private Relation Resolve(JsonNode expression)
    {
        if (expression.S("kind") == "reference")
        {
            return relations.GetValueOrDefault(expression.S("symbolId")) ?? new(null, new Number(0), new Number(0));
        }

        if (expression.S("kind") == "literal" && expression.S("valueType") == "numeric")
        {
            return new(null, new Number(0), Number.Parse(expression.S("value")));
        }

        if (expression.S("kind") == "figurative" && expression.S("value") == "zero")
        {
            return new(null, new Number(0), new Number(0));
        }

        if (expression.S("kind") == "binary")
        {
            return Combine(Resolve(expression["left"]!), Resolve(expression["right"]!), expression.S("op"));
        }

        return new(null, new Number(0), new Number(0));
    }

    /// <summary>PIC scale の最小単位で直前・一致・直後を追加します。</summary>
    /// <param name="relation">入力への関係。</param>
    /// <param name="literal">比較定数。</param>
    private void Boundary(Relation? relation, JsonNode literal)
    {
        if (relation?.Origin == null || relation.Factor == new Number(0) || literal.S("kind") is not ("literal" or "figurative"))
        {
            return;
        }

        var origin = relation.Origin;
        var id = origin[(origin.IndexOf(':') + 1)..];
        var symbol = origin.StartsWith("accept:", StringComparison.Ordinal) ? symbols[accepts.Single(a => a.S("id") == id)["target"]!.S("symbolId")] : symbols[id];
        if (!symbol.B("known"))
        {
            return;
        }

        if (symbol["pic"]!.S("category") != "numeric")
        {
            Add(origin, literal.S("kind") == "figurative" ? " " : literal.S("value"));
            Add(origin, " ");
            Add(origin, literal.S("value") == "X" ? "Y" : "X");
            return;
        }

        if (literal.S("valueType") != "numeric" && literal.S("kind") != "figurative")
        {
            return;
        }

        var scale = symbol["pic"]!.I("scale");
        var boundary = ((literal.S("kind") == "figurative" ? new Number(0) : Number.Parse(literal.S("value"))) - relation.Offset) / relation.Factor;
        var unit = new Number(BigInteger.One, BigInteger.Pow(10, scale));
        foreach (var n in new[] { boundary - unit, boundary, boundary + unit })
        {
            try
            {
                Add(origin, n.ExactDecimal());
            }
            catch (InvalidOperationException)
            {
                Add(origin, n.Numerator.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/" + n.Denominator.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }
    }

    /// <summary>同じ経路の値を重複なしで保持します。</summary>
    /// <param name="origin">入力経路。</param>
    /// <param name="value">文字列値。</param>
    private void Add(string origin, string value)
    {
        if (!values.TryGetValue(origin, out var candidates))
        {
            values[origin] = candidates = [];
        }

        if (!candidates.Contains(value, StringComparer.Ordinal))
        {
            candidates.Add(value);
        }
    }

    /// <summary>候補が変更する正式入力・ACCEPT・状態・応答を分離します。</summary>
    /// <param name="candidate">候補。</param>
    /// <param name="origin">経路。</param>
    /// <param name="value">PIC 検査前の値。</param>
    private void Set(JsonObject candidate, string origin, string value)
    {
        var id = origin[(origin.IndexOf(':') + 1)..];
        if (origin.StartsWith("accept:", StringComparison.Ordinal))
        {
            candidate["input"]!["AcceptInputs"]![accepts.FindIndex(a => a.S("id") == id)] = value;
            return;
        }

        if (origin.StartsWith("response:", StringComparison.Ordinal))
        {
            candidate["responseValues"]![id] = value;
            return;
        }

        var symbol = symbols[id];
        var dims = symbol.A("dimensions").ToArray();
        if (origin.StartsWith("state:", StringComparison.Ordinal))
        {
            candidate["input"]!["InitialState"]![symbol.S("member")] = dims.Length == 0 ? JsonValue.Create(value) : new JsonArray(new JsonObject { ["Subscripts"] = new JsonArray(dims.Select(_ => (JsonNode?)JsonValue.Create(1)).ToArray()), ["Value"] = value });
            candidate["stateInjection"] = true;
            return;
        }

        var root = Ancestors(symbol).FirstOrDefault(s => plan.A("using").Any(a => a.S("symbolId") == s.S("id"))) ?? symbol;
        var args = candidate["input"]!["Arguments"]!;
        var node = root.S("id") == id ? args : args[root.S("member")]!;
        node[symbol.S("member")] = dims.Length == 0 ? JsonValue.Create(value) : new JsonArray(Enumerable.Range(0, dims.Aggregate(1, (n, d) => n * d.GetValue<int>())).Select(_ => (JsonNode?)JsonValue.Create(value)).ToArray());
    }

    /// <summary>既知の固定引数だけに完全な型付き JSON を与えます。</summary>
    /// <param name="symbol">引数。</param>
    /// <returns>スカラー・固定表・固定グループの値。</returns>
    private JsonNode LayoutValue(JsonNode symbol)
    {
        if (symbol["pic"] == null)
        {
            return new JsonObject(symbols.Values.Where(s => s.B("known") && Ancestors(s).Any(a => a.S("id") == symbol.S("id"))).Select(s => KeyValuePair.Create<string, JsonNode?>(s.S("member"), LayoutValue(s))));
        }

        var count = symbol.A("dimensions").Aggregate(1L, (n, d) => Math.Min(1025, n * d.GetValue<int>()));
        if (count > 1024 || !symbol.B("known"))
        {
            throw new NotSupportedException("manual-unknown or oversized input layout: " + symbol.S("id"));
        }

        return symbol.A("dimensions").Any() ? new JsonArray(Enumerable.Range(0, (int)count).Select(_ => (JsonNode?)JsonValue.Create(Default(symbol))).ToArray()) : JsonValue.Create(Default(symbol))!;
    }

    /// <summary>祖先から項目までを元の宣言に従って返します。</summary>
    /// <param name="symbol">項目。</param>
    /// <returns>宣言順の祖先。</returns>
    private IEnumerable<JsonNode> Ancestors(JsonNode symbol) => symbols.TryGetValue(symbol.S("parentId"), out var parent) ? Ancestors(parent).Append(symbol) : [symbol];

    /// <summary>単一入力との精密な線形関係です。</summary>
    /// <param name="Origin">入力経路。</param>
    /// <param name="Factor">係数。</param>
    /// <param name="Offset">定数。</param>
    private sealed record Relation(string? Origin, Number Factor, Number Offset);
}
