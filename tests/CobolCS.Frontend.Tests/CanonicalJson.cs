using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CobolCS.Frontend.Tests;

/// <summary>保存した参照IRのハッシュを、キー順だけを揃えたASCII JSONで再計算します。</summary>
internal static class CanonicalJson
{
    /// <summary>配列順、値、数値の表記を保持したJSONのSHA-256です。</summary>
    /// <param name="json">IRまたはその一部分。</param>
    /// <returns>固定参照と比較するハッシュ。</returns>
    internal static string Hash(string json) => Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(Serialize(json))));

    /// <summary>オブジェクトのキーをUnicodeスカラー順にし、文字列をASCIIエスケープします。</summary>
    /// <param name="json">構造を保持するJSON。</param>
    /// <returns>空白のない正準表記。</returns>
    internal static string Serialize(string json)
    {
        using var document = JsonDocument.Parse(json);
        var output = new StringBuilder();
        Write(document.RootElement, output);
        return output.ToString();
    }

    private static void Write(JsonElement element, StringBuilder output)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                output.Append('{');
                var first = true;
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, Comparer<string>.Create(CompareKeys)))
                {
                    if (!first)
                    {
                        output.Append(',');
                    }

                    first = false;
                    Quote(property.Name, output);
                    output.Append(':');
                    Write(property.Value, output);
                }

                output.Append('}');
                break;
            case JsonValueKind.Array:
                output.Append('[');
                for (var index = 0; index < element.GetArrayLength(); index++)
                {
                    if (index > 0)
                    {
                        output.Append(',');
                    }

                    Write(element[index], output);
                }

                output.Append(']');
                break;
            case JsonValueKind.String:
                Quote(element.GetString()!, output);
                break;
            default:
                output.Append(element.GetRawText());
                break;
        }
    }

    private static void Quote(string value, StringBuilder output)
    {
        output.Append('"');
        foreach (var character in value)
        {
            output.Append(character switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\b' => "\\b",
                '\f' => "\\f",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                < ' ' or >= '\u007f' => "\\u" + ((int)character).ToString("x4", CultureInfo.InvariantCulture),
                _ => character.ToString(),
            });
        }

        output.Append('"');
    }

    private static int CompareKeys(string left, string right)
    {
        var a = left.EnumerateRunes().Select(r => r.Value).ToArray();
        var b = right.EnumerateRunes().Select(r => r.Value).ToArray();
        for (var index = 0; index < Math.Min(a.Length, b.Length); index++)
        {
            var result = a[index].CompareTo(b[index]);
            if (result != 0)
            {
                return result;
            }
        }

        return a.Length.CompareTo(b.Length);
    }
}
