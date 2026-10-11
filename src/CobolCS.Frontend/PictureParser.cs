using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CobolCS.Frontend;

/// <summary>
/// PythonBol-Translator 2b53472b9d884760ff09be2753472442b099d85a の PIC 解析を移植します。
/// Copyright (c) 2026 IronAdamant, MIT License。THIRD-PARTY-NOTICES.md を参照してください。
/// 宣言側の NATIONAL や編集項目の追加処理をここへ混在させません。
/// </summary>
public static partial class PictureParser
{
    /// <summary>既存 PIC 解析器と同じ展開・分類・表示幅計算を行います。</summary>
    /// <param name="raw">PIC 句。</param>
    /// <returns>解析した PIC。未知カテゴリも上流と同様に保持します。</returns>
    public static PictureClause Parse(string raw)
    {
        var expanded = raw.ToUpperInvariant().Trim();
        foreach (var prefix in new[] { "PIC ", "PICTURE " })
        {
            if (expanded.StartsWith(prefix, StringComparison.Ordinal))
            {
                expanded = expanded[prefix.Length..];
                break;
            }
        }

        expanded = RepeatPattern().Replace(expanded, match =>
        {
            var digits = match.Groups[2].Value.EnumerateRunes().ToArray();
            if (digits.Length == 0 || digits.Any(rune => Rune.GetUnicodeCategory(rune) != UnicodeCategory.DecimalDigitNumber))
            {
                return match.Value;
            }

            var number = string.Concat(digits.Select(rune => ((int)Rune.GetNumericValue(rune)).ToString(CultureInfo.InvariantCulture)));
            return string.Concat(Enumerable.Repeat(match.Groups[1].Value, int.Parse(number, CultureInfo.InvariantCulture)));
        });
        var nine = expanded.Contains('9');
        var x = expanded.Contains('X');
        var a = expanded.Contains('A');
        var edited = expanded.Contains("CR", StringComparison.Ordinal) || expanded.Contains("DB", StringComparison.Ordinal) || expanded.Any(c => "ZB,.+-$*/".Contains(c));
        var category = edited ? "edited" : x && !nine ? "alphanumeric" : a && !nine && !x ? "alphabetic" : nine && !x && !a ? "numeric" : x ? "alphanumeric" : "unknown";
        var clean = expanded.Replace("S", string.Empty, StringComparison.Ordinal);
        var point = clean.IndexOf('V');
        var decimals = point < 0 ? 0 : clean[(point + 1)..].Count(c => c == '9');

        // CR/DB each occupy two positions, so counting both letters preserves the upstream result.
        var size = clean.EnumerateRunes().Count(c => c.Value is not ('V' or 'P'));
        return new(raw.Trim(), expanded, category, size, decimals, expanded.Contains('S'));
    }

    [GeneratedRegex(@"(CR|DB|[A9XZVBS,.\-+$*P/])\(([^()]*)\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RepeatPattern();
}
