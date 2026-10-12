using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace CobolCS.Runtime;

/// <summary>業務ケースの共通登録・検証・実行。生成 CLI や IR なしで採用後も動きます。</summary>
public static class BusinessCases
{
    /// <summary>契約とケースを同じ比較器で静的検査し、重複 ID や未記入期待値を拒否します。</summary>
    /// <param name="root">独立した生成または採用ソリューション。</param>
    /// <returns>パス、固定ケース、型契約を含む登録一覧。</returns>
    public static IReadOnlyList<(string Path, JsonNode Case, JsonNode Contract)> Load(string root)
    {
        string[] Files(string directory) => Directory.Exists(System.IO.Path.Combine(root, directory)) ? Directory.GetFiles(System.IO.Path.Combine(root, directory), "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray() : [];
        var contracts = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
        foreach (var path in Files("Tests/BusinessContracts"))
        {
            var contract = BusinessComparison.Parse(File.ReadAllText(path));
            BusinessComparison.Keys(contract, ["schemaVersion", "contractId", "version", "reason", "input", "result", "termination", "operations", "sourceReferences"], ["schemaVersion", "contractId", "version", "reason", "input", "result", "termination", "operations"]);
            if (contract["schemaVersion"]!.GetValue<int>() != 1 || contract["version"]!.GetValue<int>() <= 0 || string.IsNullOrWhiteSpace(contract["contractId"]!.GetValue<string>()) || string.IsNullOrWhiteSpace(contract["reason"]!.GetValue<string>()) || !contracts.TryAdd(contract["contractId"]!.GetValue<string>(), contract))
            {
                throw new ArgumentException("Invalid or duplicate business contract");
            }

            foreach (var section in new[] { "input", "result", "termination" })
            {
                BusinessComparison.ValidateType(contract[section]!);
            }

            if (contract["result"]!["type"]!.GetValue<string>() != "object" || contract["result"]!["fields"] is not JsonObject { Count: > 0 } || contract["input"]!["type"]!.GetValue<string>() != "object")
            {
                throw new ArgumentException("Empty business observation contract or non-object input");
            }

            foreach (var operation in contract["operations"]!.AsObject())
            {
                if (string.IsNullOrWhiteSpace(operation.Key) || operation.Key.Length > 128 || operation.Value == null)
                {
                    throw new ArgumentException("Unset operation");
                }

                BusinessComparison.Keys(operation.Value, ["request", "response"], ["request", "response"]);
                BusinessComparison.ValidateType(operation.Value["request"]!);
                BusinessComparison.ValidateType(operation.Value["response"]!);
                if (operation.Value["request"]!["type"]!.GetValue<string>() != "object" || operation.Value["response"]!["type"]!.GetValue<string>() != "object")
                {
                    throw new ArgumentException("Operations require object request/response payloads");
                }
            }
        }

        var cases = new List<(string Path, JsonNode Case, JsonNode Contract)>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in Files("Tests/BusinessCases"))
        {
            var item = BusinessComparison.Parse(File.ReadAllText(path));
            BusinessComparison.Keys(item, ["schemaVersion", "caseId", "programId", "contractId", "required", "baselineKind", "reviewReason", "provenance", "input", "responses", "expected"], ["schemaVersion", "caseId", "programId", "contractId", "baselineKind", "reviewReason", "provenance", "input", "responses", "expected"]);
            var id = item["caseId"]!.GetValue<string>();
            if (item["schemaVersion"]!.GetValue<int>() != 1 || string.IsNullOrWhiteSpace(id) || id.Length > 128 || !ids.Add(id) || !contracts.TryGetValue(item["contractId"]!.GetValue<string>(), out var contract) || string.IsNullOrWhiteSpace(item["programId"]!.GetValue<string>()) || item["baselineKind"]!.GetValue<string>() is not ("specification" or "cobol-reference" or "csharp-characterization") || string.IsNullOrWhiteSpace(item["reviewReason"]!.GetValue<string>()) || item["reviewReason"]!.GetValue<string>().StartsWith("UNREVIEWED", StringComparison.Ordinal) || item["provenance"] is not JsonObject { Count: > 0 })
            {
                throw new ArgumentException("Invalid, duplicate, unfilled or unknown-contract business case " + id);
            }

            if (item["required"] != null)
            {
                _ = item["required"]!.GetValue<bool>();
            }

            if (item["baselineKind"]!.GetValue<string>() == "cobol-reference")
            {
                BusinessComparison.Keys(item["provenance"]!, ["processor", "version", "conditions", "method", "fileHashes", "sourceReferences"], ["processor", "version", "conditions", "method", "fileHashes"]);
                if (item["provenance"]!["fileHashes"] is not JsonObject { Count: > 0 })
                {
                    throw new ArgumentException("COBOL reference needs capture file hashes");
                }

                if (new[] { "processor", "version", "method" }.Any(key => item["provenance"]![key] is not JsonValue value || !value.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text)) || item["provenance"]!["conditions"] == null || item["provenance"]!["fileHashes"]!.AsObject().Any(p => p.Value is not JsonValue hash || !hash.TryGetValue<string>(out var text) || text.Length != 64 || text.Any(c => !char.IsAsciiHexDigit(c))))
                {
                    throw new ArgumentException("COBOL reference needs processor/version/method, conditions and SHA-256 capture hashes");
                }
            }

            BusinessComparison.Equal(id, contract["input"]!, item["input"], item["input"], "/input");
            BusinessComparison.Keys(item["expected"]!, ["result", "effects", "termination"], ["result", "effects", "termination"]);
            BusinessComparison.Equal(id, contract["result"]!, item["expected"]!["result"], item["expected"]!["result"], "/expected/result");
            BusinessComparison.Equal(id, contract["termination"]!, item["expected"]!["termination"], item["expected"]!["termination"], "/expected/termination");
            foreach (var response in item["responses"]!.AsArray())
            {
                BusinessComparison.Keys(response!, ["operation", "request", "response"], ["operation", "request", "response"]);
                var operation = contract["operations"]![response!["operation"]!.GetValue<string>()] ?? throw new ArgumentException("Unknown business operation");
                BusinessComparison.Equal(id, operation["request"]!, response["request"], response["request"], "/responses/request");
                BusinessComparison.Equal(id, operation["response"]!, response["response"], response["response"], "/responses/response");
            }

            CompareEffects(id, contract, item["expected"]!["effects"]!.AsArray(), item["expected"]!["effects"]!.AsArray());
            cases.Add((System.IO.Path.GetRelativePath(root, path).Replace('\\', '/'), item, contract));
        }

