using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CobolCS.Runtime;

/// <summary>独立期待値の版付き契約、来歴と完全比較を扱います。期待値を書き換えません。</summary>
public static class ReferenceCase
{
    /// <summary>既存の display、fields、signal を完全比較する観測境界の識別子です。</summary>
    public const string ContractId = "full-state-v1";

    /// <summary>Gets 観測境界の定義を識別するハッシュです。</summary>
    public static string ContractHash => Hash(Encoding.UTF8.GetBytes("full-state-v1:display:string[];fields:string|string[];signal:string;exact;ordered;logical-only"));

    /// <summary>内容を識別する SHA-256 を返します。</summary>
    /// <param name="bytes">対象のバイト列。</param>
    /// <returns>小文字のハッシュ。</returns>
    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>キー順序に依存しないレビュー対象のハッシュを返します。</summary>
    /// <param name="value">レビュー欄を除く契約全体。</param>
    /// <returns>入力、期待値、来歴、観測境界に結び付いたハッシュ。</returns>
    public static string ReviewHash(JsonNode value)
    {
        var copy = value.DeepClone().AsObject();
        copy.Remove("review");
        return Hash(Encoding.UTF8.GetBytes(Canonical(copy).ToJsonString()));
    }

    /// <summary>重複した JSON プロパティを拒否してケースを読み込みます。</summary>
    /// <param name="text">JSON の内容。</param>
    /// <returns>読み取った契約。</returns>
    public static JsonNode Parse(string text)
    {
        using var document = JsonDocument.Parse(text);
        void Check(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in e.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                    {
                        throw new InvalidDataException("Duplicate JSON property: " + property.Name);
                    }

                    Check(property.Value);
                }
            }
            else if (e.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in e.EnumerateArray())
                {
                    Check(child);
                }
            }
        }

        Check(document.RootElement);
        return J.Parse(text);
    }

    /// <summary>版、出自、ソース、観測境界、値型とレビューを検査します。</summary>
    /// <param name="value">独立ケース。</param>
    /// <param name="context">生成時の inputHashes、configHash、programs。</param>
    /// <param name="root">参照データの基準ディレクトリ。</param>
    public static void Validate(JsonNode value, JsonNode context, string root)
    {
        Keys(value, "schemaVersion caseId programId origin inputHashes configHash contract input expected required evidence review");
        if (value.S("schemaVersion") != "1.0" || string.IsNullOrWhiteSpace(value.S("caseId")))
        {
            throw new InvalidDataException("Invalid schemaVersion/caseId");
        }

        if (!context.A("programs").Any(p => p.GetValue<string>() == value.S("programId")))
        {
            throw new InvalidDataException("Unknown programId");
        }

        if (value.S("origin") is not ("specification" or "cobol-reference" or "csharp-characterization"))
        {
            throw new InvalidDataException("Unknown origin");
        }

        if (value["inputHashes"] is not JsonObject hashes || hashes.Count == 0 || !JsonNode.DeepEquals(hashes, context["inputHashes"]) || value.S("configHash") != context.S("configHash"))
        {
            throw new InvalidDataException("Stale source/COPY/config hash");
        }

        var contract = value["contract"] ?? throw new InvalidDataException("Missing contract");
        Keys(contract, "id version hash");
        var business = ResolveContract(value, context);
        var input = value["input"] ?? throw new InvalidDataException("Missing input");
        var expected = value["expected"] ?? throw new InvalidDataException("Missing expected");
        if (business != null)
        {
            _ = BusinessObservation.Input(input, business);
            BusinessObservation.ValidateExpected(expected, business);
        }
        else
        {
            Keys(input, "fields externalResponses");
            if (input["fields"] is not JsonObject inputFields)
            {
                throw new InvalidDataException("Missing input.fields");
            }

            FixedExternalResponses.ValidateLogical(inputFields);
            Keys(expected, "display fields signal externalRequests bytes");
            if (expected["display"] is not JsonArray display || expected["fields"] is not JsonObject fields || (display.Count == 0 && fields.Count == 0))
            {
                throw new InvalidDataException("Empty or invalid comparison target");
            }

            foreach (var item in display)
            {
                _ = item?.GetValue<string>() ?? throw new InvalidDataException("Null display value");
            }

            Strings(fields, arrays: true);
            if (expected.S("signal") is not ("goback" or "stopRun" or "exitProgram" or "fallthrough"))
            {
                throw new InvalidDataException("Unknown expected termination");
            }

            if (context.S("externalAdapter") == "fixed" && (expected["externalRequests"] is not JsonArray || expected["bytes"] is not JsonArray))
            {
                throw new InvalidDataException("Fixed external verification must compare requests and bytes");
            }

            foreach (var bytes in expected.A("bytes"))
            {
                BusinessObservation.ValidateBytes(bytes);
            }
        }

        if (input.AsObject().ContainsKey("externalResponses") && input["externalResponses"] is not JsonArray)
        {
            throw new InvalidDataException("Invalid externalResponses");
        }

        _ = new FixedExternalResponses(input["externalResponses"] as JsonArray);
        _ = value["required"]?.GetValue<bool>() ?? throw new InvalidDataException("Missing required");
        var evidence = value["evidence"] ?? throw new InvalidDataException("Missing evidence");
        Keys(evidence, "rationale referenceExecution captureFiles syntheticDependencies conditions syntheticCapture");
        if (string.IsNullOrWhiteSpace(evidence.S("rationale")))
        {
            throw new InvalidDataException("Missing independent rationale");
        }

        _ = evidence["syntheticDependencies"]?.GetValue<bool>() ?? throw new InvalidDataException("Missing syntheticDependencies");
        if (evidence["conditions"] is not JsonObject)
        {
            throw new InvalidDataException("Missing reproducibility conditions");
        }

        if (value.S("origin") == "cobol-reference")
        {
            _ = evidence["syntheticCapture"]?.GetValue<bool>() ?? throw new InvalidDataException("Missing syntheticCapture");
            var execution = evidence["referenceExecution"] ?? throw new InvalidDataException("Missing referenceExecution");
            Keys(execution, "processor version compileOptions capturedAt method");
            foreach (var name in new[] { "processor", "version", "compileOptions", "capturedAt", "method" })
            {
                if (string.IsNullOrWhiteSpace(execution.S(name)))
                {
                    throw new InvalidDataException("Missing referenceExecution." + name);
                }
            }

            if (execution.S("capturedAt") != "unknown" && !DateTimeOffset.TryParse(execution.S("capturedAt"), out _))
            {
                throw new InvalidDataException("Invalid capturedAt");
            }

            if (evidence["captureFiles"] is not JsonObject captures || captures.Count == 0)
            {
                throw new InvalidDataException("Missing capture file hashes");
            }

            foreach (var (name, hash) in captures)
            {
                if (!name.StartsWith("Data/", StringComparison.Ordinal) || name.Split('/').Any(p => p is ".." or "." or "") || name.Contains('\\'))
                {
                    throw new InvalidDataException("Unsafe capture path");
                }

                var path = Path.Combine(root, name);
                for (var current = Path.GetFullPath(path); current.Length >= Path.GetFullPath(root).Length; current = Path.GetDirectoryName(current) ?? string.Empty)
                {
                    if ((File.Exists(current) || Directory.Exists(current)) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                    {
                        throw new InvalidDataException("Symlink capture path");
                    }
                }

                if (!File.Exists(path) || Hash(File.ReadAllBytes(path)) != hash?.GetValue<string>())
                {
                    throw new InvalidDataException("Stale capture file: " + name);
                }
            }
        }
        else if (evidence["referenceExecution"] != null || evidence["captureFiles"] != null)
        {
            throw new InvalidDataException("Reference evidence requires cobol-reference origin");
        }

        var review = value["review"] ?? throw new InvalidDataException("Missing review");
        Keys(review, "approved reviewer contentHash");
        if (!review.B("approved") || string.IsNullOrWhiteSpace(review.S("reviewer")) || review.S("contentHash") != ReviewHash(value))
        {
            throw new InvalidDataException("Missing or stale content review");
        }
    }

    /// <summary>ケースの選択した契約の版とハッシュを照合します。</summary>
    /// <param name="value">独立ケース。</param>
    /// <param name="context">生成時の観測境界。</param>
    /// <returns>業務契約。従来の full-state の場合は null。</returns>
    public static JsonNode? ResolveContract(JsonNode value, JsonNode context)
    {
        var selected = value["contract"]!;
        if (selected.S("id") == ContractId)
        {
            if (selected.S("version") != "1.0" || selected.S("hash") != ContractHash)
            {
                throw new InvalidDataException("Stale or unknown observation contract");
            }

            return null;
        }

        var contract = context["businessContracts"]?[selected.S("id")] ?? throw new InvalidDataException("Unknown business observation contract");
        BusinessObservation.ValidateContract(contract);
        if (contract.S("programId") != value.S("programId") || selected.S("version") != contract.S("version") || selected.S("hash") != BusinessObservation.ContractHash(contract))
        {
            throw new InvalidDataException("Stale business observation contract");
        }

        return contract;
    }

    /// <summary>完全一致の差分を最大件数と表示上限付きで返します。</summary>
    /// <param name="expected">独立期待値。</param>
    /// <param name="actual">実際の観測値。</param>
    /// <returns>JSON パス、型、期待値、実値、全差分数。</returns>
    public static JsonObject Compare(JsonNode? expected, JsonNode? actual)
    {
        var differences = new JsonArray();
        var count = 0;
        void Difference(string path, JsonNode? left, JsonNode? right, string reason)
        {
            count++;
            if (differences.Count < 50)
            {
                differences.Add(new JsonObject
                {
                    ["path"] = path,
                    ["reason"] = reason,
                    ["expectedType"] = left?.GetValueKind().ToString() ?? "missing",
                    ["actualType"] = right?.GetValueKind().ToString() ?? "missing",
                    ["expected"] = Limited(left),
                    ["actual"] = Limited(right),
                });
            }
        }

        void Visit(string path, JsonNode? left, JsonNode? right)
        {
            if (left is JsonObject bytesLeft && right is JsonObject bytesRight && bytesLeft.ContainsKey("base64") && bytesRight.ContainsKey("base64"))
            {
                var comparison = BusinessObservation.CompareBytes(left, right);
                if (!comparison.B("matched"))
                {
                    Difference(path, comparison["expectedWindow"], comparison["actualWindow"], "byte-record");
                    if (count <= 50 && differences.LastOrDefault() is JsonObject difference)
                    {
                        difference["byteComparison"] = comparison;
                        difference["expectedType"] = "bytes";
                        difference["actualType"] = "bytes";
                    }
                }
            }
            else if (left is JsonObject lo && right is JsonObject ro)
            {
                foreach (var key in lo.Select(p => p.Key).Union(ro.Select(p => p.Key)).Order(StringComparer.Ordinal))
                {
                    var child = path + "/" + key.Replace("~", "~0").Replace("/", "~1");
                    if (!lo.ContainsKey(key) || !ro.ContainsKey(key))
                    {
                        Difference(child, lo[key], ro[key], "missing-property");
                    }
                    else
                    {
                        Visit(child, lo[key], ro[key]);
                    }
                }
            }
            else if (left is JsonArray la && right is JsonArray ra)
            {
                if (la.Count != ra.Count)
                {
                    Difference(path, left, right, "array-length");
                }

                for (var i = 0; i < Math.Min(la.Count, ra.Count); i++)
                {
                    Visit(path + "/" + i, la[i], ra[i]);
                }
            }
            else if (!JsonNode.DeepEquals(left, right))
            {
                Difference(path, left, right, "value-or-type");
            }
        }

        Visit(string.Empty, expected, actual);
        return new JsonObject { ["matched"] = count == 0, ["differences"] = differences, ["differenceCount"] = count, ["omittedCount"] = Math.Max(0, count - differences.Count) };
    }

    private static string Limited(JsonNode? value)
    {
        var text = value?.ToJsonString() ?? "null";
        return text.Length <= 512 ? text : text[..512] + "…";
    }

    private static JsonNode Canonical(JsonNode node) => node switch
    {
        JsonObject obj => new JsonObject(obj.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, p.Value == null ? null : Canonical(p.Value)))),
        JsonArray array => new JsonArray(array.Select(p => p == null ? null : Canonical(p)).ToArray()),
        _ => node.DeepClone(),
    };

    private static void Keys(JsonNode node, string allowed)
    {
        if (node is not JsonObject obj || obj.Any(p => !allowed.Split(' ').Contains(p.Key)))
        {
            throw new InvalidDataException("Unknown field or invalid object: " + allowed);
        }
    }

    private static void Strings(JsonNode node, bool arrays)
    {
        if (node is not JsonObject obj)
        {
            throw new InvalidDataException("Expected fields object");
        }

        foreach (var pair in obj)
        {
            if (arrays && pair.Value is JsonArray array)
            {
                foreach (var element in array)
                {
                    _ = element?.GetValue<string>() ?? throw new InvalidDataException("Null field value");
                }
            }
            else
            {
                _ = pair.Value?.GetValue<string>() ?? throw new InvalidDataException("Null field value");
            }
        }
    }
}
