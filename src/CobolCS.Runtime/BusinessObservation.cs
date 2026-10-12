using System.Text;
using System.Text.Json.Nodes;

namespace CobolCS.Runtime;

/// <summary>レビュー済みの業務キーと型を使い、観測境界だけを比較します。</summary>
public static class BusinessObservation
{
    /// <summary>契約の意味を識別するハッシュです。レビュー欄は対象外です。</summary>
    /// <param name="contract">観測契約。</param>
    /// <returns>キー、型、原文参照、入力境界を含むハッシュ。</returns>
    public static string ContractHash(JsonNode contract) => ReferenceCase.ReviewHash(contract);

    /// <summary>業務観測契約と、その内容に結び付いた承認を検査します。</summary>
    /// <param name="contract">利用者所有の契約。</param>
    public static void ValidateContract(JsonNode contract)
    {
        FixedExternalResponses.RequireKeys(contract, "schemaVersion id version programId inputs arguments observations includeRequests includeSignal includeBytes review");
        if (contract.S("schemaVersion") != "1.0" || string.IsNullOrWhiteSpace(contract.S("id")) || string.IsNullOrWhiteSpace(contract.S("version")) || string.IsNullOrWhiteSpace(contract.S("programId")))
        {
            throw new InvalidDataException("Invalid business observation identity");
        }

        foreach (var key in new[] { "inputs", "arguments", "observations" })
        {
            if (contract[key] is not JsonArray values)
            {
                throw new InvalidDataException("Missing business contract." + key);
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in values)
            {
                FixedExternalResponses.RequireKeys(item!, "key field type statementId");
                if (string.IsNullOrWhiteSpace(item!.S("key")) || string.IsNullOrWhiteSpace(item!.S("field")) || !names.Add(item!.S("key")) || item!.S("type") is not ("number" or "text" or "number-array" or "text-array"))
                {
                    throw new InvalidDataException("Invalid or duplicate business key/type");
                }
            }
        }

        if (!contract.A("observations").Any())
        {
            throw new InvalidDataException("Empty business observation");
        }

        if (!contract.B("includeRequests") || !contract.B("includeSignal"))
        {
            throw new InvalidDataException("External request order and termination must be observed");
        }

        _ = contract["includeBytes"]?.GetValue<bool>() ?? throw new InvalidDataException("Missing includeBytes");
        var review = contract["review"] ?? throw new InvalidDataException("Missing contract review");
        FixedExternalResponses.RequireKeys(review, "approved reviewer contentHash");
        if (!review.B("approved") || string.IsNullOrWhiteSpace(review.S("reviewer")) || review.S("contentHash") != ContractHash(contract))
        {
            throw new InvalidDataException("Missing/stale business contract review");
        }
    }

    /// <summary>業務入力と順序付き引数を明示した格納先へ写します。</summary>
    /// <param name="input">values、arguments と固定応答。fields は内部状態注入です。</param>
    /// <param name="contract">承認済み入力境界。</param>
    /// <returns>Machine に渡す固定入力。</returns>
    public static JsonNode Input(JsonNode input, JsonNode contract)
    {
        FixedExternalResponses.RequireKeys(input, "values arguments fields externalResponses");
        var fields = input["fields"]?.DeepClone().AsObject() ?? new JsonObject();
        var values = input["values"]?.AsObject() ?? new JsonObject();
        var declared = contract.A("inputs").ToArray();
        if (!values.Select(p => p.Key).ToHashSet(StringComparer.Ordinal).SetEquals(declared.Select(d => d.S("key"))))
        {
            throw new InvalidDataException("Business input keys missing or unknown");
        }

        void Put(JsonNode binding, JsonNode? value)
        {
            ValidateValue(value, binding.S("type"));
            if (fields.ContainsKey(binding.S("field")))
            {
                throw new InvalidDataException("Duplicate business input/initial-state field");
            }

            fields[binding.S("field")] = value!.DeepClone();
        }

        foreach (var binding in declared)
        {
            Put(binding, values[binding.S("key")]);
        }

        var arguments = input["arguments"]?.AsArray() ?? [];
        var bindings = contract.A("arguments").ToArray();
        if (arguments.Count != bindings.Length)
        {
            throw new InvalidDataException("Business argument count mismatch");
        }

        for (var i = 0; i < arguments.Count; i++)
        {
            Put(bindings[i], arguments[i]);
        }

        var fixture = new JsonObject { ["fields"] = fields };
        if (input["externalResponses"] != null)
        {
            fixture["externalResponses"] = input["externalResponses"]!.DeepClone();
        }

        return fixture;
    }