        return cases.AsReadOnly();
    }

    /// <summary>アダプターだけを差し替えられる固定業務比較です。内部状態 hash を比較値に含めません。</summary>
    /// <param name="item">独立期待値とケース専用の入力・応答。</param>
    /// <param name="contract">選択理由と型。</param>
    /// <param name="adapter">Executor または業務サービスへの接続。</param>
    public static void Execute(JsonNode item, JsonNode contract, IBusinessCaseAdapter adapter)
    {
        var id = item["caseId"]!.GetValue<string>();
        if (adapter.ContractId != item["contractId"]!.GetValue<string>())
        {
            throw new InvalidOperationException(id + " /contractId: Adapter mismatch");
        }

        var external = new BusinessExternalScript(id, contract["operations"]!.AsObject(), item["responses"]!.AsArray());
        var actual = adapter.Execute(new BusinessCaseInput((JsonObject)item["input"]!.DeepClone(), external));
        external.VerifyComplete();
        if (actual == null || !actual.Completed || actual.HasNoOpCalls)
        {
            throw new InvalidOperationException(id + ": NoOp or incomplete business execution");
        }

        try
        {
            BusinessComparison.Equal(id, contract["result"]!, item["expected"]!["result"], actual.Result, "/result");
            BusinessComparison.Equal(id, contract["termination"]!, item["expected"]!["termination"], JsonValue.Create(actual.Termination), "/termination");
            CompareEffects(id, contract, item["expected"]!["effects"]!.AsArray(), actual.Effects);
            CompareEffects(id, contract, external.Effects, actual.Effects);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        {
            throw new InvalidOperationException(error.Message + " sourceReferences=" + contract["sourceReferences"]?.ToJsonString()[..Math.Min(256, contract["sourceReferences"]!.ToJsonString().Length)], error);
        }
    }

    /// <summary>レビューと実行記録で用いる同じ SHA-256 表現です。</summary>
    /// <param name="path">ローカルの対象ファイル。</param>
    /// <returns>小文字の内容 hash。</returns>
    public static string FileHash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    /// <summary>操作名・要求値・呼出し順を厳密に固定します。</summary>
    /// <param name="caseId">差分の対象。</param>
    /// <param name="contract">操作別型契約。</param>
    /// <param name="expected">独立した効果列。</param>
    /// <param name="actual">現在の呼出し列。</param>
    private static void CompareEffects(string caseId, JsonNode contract, JsonArray expected, JsonArray actual)
    {
        if (expected.Count != actual.Count)
        {
            throw new InvalidOperationException(caseId + " /effects: expected count=" + expected.Count + " actual count=" + actual.Count);
        }

        for (var index = 0; index < expected.Count; index++)
        {
            BusinessComparison.Keys(expected[index]!, ["operation", "request"], ["operation", "request"]);
            BusinessComparison.Keys(actual[index]!, ["operation", "request"], ["operation", "request"]);
            var operation = expected[index]!["operation"]!.GetValue<string>();
            if (actual[index]!["operation"]!.GetValue<string>() != operation || contract["operations"]?[operation] == null)
            {
                throw new InvalidOperationException(caseId + " /effects/" + index + ": Operation order mismatch");
            }

            BusinessComparison.Equal(caseId, contract["operations"]![operation]!["request"]!, expected[index]!["request"], actual[index]!["request"], "/effects/" + index + "/request");
        }
    }
}
