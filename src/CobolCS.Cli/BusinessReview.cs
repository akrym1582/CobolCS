using System.Text.Json.Nodes;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>業務契約と必須ケースの履歴を保持し、採用前のコード・期待値・マッピングを束縛します。</summary>
internal static class BusinessReview
{
    /// <summary>生成や prepare で登録した必須 ID を削除後も保持します。期待値は更新しません。</summary>
    /// <param name="root">検査済み stage。</param>
    /// <param name="inspection">共有する一回の静的検査結果。省略時はここで収集します。</param>
    internal static void Update(string root, BusinessInspection? inspection = null)
    {
        var cases = (inspection ?? BusinessCases.Inspect(root)).Cases;
        var path = Path.Combine(root, "Testing/BusinessHistory.json");
        var prior = File.Exists(path) ? J.Parse(File.ReadAllText(path)) : new JsonObject { ["schemaVersion"] = 1, ["requiredCaseIds"] = new JsonArray() };
        if (prior.I("schemaVersion") != 1 || prior["requiredCaseIds"] is not JsonArray)
        {
            throw new IOException("Invalid business registration history");
        }

        var ids = prior.A("requiredCaseIds").Select(i => i.GetValue<string>()).Concat(cases.Where(c => c.Case["required"]?.GetValue<bool>() != false).Select(c => c.Case.S("caseId"))).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        prior["requiredCaseIds"] = new JsonArray(ids.Select(i => (JsonNode?)JsonValue.Create(i)).ToArray());
        prior["configuredCases"] = cases.Count;
        prior["status"] = cases.Count == 0 ? ids.Length == 0 ? "business-cases-unconfigured" : "required-cases-deleted" : "configured";
        CompletionWorkflow.Write(root, "Testing/BusinessHistory.json", Generator.Json(prior));
    }

    /// <summary>通常のコンパイル対象を漏らさず採用前レビューへ束縛します。</summary>
    /// <param name="root">生成先。</param>
    /// <returns>テストを除く全 C# ソースの内容 hash。</returns>
    internal static JsonObject ImplementationHashes(string root) => new(Generator.Inventory(root).Where(p => p.Key.EndsWith(".cs", StringComparison.Ordinal) && !p.Key.StartsWith("Tests/", StringComparison.Ordinal)).Select(p => KeyValuePair.Create<string, JsonNode?>(p.Key, JsonValue.Create(p.Value))));

    /// <summary>業務ケースがある場合のみ従来ゲートへ追加します。未設定だけで従来採用を禁止しません。</summary>
    /// <param name="root">生成先。</param>
    /// <param name="manifest">採用するプログラム。</param>
    /// <param name="review">共通 review.json。</param>
    /// <returns>実行確認が必要な業務ケース ID。</returns>
    internal static string[] Validate(string root, JsonNode manifest, JsonNode review)
    {
        IReadOnlyList<(string Path, JsonNode Case, JsonNode Contract)> cases;
        try
        {
            cases = BusinessCases.Load(root);
        }
        catch (ArgumentException error)
        {
            throw new InvalidOperationException("Invalid business registrations: " + error.Message, error);
        }

        var historyPath = Path.Combine(root, "Testing/BusinessHistory.json");
        var historical = File.Exists(historyPath) ? J.Parse(File.ReadAllText(historyPath)).A("requiredCaseIds").Select(i => i.GetValue<string>()).ToArray() : [];
        var ids = cases.Select(c => c.Case.S("caseId")).ToArray();
        if (historical.Except(ids, StringComparer.Ordinal).Any())
        {
            throw new InvalidOperationException("Required business case deleted: " + string.Join(", ", historical.Except(ids, StringComparer.Ordinal)));
        }

        var implementation = ImplementationHashes(root);
        var contractPaths = Generator.Inventory(root).Keys.Where(p => p.StartsWith("Tests/BusinessContracts/", StringComparison.Ordinal) && p.EndsWith(".json", StringComparison.Ordinal)).ToArray();
        foreach (var item in cases)
        {
            var id = item.Case.S("caseId");
            var entry = review.A("cases").SingleOrDefault(c => c.S("caseId") == id && c.S("caseKind") == "business") ?? throw new InvalidOperationException("Missing business review: " + id);
            if (entry["files"] is JsonObject legacyFiles)
            {
                var implementationPath = legacyFiles.Select(p => p.Key).FirstOrDefault(p => p.EndsWith(".cs", StringComparison.Ordinal));
                if (implementationPath != null)
                {
                    throw new InvalidOperationException("Business review files contains implementation path " + implementationPath + "; move it to implementationHashes and explicitly re-review using reports/business-scaffolds/*-review-files.json");
                }
            }

            if (!entry.B("reviewed") || entry.S("programId") != item.Case.S("programId") || !manifest.A("programs").Any(p => p.GetValue<string>() == item.Case.S("programId")) || entry.S("baselineKind") != item.Case.S("baselineKind") || !JsonNode.DeepEquals(entry["implementationHashes"], implementation) || !JsonNode.DeepEquals(entry["sourceHashes"], manifest["inputHashes"]) || entry.S("configHash") != manifest.S("configHash") || !JsonNode.DeepEquals(entry["contractHashes"], manifest["contractHashes"]))
            {
                throw new InvalidOperationException("Stale or invalid business implementation/mapping review: " + id);
            }

            if (entry["files"] is not JsonObject files || !files.ContainsKey(item.Path) || contractPaths.Concat(BusinessCases.ReferencePaths(root, item.Case)).Any(p => !files.ContainsKey(p)))
            {
                throw new InvalidOperationException("Business review must bind case input, fixed responses, expectations and all contracts: " + id);
            }
        }

        if (review.A("cases").Where(c => c.S("caseKind") == "business").Any(c => !ids.Contains(c.S("caseId"), StringComparer.Ordinal)))
        {
            throw new InvalidOperationException("Reviewed business case is absent");
        }

        return ids;
    }

