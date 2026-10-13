using System.Text;
using System.Text.Json.Nodes;
using CobolCS.Runtime;
using static CobolCS.Cli.BusinessProjection;

namespace CobolCS.Cli;

/// <summary>明示 mapping を検査し、利用者所有の業務雛形を初回だけ公開します。</summary>
internal static class BusinessScaffolding
{
    /// <summary>採用前の source 出力だけを stage で更新します。</summary>
    /// <param name="root">検査対象の生成先。</param>
    /// <param name="programId">必須 PROGRAM-ID。</param>
    /// <param name="mappingPath">型と候補を選択する JSON。</param>
    /// <returns>公開完了の終了コード。</returns>
    internal static int Run(string root, string programId, string mappingPath)
    {
        _ = BusinessCases.FileHash(Path.GetFullPath(mappingPath));
        var mapping = BusinessComparison.Parse(File.ReadAllText(mappingPath));
        return CompletionWorkflow.Stage(root, (stage, manifest) => Create(stage, manifest, programId, mapping));
    }

    /// <summary>検査が全て完了するまでユーザー成果物へ触れません。</summary>
    /// <param name="stage">一式の複製。</param>
    /// <param name="manifest">生成台帳。</param>
    /// <param name="programId">指定境界。</param>
    /// <param name="mapping">明示選択。</param>
    /// <returns>完了コード。</returns>
    private static int Create(string stage, JsonNode manifest, string programId, JsonNode mapping)
    {
        BusinessComparison.Keys(mapping, ["schemaVersion", "programId", "candidateHash", "contractId", "version", "selectionReason", "inputs", "results", "operations", "termination", "acceptOrder", "acceptMode", "initialState"], ["schemaVersion", "programId", "candidateHash", "contractId", "version", "selectionReason", "inputs", "results", "operations", "termination"]);
        if (!manifest["judgments"]!.B("generated") || mapping.I("schemaVersion") != 1 || mapping.S("programId") != programId || mapping.I("version") < 1 || string.IsNullOrWhiteSpace(mapping.S("contractId")) || mapping.S("contractId").Length > 128 || string.IsNullOrWhiteSpace(mapping.S("selectionReason")) || mapping.S("selectionReason").StartsWith("UNFILLED", StringComparison.Ordinal))
        {
            throw new ArgumentException("Invalid business mapping header or program");
        }

        var report = J.Parse(File.ReadAllText(Path.Combine(stage, "reports/business-test-candidates.json")));
        var program = report.A("programs").SingleOrDefault(p => p.S("programId") == programId) ?? throw new ArgumentException("Unknown program");
        if (mapping.S("candidateHash") != program.S("candidateHash"))
        {
            throw new ArgumentException("Stale business candidates; regenerate and review mapping");
        }

        if (mapping["inputs"] is not JsonArray || mapping["results"] is not JsonArray { Count: > 0 } || mapping["operations"] is not JsonArray || mapping["termination"] is not JsonObject { Count: > 0 })
        {
            throw new ArgumentException("Explicit business selections required");
        }

        var caseSequence = mapping.S("acceptMode") == "case-sequence";
        if ((mapping["acceptMode"] != null && !caseSequence) || (caseSequence && mapping["acceptOrder"] != null))
        {
            throw new ArgumentException("acceptMode must be case-sequence and cannot be combined with acceptOrder");
        }

        var inputs = Select(mapping.A("inputs"), program, true);
        var results = Select(mapping.A("results"), program, false);
        var state = Select(mapping.A("initialState"), program, false, true);
        if (inputs.Any(i => i.Selection.S("key") is "testState" or "acceptSequence"))
        {
            throw new ArgumentException("testState and acceptSequence are reserved input names");
        }

        var operations = new List<(JsonNode Selection, JsonNode Candidate)>();
        var operationKeys = new HashSet<string>(StringComparer.Ordinal);
        var statementIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var selection in mapping.A("operations"))
        {
            BusinessComparison.Keys(selection, ["key", "statementId", "request", "response"], ["key", "statementId", "request", "response"]);
            if (!Key(selection.S("key")) || !operationKeys.Add(selection.S("key")) || !statementIds.Add(selection.S("statementId")))
            {
                throw new ArgumentException("Duplicate or invalid operation key/statement");
            }

            var candidate = program.A("operations").SingleOrDefault(o => o.S("statementId") == selection.S("statementId")) ?? throw new ArgumentException("Unknown external statement");
            if (!candidate.B("supported"))
            {
                throw new ArgumentException("Unsupported external payload layout");
            }

            ValidateFields(selection["request"]!, candidate.A("requestFields").Select(f => f.S("member")));
            ValidateFields(selection["response"]!, candidate.A("responseFields").Select(f => f.S("member")).Prepend("Outcome").Prepend("StatusCode"));
            operations.Add((selection, candidate));
        }

