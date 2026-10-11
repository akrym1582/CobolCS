using System.Globalization;
using System.Text.Json;

namespace CobolCS.Frontend.Tests;

/// <summary>
/// IR の項目順以外を保持して比較します。正規化やメタデータの暗黙的な除外は行いません。
/// 数値の字句も検査し、数値文字列や欠落と null の差を保持します。
/// </summary>
public static class JsonComparison
{
    /// <summary>構造差分を、安定した順番の JSON Pointer と値で返します。</summary>
    /// <param name="inputId">固定入力 ID。</param>
    /// <param name="expected">参照 JSON。</param>
    /// <param name="actual">候補 JSON。</param>
    /// <returns>見つかった全差分。</returns>
    public static IReadOnlyList<JsonDifference> Compare(string inputId, string expected, string actual)
    {
        using var left = JsonDocument.Parse(expected);
        using var right = JsonDocument.Parse(actual);
        var differences = new List<JsonDifference>();
        Visit(left.RootElement, right.RootElement, string.Empty);
        return differences;

        void Visit(JsonElement a, JsonElement b, string path)
        {
            if (a.ValueKind != b.ValueKind)
            {
                Add(a, b, path);
                return;
            }

            if (a.ValueKind == JsonValueKind.Object)
            {
                // ToDictionary rejects duplicate property names instead of silently losing evidence.
                var aa = a.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
                var bb = b.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
                foreach (var key in aa.Keys.Union(bb.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
                {
                    aa.TryGetValue(key, out var av);
                    bb.TryGetValue(key, out var bv);
                    Visit(av, bv, path + "/" + key.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal));
                }
            }
            else if (a.ValueKind == JsonValueKind.Array)
            {
                var aa = a.EnumerateArray().ToArray();
                var bb = b.EnumerateArray().ToArray();
                for (var i = 0; i < Math.Max(aa.Length, bb.Length); i++)
                {
                    Visit(i < aa.Length ? aa[i] : default, i < bb.Length ? bb[i] : default, path + "/" + i.ToString(CultureInfo.InvariantCulture));
                }
            }
            else if (a.ValueKind == JsonValueKind.String ? a.GetString() != b.GetString() : Render(a) != Render(b))
            {
                Add(a, b, path);
            }
        }

        void Add(JsonElement a, JsonElement b, string path) => differences.Add(new(inputId, path, Render(a), Render(b)));
    }

    private static string Render(JsonElement element) => element.ValueKind == JsonValueKind.Undefined ? "<missing>" : element.GetRawText();
}