    /// <summary>指定した業務キー、終了種別、外部依頼列とバイト列を取得します。</summary>
    /// <param name="result">生成 API の結果。</param>
    /// <param name="contract">レビュー済み観測契約。</param>
    /// <returns>独立比較用の業務観測。</returns>
    public static JsonObject Observe(JsonNode result, JsonNode contract)
    {
        var values = new JsonObject();
        foreach (var observation in contract.A("observations"))
        {
            var fields = result["fields"]!.AsObject();
            if (!fields.TryGetPropertyValue(observation.S("field"), out var value))
            {
                throw new InvalidDataException("Missing business observation field: " + observation.S("field"));
            }

            ValidateValue(value, observation.S("type"));
            values[observation.S("key")] = value!.DeepClone();
        }

        var observed = new JsonObject { ["values"] = values, ["signal"] = result["signal"]!.DeepClone(), ["externalRequests"] = result["externalRequests"]?.DeepClone() ?? new JsonArray() };
        if (contract.B("includeBytes"))
        {
            observed["bytes"] = result["bytes"]?.DeepClone() ?? new JsonArray();
        }

        return observed;
    }

    /// <summary>型付き観測に一致する独立期待値を検査します。</summary>
    /// <param name="expected">独立観測。</param>
    /// <param name="contract">観測境界。</param>
    public static void ValidateExpected(JsonNode expected, JsonNode contract)
    {
        FixedExternalResponses.RequireKeys(expected, "values signal externalRequests bytes");
        var values = expected["values"]?.AsObject() ?? throw new InvalidDataException("Missing expected.values");
        var observations = contract.A("observations").ToArray();
        if (!values.Select(p => p.Key).ToHashSet(StringComparer.Ordinal).SetEquals(observations.Select(o => o.S("key"))))
        {
            throw new InvalidDataException("Missing/unknown business observation keys");
        }

        foreach (var observation in observations)
        {
            ValidateValue(values[observation.S("key")], observation.S("type"));
        }

        if (expected["externalRequests"] is not JsonArray || expected.S("signal") is not ("goback" or "stopRun" or "exitProgram" or "fallthrough"))
        {
            throw new InvalidDataException("Missing request order or termination");
        }

        if (contract.B("includeBytes"))
        {
            if (expected["bytes"] is not JsonArray bytes)
            {
                throw new InvalidDataException("Missing expected bytes");
            }

            foreach (var record in bytes)
            {
                ValidateBytes(record!);
            }
        }
        else if (expected.AsObject().ContainsKey("bytes"))
        {
            throw new InvalidDataException("Bytes outside the reviewed contract");
        }
    }

    /// <summary>数値は精密な値として比較し、文字と配列の順序は完全一致を要求します。</summary>
    /// <param name="expected">独立観測値。</param>
    /// <param name="actual">実際の観測値。</param>
    /// <param name="contract">レビュー済みの型とキー。</param>
    /// <returns>値の表記を丸めない差分。</returns>
    public static JsonObject Compare(JsonNode expected, JsonNode actual, JsonNode contract)
    {
        var comparable = expected.DeepClone();
        foreach (var observation in contract.A("observations").Where(o => o.S("type").StartsWith("number", StringComparison.Ordinal)))
        {
            var key = observation.S("key");
            var left = comparable["values"]![key]!;
            var right = actual["values"]![key]!;
            if (observation.S("type") == "number")
            {
                if (Number.Parse(left.GetValue<string>()) == Number.Parse(right.GetValue<string>()))
                {
                    comparable["values"]![key] = right.DeepClone();
                }
            }
            else
            {
                var expectedValues = left.AsArray();
                var actualValues = right.AsArray();
                for (var i = 0; i < Math.Min(expectedValues.Count, actualValues.Count); i++)
                {
                    if (Number.Parse(expectedValues[i]!.GetValue<string>()) == Number.Parse(actualValues[i]!.GetValue<string>()))
                    {
                        expectedValues[i] = actualValues[i]!.DeepClone();
                    }
                }
            }
        }

        var result = ReferenceCase.Compare(comparable, actual);
        foreach (var difference in result.A("differences"))
        {
            foreach (var observation in contract.A("observations"))
            {
                var prefix = "/values/" + observation.S("key").Replace("~", "~0").Replace("/", "~1");
                if (difference.S("path") == prefix || difference.S("path").StartsWith(prefix + "/", StringComparison.Ordinal))
                {
                    difference["businessKey"] = observation.S("key");
                    difference["statementId"] = observation["statementId"]?.DeepClone();
                    difference["observationType"] = observation.S("type");
                }
            }
        }

        return result;
    }