    /// <summary>同じ採用 stage の ID・内容・TRX 行と各ケースの正常終端を照合します。</summary>
    /// <param name="stage">新しい採用 stage。</param>
    /// <param name="required">レビュー済み登録 ID。</param>
    /// <param name="executionId">今回の子プロセスへ渡した ID。旧記録を採用しません。</param>
    /// <param name="trxPath">今回の VSTest 結果。</param>
    internal static void ValidateExecution(string stage, string[] required, string? executionId = null, string? trxPath = null)
    {
        if (required.Length == 0)
        {
            return;
        }

        if (executionId == null || !Guid.TryParseExact(executionId, "N", out _) || trxPath == null)
        {
            throw new InvalidOperationException("Missing current business execution ID/TRX; legacy business-execution.json cannot prove per-case execution");
        }

        var directory = Path.Combine(stage, "TestResults/business", executionId);
        if (!File.Exists(Path.Combine(directory, "started.json")) || !File.Exists(Path.Combine(directory, "summary.json")))
        {
            throw new InvalidOperationException("Missing actual business case execution record");
        }

        var started = BusinessComparison.Parse(File.ReadAllText(Path.Combine(directory, "started.json")));
        var summary = BusinessComparison.Parse(File.ReadAllText(Path.Combine(directory, "summary.json")));
        var current = BusinessCases.Load(stage);
        var ids = current.Select(c => c.Case.S("caseId")).ToArray();
        if (started.I("schemaVersion") != 2 || summary.I("schemaVersion") != 2 || started.S("executionId") != executionId || summary.S("executionId") != executionId || !summary.B("complete") || !summary.B("passed") || ids.Length == 0 || required.Distinct(StringComparer.Ordinal).Count() != required.Length || !required.Order(StringComparer.Ordinal).SequenceEqual(ids.Order(StringComparer.Ordinal)) || !started.A("caseIds").Select(c => c.GetValue<string>()).Order(StringComparer.Ordinal).SequenceEqual(ids.Order(StringComparer.Ordinal)) || !JsonNode.DeepEquals(started["inputHashes"], BusinessExecution.InputHashes(stage)) || !JsonNode.DeepEquals(started["implementationHashes"], BusinessExecution.ImplementationHashes(stage)))
        {
            throw new InvalidOperationException("Incomplete, stale or foreign business execution");
        }

        var completed = summary.A("cases").ToArray();
        if (completed.Length != required.Length || completed.GroupBy(c => c.S("caseId"), StringComparer.Ordinal).Any(g => g.Count() != 1))
        {
            throw new InvalidOperationException("Duplicate or missing business execution");
        }

        var trx = System.Xml.Linq.XDocument.Load(trxPath);
        System.Xml.Linq.XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        var results = trx.Descendants(ns + "UnitTestResult").ToArray();
        var definitions = trx.Descendants(ns + "UnitTest").ToArray();
        foreach (var item in current)
        {
            var id = item.Case.S("caseId");
            var key = BusinessExecution.CaseKey(id);
            var recordPath = Path.Combine(directory, "cases", key + ".json");
            if (!File.Exists(recordPath))
            {
                throw new InvalidOperationException("Missing business terminal record " + id);
            }

            var outcome = BusinessComparison.Parse(File.ReadAllText(recordPath));
            if (outcome.S("caseId") != id || outcome.S("caseKey") != key || outcome.S("executionId") != executionId || outcome.S("status") != "passed" || outcome.S("programId") != item.Case.S("programId") || outcome.S("contractId") != item.Case.S("contractId") || outcome.S("caseHash") != BusinessCases.FileHash(Path.Combine(stage, item.Path)) || outcome.S("inputHash") != BusinessExecution.CaseKey(item.Case["input"]!.ToJsonString()) || !JsonNode.DeepEquals(outcome, completed.SingleOrDefault(c => c.S("caseId") == id)))
            {
                throw new InvalidOperationException("Invalid business terminal record " + id);
            }

            BusinessTrxResult.Validate(results, definitions, key);
        }

        var businessMethods = definitions.Count(d => d.Element(ns + "TestMethod")?.Attribute("name")?.Value == "BusinessCase_Run");
        if (businessMethods != ids.Length)
        {
            throw new InvalidOperationException("Unexpected business TRX row count");
        }

        Adoption.ValidateResults(trxPath, ["BusinessCases_Integrity"]);
    }
}
