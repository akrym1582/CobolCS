using System.Security;
using System.Text;
using System.Text.Json.Nodes;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>文ごとに差し替え可能な外部契約と編集領域を生成します。</summary>
internal sealed partial class SourceBackend
{
    /// <summary>文の原文情報を文字列で示します。</summary>
    /// <param name="statement">対象文。</param>
    /// <returns>プログラム・文 ID・原文位置。</returns>
    private string Location(JsonNode statement) => $"{program.S("id")} {statement.S("id")} {statement["source"]?.S("file")}:{statement["source"]?["start"]?.I("line")}";

    /// <summary>依頼に含める不変の原文メタデータを作ります。</summary>
    /// <param name="statement">対象文。</param>
    /// <param name="dependency">外部依存。</param>
    /// <returns>C# メタデータ構築式。</returns>
    private string Metadata(JsonNode statement, JsonNode? dependency = null) => $"new SourceLocation({Quote(program.S("id"))}, {Quote(statement.S("id"))}, {Quote(dependency?.S("id") ?? string.Empty)}, {Quote(statement["source"]?.S("file") ?? string.Empty)}, {statement["source"]?["start"]?.I("line") ?? 0}, {Quote(statement["source"]?.S("raw") ?? string.Empty)}, {Quote(statement["operation"] == null ? statement.S("kind") : statement.S("operation"))}, [{string.Join(", ", (dependency?.A("modes") ?? []).Select(m => Quote(m.GetValue<string>())))}], {(statement.S("sentenceId") == string.Empty ? "null" : Quote("$sentence_" + statement.S("sentenceId")))})";

    /// <summary>独立した編集ファイルに一つの実装箇所を保存します。</summary>
    /// <param name="statement">元の文。</param>
    /// <param name="method">安定したメソッド名。</param>
    /// <param name="category">契約分類。</param>
    /// <param name="resultType">戻り型。</param>
    /// <param name="parameters">型付き引数。</param>
    /// <param name="reason">未実装理由。</param>
    /// <param name="dependency">既知の依存情報。</param>
    private void AddStub(JsonNode statement, string method, string category, string resultType, (string Type, string Name)[] parameters, string reason, JsonNode? dependency = null)
    {
        if (!emittedStubs.Add(method))
        {
            return;
        }

        var signature = $"{resultType} {method}({string.Join(", ", parameters.Select(p => p.Type + " " + p.Name))})";
        contracts[category].Add(signature + ";");
        var text = new StringBuilder("using CobolCS.Runtime;\n\nnamespace CobolCS.Generated;\n\n");
        text.AppendLine($"/// <summary>{SecurityElement.Escape(program.S("id"))} の編集可能な依存実装です。</summary>\ninternal sealed partial class {name}Dependencies\n{{");
        text.AppendLine($"    /// <summary>{SecurityElement.Escape(reason)}。原文: {SecurityElement.Escape(Location(statement))}。</summary>");
        foreach (var parameter in parameters)
        {
            text.AppendLine($"    /// <param name=\"{parameter.Name}\">{parameter.Name} に対応する COBOL の入力または原文情報。</param>");
        }

        if (resultType != "void")
        {
            text.AppendLine("    /// <returns>明示的な更新値または制御結果。</returns>");
        }

        text.AppendLine($"    public {signature}\n    {{\n        throw new NotImplementedException({Quote(Location(statement) + " " + dependency?.S("name") + ": " + reason)});\n    }}\n}}");
        var path = $"Stubs/{name}/{method}.cs";
        files[path] = text.ToString();
        stubs.Add(new JsonObject
        {
            ["programId"] = program.S("id"),
            ["statementId"] = statement.S("id"),
            ["method"] = method,
            ["file"] = path,
            ["signature"] = signature,
            ["capability"] = category == "Unsupported" ? "unsupported" : "adapter-required",
            ["reason"] = reason,
            ["source"] = statement["source"]?.DeepClone(),
            ["statement"] = statement.DeepClone(),
            ["dependency"] = dependency?.DeepClone(),
        });
    }

