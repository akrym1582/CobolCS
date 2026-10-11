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
    private string Metadata(JsonNode statement, JsonNode? dependency = null) => $"new SourceLocation({Quote(program.S("id"))}, {Quote(statement.S("id"))}, {Quote(dependency?.S("id") ?? string.Empty)}, {Quote(statement["source"]?.S("file") ?? string.Empty)}, {statement["source"]?["start"]?.I("line") ?? 0}, {Quote(statement["source"]?.S("raw") ?? string.Empty)}, {Quote(statement["operation"] == null ? statement.S("kind") : statement.S("operation"))}, Array.AsReadOnly(new string[] {{ {string.Join(", ", (dependency?.A("modes") ?? []).Select(m => Quote(m.GetValue<string>())))} }}), {(statement.S("sentenceId") == string.Empty ? "null" : Quote("$sentence_" + statement.S("sentenceId")))})";

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
        var responseFields = outputs.Select(p => "Optional<" + p.Type + "> " + p.Name + " = default").Prepend("DependencyOutcome Outcome").Prepend("string StatusCode");
        files[$"Contracts/{requestName}.g.cs"] = Header() + $"/// <summary>順序と型を保持した依頼です。</summary>\npublic sealed record {requestName}({string.Join(", ", requestFields)});\n\n/// <summary>必須の状態と更新値です。省略値で成功を補いません。</summary>\npublic sealed record {responseName}({string.Join(", ", responseFields)});\n";
        files[$"Contracts/{requestName}.g.cs"] += $"public sealed record {requestName}Trace({requestName} Request, string Status, DependencyOutcome Result, string ImplementationKind) : DependencyTrace(Request.Source, Status, Result, ImplementationKind);\n";
        AddStub(statement, method, category, responseName, [(requestName, "request")], "Implement " + dependency.S("kind") + " " + dependency.S("name"), dependency);
        if (category == "Calls")
        {
            var stubPath = $"Stubs/{name}/{method}.cs";
            var helperName = "NoOp_" + statement.S("id");
            files[stubPath] = files[stubPath].Replace("        throw new NotImplementedException", $"        if (Options.CallStubMode == CallStubMode.NoOp)\n        {{\n            return {helperName}(request);\n        }}\n\n        throw new NotImplementedException", StringComparison.Ordinal);
            var returning = outputs.FirstOrDefault(o => o.Name == "Returning");
            var overlap = returning.Reference != null && outputs.Any(o => o.Name != "Returning" && o.Reference.S("symbolId") == returning.Reference.S("symbolId"));
            var arguments = returning.Reference == null ? string.Empty : ", Returning: " + (returning.Type == "Number" ? "new Number(0)" : "string.Empty");
            var body = overlap ? "throw new NotSupportedException(\"RETURNING overlaps BY REFERENCE storage\");" : $"return new(\"NOOP\", DependencyOutcome.Normal{arguments}) {{ IsGeneratedNoOp = true }};";
            files[$"Stubs/{name}/{helperName}.cs"] = $"using CobolCS.Runtime;\n\nnamespace CobolCS.Generated;\n\n/// <summary>探索専用の CALL。実接続せず、参照引数は変更しません。</summary>\ninternal sealed partial class {name}Dependencies\n{{\n    /// <summary>仮の返値で後続の業務分岐が変わる可能性があります。</summary>\n    /// <param name=\"request\">呼出し位置と型付き引数。</param>\n    /// <returns>成功を意味しない探索応答。</returns>\n    private static {responseName} {helperName}({requestName} request)\n    {{\n        {body}\n    }}\n}}\n";
        }

        files[$"Contracts/{requestName}.g.cs"] = files[$"Contracts/{requestName}.g.cs"].Replace($"{responseName}({string.Join(", ", responseFields)});", $"{responseName}({string.Join(", ", responseFields)}) {{ internal bool IsGeneratedNoOp {{ get; init; }} }}", StringComparison.Ordinal);
        if (outputs.Count != 0)
        {
            var legacyFields = outputs.Select(p => p.Type + " " + p.Name).Prepend("DependencyOutcome Outcome").Prepend("string StatusCode");
            var legacyValues = outputs.Select(p => "new Optional<" + p.Type + ">(" + p.Name + ")").Prepend("Outcome").Prepend("StatusCode");
            files[$"Contracts/{requestName}.g.cs"] = files[$"Contracts/{requestName}.g.cs"].Replace("internal bool IsGeneratedNoOp", $"\n/// <summary>旧スタブの必須出力 constructor。EOF の旧レコードは同値の場合だけ許容します。</summary>\npublic {responseName}({string.Join(", ", legacyFields)}) : this({string.Join(", ", legacyValues)}) {{ LegacyResponse = true; }}\ninternal bool LegacyResponse {{ get; init; }}\ninternal bool IsGeneratedNoOp", StringComparison.Ordinal);
        }

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

        result.AppendLine($"var request{callId} = new {requestName}({string.Join(", ", inputs.Select(p => p.Expression).Prepend(Metadata(statement, dependency)))});\ncancellationToken.ThrowIfCancellationRequested();\nvar {response} = dependencies.{method}(request{callId}) ?? throw new InvalidOperationException(\"Undefined dependency response\");");
        var traceInitializer = string.Empty;
        if (category == "Calls")
        {
            var modes = dependency.A("modes").Select(m => m.GetValue<string>()).ToArray();
            var arguments = inputs.Where(p => p.Name.StartsWith("Argument", StringComparison.Ordinal)).Select((p, i) => $"new CallArgument({i + 1}, {Quote(modes[i])}, {(p.Type == "Number" ? "request" + callId + "." + p.Name : "null")}, {(p.Type == "string" ? "request" + callId + "." + p.Name : "null")}, {(p.Type == "bool" ? "request" + callId + "." + p.Name : "null")})");
            traceInitializer = $" {{ Target = request{callId}.Target, Arguments = Array.AsReadOnly(new CallArgument[] {{ {string.Join(", ", arguments)} }}) }}";
        }

        result.AppendLine($"dependencyTrace.Add(new {requestName}Trace(request{callId}, {response}.StatusCode, {response}.Outcome, {response}.IsGeneratedNoOp ? \"generated-call-no-op\" : \"csharp-adapter\"){traceInitializer});\ncancellationToken.ThrowIfCancellationRequested();\nif (timer.ElapsedMilliseconds > (options.TimeoutMs ?? {config.I("timeoutMs", 10000)})) throw new ExecutionTimeoutException(location);");
        result.AppendLine($"if (string.IsNullOrEmpty({response}.StatusCode) || !Enum.IsDefined({response}.Outcome)) throw new InvalidOperationException(\"Undefined dependency status\");");
        if (category == "Calls")
        {
            result.AppendLine($"if ({response}.Outcome is not (DependencyOutcome.Normal or DependencyOutcome.Exception)) throw new InvalidOperationException(\"Undefined CALL outcome\");");
        }
        else if (category == "Files")
        {
            result.AppendLine($"if (({response}.Outcome == DependencyOutcome.Normal && {response}.StatusCode != \"00\") || ({response}.Outcome == DependencyOutcome.End && {response}.StatusCode != \"10\") || ({response}.StatusCode == \"00\" && {response}.Outcome != DependencyOutcome.Normal) || ({response}.StatusCode == \"10\" && {response}.Outcome != DependencyOutcome.End)) throw new InvalidOperationException(\"Contradictory FILE status/outcome\");");
        }

        for (var i = 0; i < outputs.Count; i++)
        {
            var output = outputs[i];
            var destination = destinations[i];
            var field = response + "." + output.Name;
            if (category == "Files" && output.Name.StartsWith("Record", StringComparison.Ordinal))
            {
                result.AppendLine($"if ({response}.Outcome == DependencyOutcome.Normal && !{field}.HasValue) throw new InvalidOperationException(\"Missing READ record\");\nif ({response}.Outcome == DependencyOutcome.End && {field}.HasValue && (!{response}.LegacyResponse || {field}.Value != {destination.Access})) throw new InvalidOperationException(\"EOF must preserve record; migrate legacy response to omitted record outputs\");");
            }

            if (output.Name is "FileStatus" or "SqlCode" or "SqlState")
            {
                result.AppendLine($"if (!{field}.HasValue) throw new InvalidOperationException(\"Missing dependency status output\");");
            }

            if (category == "Sql" && output.Name.StartsWith("Host", StringComparison.Ordinal))
            {
                var indicatorName = "Indicator" + output.Name[4..];
                var hasIndicator = outputs.Any(o => o.Name == indicatorName);
                var isNull = hasIndicator ? $"({response}.{indicatorName}.HasValue && {response}.{indicatorName}.Value.Numerator.Sign < 0)" : "false";
                result.AppendLine($"if ({response}.Outcome == DependencyOutcome.Normal && !{isNull} && !{field}.HasValue) throw new InvalidOperationException(\"Missing SQL output host\");\nif ({isNull} && {field}.HasValue) throw new InvalidOperationException(\"NULL indicator requires omitted host value\");");
            }

            if (category == "Sql" && output.Name.StartsWith("Indicator", StringComparison.Ordinal))
            {
                result.AppendLine($"if ({response}.Outcome == DependencyOutcome.Normal && !{field}.HasValue) throw new InvalidOperationException(\"Missing SQL output indicator\");");
            }

            if (category == "Calls" && output.Name == "Returning")
            {
                result.AppendLine($"if ({response}.Outcome == DependencyOutcome.Normal && !{field}.HasValue) throw new InvalidOperationException(\"Missing CALL RETURNING\");");
            }

            var value = field + ".Value" + (output.Type == "string" ? " ?? throw new InvalidOperationException(\"Missing dependency field\")" : string.Empty);
            if (destination.Start != null)
            {
                value = $"SourceSupport.Replace({destination.Access}, {destination.Start}, {destination.Length}, {value})";
            }

            result.AppendLine($"var converted{callId}_{i} = {field}.HasValue ? {ConvertValue(destination.Symbol, value, sizeError: true)} : {destination.Access};");
            if (output.Name == "FileStatus")
            {
                result.AppendLine($"if (converted{callId}_{i} != {response}.StatusCode) throw new InvalidOperationException(\"FILE STATUS mismatch\");");
            }

            if (output.Name == "SqlState")
            {
                result.AppendLine($"if (({response}.Outcome == DependencyOutcome.Normal && !converted{callId}_{i}.StartsWith(\"00\", StringComparison.Ordinal)) || ({response}.Outcome == DependencyOutcome.End && !converted{callId}_{i}.StartsWith(\"02\", StringComparison.Ordinal)) || ({response}.Outcome == DependencyOutcome.Exception && (converted{callId}_{i}.StartsWith(\"00\", StringComparison.Ordinal) || converted{callId}_{i}.StartsWith(\"02\", StringComparison.Ordinal)))) throw new InvalidOperationException(\"SQLSTATE/outcome mismatch\");");
            }

            if (output.Name == "SqlCode")
            {
                result.AppendLine($"if (converted{callId}_{i} != CobolValue.Numeric({response}.StatusCode) || ({response}.Outcome == DependencyOutcome.Normal && converted{callId}_{i} != new Number(0)) || ({response}.Outcome == DependencyOutcome.End && converted{callId}_{i} != new Number(100)) || ({response}.Outcome == DependencyOutcome.Exception && converted{callId}_{i}.Numerator.Sign >= 0)) throw new InvalidOperationException(\"SQL status/outcome mismatch\");");
            }
        }

        for (var i = 0; i < outputs.Count; i++)
        {
            result.AppendLine($"{destinations[i].Access} = converted{callId}_{i};");
        }

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
            result.AppendLine($"/// <summary>{category} の明示的な依存契約です。</summary>\n{(category == "Unsupported" ? "internal" : "public")} interface I{name}{category}\n{{");
            foreach (var signature in methods)
            {
                result.AppendLine("    /// <summary>文の型付き依頼と明示応答。未実装時は位置付き失敗です。</summary>\n    " + signature);
            }

            result.AppendLine("}");
        }

        result.AppendLine($"/// <summary>プログラムへ注入する依存の集合です。</summary>\ninternal interface I{name}Dependencies : {string.Join(", ", contracts.Keys.Select(c => "I" + name + c))}\n{{\n}}");
        result.AppendLine($"/// <summary>文別の編集ファイルを契約に結び付けます。</summary>\ninternal sealed partial class {name}Dependencies : I{name}Dependencies\n{{\ninternal ExecutionOptions Options {{ get; init; }} = new();\n}}");
        files[$"Contracts/I{name}Dependencies.g.cs"] = result.ToString();
        RouterAndFake();
    }
}
