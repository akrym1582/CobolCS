using System.Text.Json.Nodes;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>業務仕様の固定ファイルと採用時の実装来歴を分離した、未承認の参考行を作ります。</summary>
internal static class BusinessReviewReference
{
    /// <summary>prepare でも現在の有効ケースを反映し、利用者の承認や削除へ触れません。</summary>
    /// <param name="stage">検査済みの複製。</param>
    /// <param name="manifest">現在の生成条件。</param>
    /// <param name="inspection">既存の共通静的検査結果。</param>
    internal static void Refresh(string stage, JsonNode manifest, BusinessInspection inspection)
    {
        var directory = Path.Combine(stage, "reports/business-scaffolds");
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var path in Directory.GetFiles(directory, "*.json").Where(p => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(p), @"\ABusiness_[a-f0-9]{64}\.json\z")))
        {
            Write(stage, manifest, "Tests/Templates/Business/" + Path.GetFileNameWithoutExtension(path), false, inspection);
        }
    }

    /// <summary>全業務契約とケースの参照 Data を束縛する参考 JSON を更新します。</summary>
    /// <param name="stage">stage 内の生成物。</param>
    /// <param name="manifest">入力・設定・型契約の来歴。</param>
    /// <param name="templateRoot">初回の利用者参考領域。</param>
    /// <param name="first">初回だけ利用者参考ファイルも作ります。</param>
    /// <param name="inspection">省略時は既存 Inspect で検査します。</param>
    internal static void Write(string stage, JsonNode manifest, string templateRoot, bool first, BusinessInspection? inspection = null)
    {
        var inventory = Generator.Inventory(stage);
        var contracts = inventory.Keys.Where(p => p.StartsWith("Tests/BusinessContracts/", StringComparison.Ordinal) && p.EndsWith(".json", StringComparison.Ordinal)).ToArray();
        var implementation = BusinessReview.ImplementationHashes(stage);
        var rows = new JsonArray();
        foreach (var item in (inspection ?? BusinessCases.Inspect(stage)).Cases)
        {
            rows.Add(new JsonObject
            {
                ["caseId"] = item.Case.S("caseId"),
                ["programId"] = item.Case.S("programId"),
                ["caseKind"] = "business",
                ["baselineKind"] = item.Case.S("baselineKind"),
                ["reviewed"] = false,
                ["reviewReason"] = "UNREVIEWED: independently review input, responses, expected values and implementation",
                ["callStubMode"] = "Throw",
                ["hasNoOpCalls"] = false,
                ["files"] = Hashes(contracts.Append(item.Path).Concat(BusinessCases.ReferencePaths(stage, item.Case))),
                ["implementationHashes"] = implementation.DeepClone(),
                ["sourceHashes"] = manifest["inputHashes"]!.DeepClone(),
                ["configHash"] = manifest.S("configHash"),
                ["contractHashes"] = manifest["contractHashes"]!.DeepClone(),
            });
        }

        var reference = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["status"] = rows.Count == 0 ? "case-unconfigured-reference-only" : "reference-only-not-an-approval",
            ["executionRequirement"] = "hasNoOpCalls is a placeholder, not execution evidence; adopt must verify current per-case execution",
            ["files"] = Hashes(contracts),
            ["cases"] = rows,
            ["implementationHashes"] = implementation,
            ["sourceHashes"] = manifest["inputHashes"]!.DeepClone(),
            ["configHash"] = manifest.S("configHash"),
            ["contractHashes"] = manifest["contractHashes"]!.DeepClone(),
        };
        var report = templateRoot.Replace("Tests/Templates/Business/", "reports/business-scaffolds/", StringComparison.Ordinal) + "-review-files.json";
        CompletionWorkflow.Write(stage, report, Generator.Json(reference));
        var userPath = templateRoot + "/review-files.json.txt";
        if (first && !File.Exists(Path.Combine(stage, userPath)))
        {
            CompletionWorkflow.Write(stage, userPath, Generator.Json(reference));
        }

        JsonObject Hashes(IEnumerable<string> paths) => new(paths.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(p => KeyValuePair.Create<string, JsonNode?>(p, JsonValue.Create(inventory[p]))));
    }
}
