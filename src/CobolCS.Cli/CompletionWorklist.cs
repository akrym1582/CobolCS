using System.Text;
using System.Text.Json.Nodes;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>静的候補、手動メモ、実測を混同せず、次に編集する場所を再構成します。</summary>
internal static class CompletionWorklist
{
    /// <summary>コード実行やソース解析による実装済み判定を行わない案内です。</summary>
    /// <param name="root">固定した生成先または stage。</param>
    /// <param name="manifest">現在の入力と契約。</param>
    internal static void Update(string root, JsonNode manifest)
    {
        var readinessPath = Path.Combine(root, "reports/execution-readiness.json");
        var readiness = File.Exists(readinessPath) ? J.Parse(File.ReadAllText(readinessPath)) : new JsonObject { ["items"] = new JsonArray() };
        var ir = J.Parse(File.ReadAllText(Path.Combine(root, "reports/ir.json")));
        var stubPath = Path.Combine(root, "reports/stubs.json");
        var stubs = File.Exists(stubPath) ? J.Parse(File.ReadAllText(stubPath)).AsArray() : new JsonArray();
        var manualPath = Path.Combine(root, "UserCode/Completion/work-items.json");
        var manual = File.Exists(manualPath) ? J.Parse(File.ReadAllText(manualPath)) : new JsonObject { ["schemaVersion"] = 1, ["items"] = new JsonArray() };
        if (manual.I("schemaVersion") != 1 || manual["items"] is not JsonArray || manual.A("items").Any(i => i.S("itemId") == string.Empty || (i["status"]?.GetValue<string>() ?? "open") is not ("open" or "in-progress" or "done")) || manual.A("items").GroupBy(i => i.S("itemId"), StringComparer.Ordinal).Any(g => g.Count() != 1))
        {
            throw new IOException("Invalid UserCode/Completion/work-items.json; expected unique itemId and open/in-progress/done status.");
        }

        var fingerprint = CompletionWorkflow.Fingerprint(root, manifest);
        var records = Generator.Inventory(root).Keys.Where(CompletionWorkflow.IsRecord).OrderDescending(StringComparer.Ordinal).Select(p => J.Parse(File.ReadAllText(Path.Combine(root, p)))).ToArray();
        foreach (var record in records)
        {
            if (record.I("schemaVersion") != 1 || record.S("status") is not ("started" or "completed" or "failed" or "incomplete"))
            {
                throw new IOException("Invalid execution record");
            }
        }

        var items = new JsonArray();
        var sourceIdentity = Generator.Json(manifest["inputHashes"]!);
        foreach (var candidate in readiness.A("items"))
        {
            var stub = stubs.FirstOrDefault(s => s!.S("programId") == candidate.S("programId") && s!.S("statementId") == candidate.S("statementId") && (candidate["stubImplementationFile"] == null || s!.S("file") == candidate.S("stubImplementationFile")));
            var role = stub?.S("executionRole") ?? (stub == null ? string.Empty : StubRoles.Classify(stub["statement"]!, stub.S("capability") == "adapter-required"));
            var category = candidate.S("classification") switch
            {
                "unknown-declaration" => "unknown-layout",
                "accept-input" or "linkage-input" => "required-input",
                _ => role == "internal-logic" ? "internal-implementation" : "external-response",
            };
            var originalProgram = ir.A("programs").FirstOrDefault(p => p.S("id") == candidate.S("programId"));
            var original = originalProgram == null ? null : Validation.Walk(originalProgram["paragraphs"]).FirstOrDefault(n => n.S("id") == candidate.S("statementId"));
            original ??= originalProgram?.A("symbols").FirstOrDefault(n => "linkage_" + n.S("id") == candidate.S("statementId"));
            var itemId = Generator.Hash(Encoding.UTF8.GetBytes(sourceIdentity + candidate.S("programId") + category + candidate.S("statementId") + candidate.S("editFile")))[..24];
            var hand = manual.A("items").SingleOrDefault(i => i.S("itemId") == itemId);
            var latest = records.FirstOrDefault(r => r.S("programId") == candidate.S("programId") || r.S("programId") == "all");
            var valid = latest != null && latest.S("fingerprint") == fingerprint && InputCurrent(root, latest);
            var trials = latest?.A("trials").Where(t => t.S("programId") == candidate.S("programId")).ToArray() ?? [];
            var failed = trials.LastOrDefault(t => t["failure"]?["source"]?.S("StatementId") == candidate.S("statementId"));
            var reached = trials.SelectMany(t => t.A("internalTrace")).Where(t => t.S("StatementId") == candidate.S("statementId")).ToArray();
            var runFailure = latest?["failure"]?["source"]?.S("StatementId") == candidate.S("statementId");
            var runReached = latest?.A("internalTrace").Where(t => t.S("StatementId") == candidate.S("statementId")).ToArray() ?? [];
            var observation = latest == null ? "not-observed" : !valid ? "stale" : failed != null || runFailure || (reached.Any(t => !t.B("Completed")) && !reached.Any(t => t.B("Completed"))) || (runReached.Any(t => !t.B("Completed")) && !runReached.Any(t => t.B("Completed"))) ? "reached-failed" : reached.Any(t => t.B("Completed")) || runReached.Any(t => t.B("Completed")) ? "reached-completed" : "not-observed";
            items.Add(new JsonObject
            {
                ["schemaVersion"] = 1,
                ["itemId"] = itemId,
                ["programId"] = candidate.S("programId"),
                ["category"] = category,
                ["statementId"] = candidate.S("statementId"),
                ["symbolId"] = candidate.A("fields").FirstOrDefault()?["symbolId"]?.DeepClone(),
                ["source"] = stub?["source"]?.DeepClone() ?? original?["source"]?.DeepClone() ?? candidate["source"]?.DeepClone(),
                ["reasonCode"] = candidate.S("classification"),
                ["reason"] = candidate.S("reason"),
                ["executionRole"] = role,
                ["fields"] = candidate["fields"]?.DeepClone(),
                ["readCandidates"] = stub?["referencedLayouts"]?.DeepClone() ?? candidate["inputs"]?.DeepClone(),
                ["writeCandidates"] = candidate["outputs"]?.DeepClone(),
                ["edit"] = new JsonObject { ["path"] = candidate.S("editFile"), ["type"] = candidate.S("contractType"), ["method"] = stub?.S("method") ?? candidate.S("exampleMethod") },
                ["responseType"] = candidate["responseType"]?.DeepClone(),
                ["responseOutcomes"] = category == "external-response" ? new JsonArray("normal", "EOF/no-data", "error") : null,
                ["confirmCommand"] = "cobolcs run . --program " + candidate.S("programId") + " --scenario sample",
                ["blockedBy"] = new JsonArray(),
                ["manualStatus"] = hand?["status"]?.GetValue<string>() ?? "open",
                ["manualNotes"] = hand?["notes"]?.DeepClone(),
                ["assignee"] = hand?["assignee"]?.DeepClone(),
                ["observedStatus"] = observation,
                ["fileChanged"] = stub != null && File.Exists(Path.Combine(root, stub.S("file"))) && manifest["stubTemplateHashes"]?[stub.S("file")]?.GetValue<string>() != Generator.Hash(File.ReadAllBytes(Path.Combine(root, stub.S("file")))),
                ["latestExecutionId"] = latest?.S("executionId"),
            });
        }

        var diagnostics = J.Parse(File.ReadAllText(Path.Combine(root, "reports/diagnostics.json")));
        var missingCopy = diagnostics.AsArray().Any(d => d!.S("code").Contains("COPY", StringComparison.Ordinal));
        foreach (var diagnostic in diagnostics.AsArray().Where(d => d!.S("code").Contains("COPY", StringComparison.Ordinal)))
        {
            AddAuxiliary(items, sourceIdentity, diagnostic!.S("programId"), "missing-copy", diagnostic!.S("code"), diagnostic!.S("message"), diagnostic!["source"]?.S("file") ?? "reports/diagnostics.json", diagnostic!["source"]);
        }

        foreach (var diagnostic in diagnostics.AsArray().Where(d => missingCopy && d!.S("code") == "PARSE_ERROR" && d!.S("message").Contains("layout", StringComparison.OrdinalIgnoreCase)))
        {
            AddAuxiliary(items, sourceIdentity, diagnostic!.S("programId"), "unknown-layout", diagnostic!.S("code"), diagnostic!.S("message"), diagnostic!["source"]?.S("file") ?? "reports/diagnostics.json", diagnostic!["source"]);
        }

        foreach (var program in manifest.A("programs"))
        {
            var configured = BusinessCases.Load(root).Count(c => c.Case.S("programId") == program.GetValue<string>());
            AddAuxiliary(items, sourceIdentity, program.GetValue<string>(), "test-expectation", configured == 0 ? "business-cases-unconfigured" : "business-expectations-review-required", configured == 0 ? "Business names and independent expected values must be selected explicitly; templates are not successful tests." : configured + " configured business cases; execute the fixed comparisons and review their provenance. Configuration is not a passed test.", configured == 0 ? "Tests/Templates/Business/README.md" : "Tests/BusinessCases", null);
            AddAuxiliary(items, sourceIdentity, program.GetValue<string>(), "review", "review-required", "Review current cases, internal completion, helper code and adapter hashes before adopt.", "Tests/review.json", null);
        }

        foreach (var record in records.Take(1).Where(r => r["blocked"] != null))
        {
            AddAuxiliary(items, sourceIdentity, record.S("programId"), "build-error", "execution-blocked", record.S("blocked"), "reports/executions/" + record.S("executionId") + ".json", null);
            items[^1]!["observedStatus"] = record.S("fingerprint") == fingerprint ? "reached-failed" : "stale";
        }

        foreach (var hand in manual.A("items").Where(h => !items.Any(i => i!.S("itemId") == h.S("itemId"))))
        {
            items.Add(new JsonObject { ["schemaVersion"] = 1, ["itemId"] = hand.S("itemId"), ["programId"] = "unknown", ["category"] = "review", ["reasonCode"] = "orphaned", ["reason"] = "Source identity changed; manual notes were not reassigned to a different statement.", ["manualStatus"] = hand["status"]?.GetValue<string>() ?? "open", ["manualNotes"] = hand["notes"]?.DeepClone(), ["observedStatus"] = "stale", ["orphaned"] = true });
        }

        int Priority(JsonNode item) => item.S("category") is "missing-copy" or "unknown-layout" ? 0 : item.S("category") == "build-error" ? 1 : item.S("observedStatus") == "reached-failed" ? 2 : item.S("category") is "test-expectation" or "review" ? 4 : 3;
        foreach (var work in items)
        {
            var hand = manual.A("items").SingleOrDefault(h => h.S("itemId") == work!.S("itemId"));
            if (hand != null)
            {
                work!["manualStatus"] = hand["status"]?.GetValue<string>() ?? "open";
                work["manualNotes"] = hand["notes"]?.DeepClone();
                work["assignee"] = hand["assignee"]?.DeepClone();
            }

            work!["blockedBy"] = new JsonArray(items.Where(other => other!.S("category") is "missing-copy" or "unknown-layout" && other!.S("itemId") != work.S("itemId") && (other!.S("programId") == work.S("programId") || other!.S("programId") == string.Empty)).Select(other => (JsonNode?)JsonValue.Create(other!.S("itemId"))).ToArray());
        }

        var ordered = items.OrderBy(i => Priority(i!)).ThenBy(i => i!.S("itemId"), StringComparer.Ordinal).ToArray();
        var report = new JsonObject { ["schemaVersion"] = 1, ["programs"] = manifest["programs"]!.DeepClone(), ["fingerprint"] = fingerprint, ["latestExecution"] = records.FirstOrDefault()?.DeepClone(), ["limitations"] = "Static candidates are not required inputs on every path. Changed files and manual done do not prove completion. A successful case is not all-input coverage. Internal helpers must not connect to external systems. Worker isolation is not a security sandbox.", ["items"] = new JsonArray(ordered.Select(i => i!.DeepClone()).ToArray()) };
        var markdown = new StringBuilder("# Completion worklist\n\nPROGRAM-ID: " + string.Join(", ", manifest.A("programs").Select(p => p.GetValue<string>())) + "\n\nLatest execution: " + (records.FirstOrDefault()?.S("status") ?? "not-executed") + ". Evidence with changed code or inputs is stale; the latest failure is not hidden by older success.\n\n");
        var firstStop = ordered.FirstOrDefault(i => i!.S("observedStatus") == "reached-failed");
        markdown.AppendLine("First recorded stop: " + (firstStop?.S("statementId") ?? "unknown/not-observed") + ". See structured failure in the execution record.\n\nPriority (maximum 5):");
        foreach (var item in ordered.Take(5))
        {
            markdown.AppendLine($"- {item!.S("programId")} {item!.S("statementId")} {item!.S("category")}: {item!.S("reason")}");
        }

        foreach (var group in new[] { "required-input", "external-response", "internal-implementation", "missing-copy", "unknown-layout", "build-error", "test-expectation", "review" })
        {
            markdown.AppendLine("\n## " + group + "\n\n| Program / item | Manual / observed | Edit / type / method | Reason / source / declarations | Verify |\n|---|---|---|---|---|");
            foreach (var item in ordered.Where(i => i!.S("category") == group))
            {
                string Cell(string value) => value.Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");
                markdown.AppendLine($"| {item!.S("programId")} {item!.S("statementId")} | {item!.S("manualStatus")} / {item!.S("observedStatus")} | `{item!["edit"]?.S("path")}` `{item!["edit"]?.S("type")}` `{item!["edit"]?.S("method")}` | {Cell(item.S("reason"))}; {Cell(item["source"]?.S("file") ?? string.Empty)}:{item!["source"]?["start"]?.I("line")}; {Cell(item["source"]?.S("raw") ?? string.Empty)}; {Cell(item["fields"]?.ToJsonString() ?? string.Empty)} | `{item!.S("confirmCommand")}` |");
            }
        }

        markdown.AppendLine("\nInput examples describe types/PIC only; no business validity is inferred. Keep Preparation.Required until explicitly supplied. External responses require separate normal, EOF/no-data and error choices; see ResponseExamples and Contracts. Unexecuted and stale evidence cannot establish completion. See reports/test-generation for finite exploration exclusions.\n" + report.S("limitations"));
        CompletionWorkflow.Write(root, "reports/completion-worklist.json", Generator.Json(report));
        CompletionWorkflow.Write(root, "reports/completion-worklist.md", markdown.ToString());
    }

