using System.Text;
using System.Text.Json.Nodes;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>選択した型構造を未記入値で示し、期待値や実行順を推測しません。</summary>
internal static class BusinessCaseTemplates
{
    /// <summary>最新の参考資料を管理側へ保存し、初回だけ利用者領域へ配置します。</summary>
    /// <param name="stage">検査済み stage。</param>
    /// <param name="key">contractId から決まる成果物キー。</param>
    /// <param name="mapping">明示した業務選択。</param>
    /// <param name="program">型と元位置の候補。</param>
    /// <param name="contract">生成予定の型契約。</param>
    /// <param name="first">利用者ファイルの初回作成。</param>
    internal static void Write(string stage, string key, JsonNode mapping, JsonNode program, JsonNode contract, bool first)
    {
        var guide = new StringBuilder("# Case guide\n\nFill caseId, independent provenance and reviewReason explicitly. Null scalar values are unfilled. expected.termination is unfilled; no successful termination is assumed. Templates are not discovered. Copy only completed cases into Tests/BusinessCases; Inspect/prepare validates them.\n\nResponses below are operation candidates, not a consumption script. Choose actual call counts/order and effects yourself. StatusCode/Outcome are mandatory explicit choices. Optional output null means no update: choose it intentionally, separately from leaving a field unfilled. FILE EOF, SQL no-data/NULL indicators and CALL passing modes retain their existing meaning.\n\nACCEPT static candidates do not prove consumption order. Default mode uses the reviewed fixed acceptOrder; acceptMode=case-sequence uses value arrays and acceptSequence for each case.\n\n| JSON Pointer | Type / PIC | Fixed count / element | Reason / original location |\n|---|---|---|---|\n");
        var budget = 10000;
        JsonNode? Shape(JsonNode type, string pointer, JsonNode? wire = null, JsonNode? candidate = null, int depth = 0)
        {
            var kind = type.S("type");
            var count = wire?["count"]?.GetValue<int>();
            var detail = kind == "array" ? "element: " + type["items"]!.ToJsonString() : string.Empty;
            guide.AppendLine($"| {Cell(pointer)} | {kind} {Cell(type.S("pic"))}{(type.B("nullable") ? " (null intentionally means no update)" : string.Empty)} | {count?.ToString() ?? "dynamic"} {Cell(detail)} | {Cell(candidate?.S("reason") ?? mapping.S("selectionReason"))}; {Cell(candidate?["location"]?.ToJsonString() ?? candidate?["source"]?.ToJsonString() ?? string.Empty)} |");
            if (depth >= 24 || --budget < 0)
            {
                guide.AppendLine("\nStructure limit: " + Cell(pointer) + " remains null; complete the full declared structure explicitly.\n");
                return null;
            }

            if (kind == "object")
            {
                var result = new JsonObject();
                foreach (var field in type["fields"]!.AsObject())
                {
                    var childWire = wire?.A("fields").SingleOrDefault(f => f.S("key") == field.Key);
                    result[field.Key] = Shape(field.Value!, pointer + "/" + Pointer(field.Key), childWire, candidate, depth + 1);
                }

                return result;
            }

            if (kind == "array")
            {
                if (count is > 100)
                {
                    guide.AppendLine("\nFull fixed array required: " + Cell(pointer) + " count=" + count + "; no truncated input is generated.\n");
                    return null;
                }

                var example = Shape(type["items"]!, pointer + "/*", wire?["items"], candidate, depth + 1);
                guide.AppendLine("\nElement example for " + Cell(pointer) + ": `" + (example?.ToJsonString() ?? "null") + "`. Dynamic counts/order must be selected independently.\n");
                return count == null ? new JsonArray() : new JsonArray(Enumerable.Range(0, count.Value).Select(_ => example?.DeepClone()).ToArray());
            }

            return null;
        }

        JsonObject Section(string section, string selectionName)
        {
            var result = new JsonObject();
            foreach (var field in contract[section]!["fields"]!.AsObject())
            {
                if (field.Key == "testState")
                {
                    var fields = new JsonObject();
                    foreach (var selected in mapping.A("initialState"))
                    {
                        var c = Candidate(selected, "test-state-injection");
                        fields[selected.S("key")] = Shape(field.Value!["fields"]![selected.S("key")]!, "/input/testState/" + Pointer(selected.S("key")), c?["wire"], c);
                    }

                    result[field.Key] = fields;
                    continue;
                }

                var selection = mapping.A(selectionName).SingleOrDefault(s => s.S("key") == field.Key);
                var candidate = selection == null ? null : Candidate(selection, section == "input" ? "formal-input" : "result");
                var wire = candidate?["wire"];
                if (section == "input" && mapping.S("acceptMode") == "case-sequence" && selection?.S("source") == "accept")
                {
                    wire = new JsonObject { ["kind"] = "array", ["items"] = wire?.DeepClone() };
                }

                result[field.Key] = Shape(field.Value!, (section == "input" ? "/input/" : "/expected/result/") + Pointer(field.Key), wire, candidate);
            }

            return result;
        }

        JsonNode? Candidate(JsonNode s, string role) => program.A("candidates").SingleOrDefault(c => c.S("role") == role && c.S("source") == s.S("source") && c.S("symbolId") == s.S("symbolId") && c.S("statementId") == s.S("statementId"));
        var item = new JsonObject { ["schemaVersion"] = 1, ["caseId"] = "UNFILLED", ["programId"] = mapping.S("programId"), ["contractId"] = mapping.S("contractId"), ["required"] = true, ["baselineKind"] = "specification", ["reviewReason"] = "UNREVIEWED: independent justification required", ["provenance"] = new JsonObject(), ["input"] = Section("input", "inputs"), ["responses"] = new JsonArray(), ["expected"] = new JsonObject { ["result"] = Section("result", "results"), ["effects"] = new JsonArray(), ["termination"] = null } };
        var examples = new JsonArray();
        foreach (var operation in contract["operations"]!.AsObject())
        {
            var selected = mapping.A("operations").Single(s => s.S("key") == operation.Key);
            var candidate = program.A("operations").Single(c => c.S("statementId") == selected.S("statementId"));
            JsonObject Payload(string direction)
            {
                var payload = new JsonObject();
                foreach (var field in operation.Value![direction]!["fields"]!.AsObject())
                {
                    var member = selected[direction]![field.Key]!.GetValue<string>();
                    var wire = candidate.A(direction + "Fields").SingleOrDefault(f => f.S("member") == member);
                    payload[field.Key] = Shape(field.Value!, "/responses/*/" + direction + "/" + Pointer(field.Key), wire, candidate);
                }

                return payload;
            }

            examples.Add(new JsonObject { ["operation"] = operation.Key, ["request"] = Payload("request"), ["response"] = Payload("response") });
        }

        foreach (var formal in program.A("candidates").Where(c => c.S("role") == "formal-input" && c.S("source") == "arguments" && !mapping.A("inputs").Any(s => s.S("source") == "arguments" && s.S("symbolId") == c.S("symbolId"))))
        {
            guide.AppendLine("\nmissing-formal-argument: " + Cell(formal.S("originalName")) + ". Select every required PROCEDURE USING argument; the Adapter must stop until explicitly supplied.\n");
        }

        if (Encoding.UTF8.GetByteCount(Generator.Json(item)) > 1024 * 1024)
        {
            item["input"] = null;
            item["expected"]!["result"] = null;
            guide.AppendLine("\nCase structure exceeds the size budget: input/result remain null. Complete all fields from the contract; no valid partial input is generated.\n");
        }

        Publish("case.json", Generator.Json(item));
        Publish("response-examples.json", Generator.Json(new JsonObject { ["status"] = "candidates-not-a-script", ["operations"] = examples }));
        Publish("case-guide.md", guide.ToString());
        void Publish(string file, string content)
        {
            CompletionWorkflow.Write(stage, "reports/business-scaffolds/" + key + "-" + file, content);
            var path = "Tests/Templates/Business/" + key + "/" + file + (file.EndsWith(".json", StringComparison.Ordinal) ? ".txt" : string.Empty);
            if (first && !File.Exists(Path.Combine(stage, path)))
            {
                CompletionWorkflow.Write(stage, path, content);
            }
        }
    }

    /// <summary>業務キーを JSON Pointer の一要素として表示します。</summary>
    private static string Pointer(string key) => key.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    /// <summary>原位置と選択理由を表の単一セルに保ちます。</summary>
    private static string Cell(string text) => (text.Length > 2048 ? text[..2048] + "… (full structure in contract)" : text).Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");
}