    /// <summary>バイト列の長さ、境界、文字コード情報を検査します。</summary>
    /// <param name="value">base64、length、recordLengths、encoding。</param>
    public static void ValidateBytes(JsonNode value)
    {
        FixedExternalResponses.RequireKeys(value, "base64 length recordLengths encoding");
        var bytes = Convert.FromBase64String(value.S("base64"));
        if (value["length"] == null || value.I("length") != bytes.Length || string.IsNullOrWhiteSpace(value.S("encoding")) || value["recordLengths"] is not JsonArray lengths || lengths.Count == 0 || lengths.Any(l => l == null || l.GetValue<int>() < 0) || lengths.Sum(l => l!.GetValue<int>()) != bytes.Length)
        {
            throw new InvalidDataException("Invalid byte length, record boundaries or encoding");
        }
    }

    /// <summary>最初の不一致 offset と限定範囲を返します。論理 PIC デコードは行いません。</summary>
    /// <param name="expected">参照バイト列。</param>
    /// <param name="actual">実際のバイト列。</param>
    /// <returns>一致状態と offset、最大 16 バイトの表示。</returns>
    public static JsonObject CompareBytes(JsonNode expected, JsonNode actual)
    {
        ValidateBytes(expected);
        ValidateBytes(actual);
        var left = Convert.FromBase64String(expected.S("base64"));
        var right = Convert.FromBase64String(actual.S("base64"));
        var offset = 0;
        while (offset < Math.Min(left.Length, right.Length) && left[offset] == right[offset])
        {
            offset++;
        }

        return new JsonObject { ["matched"] = offset == left.Length && offset == right.Length && JsonNode.DeepEquals(expected["recordLengths"], actual["recordLengths"]) && expected.S("encoding") == actual.S("encoding"), ["firstMismatchOffset"] = offset < Math.Max(left.Length, right.Length) ? offset : null, ["expectedLength"] = left.Length, ["actualLength"] = right.Length, ["expectedEncoding"] = expected.S("encoding"), ["actualEncoding"] = actual.S("encoding"), ["expectedRecordLengths"] = new JsonArray(expected.A("recordLengths").Take(32).Select(v => v.DeepClone()).ToArray()), ["actualRecordLengths"] = new JsonArray(actual.A("recordLengths").Take(32).Select(v => v.DeepClone()).ToArray()), ["omittedRecordBoundaries"] = Math.Max(0, Math.Max(expected.A("recordLengths").Count(), actual.A("recordLengths").Count()) - 32), ["expectedWindow"] = Convert.ToHexString(left.AsSpan(Math.Min(offset, left.Length), Math.Min(16, Math.Max(0, left.Length - offset)))), ["actualWindow"] = Convert.ToHexString(right.AsSpan(Math.Min(offset, right.Length), Math.Min(16, Math.Max(0, right.Length - offset)))), ["omittedBytes"] = Math.Max(0, Math.Max(left.Length, right.Length) - offset - 16) };
    }

    private static void ValidateValue(JsonNode? value, string type)
    {
        var array = type.EndsWith("-array", StringComparison.Ordinal);
        var values = array ? value as JsonArray ?? throw new InvalidDataException("Expected ordered array") : new JsonArray(value?.DeepClone());
        foreach (var item in values)
        {
            var text = item?.GetValue<string>() ?? throw new InvalidDataException("Missing/null observation value");
            if (type.StartsWith("number", StringComparison.Ordinal))
            {
                _ = Number.Parse(text);
            }
        }
    }
}
