using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CobolCS.Frontend.Parsing;

/// <summary>原文のUnicodeスカラー位置を保つ字句パターン照合です。</summary>
internal static partial class Values
{
    /// <summary>位置付きCOBOLトークンのパターン。</summary>
    internal static readonly V TokenPattern = new(new RegexPattern("\"(?:\"\"|[^\"\\n])*\"|'(?:''|[^'\\n])*'|\\d[\\w\\-\uFF0D\u2212]*[^\\W\\d_][\\w\\-\uFF0D\u2212]*|\\d+(?:\\.\\d+)?|(?:[^\\W\\d]|_)[\\w\\-\uFF0D\u2212]*|==|\\*\\*|<>|<=|>=|[.,()+*/=<>:\\-]|\\S"));

    /// <summary>解析値の RegexFullmatch 操作を行い、IRの値と順序を維持します。</summary>
    /// <param name="pattern">pattern として渡す操作対象。</param>
    /// <param name="text">text として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V RegexFullmatch(V pattern, V text) => MatchPattern(Text(pattern), Text(text), true, false);

    /// <summary>解析値の RegexMatch 操作を行い、IRの値と順序を維持します。</summary>
    /// <param name="pattern">pattern として渡す操作対象。</param>
    /// <param name="text">text として渡す操作対象。</param>
    /// <param name="flags">flags として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V RegexMatch(V pattern, V text, V? flags = null) => MatchPattern(Text(pattern), Text(text), false, flags is not null && Truth(flags));

    /// <summary>解析値の RegexFindall 操作を行い、IRの値と順序を維持します。</summary>
    /// <param name="pattern">pattern として渡す操作対象。</param>
    /// <param name="text">text として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V RegexFindall(V pattern, V text)
    {
        return ListOf(Items(RegexFinditer(Text(pattern), text)).Select(value =>
        {
            var match = (LocatedMatch)value.Data!;
            return S(match.OriginalGroup(match.Match.Groups.Count > 1 ? 1 : 0));
        }));
    }

    /// <summary>解析値の RegexSub 操作を行い、IRの値と順序を維持します。</summary>
    /// <param name="pattern">pattern として渡す操作対象。</param>
    /// <param name="replace">replace として渡す操作対象。</param>
    /// <param name="text">text として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V RegexSub(V pattern, Func<V, V> replace, V text)
    {
        var runes = Text(text).EnumerateRunes().Select(r => r.ToString()).ToArray();
        var result = new StringBuilder();
        var position = 0;
        foreach (var value in Items(RegexFinditer(Text(pattern), text)))
        {
            var match = (LocatedMatch)value.Data!;
            result.Append(string.Concat(runes.Skip(position).Take(match.Match.Index - position)));
            result.Append(Text(replace(value)));
            position = match.Match.Index + match.Match.Length;
        }

        return S(result.Append(string.Concat(runes.Skip(position))).ToString());
    }

    private static V RegexFinditer(string pattern, V text, V? start = null, V? end = null)
    {
        var original = Text(text).EnumerateRunes().Select(r => r.ToString()).ToArray();
        var mapped = MapRegexText(original);
        if (end is not null)
        {
            mapped = mapped[..Number(end)];
        }

        return ListOf(CreateRegex(pattern).Matches(mapped, start is null ? 0 : Number(start)).Select(m => new V(new LocatedMatch(m, original))));
    }

    private static V MatchPattern(string pattern, string text, bool full, bool ignoreCase)
    {
        var original = text.EnumerateRunes().Select(r => r.ToString()).ToArray();
        var mapped = MapRegexText(original);
        var match = CreateRegex(full ? "(?:" + pattern + ")\\z" : pattern, ignoreCase).Match(mapped);
        return match.Success && match.Index == 0 ? new V(new LocatedMatch(match, original)) : Nil;
    }

    private static Regex CreateRegex(string pattern, bool ignoreCase = false)
    {
        pattern = pattern.Replace(@"[^\W\d_]", @"[\p{L}\p{Nl}\p{No}]", StringComparison.Ordinal)
            .Replace(@"[^\W\d]", @"[\p{L}\p{Nl}\p{No}_]", StringComparison.Ordinal)
            .Replace(@"\w", @"\p{L}\p{N}_", StringComparison.Ordinal)
            .Replace(@"\s", @"[\s\u001c-\u001f]", StringComparison.Ordinal)
            .Replace(@"\S", @"[^\s\u001c-\u001f]", StringComparison.Ordinal);
        return new Regex(pattern, RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None), TimeSpan.FromSeconds(5));
    }

    private static string MapRegexText(string[] original)
    {
        return string.Concat(original.Select(s =>
        {
            if (s.Length == 1)
            {
                return s;
            }

            var rune = Rune.GetRuneAt(s, 0);
            return Rune.GetUnicodeCategory(rune) switch
            {
                UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter => "中",
                UnicodeCategory.DecimalDigitNumber => ((int)Rune.GetNumericValue(rune)).ToString(CultureInfo.InvariantCulture),
                UnicodeCategory.LetterNumber => "Ⅰ",
                UnicodeCategory.OtherNumber => "²",
                _ => "\ue000",
            };
        }));
    }

    private sealed record RegexPattern(string Pattern);

    private sealed record LocatedMatch(Match Match, string[] Original)
    {
        /// <summary>解析値の OriginalGroup 操作を行い、IRの値と順序を維持します。</summary>
        /// <param name="group">group として渡す操作対象。</param>
        /// <returns>操作の結果。</returns>
        internal string OriginalGroup(int group) => string.Concat(Original.Skip(Match.Groups[group].Index).Take(Match.Groups[group].Length));
    }
}
