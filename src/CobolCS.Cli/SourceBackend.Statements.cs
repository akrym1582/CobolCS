using System.Text;
using System.Text.Json.Nodes;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>通常文は C# の代入・分岐・反復として生成します。</summary>
internal sealed partial class SourceBackend
{
    /// <summary>入れ子の文列を生成します。</summary>
    /// <param name="statements">宣言順の文。</param>
    /// <returns>C# 本文。</returns>
    private string Block(IEnumerable<JsonNode> statements) => string.Concat(statements.Select(s => Statement(s)));

    /// <summary>一つの文を生成し、復旧可能な不足箇所をスタブへ変換します。</summary>
    /// <param name="statement">原文位置を持つ文。</param>
    /// <param name="flow">算術の分岐を enum 制御へ渡す場合は true。</param>
    /// <returns>位置情報と本文。</returns>
    private string Statement(JsonNode statement, bool flow = false)
    {
        if (statement.S("kind") == "unsupportedString")
        {
            statement = statement["originalStatement"]!.DeepClone();
        }

        var previous = currentStatement;
        currentStatement = statement;
        string body;
        try
        {
            body = flow && statement.S("kind") == "arithmetic" ? Arithmetic(statement, includeBranches: false) : flow && statement.S("kind") == "stringTransfer" ? StringStatement(statement, includeBranches: false) : NativeStatement(statement);
        }
        catch (NotSupportedException error)
        {
            body = StatementStub(statement, error.Message);
        }
        finally
        {
            currentStatement = previous;
        }

        sourceMap.Add(new JsonObject { ["programId"] = program.S("id"), ["statementId"] = statement.S("id"), ["capability"] = stubs.OfType<JsonObject>().FirstOrDefault(s => s.S("programId") == program.S("id") && s.S("statementId") == statement.S("id"))?.S("capability") ?? "native", ["source"] = statement["source"]?.DeepClone(), ["generatedFile"] = $"Programs/{name}Program.g.cs" });
        return "{\n// " + Location(statement).Replace('\n', ' ').Replace('\r', ' ') + " " + (statement["source"]?.S("raw") ?? string.Empty).Replace('\n', ' ').Replace('\r', ' ') + "\nlocation = " + Quote(Location(statement)) + ";\ncurrentSource = " + Metadata(statement) + ";\nTick();\n" + body + "}\n";
    }