    /// <summary>fixture のバイトが変わった証跡を有効として取り込みません。</summary>
    /// <param name="root">生成先。</param>
    /// <param name="record">実行記録。</param>
    /// <returns>記録した入力が現在も同じ場合は true。</returns>
    private static bool InputCurrent(string root, JsonNode record)
    {
        var input = record["input"];
        if (input?["fixturePath"] is not JsonValue path)
        {
            return true;
        }

        var file = path.GetValue<string>();
        Generator.SafePath(file);
        return File.Exists(file) && Generator.Hash(File.ReadAllBytes(file)) == input.S("fixtureHash");
    }

    /// <summary>構造問題・期待値・レビューにも安定した項目 ID を与えます。</summary>
    /// <param name="items">追加先。</param>
    /// <param name="identity">入力の内容。</param>
    /// <param name="program">対象。</param>
    /// <param name="category">作業種別。</param>
    /// <param name="reasonCode">識別できる不足。</param>
    /// <param name="reason">説明。</param>
    /// <param name="path">編集先。</param>
    /// <param name="source">位置と COPY 履歴。</param>
    private static void AddAuxiliary(JsonArray items, string identity, string program, string category, string reasonCode, string reason, string path, JsonNode? source)
        => items.Add(new JsonObject { ["schemaVersion"] = 1, ["itemId"] = Generator.Hash(Encoding.UTF8.GetBytes(identity + program + category + reasonCode + (source == null ? string.Empty : Generator.Json(source))))[..24], ["programId"] = program, ["category"] = category, ["reasonCode"] = reasonCode, ["reason"] = reason, ["source"] = source?.DeepClone(), ["edit"] = new JsonObject { ["path"] = path }, ["manualStatus"] = "open", ["observedStatus"] = "not-observed", ["blockedBy"] = new JsonArray(), ["confirmCommand"] = category is "missing-copy" or "unknown-layout" ? "cobolcs " + path + " -I copybooks -o output" : "dotnet test Tests/CobolCS.Generated.Tests.csproj" });
}