        foreach (var (termination, value) in mapping["termination"]!.AsObject())
        {
            if (!new[] { "Goback", "StopRun", "ExitProgram", "Fallthrough" }.Contains(termination, StringComparer.Ordinal) || !Key(value?.GetValue<string>() ?? string.Empty))
            {
                throw new ArgumentException("Invalid explicit termination projection");
            }
        }

        var accepts = inputs.Where(i => i.Selection.S("source") == "accept").ToArray();
        var order = caseSequence ? [] : mapping["acceptOrder"] == null ? program.B("fixedAcceptOrder") ? program.A("acceptStatements").Select(a => a.S("statementId")).ToArray() : accepts.Length == 0 ? [] : throw new ArgumentException("Conditional/loop ACCEPT requires explicit acceptOrder; review each case") : mapping["acceptOrder"]!.AsArray().Select(a => a!.GetValue<string>()).ToArray();
        if (!caseSequence && (order.Any(id => accepts.Count(i => i.Candidate.S("statementId") == id) != 1) || accepts.Any(i => !order.Contains(i.Candidate.S("statementId"), StringComparer.Ordinal))))
        {
            throw new ArgumentException("ACCEPT sequence contains unknown/unselected statements; select all consumed inputs explicitly");
        }

        var contractId = mapping.S("contractId");
        var contractKey = "Business_" + Generator.Hash(Encoding.UTF8.GetBytes(contractId));
        var templateRoot = "Tests/Templates/Business/" + contractKey;
        var reportPath = "reports/business-scaffolds/" + contractKey + ".json";
        var normalized = Generator.Json(mapping);
        var mappingHash = Generator.Hash(Encoding.UTF8.GetBytes(normalized));
        var contract = Contract(mapping, inputs, results, operations, state);
        if (File.Exists(Path.Combine(stage, reportPath)))
        {
            var prior = J.Parse(File.ReadAllText(Path.Combine(stage, reportPath)));
            if (prior.S("mappingHash") != mappingHash)
            {
                prior["proposal"] = new JsonObject { ["mapping"] = mapping.DeepClone(), ["mappingHash"] = mappingHash, ["action"] = "User files preserved. Review the proposal and use a new contractId for separate artifacts, or explicitly edit and re-review the existing contract." };
            }
            else
            {
                prior.AsObject().Remove("proposal");
            }

            CompletionWorkflow.Write(stage, reportPath, Generator.Json(prior));

            BusinessCaseTemplates.Write(stage, contractKey, mapping, program, contract, false);
            BusinessReviewReference.Write(stage, manifest, templateRoot, false);
            return 0;
        }