    /// <summary>通常の業務文を直接生成します。</summary>
    /// <param name="statement">解析済み文。</param>
    /// <returns>C# 本文。</returns>
    private string NativeStatement(JsonNode statement)
    {
        var result = new StringBuilder();
        switch (statement.S("kind"))
        {
            case "initialize":
                return InitializeStatement(statement);
            case "inspectOperations":
                return InspectStatement(statement);
            case "stringTransfer":
                return StringStatement(statement);
            case "move":
                var sending = statement["value"]!;
                var type = ExpressionTypes.TypeOf(sending, symbols);
                if (type == "unknown" || (type == "numeric" && statement.A("targets").Any(t => ExpressionTypes.TypeOf(t, symbols) != "numeric")))
                {
                    throw new NotSupportedException("CS_MOVE: numeric-to-text or unknown MOVE type");
                }

                result.AppendLine($"var value = {Expression(sending)};");
                foreach (var target in statement.A("targets"))
                {
                    result.Append(Assign(target, "value"));
                }

                break;
            case "acceptInput":
                if (statement.S("inputSource") != "CONSOLE")
                {
                    throw new NotSupportedException("Unsupported ACCEPT FROM " + statement.S("inputSource"));
                }

                result.AppendLine("string accepted;\nif (acceptProvider != null) accepted = acceptProvider.Read(currentSource!);\nelse if (!input.TryDequeue(out accepted!))\n{\nthrow new InvalidOperationException(\"ACCEPT fixture input exhausted\");\n}");
                result.Append(Assign(statement["target"]!, "accepted", sizeError: true));
                break;
            case "display":
                if (statement["upon"] != null)
                {
                    throw new NotSupportedException("DISPLAY UPON requires a device adapter");
                }

                var values = statement.A("values").Select(e => e.S("kind") == "reference" && e["modification"] == null && symbols.TryGetValue(e.S("symbolId"), out var s) && Known(s) && s["pic"]!.S("category") == "numeric" ? $"SourceSupport.Display({Reference(e)}, {s["pic"]!.I("digits")}, {s["pic"]!.I("scale")}, {Bool(s["pic"]!.B("signed"))})" : $"CobolValue.Text({Expression(e)})");
                result.AppendLine($"display.Add(string.Concat(new string[] {{ {string.Join(", ", values)} }}));");
                break;
            case "if":
                result.AppendLine($"if (branches.Test({Quote(BranchId(statement))}, CobolValue.Truth({Expression(statement["condition"]!)})))\n{{");
                result.Append(Block(statement.A("then")));
                result.AppendLine("}\nelse\n{");
                result.Append(Block(statement.A("else")));
                result.AppendLine("}");
                break;
            case "evaluate":
                var subject = "subject" + temporary++;
                result.AppendLine($"var {subject} = {Expression(statement["subject"]!)};");
                var first = true;
                var choiceIndex = 0;
                foreach (var choice in statement.A("cases"))
                {
                    result.AppendLine($"{(first ? "if" : "else if")} ({string.Join(" || ", choice.A("values").Select(v => $"CobolValue.Compare({subject}, {Expression(v)}) == 0"))})\n{{");
                    result.AppendLine($"branches.Record({Quote(BranchId(statement))}, {Quote("when-" + choiceIndex++)});");
                    result.Append(Block(choice.A("body")));
                    result.AppendLine("}");
                    first = false;
                }

                result.AppendLine(first ? "{" : "else\n{");
                result.AppendLine($"branches.Record({Quote(BranchId(statement))}, {Quote(statement.A("other").Any() ? "other" : "none")});");
                result.Append(Block(statement.A("other")));
                result.AppendLine("}");
                break;
            case "loop":
                if (statement.A("varyingAfter").Any() || (statement.B("testAfter") && statement["varying"] != null))
                {
                    throw new NotSupportedException("PERFORM AFTER / TEST AFTER VARYING");
                }

                var varying = statement["varying"];
                if (varying != null)
                {
                    result.Append(Assign(varying["reference"]!, Expression(varying["from"]!)));
                }

                var loopId = temporary++;
                result.AppendLine($"branches.StartLoop({Quote(BranchId(statement))});");
                if (statement["times"] is JsonNode times)
                {
                    result.AppendLine($"for (var remaining{loopId} = Math.Max(0, CobolValue.Integer({Expression(times)})); branches.Loop({Quote(BranchId(statement))}, remaining{loopId} > 0); remaining{loopId}--)\n{{");
                }
                else
                {
                    result.AppendLine($"var first{loopId} = {Bool(statement.B("testAfter"))};\nwhile (branches.Loop({Quote(BranchId(statement))}, first{loopId} || !({(statement["until"] is JsonNode until ? "CobolValue.Truth(" + Expression(until) + ")" : "false")})))\n{{\nfirst{loopId} = false;");
                }

                result.AppendLine("Tick();");
                loopDepth++;
                try
                {
                    result.Append(Block(statement.A("body")));
                }
                finally
                {
                    loopDepth--;
                }

                if (varying != null)
                {
                    result.Append(Assign(varying["reference"]!, $"CobolValue.Numeric({Expression(varying["reference"]!)}) + CobolValue.Numeric({Expression(varying["by"]!)})"));
                }

                result.AppendLine($"}}\nbranches.FinishLoop({Quote(BranchId(statement))});");
                break;
            case "sequence":
                result.Append(Block(statement.A("body")));
                break;
            case "arithmetic":
                result.Append(Arithmetic(statement));
                break;
            case "setIndex":
                result.AppendLine($"var value = {Expression(statement["value"]!)};");
                foreach (var target in statement.A("targets"))
                {
                    result.Append(Assign(target, statement.S("operation") is "up" or "down" ? $"CobolValue.Numeric({Expression(target)}) {(statement.S("operation") == "up" ? "+" : "-")} CobolValue.Numeric(value)" : "value"));
                }

                break;
            case "set88":
                var condition = symbols[statement.S("symbolId")];
                var parent = new JsonObject { ["symbolId"] = condition.S("parentId"), ["subscripts"] = statement["subscripts"]?.DeepClone() };
                result.Append(Assign(parent, Expression(condition.A("conditions").First()["low"]!)));
                break;
            case "external":
                result.Append(External(statement));
                break;
            case "halt":
                result.AppendLine($"signal = {Quote(statement.S("signal"))};\nif (signal.Length != 0)\n{{\n    return;\n}}");
                break;
            case "exitPerform":
                if (loopDepth == 0)
                {
                    throw new NotSupportedException("EXIT PERFORM outside inline loop");
                }

                result.AppendLine("var exitRequested = true;\nif (exitRequested)\n{\nbreak;\n}");
                break;
            case "continue":
            case "sentenceBoundary":
                break;
            default:
                throw new NotSupportedException(statement.S("reason") == string.Empty ? "Unsupported statement: " + statement.S("kind") : statement.S("reason"));
        }

        return result.ToString();
    }

