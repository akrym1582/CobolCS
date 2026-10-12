using System.Text.Json.Nodes;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>業務契約と必須ケースの履歴を保持し、採用前のコード・期待値・マッピングを束縛します。</summary>
internal static class BusinessReview
{
    /// <summary>生成や prepare で登録した必須 ID を削除後も保持します。期待値は更新しません。</summary>
    /// <param name="root">検査済み stage。</param>
    internal static void Update(string root)
    {
        var cases = BusinessCases.Load(root);
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
        var cases = BusinessCases.Load(root);
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
            if (!entry.B("reviewed") || entry.S("programId") != item.Case.S("programId") || !manifest.A("programs").Any(p => p.GetValue<string>() == item.Case.S("programId")) || entry.S("baselineKind") != item.Case.S("baselineKind") || !JsonNode.DeepEquals(entry["implementationHashes"], implementation) || !JsonNode.DeepEquals(entry["sourceHashes"], manifest["inputHashes"]) || entry.S("configHash") != manifest.S("configHash") || !JsonNode.DeepEquals(entry["contractHashes"], manifest["contractHashes"]))
            {
                throw new InvalidOperationException("Stale or invalid business implementation/mapping review: " + id);
            }

            if (entry["files"] is not JsonObject files || !files.ContainsKey(item.Path) || contractPaths.Any(p => !files.ContainsKey(p)))
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

    /// <summary>同じ採用 stage の実行証跡で全業務 ID の正常完了を確認します。</summary>
    /// <param name="stage">新しい採用 stage。</param>
    /// <param name="required">レビュー済み登録 ID。</param>
    internal static void ValidateExecution(string stage, string[] required)
    {
        if (required.Length == 0)
        {
            return;
        }

        var recordPath = Path.Combine(stage, "TestResults/business-execution.json");
        if (!File.Exists(recordPath))
        {
            throw new InvalidOperationException("Missing actual business case execution record");
        }

        var record = J.Parse(File.ReadAllText(recordPath));
        var completed = record.A("cases");
        if (!record.B("complete") || completed.Count() != required.Length || completed.GroupBy(c => c.S("caseId"), StringComparer.Ordinal).Any(g => g.Count() != 1) || required.Any(id => !completed.Any(c => c.S("caseId") == id && c.S("status") == "passed")))
        {
            throw new InvalidOperationException("Incomplete, duplicate or missing required business execution");
        }
    }
}