    /// <summary>未対応ブロックを状態と元位置を渡す単一スタブへ隔離します。</summary>
    /// <param name="statement">ブロック全体。</param>
    /// <param name="reason">隔離理由。</param>
    /// <returns>到達時にだけ呼び出す C#。</returns>
    private string StatementStub(JsonNode statement, string reason)
    {
        var method = "Statement_" + statement.S("id");
        var parameters = new List<(string Type, string Name)>();
        var arguments = new List<string>();
        var nodes = Validation.Walk(statement).ToArray();
        var roots = new Dictionary<string, JsonNode>();
        var unresolved = nodes.Any(n => n.S("kind") is "unknown" or "controlStub" or "controlTarget");
        foreach (var node in nodes)
        {
            foreach (var key in new[] { "symbolId", "tableId" })
            {
                if (symbols.TryGetValue(node.S(key), out var symbol))
                {
                    var root = Ancestors(symbol).First();
                    roots[root.S("id")] = root;
                }
            }
        }

        if (statement.S("kind") is "sort" or "sortIo")
        {
            foreach (var dependency in program.A("dependencies").Where(d => d.S("kind") == "file"))
            {
                if (symbols.TryGetValue(dependency.S("recordId"), out var record))
                {
                    var root = Ancestors(record).First();
                    roots[root.S("id")] = root;
                }
            }
        }

        unresolved |= roots.Values.Any(root => !ArraySafe(root));
        if (!unresolved && roots.Count != 0)
        {
            foreach (var root in roots.Values.OrderBy(r => r.S("id"), StringComparer.Ordinal))
            {
                var group = root["pic"] == null && !root.B("stub");
                var array = root.I("occurs", 1) > 1 || root["occursClause"] != null;
                var type = group ? name + "Group" + root.S("id") : TypeName(root);
                var access = "state." + Member(root);
                if (group || array)
                {
                    parameters.Add((type + (array ? "[]" : string.Empty), "value_" + root.S("id")));
                    arguments.Add(access);
                }
                else
                {
                    parameters.Add(($"CobolReference<{type}>", "value_" + root.S("id")));
                    var converted = Known(root) ? ConvertValue(root, "value") : "value ?? throw new InvalidOperationException(\"Unknown value must be explicit\")";
                    arguments.Add($"new CobolReference<{type}>(() => {access}, value => {access} = {converted})");
                }
            }
        }
        else
        {
            parameters.Add((name + "State", "state"));
            arguments.Add("state");
        }

        parameters.Add(("SourceLocation", "source"));
        arguments.Add(Metadata(statement));
        AddStub(statement, method, "Unsupported", "ControlResult", parameters.ToArray(), reason);
        var control = "control" + temporary++;
        return $"var {control} = dependencies.{method}({string.Join(", ", arguments)});\nif (!Enum.IsDefined({control}.Kind) || ({control}.Kind != ControlKind.Jump && {control}.Target != null))\n{{\n    throw new InvalidOperationException(\"Undefined stub control result\");\n}}\nif ({control}.Kind == ControlKind.Jump)\n{{\n    throw new CobolControlTransfer({control}.Target ?? throw new InvalidOperationException(\"Missing control target\"));\n}}\nif ({control} != ControlResult.Continue)\n{{\n    signal = {control} == ControlResult.Goback ? \"goback\" : \"stopRun\";\n    return;\n}}\n";
    }

    /// <summary>未対応の既知戻り型を持つ関数を型付きスタブへ変換します。</summary>
    /// <param name="expression">関数式。</param>
    /// <param name="type">戻りカテゴリ。</param>
    /// <param name="args">生成済み引数式。</param>
    /// <returns>型付きメソッド呼出し。</returns>
    private string ExpressionStub(JsonNode expression, string type, string[] args)
    {
        var statement = currentStatement ?? throw new NotSupportedException("Unsupported VALUE expression");
        var method = "Expression_" + statement.S("id") + "_" + Generator.Hash(Encoding.UTF8.GetBytes(expression.ToJsonString()))[..12];
        var types = expression.A("arguments").Select(a => ExpressionTypes.TypeOf(a, symbols) switch { "numeric" => "Number", "text" => "string", "boolean" => "bool", _ => throw new NotSupportedException("Unknown argument type") }).ToArray();
        var parameters = types.Select((t, i) => (t, "argument" + i)).Append(("SourceLocation", "source")).ToArray();
        AddStub(statement, method, "Unsupported", type == "numeric" ? "Number" : "string", parameters, "FUNCTION " + expression.S("name"));
        return $"dependencies.{method}({string.Join(", ", args.Append(Metadata(statement)))})";
    }