    /// <summary>書込み前に全算術値を評価し、桁あふれ分岐も通常の C# として出力します。</summary>
    /// <param name="statement">算術文。</param>
    /// <param name="includeBranches">分岐本文をこの場所に生成する場合は true。</param>
    /// <returns>計算・格納・分岐。</returns>
    private string Arithmetic(JsonNode statement, bool includeBranches = true)
    {
        var sizeError = "sizeError" + temporary++;
        var result = new StringBuilder($"var {sizeError} = false;\n{{\n");
        var hasError = statement.B("hasSizeError") || statement.A("notError").Any();
        if (hasError)
        {
            result = new StringBuilder($"var {sizeError} = false;\ntry\n{{\n");
        }

        var remainder = statement["remainder"];
        if (remainder != null)
        {
            result.AppendLine($"var dividend = CobolValue.Numeric({Expression(statement["value"]!["left"]!)});\nvar divisor = CobolValue.Numeric({Expression(statement["value"]!["right"]!)});\nNumber? quotient = null;");
        }

        result.AppendLine($"var value = CobolValue.Numeric({Expression(statement["value"]!)});\nvar commits = new List<Action>();");
        foreach (var target in statement.A("targets"))
        {
            var reference = target["reference"]!;
            var symbol = symbols[reference.S("symbolId")];
            var calculation = "value";
            if (statement.B("update"))
            {
                var op = statement.S("verb") switch { "ADD" => "+", "SUBTRACT" => "-", "MULTIPLY" => "*", "DIVIDE" => "/", _ => throw new NotSupportedException("Arithmetic verb") };
                calculation = $"CobolValue.Numeric({Expression(reference)}) {op} value";
            }

            var dims = Dimensions(symbol);
            var indices = reference.A("subscripts").ToArray();
            _ = Reference(reference);
            result.AppendLine("{\ntry\n{");
            for (var i = 0; i < dims.Length; i++)
            {
                result.AppendLine($"var index{i} = SourceSupport.Index({Expression(indices[i])}, {dims[i]});");
            }

            result.AppendLine($"var calculated = {calculation};\nvar converted = {ConvertValue(symbol, "calculated", target.B("rounded"), hasError)};\ncommits.Add(() => {Access(symbol, dims.Select((_, i) => "index" + i).ToArray())} = converted);");
            if (remainder != null)
            {
                result.AppendLine($"quotient = new Number(calculated.Units({symbol["pic"]!.I("scale")}), System.Numerics.BigInteger.Pow(10, {symbol["pic"]!.I("scale")}));");
            }

            result.AppendLine($"}}\ncatch (SizeError)\n{{\n{sizeError} = true;\n}}\n}}");
        }

        if (remainder != null)
        {
            result.AppendLine("if (quotient is Number q)\n{");
            var symbol = symbols[remainder.S("symbolId")];
            var dims = Dimensions(symbol);
            var subscripts = remainder.A("subscripts").ToArray();
            _ = Reference(remainder);
            for (var i = 0; i < dims.Length; i++)
            {
                result.AppendLine($"var remainderIndex{i} = SourceSupport.Index({Expression(subscripts[i])}, {dims[i]});");
            }

            var access = Access(symbol, dims.Select((_, i) => "remainderIndex" + i).ToArray());
            result.AppendLine($"var remainderValue = {ConvertValue(symbol, "dividend - (q * divisor)", sizeError: hasError)};\ncommits.Add(() => {access} = remainderValue);\n}}");
        }

        result.AppendLine("foreach (var commit in commits)\n{\ncommit();\n}\n}");
        if (hasError)
        {
            result.AppendLine($"catch (DivideByZeroException)\n{{\n{sizeError} = true;\n}}\ncatch (SizeError)\n{{\n{sizeError} = true;\n}}");
        }

        if (!includeBranches)
        {
            result.AppendLine($"lastSizeError = {sizeError};");
            return result.ToString();
        }

        result.AppendLine($"if ({(hasError ? $"branches.Test({Quote(BranchId(statement))}, {sizeError}, \"error\", \"normal\")" : sizeError)})\n{{");
        result.Append(Block(statement.A("onError")));
        result.AppendLine("}\nelse\n{");
        result.Append(Block(statement.A("notError")));
        result.AppendLine("}");
        return result.ToString();
    }

    /// <summary>構造化可能な本文と段落制御の生成方式を選びます。</summary>
    /// <returns>実行メソッドと必要な制御 enum。</returns>
    private string ProgramBody()
    {
        var paragraphs = program.A("paragraphs").ToArray();
        var unstructured = paragraphs.Length > 1 || Validation.Walk(program["paragraphs"]).Any(n => n.S("kind") is "goto" or "gotoDepending" or "perform" or "nextSentence" or "controlStub");
        var body = unstructured ? Flow() : "    private void Execute()\n    {\n" + Block(paragraphs.SelectMany(p => p.A("statements"))) + "    }\n";
        var prefix = new StringBuilder();
        if (program.A("using").Any() && !SupportedUsing())
        {
            var linkage = new JsonObject { ["kind"] = "linkageArguments", ["id"] = "linkage", ["source"] = paragraphs.FirstOrDefault()?["source"]?.DeepClone(), ["arguments"] = program["using"]?.DeepClone() };
            prefix.Append(Statement(linkage));
        }

        foreach (var diagnostic in program.A("diagnostics").Where(d => d.S("code") is "UNKNOWN_HEADER" or "UNCONSUMED_TOKEN" or "PROGRAM_LIFECYCLE" or "UNKNOWN_DECLARATION" or "CONDITIONAL_LAYOUT_OPAQUE" or "UNTERMINATED_LITERAL"))
        {
            var unresolved = new JsonObject { ["kind"] = "unknown", ["id"] = "unrecovered_" + temporary++, ["source"] = diagnostic["source"]?.DeepClone(), ["reason"] = diagnostic.S("message") };
            prefix.Append(Statement(unresolved));
        }

        return body.Replace("private void Execute()\n    {\n", "private void Execute()\n    {\n" + prefix);
    }

    /// <summary>段落落下・THRU・GO TO のみに生成時の制御解析を使います。</summary>
    /// <returns>本文が通常の C# である enum と switch。</returns>
    private string Flow()
    {
        var lowered = program.DeepClone();
        foreach (var node in Validation.Walk(lowered["paragraphs"]).ToArray())
        {
            if (node.S("kind") == "loop" && (node.A("varyingAfter").Any() || (node.B("testAfter") && node["varying"] != null)))
            {
                node["kind"] = "unsupportedLoop";
            }
        }

        foreach (var node in Validation.Walk(lowered["paragraphs"]).ToArray())
        {
            if (node.S("kind") is not ("if" or "evaluate" or "loop"))
            {
                continue;
            }

            currentStatement = node;
            try
            {
                var expressions = new[] { "condition", "subject", "until", "times" }.Select(key => node[key]).Where(e => e != null).Cast<JsonNode>();
                if (node.S("kind") == "evaluate")
                {
                    expressions = expressions.Concat(node.A("cases").SelectMany(c => c.A("values")));
                }

                if (node["varying"] is JsonNode variation)
                {
                    expressions = expressions.Concat(new[] { variation["reference"]!, variation["from"]!, variation["by"]! });
                }

                foreach (var expression in expressions)
                {
                    if (expression != null)
                    {
                        if (Validation.Walk(expression).Any(e => e.S("kind") == "function" && (!ExpressionTypes.Functions.TryGetValue(e.S("name"), out var f) || !f.Native)))
                        {
                            throw new NotSupportedException("Unsupported control expression");
                        }

                        _ = Expression(expression);
                    }
                }
            }
            catch (NotSupportedException)
            {
                node["kind"] = "unsupportedControl";
            }
        }

        foreach (var node in Validation.Walk(lowered["paragraphs"]).Where(n => n.S("kind") is "arithmetic" or "stringTransfer").ToArray())
        {
            currentStatement = node;
            try
            {
                _ = node.S("kind") == "arithmetic" ? Arithmetic(node, includeBranches: false) : StringStatement(node, includeBranches: false);
            }
            catch (NotSupportedException)
            {
                if (node.S("kind") == "stringTransfer")
                {
                    var original = node.DeepClone();
                    node["kind"] = "unsupportedString";
                    node["originalStatement"] = original;
                }
                else
                {
                    node["kind"] = "unsupportedArithmetic";
                }
            }
        }

        currentStatement = null;
        Instruction[] flow;
        try
        {
            flow = FlowBuilder.Build(lowered, preserveSentences: true);
        }
        catch (Exception error) when (error is KeyNotFoundException or NotSupportedException)
        {
            var block = new JsonObject { ["id"] = "control", ["kind"] = "unresolvedControl", ["source"] = program.A("paragraphs").First()["source"]?.DeepClone(), ["paragraphs"] = program["paragraphs"]?.DeepClone() };
            return "    private void Execute()\n    {\n" + StatementStub(block, error.Message) + "    }\n";
        }

        string Pc(int i) => "Step.S" + i;
        var result = new StringBuilder("    private enum Step\n    {\n");
        for (var i = 0; i <= flow.Length; i++)
        {
            result.AppendLine($"        S{i},");
        }

        result.AppendLine("    }\n    private void Execute()\n    {\n        var pc = Step.S0;\n        var returns = new Stack<(Step End, Step Return)>();");
        if (flow.Any(i => i.Op == "sizeBranch"))
        {
            result.AppendLine("        var lastSizeError = false;");
        }

        foreach (var loop in flow.Where(i => i.Op == "loopInit").Select(i => i.Node!))
        {
            if (loop["times"] == null)
            {
                result.AppendLine($"        var first_{loop.S("id")} = false;");
            }

            if (loop["times"] != null)
            {
                result.AppendLine($"        var remaining_{loop.S("id")} = 0;");
            }
        }

        foreach (var evaluation in flow.Where(i => i.Op == "evaluateInit").Select(i => i.Node!).DistinctBy(n => n.S("id")))
        {
            result.AppendLine($"object evaluate_{evaluation.S("id")} = new Number(0);");
        }

        result.AppendLine($"        while (pc != {Pc(flow.Length)})\n        {{\n            try\n            {{\n            Tick();\n            switch (pc)\n            {{");
        for (var i = 0; i < flow.Length; i++)
        {
            var instruction = flow[i];
            var s = instruction.Node;
            currentStatement = s;
            for (JsonNode? context = s; context != null; context = context.Parent)
            {
                if (context is JsonObject && context["source"] != null && context.S("id").StartsWith('s'))
                {
                    currentStatement = context;
                    break;
                }
            }

            var next = Pc(i + 1);
            result.AppendLine($"                case {Pc(i)}:\n                {{\n                    pc = {next};");
            if (currentStatement?["source"] != null)
            {
                result.AppendLine($"location = {Quote(Location(currentStatement))};\ncurrentSource = {Metadata(currentStatement)};");
            }

            switch (instruction.Op)
            {
                case "branch":
                    result.AppendLine($"if (branches.Test({Quote(BranchId(currentStatement!))}, CobolValue.Truth({Expression(s!)})))\n{{\npc = {Pc(instruction.Next)};\n}}\nelse\n{{\npc = {Pc(instruction.Alternate)};\n}}");
                    break;
                case "sizeBranch":
                    var measuredSize = s!.S("kind") == "stringTransfer" || s!.B("hasSizeError") || s!.A("notError").Any();
                    var sizeCondition = measuredSize ? $"branches.Test({Quote(BranchId(s!))}, lastSizeError, {Quote(s!.S("kind") == "stringTransfer" ? "overflow" : "error")}, \"normal\")" : "lastSizeError";
                    result.AppendLine($"pc = {sizeCondition} ? {Pc(instruction.Next)} : {Pc(instruction.Alternate)};");
                    break;
                case "jump":
                case "goto":
                    if (s?.S("kind") == "exitPerform")
                    {
                        for (var parent = s.Parent; parent != null; parent = parent.Parent)
                        {
                            if (parent is JsonObject && parent.S("kind") == "loop")
                            {
                                result.AppendLine($"branches.FinishLoop({Quote(BranchId(parent))});");
                                break;
                            }
                        }
                    }

                    result.AppendLine($"pc = {Pc(instruction.Next)};");
                    break;
                case "gotoDepending":
                    result.AppendLine($"pc = CobolValue.Integer({Expression(s!["depending"]!)}) switch\n{{");
                    var t = 1;
                    foreach (var target in s.A("targetPcs"))
                    {
                        result.AppendLine($"{t++} => {Pc(target.GetValue<int>())},");
                    }

                    result.AppendLine($"_ => {next},\n}};");
                    break;
                case "perform":
                    result.AppendLine($"if (returns.Count > 10000)\n{{\nthrow new InvalidOperationException(\"PERFORM nesting limit\");\n}}\nreturns.Push(({Pc(instruction.Alternate)}, {next}));\npc = {Pc(instruction.Next)};");
                    break;
                case "boundary":
                    result.AppendLine($"if (returns.TryPeek(out var frame) && frame.End == {Pc(i)})\n{{\nreturns.Pop();\npc = frame.Return;\n}}");
                    break;
                case "evaluateInit":
                    result.AppendLine($"evaluate_{s!.S("id")} = {Expression(s!["subject"]!)};");
                    break;
                case "evaluateCheck":
                    var choice = s!["choice"]!;
                    result.AppendLine($"if ({string.Join(" || ", choice!.A("values").Select(v => $"CobolValue.Compare(evaluate_{s!.S("id")}, {Expression(v)}) == 0"))})\n{{\nbranches.Record({Quote(BranchId(s!))}, {Quote("when-" + s!.I("choiceIndex"))});\npc = {Pc(instruction.Next)};\n}}\nelse\n{{\npc = {Pc(instruction.Alternate)};\n}}");
                    break;
                case "evaluateOther":
                    result.AppendLine($"branches.Record({Quote(BranchId(s!))}, {Quote(s!.A("other").Any() ? "other" : "none")});");
                    break;
                case "loopInit":
                    result.AppendLine($"branches.StartLoop({Quote(BranchId(s!))});");
                    if (s!["times"] == null)
                    {
                        result.AppendLine($"first_{s.S("id")} = {Bool(s.B("testAfter"))};");
                    }

                    if (s["times"] is JsonNode times)
                    {
                        result.AppendLine($"remaining_{s.S("id")} = Math.Max(0, CobolValue.Integer({Expression(times)}));");
                    }

                    if (s["varying"] is JsonNode varying)
                    {
                        result.Append(Assign(varying["reference"]!, Expression(varying["from"]!)));
                    }

                    break;
                case "loopCheck":
                    var stop = s!["times"] != null ? $"remaining_{s.S("id")} <= 0" : s["until"] != null ? $"!first_{s.S("id")} && CobolValue.Truth({Expression(s["until"]!)})" : "false";
                    result.AppendLine($"pc = branches.Loop({Quote(BranchId(s!))}, !({stop})) ? {Pc(instruction.Next)} : {Pc(instruction.Alternate)};");
                    if (s["times"] == null)
                    {
                        result.AppendLine($"first_{s.S("id")} = false;");
                    }

                    break;
                case "loopNext":
                    if (s!["times"] != null)
                    {
                        result.AppendLine($"remaining_{s.S("id")}--;");
                    }

                    if (s["varying"] is JsonNode v)
                    {
                        result.Append(Assign(v["reference"]!, $"CobolValue.Numeric({Expression(v["reference"]!)}) + CobolValue.Numeric({Expression(v["by"]!)})"));
                    }

                    break;
                case "end":
                    result.AppendLine($"pc = {Pc(flow.Length)};");
                    break;
                default:
                    result.Append(Statement(s!, flow: true));
                    break;
            }

            result.AppendLine("                    break;\n                }");
        }

        result.AppendLine("            }\n            }\n            catch (CobolControlTransfer transfer)\n            {\n                pc = transfer.Target.ToUpperInvariant() switch\n                {");
        var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var paragraphIndex = 0;
        var paragraphStart = 0;
        var paragraphs = program.A("paragraphs").ToArray();
        for (var i = 0; i < flow.Length; i++)
        {
            if (flow[i].Op == "sentenceBoundary")
            {
                labels["$sentence_" + flow[i].Node!.S("id")] = i + 1;
            }

            if (flow[i].Op == "boundary")
            {
                var paragraph = paragraphs[paragraphIndex++];
                labels[paragraph.S("name")] = paragraphStart;
                if (paragraph.S("section") != string.Empty)
                {
                    labels.TryAdd(paragraph.S("section"), paragraphStart);
                }

                paragraphStart = i + 1;
            }
        }

        foreach (var (label, target) in labels)
        {
            result.AppendLine($"                    {Quote(label.ToUpperInvariant())} => {Pc(target)},");
        }

        result.AppendLine("                    _ => throw new InvalidOperationException(\"Unknown control target: \" + transfer.Target),\n                };\n            }\n        }\n    }");
        currentStatement = null;
        return result.ToString();
    }
}
