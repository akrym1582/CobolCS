using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CobolCS.Runtime;

/// <summary>生成メンバーから独立した業務型契約を検査し、精密数値と完全一致の文字・配列を比較します。</summary>
public static class BusinessComparison
{
    /// <summary>JSON の重複キーを破棄せず拒否し、有限サイズと深さで読み込みます。</summary>
    /// <param name="json">最大 2 MiB のローカル JSON。</param>
    /// <returns>重複キーのない業務データ。</returns>
    public static JsonNode Parse(string json)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 2 * 1024 * 1024)
        {
            throw new ArgumentException("Business JSON size limit");
        }

        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        void Check(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                    {
                        throw new ArgumentException("Duplicate business key: " + property.Name);
                    }

                    Check(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var value in element.EnumerateArray())
                {
                    Check(value);
                }
            }
        }

        Check(document.RootElement);
        return JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { MaxDepth = 64 }) ?? throw new ArgumentException("Null business document");
    }

    /// <summary>未知キーと必須キー欠落を入口で拒否します。</summary>
    /// <param name="node">検査するオブジェクト。</param>
    /// <param name="allowed">許可するキー。</param>
    /// <param name="required">省略できないキー。</param>
    public static void Keys(JsonNode node, string[] allowed, string[] required)
    {
        if (node is not JsonObject obj || obj.Any(p => !allowed.Contains(p.Key, StringComparer.Ordinal)) || required.Any(p => !obj.ContainsKey(p)))
        {
            throw new ArgumentException("Unknown or missing business key: " + Clip(node.ToJsonString()));
        }
    }

    /// <summary>契約の型を決める記述自体を検査します。未知型・不正 PIC は比較前に失敗します。</summary>
    /// <param name="type">number/string/boolean/object/array の記述。</param>
    public static void ValidateType(JsonNode type)
    {
        Keys(type, ["type", "nullable", "pic", "fields", "items"], ["type"]);
        if (type["nullable"] != null)
        {
            _ = type["nullable"]!.GetValue<bool>();
        }

        var kind = type["type"]!.GetValue<string>();
        if (kind is not ("number" or "string" or "boolean" or "object" or "array"))
        {
            throw new ArgumentException("Unknown business type " + kind);
        }

        if ((kind != "object" && type["fields"] != null) || (kind != "array" && type["items"] != null))
        {
            throw new ArgumentException("Unexpected type members");
        }

        if (type["pic"] != null)
        {
            var pic = type["pic"]!.GetValue<string>();
            var numeric = Regex.IsMatch(pic, "^S?9(?:\\([1-9][0-9]{0,3}\\)|9*)?(?:V9(?:\\([1-9][0-9]{0,3}\\)|9*)?)?$", RegexOptions.CultureInvariant);
            var text = Regex.IsMatch(pic, "^[XAN](?:\\([1-9][0-9]{0,3}\\)|[XAN]*)$", RegexOptions.CultureInvariant) && pic.Where(char.IsLetter).Distinct().Count() == 1;
            if ((kind == "number" && !numeric) || (kind == "string" && !text) || kind is not ("number" or "string"))
            {
                throw new ArgumentException("Invalid business PIC " + pic);
            }

            var (digits, scale) = Dimensions(pic);
            if (digits > 1000 || scale > 1000)
            {
                throw new ArgumentException("Business PIC limit");
            }
        }

        if (kind == "object")
        {
            if (type["fields"] is not JsonObject fields)
            {
                throw new ArgumentException("Object requires fields");
            }

            foreach (var pair in fields)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 128 || pair.Value == null)
                {
                    throw new ArgumentException("Unset business field type");
                }

                ValidateType(pair.Value);
            }
        }
        else if (kind == "array")
        {
            ValidateType(type["items"] ?? throw new ArgumentException("Array requires items"));
        }
    }

    /// <summary>値を double に変換せず、欠落・null・型・配列順・末尾空白も比較します。</summary>
    /// <param name="caseId">失敗した業務ケース。</param>
    /// <param name="type">検証する型契約。</param>
    /// <param name="expected">独立期待値または固定要求。</param>
    /// <param name="actual">現在の業務観測。</param>
    /// <param name="pointer">元文リンクと併記できる JSON Pointer。</param>
    public static void Equal(string caseId, JsonNode type, JsonNode? expected, JsonNode? actual, string pointer = "")
    {
        ValidateType(type);
        void Fail(string location, JsonNode? wanted, JsonNode? got, string? detail = null)
        {
            var error = new InvalidOperationException(caseId + " " + location + (detail == null ? string.Empty : " (" + detail + ")") + " expected=" + Clip(wanted?.ToJsonString() ?? "null") + " actual=" + Clip(got?.ToJsonString() ?? "null"));
            error.Data["BusinessPointer"] = location;
            error.Data["BusinessExpected"] = Clip(wanted?.ToJsonString() ?? "null");
            error.Data["BusinessActual"] = Clip(got?.ToJsonString() ?? "null");
            error.Data["BusinessOmittedCharacters"] = Math.Max(0, (wanted?.ToJsonString().Length ?? 4) - 256) + Math.Max(0, (got?.ToJsonString().Length ?? 4) - 256);
            throw error;
        }

        void Compare(JsonNode schema, JsonNode? wanted, JsonNode? got, string location)
        {
            if (wanted == null || got == null)
            {
                if (schema["nullable"]?.GetValue<bool>() != true || wanted != null || got != null)
                {
                    Fail(location, wanted, got);
                }

                return;
            }

            switch (schema["type"]!.GetValue<string>())
            {
                case "object":
                    var fields = schema["fields"]!.AsObject();
                    if (wanted is not JsonObject expectedObject || got is not JsonObject actualObject || fields.Count != expectedObject.Count || fields.Count != actualObject.Count || fields.Any(p => !expectedObject.ContainsKey(p.Key) || !actualObject.ContainsKey(p.Key)))
                    {
                        Fail(location, wanted, got, "unknown/missing key");
                        return;
                    }

                    foreach (var field in fields)
                    {
                        Compare(field.Value!, expectedObject[field.Key], actualObject[field.Key], location + "/" + field.Key.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal));
                    }

                    break;
                case "array":
                    if (wanted is not JsonArray expectedArray || got is not JsonArray actualArray || expectedArray.Count != actualArray.Count)
                    {
                        Fail(location, wanted, got);
                        return;
                    }

                    for (var index = 0; index < expectedArray.Count; index++)
                    {
                        Compare(schema["items"]!, expectedArray[index], actualArray[index], location + "/" + index);
                    }

                    break;
                case "number":
                    if (wanted is not JsonValue expectedNumber || got is not JsonValue actualNumber || !expectedNumber.TryGetValue<string>(out var left) || !actualNumber.TryGetValue<string>(out var right))
                    {
                        Fail(location, wanted, got, "exact number string required");
                        return;
                    }

                    Number first;
                    Number second;
                    try
                    {
                        first = Number.Parse(left);
                        second = Number.Parse(right);
                    }
                    catch (FormatException)
                    {
                        Fail(location, wanted, got, "invalid exact number");
                        return;
                    }

                    ValidatePic(schema, first, left);
                    ValidatePic(schema, second, right);
                    if (first != second)
                    {
                        Fail(location, wanted, got);
                    }

                    break;
                case "string":
                    if (wanted is not JsonValue expectedText || got is not JsonValue actualText || !expectedText.TryGetValue<string>(out var text) || !actualText.TryGetValue<string>(out var actualTextValue) || text != actualTextValue)
                    {
                        Fail(location, wanted, got);
                        return;
                    }

                    if (schema["pic"] is JsonValue pic && (text.Length > Dimensions(pic.GetValue<string>()).Digits || (pic.GetValue<string>().StartsWith('N') ? text.Any(char.IsSurrogate) : text.Any(c => c > 127))))
                    {
                        Fail(location, wanted, got, "PIC length");
                    }

                    break;
                case "boolean":
                    if (wanted is not JsonValue expectedBoolean || got is not JsonValue actualBoolean || !expectedBoolean.TryGetValue<bool>(out var flag) || !actualBoolean.TryGetValue<bool>(out var actualFlag) || flag != actualFlag)
                    {
                        Fail(location, wanted, got);
                    }

                    break;
            }
        }

        Compare(type, expected, actual, pointer);
    }

    /// <summary>既知の業務位置と理由を保持し、メッセージから位置を推測しません。</summary>
    /// <param name="message">有限長に保存する人向けの説明。</param>
    /// <param name="pointer">判明した JSON Pointer。</param>
    /// <param name="reasonCode">比較または外部応答の失敗。</param>
    /// <returns>xUnit に伝える同じ例外。</returns>
    internal static InvalidOperationException Failure(string message, string pointer, string reasonCode)
    {
        var error = new InvalidOperationException(message);
        error.Data["BusinessPointer"] = pointer;
        error.Data["BusinessReason"] = reasonCode;
        return error;
    }

    /// <summary>差分に生の巨大値を出さず、診断出力を有限にします。</summary>
    /// <param name="text">JSON または説明。</param>
    /// <returns>最大 256 文字の抜粋。</returns>
    private static string Clip(string text) => text.Length <= 256 ? text : text[..256] + "…";

    /// <summary>限定した業務 PIC の桁数を数えます。COBOL 解析器への実行依存を作りません。</summary>
    /// <param name="pic">検証済みの単純 PIC。</param>
    /// <returns>全桁と小数桁。</returns>
    private static (int Digits, int Scale) Dimensions(string pic)
    {
        int Count(string part) => Regex.Matches(part, "[9XAN](?:\\(([0-9]+)\\))?", RegexOptions.CultureInvariant).Sum(m => m.Groups[1].Success ? int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 1);
        var parts = pic.Split('V');
        return (parts.Sum(Count), parts.Length == 2 ? Count(parts[1]) : 0);
    }

    /// <summary>PIC に格納する前の業務値を、丸めて同値として扱いません。</summary>
    /// <param name="schema">数値型。</param>
    /// <param name="number">精密な値。</param>
    /// <param name="text">診断用の入力。</param>
    private static void ValidatePic(JsonNode schema, Number number, string text)
    {
        if (schema["pic"] is not JsonValue value)
        {
            return;
        }

        var pic = value.GetValue<string>();
        var (digits, scale) = Dimensions(pic);
        var units = number.Units(scale);
        if (number != new Number(units, System.Numerics.BigInteger.Pow(10, scale)) || System.Numerics.BigInteger.Abs(units) >= System.Numerics.BigInteger.Pow(10, digits) || (!pic.StartsWith('S') && number.Numerator.Sign < 0))
        {
            throw new ArgumentException("Business value outside PIC " + pic + ": " + Clip(text));
        }
    }
}