    /// <summary>解決した参照から依頼と更新対象を構築します。</summary>
    /// <param name="statement">外部文。</param>
    /// <returns>型付き依頼・応答とその反映処理。</returns>
    private string External(JsonNode statement)
    {
        var dependency = program.A("dependencies").Single(d => d.S("id") == statement.S("dependencyId"));
        var category = dependency.S("kind") switch { "call" => "Calls", "file" => "Files", "sql" => "Sql", _ => throw new NotSupportedException("External service " + dependency.S("kind")) };
        var method = "Dependency_" + statement.S("id");
        var requestName = name + method + "Request";
        var responseName = name + method + "Response";
        var inputs = new List<(string Type, string Name, string Expression)>();
        var outputs = new List<(string Type, string Name, JsonNode Reference)>();
        void Input(JsonNode expression, string label)
        {
            var type = ExpressionTypes.TypeOf(expression, symbols) switch { "numeric" => "Number", "text" => "string", "boolean" => "bool", _ => throw new NotSupportedException("Unknown external argument type") };
            inputs.Add((type, label, Expression(expression)));
        }

        void Output(JsonNode reference, string label)
        {
            _ = Reference(reference);
            outputs.Add((TypeName(symbols[reference.S("symbolId")]), label, reference));
        }

        if (category == "Calls")
        {
            Input(dependency["call"]!["target"]!, "Target");
            var args = dependency.A("inputs").ToArray();
            var modes = dependency.A("modes").Select(m => m.GetValue<string>()).ToArray();
            if (modes.Length != args.Length)
            {
                throw new NotSupportedException("Unresolved CALL passing modes");
            }

            var referenceSymbols = new HashSet<string>();
            for (var i = 0; i < args.Length; i++)
            {
                Input(args[i], "Argument" + i);
                if (modes[i] == "reference")
                {
                    if (!referenceSymbols.Add(args[i].S("symbolId")))
                    {
                        throw new NotSupportedException("Potentially aliased BY REFERENCE arguments require reference-cell implementation");
                    }

                    Output(args[i], "Argument" + i);
                }
                else if (modes[i] is not ("value" or "content"))
                {
                    throw new NotSupportedException("CALL mode " + modes[i]);
                }
            }

            if (dependency["call"]!["returning"] is JsonNode returning)
            {
                Output(returning, "Returning");
            }
        }
        else if (category == "Sql")
        {
            if (dependency["sql"]!["whenever"]!.AsObject().Any(p => p.Value != null))
            {
                throw new NotSupportedException("SQL WHENEVER control transfer requires implementation");
            }

            var i = 0;
            foreach (var binding in dependency["sql"]!.A("bindings"))
            {
                var host = binding["host"]!;
                Input(host, "Host" + i);
                if (binding.S("direction") == "output")
                {
                    Output(host, "Host" + i);
                }

                if (binding["indicator"] is JsonNode indicator)
                {
                    Input(indicator, "Indicator" + i);
                    if (binding.S("direction") == "output")
                    {
                        Output(indicator, "Indicator" + i);
                    }
                }

                i++;
            }

            if (dependency["status"] is JsonNode status)
            {
                Output(status, "SqlCode");
            }

            if (dependency["sql"]!["sqlstate"] is JsonNode sqlstate)
            {
                Output(sqlstate, "SqlState");
            }
        }
        else
        {
            if (statement["transfer"] != null || statement["key"] != null || statement["advancing"] != null || dependency["file"]?.S("organization") is not ("sequential" or "line-sequential"))
            {
                throw new NotSupportedException("File transfer/key/organization requires implementation");
            }

            var recordId = dependency.S("recordId");
            var recordFields = symbols.Values.Where(s => s.I("level") != 88 && s["pic"] != null && Ancestors(s).Any(a => a.S("id") == recordId)).ToArray();
            foreach (var symbol in recordFields)
            {
                var reference = new JsonObject { ["kind"] = "reference", ["symbolId"] = symbol.S("id"), ["subscripts"] = new JsonArray() };
                Input(reference, "Record" + symbol.S("id"));
                if (statement.S("operation").StartsWith("read", StringComparison.Ordinal))
                {
                    Output(reference, "Record" + symbol.S("id"));
                }
            }

            if (dependency["status"] is JsonNode status)
            {
                Output(status, "FileStatus");
            }
        }

        var requestFields = inputs.Select(p => p.Type + " " + p.Name).Prepend("SourceLocation Source");
        var responseFields = outputs.Select(p => p.Type + " " + p.Name).Prepend("DependencyOutcome Outcome").Prepend("string StatusCode");
        files[$"Contracts/{requestName}.g.cs"] = Header() + $"/// <summary>順序と型を保持した依頼です。</summary>\ninternal sealed record {requestName}({string.Join(", ", requestFields)});\n\n/// <summary>必須の状態と更新値です。省略値で成功を補いません。</summary>\ninternal sealed record {responseName}({string.Join(", ", responseFields)});\n";
        AddStub(statement, method, category, responseName, [(requestName, "request")], "Implement " + dependency.S("kind") + " " + dependency.S("name"), dependency);
        var result = new StringBuilder();
        var callId = temporary++;
        var response = "response" + callId;
        var destinations = new List<(JsonNode Symbol, string Access, string? Start, string? Length)>();
        for (var outputIndex = 0; outputIndex < outputs.Count; outputIndex++)
        {
            var reference = outputs[outputIndex].Reference;
            var symbol = symbols[reference.S("symbolId")];
            var dims = Dimensions(symbol);
            var subscripts = reference.A("subscripts").ToArray();
            var indices = dims.Select((_, i) => $"reference{callId}_{outputIndex}_{i}").ToArray();
            for (var i = 0; i < dims.Length; i++)
            {
                result.AppendLine($"var {indices[i]} = SourceSupport.Index({Expression(subscripts[i])}, {dims[i]});");
            }

            string? start = null;
            string? length = null;
            if (reference["modification"] is JsonNode slice)
            {
                start = "start" + callId + "_" + outputIndex;
                length = "length" + callId + "_" + outputIndex;
                result.AppendLine($"var {start} = CobolValue.Integer({Expression(slice["start"]!)});\nvar {length} = CobolValue.Integer({Expression(slice["length"]!)});");
            }

            destinations.Add((symbol, Access(symbol, indices), start, length));
        }

        result.AppendLine($"var {response} = dependencies.{method}(new {requestName}({string.Join(", ", inputs.Select(p => p.Expression).Prepend(Metadata(statement, dependency)))})) ?? throw new InvalidOperationException(\"Undefined dependency response\");");
        result.AppendLine($"if (string.IsNullOrEmpty({response}.StatusCode) || !Enum.IsDefined({response}.Outcome))\n{{\n    throw new InvalidOperationException(\"Undefined dependency status\");\n}}");
        if (category == "Calls")
        {
            result.AppendLine($"if ({response}.Outcome is not (DependencyOutcome.Normal or DependencyOutcome.Exception))\n{{\nthrow new InvalidOperationException(\"Undefined CALL outcome\");\n}}");
        }

        for (var i = 0; i < outputs.Count; i++)
        {
            var output = outputs[i];
            var destination = destinations[i];
            var value = response + "." + output.Name + (output.Type == "string" ? " ?? throw new InvalidOperationException(\"Missing dependency field\")" : string.Empty);
            if (destination.Start != null)
            {
                value = $"SourceSupport.Replace({destination.Access}, {destination.Start}, {destination.Length}, {value})";
            }

            result.AppendLine($"{destination.Access} = {ConvertValue(destination.Symbol, value, sizeError: true)};");
        }

        result.AppendLine($"dependencyTrace.Add(new JsonObject {{ [\"dependencyId\"] = {Quote(dependency.S("id"))}, [\"statementId\"] = {Quote(statement.S("id"))}, [\"operation\"] = {Quote(statement.S("operation"))}, [\"status\"] = {response}.StatusCode, [\"outcome\"] = {response}.Outcome.ToString(), [\"implementation\"] = \"csharp-adapter\" }});");
        var handledOutcome = category == "Calls" ? "DependencyOutcome.Exception" : "DependencyOutcome.End";
        result.AppendLine($"if ({response}.Outcome == {handledOutcome})\n{{");
        result.Append(Block(statement.A("atEnd")));
        if (!statement.A("atEnd").Any() && category != "Sql" && dependency["status"] == null)
        {
            result.AppendLine($"throw new InvalidOperationException(\"Unhandled dependency outcome: \" + {response}.StatusCode);");
        }

        result.AppendLine($"}}\nelse if ({response}.Outcome == DependencyOutcome.InvalidKey)\n{{");
        result.Append(Block(statement.A("onInvalid")));
        if (!statement.A("onInvalid").Any() && dependency["status"] == null)
        {
            result.AppendLine($"throw new InvalidOperationException(\"Unhandled invalid key: \" + {response}.StatusCode);");
        }

        result.AppendLine($"}}\nelse if ({response}.Outcome == DependencyOutcome.Exception)\n{{");
        if (dependency["status"] == null)
        {
            result.AppendLine($"throw new InvalidOperationException(\"Unhandled dependency failure: \" + {response}.StatusCode);");
        }

        result.AppendLine("}\nelse\n{");
        result.Append(Block(statement.A("notEnd")));
        result.Append(Block(statement.A("notInvalid")));
        result.AppendLine("}");
        return result.ToString();
    }

    /// <summary>分類ごとの契約を保存します。</summary>
    private void Contracts()
    {
        var result = new StringBuilder(Header());
        foreach (var (category, methods) in contracts)
        {
            result.AppendLine($"/// <summary>{category} の明示的な依存契約です。</summary>\ninternal interface I{name}{category}\n{{");
            foreach (var signature in methods)
            {
                result.AppendLine("    " + signature);
            }

            result.AppendLine("}");
        }

        result.AppendLine($"/// <summary>プログラムへ注入する依存の集合です。</summary>\ninternal interface I{name}Dependencies : {string.Join(", ", contracts.Keys.Select(c => "I" + name + c))}\n{{\n}}");
        result.AppendLine($"/// <summary>文別の編集ファイルを契約に結び付けます。</summary>\ninternal sealed partial class {name}Dependencies : I{name}Dependencies\n{{\n}}");
        files[$"Contracts/I{name}Dependencies.g.cs"] = result.ToString();
    }
}