        var artifacts = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["UserCode/Business/" + contractKey + "Adapter.cs"] = SourceFormatting.Indent(Adapter(program, mapping, contractKey, inputs, results, operations, order, state)),
            ["Tests/BusinessContracts/" + contractKey + ".json"] = Generator.Json(contract),
            [templateRoot + "/registration.cs.txt"] = $"adapters.Add({Quote(contractId)}, () => new {contractKey}Adapter());\n",
            [templateRoot + "/mapping.json.txt"] = normalized,
            [templateRoot + "/README.md"] = "# Explicit business boundary\n\nThis mapping selects only the listed values. Group field keys are provisional and require review. Fill independent case input/responses/expected values, then copy case.json.txt into Tests/BusinessCases and append registration.cs.txt inside BusinessRegistration.Register. Templates are not discovered. Never infer expected values from generated execution.\n\nUse prepare, Category=Business tests, explicit review entries, then adopt. Map StatusCode/Outcome explicitly; null Optional output means no update. Use acceptMode=case-sequence for conditional/loop ACCEPT with arrays per selected key and input.acceptSequence. Do not combine acceptMode with acceptOrder. Required formal arguments cannot be omitted. See case-guide.md and response-examples.json.txt. Unknown termination throws Preparation.Required. Internal completion helpers can be edited; external Stubs are never used by this adapter.\n\nOn repeat, adapters/contracts/templates and deletions are preserved. Changed mapping is a managed proposal, not a patch. Current hash references are in reports/business-scaffolds/*-review-files.json; copy only after reviewing, never as automatic approval. Adopted projects use dotnet test and do not accept scaffold-business.\n",
        };
        if (artifacts.Keys.Concat(new[] { templateRoot + "/case.json.txt", templateRoot + "/response-examples.json.txt", templateRoot + "/case-guide.md" }).Any(p => File.Exists(Path.Combine(stage, p))))
        {
            throw new IOException("Existing business artifacts have no scaffold provenance; use a new contractId");
        }

        foreach (var (path, content) in artifacts) CompletionWorkflow.Write(stage, path, content);
        BusinessCaseTemplates.Write(stage, contractKey, mapping, program, contract, true);
        foreach (var file in new[] { "case.json.txt", "response-examples.json.txt", "case-guide.md" })
        {
            artifacts[templateRoot + "/" + file] = File.ReadAllText(Path.Combine(stage, templateRoot, file));
        }

        var evidence = new JsonObject { ["schemaVersion"] = 1, ["programId"] = programId, ["contractId"] = contractId, ["mapping"] = mapping.DeepClone(), ["mappingHash"] = mappingHash, ["candidateHash"] = program.S("candidateHash"), ["sourceHashes"] = manifest["inputHashes"]!.DeepClone(), ["configHash"] = manifest.S("configHash"), ["contractHashes"] = manifest["contractHashes"]!.DeepClone(), ["files"] = new JsonArray(artifacts.Keys.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray()), ["unfilled"] = new JsonArray("case ID", "independent inputs/responses/expectations", "factory registration", "review approval"), ["status"] = "unconfigured-user-owned" };
        CompletionWorkflow.Write(stage, reportPath, Generator.Json(evidence));
        BusinessReviewReference.Write(stage, manifest, templateRoot, true);
        return 0;
    }

    /// <summary>キーはデータであり、C# 式・ファイル名として使いません。</summary>
    private static bool Key(string key) => !string.IsNullOrWhiteSpace(key) && key.Length <= 128 && !key.StartsWith("UNFILLED", StringComparison.Ordinal);

    /// <summary>request/response の全 field を明示的に一対一選択します。</summary>
    private static void ValidateFields(JsonNode selection, IEnumerable<string> expected)
    {
        if (selection is not JsonObject fields || fields.Any(p => !Key(p.Key) || p.Value is not JsonValue) || !fields.Select(p => p.Value!.GetValue<string>()).Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal)))
        {
            throw new ArgumentException("Unknown, duplicate or missing typed payload field");
        }
    }

    /// <summary>source と役割の取り違え・重複選択を拒否します。</summary>
    private static List<(JsonNode Selection, JsonNode Candidate)> Select(IEnumerable<JsonNode> selections, JsonNode program, bool input, bool state = false)
    {
        var result = new List<(JsonNode Selection, JsonNode Candidate)>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var selection in selections)
        {
            BusinessComparison.Keys(selection, ["key", "source", "symbolId", "statementId", "type"], ["key", "source"]);
            if (!Key(selection.S("key")) || !keys.Add(selection.S("key")) || !identities.Add(selection.S("source") + ":" + selection.S("symbolId") + ":" + selection.S("statementId")))
            {
                throw new ArgumentException("Duplicate business key or selected ID");
            }

            var candidate = program.A("candidates").SingleOrDefault(c => c.S("source") == selection.S("source") && c.S("symbolId") == selection.S("symbolId") && c.S("statementId") == selection.S("statementId") && c.S("role") == (state ? "test-state-injection" : input ? "formal-input" : "result")) ?? throw new ArgumentException("Unknown ID/source or invalid input/result role");
            if (candidate["wire"] == null)
            {
                throw new ArgumentException("Unknown layout/conversion requires explicit internal completion");
            }

            if (selection["type"] != null && !JsonNode.DeepEquals(selection["type"], Descriptor(candidate["wire"]!)))
            {
                throw new ArgumentException("Business mapping type mismatch");
            }

            if (input && selection.S("source") == "accept" && candidate["wire"]!.S("kind") is not ("number" or "string"))
            {
                throw new ArgumentException("ACCEPT table requires explicit case-specific conversion");
            }

            result.Add((selection, candidate));
        }

        return result;
    }

    /// <summary>明示選択だけを比較契約へ追加します。</summary>
    private static JsonObject Contract(JsonNode mapping, List<(JsonNode Selection, JsonNode Candidate)> inputs, List<(JsonNode Selection, JsonNode Candidate)> results, List<(JsonNode Selection, JsonNode Candidate)> operations, List<(JsonNode Selection, JsonNode Candidate)> state)
    {
        JsonObject Object(IEnumerable<(JsonNode Selection, JsonNode Candidate)> selected) => new() { ["type"] = "object", ["fields"] = new JsonObject(selected.Select(i => KeyValuePair.Create<string, JsonNode?>(i.Selection.S("key"), Descriptor(i.Candidate["wire"]!)))) };
        var ops = new JsonObject();
        foreach (var (selection, candidate) in operations)
        {
            JsonObject Payload(string direction)
            {
                var fields = new JsonObject();
                foreach (var (key, value) in selection[direction]!.AsObject())
                {
                    var member = value!.GetValue<string>();
                    var wire = candidate.A(direction + "Fields").SingleOrDefault(f => f.S("member") == member);
                    var descriptor = wire == null ? new JsonObject { ["type"] = "string" } : Descriptor(wire);
                    if (direction == "response" && wire != null)
                    {
                        descriptor["nullable"] = true;
                    }

                    fields[key] = descriptor;
                }

                return new JsonObject { ["type"] = "object", ["fields"] = fields };
            }

            ops[selection.S("key")] = new JsonObject { ["request"] = Payload("request"), ["response"] = Payload("response") };
        }

        var inputType = Object(inputs);
        if (mapping.S("acceptMode") == "case-sequence")
        {
            foreach (var accept in inputs.Where(i => i.Selection.S("source") == "accept"))
            {
                inputType["fields"]![accept.Selection.S("key")] = new JsonObject { ["type"] = "array", ["items"] = Descriptor(accept.Candidate["wire"]!) };
            }

            inputType["fields"]!["acceptSequence"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } };
        }

        if (state.Count > 0)
        {
            inputType["fields"]!["testState"] = Object(state);
        }

        var contract = new JsonObject { ["schemaVersion"] = 1, ["contractId"] = mapping.S("contractId"), ["version"] = mapping.I("version"), ["reason"] = mapping.S("selectionReason"), ["input"] = inputType, ["result"] = Object(results), ["termination"] = new JsonObject { ["type"] = "string" }, ["operations"] = ops, ["sourceReferences"] = new JsonArray(inputs.Concat(results).Concat(operations).Select(i => i.Candidate["location"]?.DeepClone() ?? i.Candidate["source"]?.DeepClone()).ToArray()) };
        if (mapping.S("acceptMode") == "case-sequence")
        {
            contract["acceptInputs"] = new JsonObject { ["sequenceField"] = "acceptSequence", ["valueFields"] = new JsonArray(inputs.Where(i => i.Selection.S("source") == "accept").Select(i => (JsonNode?)JsonValue.Create(i.Selection.S("key"))).ToArray()) };
        }

        return contract;
    }

    /// <summary>型付き fake を必ず渡し、編集済みの外部 Stubs への fallback を遮断します。</summary>
    private static string Adapter(JsonNode program, JsonNode mapping, string key, List<(JsonNode Selection, JsonNode Candidate)> inputs, List<(JsonNode Selection, JsonNode Candidate)> results, List<(JsonNode Selection, JsonNode Candidate)> operations, string[] acceptOrder, List<(JsonNode Selection, JsonNode Candidate)> state)
    {
        var code = new StringBuilder("#nullable enable\nusing System.Text.Json.Nodes;\nusing CobolCS.Runtime;\n\nnamespace CobolCS.Generated;\n\n");
        code.AppendLine($"/// <summary>利用者が明示した業務境界。独立期待値とレビューを別途確定します。</summary>\npublic sealed class {key}Adapter : IBusinessCaseAdapter\n{{\n/// <summary>Gets 固定業務契約。</summary>\npublic string ContractId => {Quote(mapping.S("contractId"))};\n\n/// <summary>ケース専用の Executor と fake を作ります。</summary>\n/// <param name=\"input\">明示入力と固定応答。</param>\n/// <returns>選択した結果と外部効果。</returns>\npublic BusinessObservation Execute(BusinessCaseInput input)\n{{\nvar fake = new {program.S("fakeType")}();\nforeach (var operation in input.External.BindingOperations)\n{{\nswitch (operation)\n{{");
        foreach (var (selection, candidate) in operations)
        {
            var request = "new JsonObject { " + string.Join(", ", selection["request"]!.AsObject().Select(p => "[" + Quote(p.Key) + "] = " + Write(candidate.A("requestFields").Single(f => f.S("member") == p.Value!.GetValue<string>()), "request." + p.Value!.GetValue<string>()))) + " }";
            var responseFields = selection["response"]!.AsObject().ToDictionary(p => p.Value!.GetValue<string>(), p => p.Key, StringComparer.Ordinal);
            var response = $"new {candidate.S("responseType")}(StatusCode: response[{Quote(responseFields["StatusCode"])}]!.GetValue<string>(), Outcome: BusinessValues.Outcome<DependencyOutcome>(response[{Quote(responseFields["Outcome"])}])) {{ " + string.Join(", ", candidate.A("responseFields").Select(f => f.S("member") + " = response[" + Quote(responseFields[f.S("member")]) + "] == null ? default : new Optional<" + f.S("csharpType") + ">(" + Read(f, "response[" + Quote(responseFields[f.S("member")]) + "]") + ")")) + " }";
            code.AppendLine($"case {Quote(selection.S("key"))}:\ninput.External.BindNext<{candidate.S("requestType")}, {candidate.S("responseType")}>({Quote(selection.S("key"))}, fake.{candidate.S("expectMethod")}, request => {request}, response => {response});\nbreak;");
        }

        code.AppendLine("default: throw new ArgumentException(\"Unselected business operation: \" + operation);\n}\n}\n");
        if (program.I("blockedExternal") > 0)
        {
            code.AppendLine("Preparation.Required<bool>(\"Unsupported external/unknown block: resolve layout and use typed fake before business execution\");");
        }

        foreach (var formal in program.A("candidates").Where(c => c.S("source") == "arguments" && c.S("role") == "formal-input" && !inputs.Any(i => i.Selection.S("source") == "arguments" && i.Candidate.S("symbolId") == c.S("symbolId"))))
        {
            code.Replace("var fake = new ", "Preparation.Required<bool>(" + Quote("missing-formal-argument: " + formal.S("originalName")) + ");\nvar fake = new ");
        }

        var accepts = inputs.Where(i => i.Selection.S("source") == "accept").ToArray();
        var acceptValues = acceptOrder.Select(id => accepts.Single(i => i.Candidate.S("statementId") == id)).Select(i => i.Candidate["wire"]!.S("kind") == "number" ? $"BusinessValues.Decimal({Read(i.Candidate["wire"]!, "input.Values[" + Quote(i.Selection.S("key")) + "]")}, {i.Candidate["wire"]!.I("scale")})" : Read(i.Candidate["wire"]!, "input.Values[" + Quote(i.Selection.S("key")) + "]"));
        var acceptBinding = "AcceptInputs = new string[] { " + string.Join(", ", acceptValues) + " }";
        if (mapping.S("acceptMode") == "case-sequence")
        {
            code.AppendLine("var accept = new BusinessAcceptScript(input.Values, new string[] { " + string.Join(", ", accepts.Select(i => Quote(i.Selection.S("key")))) + " });");
            acceptBinding = "AcceptInput = new CaseAcceptInput(accept)";
        }

        var arguments = inputs.Where(i => i.Selection.S("source") == "arguments").Select(i => i.Candidate.S("expression") + " = " + Read(i.Candidate["wire"]!, "input.Values[" + Quote(i.Selection.S("key")) + "]"));
        var setup = state.Select(i =>
        {
            var wire = i.Candidate["wire"]!;
            var read = Read(wire, "input.Values[\"testState\"]![" + Quote(i.Selection.S("key")) + "]");
            if (wire.S("kind") == "array")
            {
                var dims = string.Join(", ", i.Candidate.A("dimensions").Select(d => d.GetValue<int>()));
                read += $".Select((item, index) => new TableEntry<{wire["items"]!.S("csharpType")}>(BusinessValues.Subscripts(index, new int[] {{ {dims} }}), item)).ToArray()";
            }

            return i.Candidate.S("expression") + " = " + read;
        });
        code.AppendLine($"var value = new {program.S("inputType")} {{ Arguments = new {program.S("argumentsType")} {{ {string.Join(", ", arguments)} }}, {acceptBinding}, InitialState = new {program.S("setupType")} {{ {string.Join(", ", setup)} }} }};\nvar result = new {program.S("executorType")}().Run(value, fake);\ninput.External.VerifyComplete();\nvar output = new JsonObject {{ {string.Join(", ", results.Select(i => "[" + Quote(i.Selection.S("key")) + "] = " + Write(i.Candidate["wire"]!, i.Candidate.S("expression"))))} }};\nvar termination = result.Termination switch\n{{");
        if (mapping.S("acceptMode") == "case-sequence")
        {
            code.Replace("input.External.VerifyComplete();", "accept.VerifyComplete();\ninput.External.VerifyComplete();");
        }

        foreach (var (termination, value) in mapping["termination"]!.AsObject()) code.AppendLine($"TerminationKind.{termination} => {Quote(value!.GetValue<string>())},");
        code.AppendLine("_ => Preparation.Required<string>(\"Explicit business termination mapping\"),\n};\nreturn new(output, input.External.Effects, termination, result.HasNoOpCalls);\n}");
        if (mapping.S("acceptMode") == "case-sequence")
        {
            code.AppendLine("\n/// <summary>ケースの業務キーと生成文の元位置を結び付けます。</summary>\n/// <param name=\"script\">ケース専用の消費器。</param>\nprivate sealed class CaseAcceptInput(BusinessAcceptScript script) : IAcceptInput\n{\n/// <summary>実行文に対応する入力だけを消費します。</summary>\n/// <param name=\"source\">文の元位置。</param>\n/// <returns>丸めない数値文字列または完全な文字列。</returns>\npublic string Read(SourceLocation source)\n{\nswitch (source.StatementId)\n{");
            foreach (var accept in accepts)
            {
                var wire = accept.Candidate["wire"]!;
                var value = Read(wire, "script.Read(" + Quote(accept.Selection.S("key")) + ")");
                if (wire.S("kind") == "number")
                {
                    value = "BusinessValues.Decimal(" + value + ", " + wire.I("scale") + ")";
                }

                code.AppendLine("case " + Quote(accept.Candidate.S("statementId")) + ": return " + value + ";");
            }

            code.AppendLine("default: throw BusinessAcceptScript.Unmapped(source.ToString());\n}\n}\n}");
        }

        code.AppendLine("}");
        return code.ToString().Replace("{  }", "{ }", StringComparison.Ordinal);
    }
}
