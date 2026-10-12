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
        var inspection = Inspect(root);
        if (inspection.Diagnostics.Count != 0)
        {
            throw new ArgumentException(string.Join("\n", inspection.Diagnostics.Select(d => d.Path + ": " + d.Reason)));
        }

        return inspection.Cases;
    }

    /// <summary>同じ型・由来検査の診断を収集し、有効な別ケースを失わず返します。I/O と安全境界違反は中断します。</summary>
    /// <param name="root">生成または採用先。</param>
    /// <returns>有効な登録とファイル別の診断。</returns>
    public static BusinessInspection Inspect(string root)
    {
        SafeFile(root);
        var diagnostics = new List<BusinessDiagnostic>();
        var contracts = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
        var cases = new List<(string Path, JsonNode Case, JsonNode Contract)>();
        var contractDocuments = Documents("Tests/BusinessContracts", "business-contract-error");
        var caseDocuments = Documents("Tests/BusinessCases", "business-case-error");
        foreach (var group in contractDocuments.GroupBy(d => Text(d.Node, "contractId"), StringComparer.Ordinal))
        {
            if (group.Key != null && group.Count() > 1)
            {
                foreach (var document in group)
                {
                    Add(document.Path, null, "business-contract-error", "duplicate-contract-id", "Duplicate contract ID in " + string.Join(", ", group.Select(d => d.Path)), "/contractId");
                }

                continue;
            }

            foreach (var document in group)
            {
                try
                {
                    ValidateContract(document.Node);
                    contracts.Add(document.Node["contractId"]!.GetValue<string>(), document.Node);
                }
                catch (Exception error) when (IsValidation(error))
                {
                    Add(document.Path, null, "business-contract-error", "invalid-contract", error.Message, error.Data["BusinessPointer"] as string ?? string.Empty);
                }
            }
        }

        foreach (var group in caseDocuments.GroupBy(d => Text(d.Node, "caseId"), StringComparer.Ordinal))
        {
            if (group.Key != null && group.Count() > 1)
            {
                foreach (var document in group)
                {
                    Add(document.Path, group.Key, "business-case-error", "duplicate-case-id", "Duplicate case ID in " + string.Join(", ", group.Select(d => d.Path)), "/caseId");
                }

                continue;
            }

            foreach (var document in group)
            {
                var contractId = Text(document.Node, "contractId");
                if (contractId == null || !contracts.TryGetValue(contractId, out var contract))
                {
                    Add(document.Path, group.Key, "business-case-error", "blocked-contract", "Blocked by missing or invalid contract: " + contractId + "; files=" + string.Join(", ", contractDocuments.Where(d => Text(d.Node, "contractId") == contractId).Select(d => d.Path)), "/contractId");
                    continue;
                }

                try
                {
                    ValidateCase(root, document.Node, contract);
                    cases.Add((document.Path, document.Node, contract));
                }
                catch (Exception error) when (IsValidation(error))
                {
                    var reference = error.Message.StartsWith("reference-data-error:", StringComparison.Ordinal);
                    Add(document.Path, group.Key, reference ? "reference-data-error" : "business-case-error", reference ? "invalid-reference-data" : "invalid-case", error.Message, reference ? "/provenance/fileHashes" : error.Data["BusinessPointer"] as string ?? (document.Node["expected"] == null ? "/expected" : string.Empty));
                }
            }
        }

        var historyPath = System.IO.Path.Combine(root, "Testing/BusinessHistory.json");
        if (File.Exists(historyPath))
        {
            SafeFile(historyPath);
            JsonNode history;
            try
            {
                history = BusinessComparison.Parse(File.ReadAllText(historyPath));
                if (history["schemaVersion"]!.GetValue<int>() != 1 || history["requiredCaseIds"] is not JsonArray || history["requiredCaseIds"]!.AsArray().Any(id => id is not JsonValue value || !value.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text)) || history["requiredCaseIds"]!.AsArray().Select(id => id!.GetValue<string>()).Distinct(StringComparer.Ordinal).Count() != history["requiredCaseIds"]!.AsArray().Count)
                {
                    throw new ArgumentException("Invalid required IDs");
                }
            }
            catch (Exception error) when (IsValidation(error))
            {
                throw new IOException("Invalid business registration history", error);
            }

            foreach (var id in history["requiredCaseIds"]!.AsArray().Select(id => id!.GetValue<string>()).Except(cases.Select(c => c.Case["caseId"]!.GetValue<string>()), StringComparer.Ordinal))
            {
                Add("Testing/BusinessHistory.json", id, "business-case-error", "required-case-missing", "Required business case deleted or invalid: " + id, "/requiredCaseIds");
            }
        }

        var registrationPath = System.IO.Path.Combine(root, "UserCode/Business/BusinessRegistration.cs");
        SafeFile(registrationPath);
        if (File.Exists(registrationPath) && System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(registrationPath), @"(?:IDictionary|Dictionary)\s*<\s*string\s*,\s*(?:CobolCS\.Runtime\.)?IBusinessCaseAdapter\s*>", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
        {
            Add("UserCode/Business/BusinessRegistration.cs", null, "business-contract-error", "legacy-registration", "Migrate to IDictionary<string, Func<IBusinessCaseAdapter>> and adapters.Add(\"payment-v1\", () => new PaymentAdapter()); generate into a new directory and port user edits if managed files cannot be updated.", string.Empty);
        }

        return new BusinessInspection(cases.AsReadOnly(), contracts, diagnostics.AsReadOnly());

        void Add(string path, string? id, string category, string code, string reason, string pointer)
            => diagnostics.Add(new BusinessDiagnostic(path, id, category, code, reason[..Math.Min(2048, reason.Length)], pointer));

        List<(string Path, JsonNode Node)> Documents(string directory, string category)
        {
            var result = new List<(string Path, JsonNode Node)>();
            var full = System.IO.Path.Combine(root, directory);
            if (!Directory.Exists(full))
            {
                return result;
            }

            void Visit(string folder)
            {
                SafeFile(folder);
                foreach (var path in Directory.EnumerateFileSystemEntries(folder).Order(StringComparer.Ordinal))
                {
                    SafeFile(path);
                    if (Directory.Exists(path))
                    {
                        if (path != System.IO.Path.Combine(root, "Tests/BusinessCases/Data"))
                        {
                            Visit(path);
                        }
                    }
                    else if (path.EndsWith(".json", StringComparison.Ordinal))
                    {
                        try
                        {
                            BusinessFileSafety.RequireRegular(path);
                            result.Add((System.IO.Path.GetRelativePath(root, path).Replace('\\', '/'), BusinessComparison.Parse(File.ReadAllText(path))));
                        }
                        catch (Exception error) when (IsValidation(error))
                        {
                            Add(System.IO.Path.GetRelativePath(root, path).Replace('\\', '/'), null, category, "invalid-json", error.Message, string.Empty);
                        }
                    }
                }
            }

            Visit(full);
            return result;
        }
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
            var error = new InvalidOperationException(id + " /contractId: Adapter mismatch");
            error.Data["BusinessReason"] = "contract-mismatch";
            error.Data["BusinessPointer"] = "/contractId";
            throw error;
        }

        var external = new BusinessExternalScript(id, contract["operations"]!.AsObject(), item["responses"]!.AsArray());
        var actual = adapter.Execute(new BusinessCaseInput((JsonObject)item["input"]!.DeepClone(), external));
        external.VerifyComplete();
        if (actual == null || !actual.Completed || actual.HasNoOpCalls)
        {
            var error = new InvalidOperationException(id + ": NoOp or incomplete business execution");
            error.Data["BusinessReason"] = actual?.HasNoOpCalls == true ? "noop" : "incomplete-observation";
            throw error;
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
            var failure = new InvalidOperationException(error.Message + " sourceReferences=" + contract["sourceReferences"]?.ToJsonString()[..Math.Min(256, contract["sourceReferences"]!.ToJsonString().Length)], error);
            foreach (var key in new[] { "BusinessReason", "BusinessPointer", "BusinessExpected", "BusinessActual", "BusinessOmittedCharacters" })
            {
                failure.Data[key] = error.Data[key];
            }

            throw failure;
        }
    }

    /// <summary>レビューと実行記録で用いる同じ SHA-256 表現です。</summary>
    /// <param name="path">ローカルの対象ファイル。</param>
    /// <returns>小文字の内容 hash。</returns>
    public static string FileHash(string path)
    {
        SafeFile(path);
        BusinessFileSafety.RequireRegular(path);
        return Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    }

    /// <summary>採取データの実体を安全な相対パスから読み、指定 SHA-256 と照合します。</summary>
    /// <param name="root">生成または採用先。</param>
    /// <param name="item">検査済みの業務ケース。</param>
    /// <returns>レビューで束縛する採取ファイルの相対パス。</returns>
    public static string[] ReferencePaths(string root, JsonNode item)
    {
        if (item["baselineKind"]?.GetValue<string>() != "cobol-reference")
        {
            return [];
        }

        var result = new List<string>();
        foreach (var pair in item["provenance"]!["fileHashes"]!.AsObject())
        {
            var relative = pair.Key;
            if (System.IO.Path.IsPathRooted(relative) || relative.Contains('\\') || relative.Contains(':') || relative.Contains('\0') || relative.Split('/').Any(p => p is "" or "." or ".."))
            {
                throw new ArgumentException("reference-data-error: Unsafe capture path " + relative);
            }

            var path = System.IO.Path.Combine(root, "Tests/BusinessCases/Data", relative);
            SafeFile(path);
            if (!File.Exists(path) || Directory.Exists(path))
            {
                throw new ArgumentException("reference-data-error: Missing regular capture file " + relative);
            }

            BusinessFileSafety.RequireRegular(path);
            var hash = pair.Value?.GetValue<string>() ?? string.Empty;
            if (hash.Length != 64 || hash.Any(c => !char.IsAsciiHexDigit(c)) || FileHash(path) != hash.ToLowerInvariant())
            {
                throw new ArgumentException("reference-data-error: Capture SHA-256 mismatch " + relative);
            }

            result.Add("Tests/BusinessCases/Data/" + relative);
        }

        return result.ToArray();
    }

    /// <summary>親を含むリンクを拒否し、安全境界の問題は診断で許容しません。</summary>
    /// <param name="path">絶対または相対のローカルパス。</param>
    public static void SafeFile(string path)
    {
        for (var current = System.IO.Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = System.IO.Path.GetDirectoryName(current))
        {
            var info = new FileInfo(current);
            if (info.LinkTarget != null || (info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0) || new DirectoryInfo(current).LinkTarget != null)
            {
                throw new IOException("Business path contains a link: " + path);
            }
        }
    }

    /// <summary>静的な ID だけを読み、型不正を診断へ残します。</summary>
    private static string? Text(JsonNode node, string key)
    {
        try
        {
            return node is JsonObject && node[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
        }
        catch (Exception error) when (IsValidation(error))
        {
            return null;
        }
    }

    /// <summary>業務データの不正だけを収集し、I/O と安全境界の失敗を隠しません。</summary>
    private static bool IsValidation(Exception error) => error is ArgumentException or InvalidOperationException or System.Text.Json.JsonException or FormatException or NullReferenceException or KeyNotFoundException;

    /// <summary>契約の共通検査。</summary>
    private static void ValidateContract(JsonNode contract)
    {
        BusinessComparison.Keys(contract, ["schemaVersion", "contractId", "version", "reason", "input", "result", "termination", "operations", "sourceReferences"], ["schemaVersion", "contractId", "version", "reason", "input", "result", "termination", "operations"]);
        if (contract["schemaVersion"]!.GetValue<int>() != 1 || contract["version"]!.GetValue<int>() <= 0 || string.IsNullOrWhiteSpace(contract["contractId"]!.GetValue<string>()) || string.IsNullOrWhiteSpace(contract["reason"]!.GetValue<string>()))
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

    /// <summary>固定ケースの共通検査。</summary>
    private static void ValidateCase(string root, JsonNode item, JsonNode contract)
    {
        BusinessComparison.Keys(item, ["schemaVersion", "caseId", "programId", "contractId", "required", "baselineKind", "reviewReason", "provenance", "input", "responses", "expected"], ["schemaVersion", "caseId", "programId", "contractId", "baselineKind", "reviewReason", "provenance", "input", "responses", "expected"]);
        var id = item["caseId"]!.GetValue<string>();
        if (item["schemaVersion"]!.GetValue<int>() != 1 || string.IsNullOrWhiteSpace(id) || id.Length > 128 || string.IsNullOrWhiteSpace(item["programId"]!.GetValue<string>()) || item["baselineKind"]!.GetValue<string>() is not ("specification" or "cobol-reference" or "csharp-characterization") || string.IsNullOrWhiteSpace(item["reviewReason"]!.GetValue<string>()) || item["reviewReason"]!.GetValue<string>().StartsWith("UNREVIEWED", StringComparison.Ordinal) || item["provenance"] is not JsonObject { Count: > 0 })
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
                throw new ArgumentException("reference-data-error: COBOL reference needs capture file hashes");
            }

            if (new[] { "processor", "version", "method" }.Any(key => item["provenance"]![key] is not JsonValue value || !value.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text)) || item["provenance"]!["conditions"] == null || item["provenance"]!["fileHashes"]!.AsObject().Any(p => p.Value is not JsonValue hash || !hash.TryGetValue<string>(out var text) || text.Length != 64 || text.Any(c => !char.IsAsciiHexDigit(c))))
            {
                throw new ArgumentException("reference-data-error: COBOL reference needs processor/version/method, conditions and SHA-256 capture hashes");
            }
        }

        ValidateReferenceFiles(root, item);
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
    }

    /// <summary>採取元がないケースも同じ入口で検査します。</summary>
    private static void ValidateReferenceFiles(string root, JsonNode item) => _ = ReferencePaths(root, item);

    /// <summary>操作名・要求値・呼出し順を厳密に固定します。</summary>
    /// <param name="caseId">差分の対象。</param>
    /// <param name="contract">操作別型契約。</param>
    /// <param name="expected">独立した効果列。</param>
    /// <param name="actual">現在の呼出し列。</param>
    private static void CompareEffects(string caseId, JsonNode contract, JsonArray expected, JsonArray actual)
    {
        if (expected.Count != actual.Count)
        {
            throw BusinessComparison.Failure(caseId + " /effects: expected count=" + expected.Count + " actual count=" + actual.Count, "/effects", "comparison-failed");
        }

        for (var index = 0; index < expected.Count; index++)
        {
            BusinessComparison.Keys(expected[index]!, ["operation", "request"], ["operation", "request"]);
            BusinessComparison.Keys(actual[index]!, ["operation", "request"], ["operation", "request"]);
            var operation = expected[index]!["operation"]!.GetValue<string>();
            if (actual[index]!["operation"]!.GetValue<string>() != operation || contract["operations"]?[operation] == null)
            {
                throw BusinessComparison.Failure(caseId + " /effects/" + index + ": Operation order mismatch", "/effects/" + index + "/operation", "comparison-failed");
            }

            BusinessComparison.Equal(caseId, contract["operations"]![operation]!["request"]!, expected[index]!["request"], actual[index]!["request"], "/effects/" + index + "/request");
        }
    }
}
