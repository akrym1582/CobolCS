using System.Text;
using System.Text.Json.Nodes;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>原文に対応する計測識別子と、型付き探索・固定再生の境界を生成します。</summary>
internal sealed partial class SourceBackend
{
    /// <summary>生成行番号や dispatcher の番号に依存しない識別子です。</summary>
    /// <param name="statement">原文の文。</param>
    /// <returns>プログラム・文・役割の識別子。</returns>
    private string BranchId(JsonNode statement) => program.S("id") + ":" + statement.S("id") + ":" + statement.S("kind");

    /// <summary>入力候補の型情報と、編集済み外部スタブへ落ちない合成アダプターを作ります。</summary>
    private void BranchPreparation()
    {
        var plan = new JsonObject
        {
            ["programId"] = program.S("id"),
            ["name"] = name,
            ["symbols"] = new JsonArray(symbols.Values.Select(s =>
            {
                var copy = s.DeepClone();
                copy["member"] = Member(s);
                copy["known"] = Known(s);
                copy["dimensions"] = new JsonArray(Dimensions(s).Select(d => (JsonNode?)JsonValue.Create(d)).ToArray());
                return copy;
            }).ToArray()),
            ["dependencies"] = program["dependencies"]?.DeepClone(),
            ["using"] = program["using"]?.DeepClone(),
            ["paragraphs"] = program["paragraphs"]?.DeepClone(),
        };
        var responseOutputs = new JsonObject();
        foreach (var stub in stubs.Where(s => s!.S("programId") == program.S("id") && s!.S("capability") == "adapter-required"))
        {
            responseOutputs[stub!["statement"]!.S("id")] = new JsonArray(DependencyOutputs(stub["statement"]!, stub["dependency"]!)
                .Where(o => o.Label is not ("FileStatus" or "SqlCode" or "SqlState") && !o.Label.StartsWith("Indicator", StringComparison.Ordinal))
                .SelectMany(o => ResponseLeaves(symbols[o.Reference.S("symbolId")])).Select(s => s.S("id")).Distinct(StringComparer.Ordinal)
                .Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
        }

        plan["responseOutputs"] = responseOutputs;
        plan["responseItems"] = new JsonArray(responseOutputs.SelectMany(p => p.Value!.AsArray()).Select(n => n!.GetValue<string>()).Distinct(StringComparer.Ordinal).Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
        var plans = files.TryGetValue("reports/branch-plans.json", out var text) ? J.Parse(text).AsArray() : new JsonArray();
        plans.Add(plan);
        files["reports/branch-plans.json"] = Generator.Json(plans);
        var code = new StringBuilder(Header());
        code.AppendLine($"public static class {name}Replay\n{{\npublic static {name}Result Run(string inputJson, string scriptJson, ExecutionOptions? options = null)\n{{\nvar input = Characterization.Read<{name}Input>(inputJson);\nvar fake = new {name}ScriptedDependencies();\nforeach (var step in JsonNode.Parse(scriptJson)!.AsArray())\n{{\nvar request = step![\"request\"]!.ToJsonString();\nvar response = step[\"response\"]!.ToJsonString();\nswitch (step[\"method\"]!.GetValue<string>())\n{{");
        foreach (var signature in contracts.Where(p => p.Key != "Unsupported").SelectMany(p => p.Value))
        {
            var open = signature.IndexOf('(');
            var method = signature[..open].Split(' ')[1];
            var responseType = signature[..open].Split(' ')[0];
            code.AppendLine($"case {Quote(method)}: fake.Expect{method}(r => Characterization.Request(r) == Characterization.Normalize(request), Characterization.Read<{responseType}>(response)); break;");
        }

        code.AppendLine($"default: throw new ArgumentException(\"Unknown replay method\");\n}}\n}}\nreturn new {name}Executor().Run(input, fake, options);\n}}\n}}");
        files[$"Testing/{name}Replay.g.cs"] = code.ToString();
        var probe = new StringBuilder(Header());
        probe.AppendLine($"internal sealed class {name}BranchProbe(JsonNode plan) : I{name}ExternalDependencies\n{{\ninternal JsonNode Plan => plan;\ninternal JsonArray Script {{ get; }} = new();\nprivate readonly Dictionary<string, int> counts = new();");
        foreach (var stub in stubs.Where(s => s!.S("programId") == program.S("id") && s!.S("capability") == "adapter-required"))
        {
            var statement = stub!["statement"]!;
            var dependency = stub["dependency"]!;
            var method = stub.S("method");
            var response = name + method + "Response";
            var request = name + method + "Request";
            probe.AppendLine($"public {response} {method}({request} request)\n{{\ncounts.TryGetValue({Quote(method)}, out var count);\ncounts[{Quote(method)}] = count + 1;\nvar mode = plan[\"responseModes\"]?[{Quote(method)}]?.GetValue<int>() ?? plan[\"responseMode\"]?.GetValue<int>() ?? 0;\nvar values = plan[\"responseValues\"]?.AsObject() ?? new JsonObject();");
            var kind = dependency.S("kind");
            var isRead = kind == "file" && statement.S("operation").StartsWith("read", StringComparison.Ordinal);
            var outcome = kind == "call" ? "mode == 1 ? DependencyOutcome.Exception : DependencyOutcome.Normal" : kind == "sql" ? "mode == 1 ? DependencyOutcome.End : mode == 2 ? DependencyOutcome.Exception : DependencyOutcome.Normal" : isRead ? "mode == 1 || count >= (mode == 3 ? 2 : 1) ? DependencyOutcome.End : mode == 2 ? DependencyOutcome.Exception : DependencyOutcome.Normal" : "DependencyOutcome.Normal";
            probe.AppendLine($"var outcome = {outcome};\nvar status = {(kind == "sql" ? "outcome == DependencyOutcome.Normal ? \"0\" : outcome == DependencyOutcome.End ? \"100\" : \"-911\"" : "outcome == DependencyOutcome.Normal ? \"00\" : outcome == DependencyOutcome.End ? \"10\" : \"99\"")};\nvar response = new {response}(status, outcome)\n{{");
            var outputMap = DependencyOutputs(statement, dependency);
            foreach (var (label, reference) in outputMap)
            {
                var symbol = symbols[reference.S("symbolId")];
                string value;
                if (label == "SqlCode")
                {
                    value = "CobolValue.Numeric(status)";
                }
                else if (label == "SqlState")
                {
                    value = "outcome == DependencyOutcome.Normal ? \"00000\" : outcome == DependencyOutcome.End ? \"02000\" : \"HY000\"";
                }
                else if (label == "FileStatus")
                {
                    value = "status";
                }
                else if (label.StartsWith("Indicator", StringComparison.Ordinal))
                {
                    value = "new Number(0)";
                }
                else
                {
                    value = SyntheticValue(symbol, "values");
                }

                probe.AppendLine($"{label} = {(label is "FileStatus" or "SqlCode" or "SqlState" ? value : "outcome == DependencyOutcome.Normal ? new Optional<" + ArgumentType(symbol) + ">(" + value + ") : default")},");
            }

            probe.AppendLine($"}};\nScript.Add(new JsonObject {{ [\"method\"] = {Quote(method)}, [\"request\"] = JsonNode.Parse(Characterization.Request(request)), [\"response\"] = JsonNode.Parse(Characterization.Serialize(response)) }});\nreturn response;\n}}");
        }

        probe.AppendLine("}");
        files[$".branch-probe/{name}Probe.g.cs"] = probe.ToString();
    }

    /// <summary>既存外部契約と同じ更新可能ラベルを列挙します。</summary>
    /// <param name="statement">外部文。</param>
    /// <param name="dependency">解決済み依存契約。</param>
    /// <returns>応答ラベルと元項目。</returns>
    private IEnumerable<(string Label, JsonNode Reference)> DependencyOutputs(JsonNode statement, JsonNode dependency)
    {
        if (dependency.S("kind") == "call")
        {
            var modes = dependency.A("modes").ToArray();
            var inputs = dependency.A("inputs").ToArray();
            for (var i = 0; i < inputs.Length; i++)
            {
                if (modes[i].GetValue<string>() == "reference")
                {
                    yield return ("Argument" + i, inputs[i]);
                }
            }

            if (dependency["call"]?["returning"] is JsonNode returning)
            {
                yield return ("Returning", returning);
            }
        }
        else if (dependency.S("kind") == "sql")
        {
            var index = 0;
            foreach (var binding in dependency["sql"]!.A("bindings"))
            {
                if (binding.S("direction") == "output")
                {
                    yield return ("Host" + index, binding["host"]!);
                    if (binding["indicator"] is JsonNode indicator)
                    {
                        yield return ("Indicator" + index, indicator);
                    }
                }

                index++;
            }

            if (dependency["status"] is JsonNode code)
            {
                yield return ("SqlCode", code);
            }

            if (dependency["sql"]?["sqlstate"] is JsonNode state)
            {
                yield return ("SqlState", state);
            }
        }
        else if (dependency.S("kind") == "file")
        {
            if (statement.S("operation").StartsWith("read", StringComparison.Ordinal))
            {
                foreach (var symbol in symbols.Values.Where(s => s.I("level") != 88 && s["pic"] != null && Ancestors(s).Any(a => a.S("id") == dependency.S("recordId"))))
                {
                    yield return ("Record" + symbol.S("id"), new JsonObject { ["symbolId"] = symbol.S("id") });
                }
            }

            if (dependency["status"] is JsonNode status)
            {
                yield return ("FileStatus", status);
            }
        }
    }

    /// <summary>既知 PIC と固定グループだけに合成値を与えます。</summary>
    /// <param name="symbol">既知項目。</param>
    /// <param name="values">候補辞書。</param>
    /// <returns>型付き値構築式。</returns>
    private string SyntheticValue(JsonNode symbol, string values)
    {
        if (symbol["pic"] == null)
        {
            return $"new {ArgumentType(symbol)}({string.Join(", ", ResponseLeaves(symbol).Select(s => Member(s) + ": " + SyntheticValue(s, values)))})";
        }

        var scalar = TypeName(symbol) == "Number" ? $"CobolValue.Numeric({values}[{Quote(symbol.S("id"))}]?.GetValue<string>() ?? \"0\")" : $"{values}[{Quote(symbol.S("id"))}]?.GetValue<string>() ?? {Quote(new string(' ', Math.Min(1000, symbol["pic"]!.I("digits"))))}";
        return Dimensions(symbol).Length == 0 ? scalar : $"Enumerable.Repeat({scalar}, {Dimensions(symbol).Aggregate(1, (a, b) => a * b)}).ToArray()";
    }

    /// <summary>候補辞書・許可リスト・合成グループ構築で共有する既知の基本項目です。</summary>
    /// <param name="root">既存契約で固定レイアウトと判定された領域。</param>
    /// <returns>入れ子を含む元の宣言順の基本項目。</returns>
    private IEnumerable<JsonNode> ResponseLeaves(JsonNode root) => LayoutLeaves(root).Where(s => Known(s) && s.I("level") != 88 && s["pic"] != null);
}
