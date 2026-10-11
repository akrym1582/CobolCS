using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CobolCS.Frontend;

/// <summary>
/// portable-test の設定を検証し、Python 版と同じ UTF-8 正規化表現のハッシュを生成します。
/// ホスト依存の規則は fujitsu-review で明示が必要です。
/// </summary>
public sealed class FrontendConfiguration
{
    private readonly JsonObject values;

    private FrontendConfiguration(JsonObject values)
    {
        this.values = values;
    }

    /// <summary>Gets Python json.dumps(sort_keys=True, separators=(',', ':'), ensure_ascii=False) に対応する設定文字列。</summary>
    public string CanonicalJson => Canonicalize(values);

    /// <summary>Gets 設定の UTF-8 表現の SHA-256 の小文字表記。</summary>
    public string Hash => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalJson)));

    /// <summary>入力 JSON と既定値を合成して検証します。</summary>
    /// <param name="json">明示設定。省略時は portable-test の既定値。</param>
    /// <returns>検証された設定。</returns>
    public static FrontendConfiguration Parse(string? json = null)
    {
        var supplied = json is null ? new JsonObject() : JsonNode.Parse(json) as JsonObject ?? throw new ArgumentException("Config must be an object");
        var config = JsonNode.Parse("""
            {"profile":"portable-test","sourceFormat":"free","sourceEncoding":"utf-8",
             "fixedColumnPolicy":"unicode-ascii","dataEncoding":"ascii","nationalEncoding":"utf-16be",
             "collation":"ascii","binaryLayout":"big-endian","signStorage":"leading-separate",
             "arithmeticProfile":"exact-rational","controlFlowMode":"native","sqlcaInclude":"none",
             "p64":null,"whenCompiled":null,"redefinesSizePolicy":"equal-fixed","decimalPoint":"dot",
             "currencySymbol":"$","mathPrecision":34,"maxSortBytes":16777216,"maxRecords":100000,
             "maxSteps":1000000,"timeoutMs":10000}
            """)!.AsObject();
        foreach (var (key, value) in supplied)
        {
            if (key == "testGeneration")
            {
                BranchGenerationConfiguration.Validate(value);
                config[key] = value!.DeepClone();
                continue;
            }

            if (!config.ContainsKey(key))
            {
                throw new ArgumentException("Unknown config keys");
            }

            config[key] = value?.DeepClone();
        }

        string? Text(string key) => config[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

        void OneOf(string key, params string[] allowed)
        {
            if (!allowed.Contains(Text(key), StringComparer.Ordinal))
            {
                throw new ArgumentException("Unsupported config " + key);
            }
        }

        if (Text("profile") == "fujitsu-review" && new[] { "fixedColumnPolicy", "dataEncoding", "nationalEncoding", "collation", "binaryLayout", "signStorage", "arithmeticProfile" }.Any(key => !supplied.ContainsKey(key)))
        {
            throw new ArgumentException("fujitsu-review requires explicit host-sensitive profile fields; no host defaults are inferred");
        }

        if (config["p64"] is not null && (config["p64"] is not JsonValue p64 || !p64.TryGetValue<bool>(out _)))
        {
            throw new ArgumentException("p64 must be true, false or null (unset)");
        }

        OneOf("fixedColumnPolicy", "unicode-ascii", "unicode-columns");
        OneOf("dataEncoding", "ascii", "fujitsu-ebcdic-kana");
        OneOf("collation", "ascii", "host-byte-order");
        OneOf("controlFlowMode", "native", "stub-control");
        OneOf("sqlcaInclude", "none", "portable-minimal");
        OneOf("redefinesSizePolicy", "equal-fixed", "max-family");
        OneOf("decimalPoint", "dot", "comma");
        OneOf("arithmeticProfile", "exact-rational", "approximate-math");
        if (Text("signStorage") is not ("leading-separate" or "trailing-overpunch"))
        {
            throw new ArgumentException("Unsupported signStorage");
        }

        if (Text("signStorage") == "trailing-overpunch" && Text("dataEncoding") != "fujitsu-ebcdic-kana")
        {
            throw new ArgumentException("Overpunch requires explicit EBCDIC kana profile");
        }

        OneOf("sourceEncoding", "utf-8");
        if (Text("sourceFormat") is not ("fixed" or "free") || Text("profile") is not ("portable-test" or "fujitsu-review"))
        {
            throw new ArgumentException("Invalid profile/sourceFormat");
        }

        if (Text("nationalEncoding") is not ("utf-16be" or "utf-16le"))
        {
            throw new ArgumentException("Invalid nationalEncoding");
        }

        if (Text("binaryLayout") is not ("big-endian" or "little-endian"))
        {
            throw new ArgumentException("Invalid binaryLayout");
        }

        foreach (var key in new[] { "maxSteps", "timeoutMs", "mathPrecision", "maxSortBytes", "maxRecords" })
        {
            if (!BigInteger.TryParse(config[key]?.ToJsonString(), NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number <= 0)
            {
                throw new ArgumentException("Invalid " + key);
            }
        }

        var currency = Text("currencySymbol");
        if (BigInteger.Parse(config["mathPrecision"]!.ToJsonString(), CultureInfo.InvariantCulture) > 100 || currency is null || currency.EnumerateRunes().Count() != 1 || "0123456789.,+- ".Contains(currency, StringComparison.Ordinal))
        {
            throw new ArgumentException("Invalid mathPrecision/currencySymbol");
        }

        return new FrontendConfiguration(config);
    }

    /// <summary>呼び出し元が内部設定を変更できないコピーを返します。</summary>
    /// <returns>IR に格納する設定オブジェクト。</returns>
    public JsonObject ToJson() => JsonNode.Parse(CanonicalJson)!.AsObject();

    private static string Canonicalize(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            return "{" + string.Join(",", obj.OrderBy(p => p.Key, Comparer<string>.Create(CompareCodePoints)).Select(p => Quote(p.Key) + ":" + Canonicalize(p.Value))) + "}";
        }

        if (node is JsonArray array)
        {
            return "[" + string.Join(",", array.Select(Canonicalize)) + "]";
        }

        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            return Quote(text);
        }

        var raw = node?.ToJsonString() ?? "null";
        if (node?.GetValueKind() != JsonValueKind.Number)
        {
            return raw;
        }

        if (BigInteger.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
        {
            return integer.ToString(CultureInfo.InvariantCulture);
        }

        return raw.IndexOfAny(['.', 'e', 'E']) < 0 ? raw : PythonFloat(raw);
    }

    private static int CompareCodePoints(string a, string b)
    {
        var left = a.EnumerateRunes().GetEnumerator();
        var right = b.EnumerateRunes().GetEnumerator();
        while (left.MoveNext())
        {
            if (!right.MoveNext())
            {
                return 1;
            }

            var compared = left.Current.Value.CompareTo(right.Current.Value);
            if (compared != 0)
            {
                return compared;
            }
        }

        return right.MoveNext() ? -1 : 0;
    }

    private static string PythonFloat(string raw)
    {
        var number = double.Parse(raw, CultureInfo.InvariantCulture);
        if (!double.IsFinite(number))
        {
            return number < 0 ? "-Infinity" : "Infinity";
        }

        var sign = double.IsNegative(number) ? "-" : string.Empty;
        if (number == 0)
        {
            return sign + "0.0";
        }

        var parts = Math.Abs(number).ToString("R", CultureInfo.InvariantCulture).Split('E');
        var dot = parts[0].IndexOf('.');
        var digits = parts[0].Replace(".", string.Empty, StringComparison.Ordinal);
        var leading = digits.Length - digits.TrimStart('0').Length;
        var exponent = (dot < 0 ? parts[0].Length : dot) - leading - 1 + (parts.Length == 1 ? 0 : int.Parse(parts[1], CultureInfo.InvariantCulture));
        digits = digits.TrimStart('0').TrimEnd('0');
        if (exponent < -4 || exponent >= 16)
        {
            return sign + digits[0] + (digits.Length > 1 ? "." + digits[1..] : string.Empty) + "e" + (exponent < 0 ? "-" : "+") + Math.Abs(exponent).ToString("D2", CultureInfo.InvariantCulture);
        }

        if (exponent < 0)
        {
            return sign + "0." + new string('0', -exponent - 1) + digits;
        }

        return sign + (digits.Length > exponent + 1 ? digits.Insert(exponent + 1, ".") : digits.PadRight(exponent + 1, '0') + ".0");
    }

    private static string Quote(string text)
    {
        var result = new StringBuilder("\"");
        foreach (var ch in text)
        {
            result.Append(ch switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\b' => "\\b",
                '\f' => "\\f",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                < ' ' => "\\u" + ((int)ch).ToString("x4", CultureInfo.InvariantCulture),
                _ => ch.ToString(),
            });
        }

        return result.Append('"').ToString();
    }
}
