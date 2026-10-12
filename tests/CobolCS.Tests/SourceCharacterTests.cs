using CobolCS.Runtime;
using Xunit;

namespace CobolCS.Tests;

/// <summary>走査の独立手計算と、受信領域・pointer の境界を検証します。</summary>
public sealed class SourceCharacterTests
{
    /// <summary>非重複・先頭一致・空白を固定期待値で確認します。</summary>
    /// <param name="value">領域。</param>
    /// <param name="mode">走査方法。</param>
    /// <param name="pattern">対象。</param>
    /// <param name="count">手計算個数。</param>
    [Theory]
    [InlineData("AAAA", "all", "AA", 2)]
    [InlineData("AAAA", "leading", "AA", 2)]
    [InlineData("AAAAA", "all", "AA", 2)]
    [InlineData("BAAAA", "leading", "AA", 0)]
    [InlineData("AA AA", "all", "AA", 2)]
    [InlineData("A  ", "characters", null, 3)]
    public void TallyPreservesExistingValue(string value, string mode, string? pattern, int count) => Assert.Equal(new Number(5 + count), new Number(5) + SourceCharacters.Tally(value, mode, pattern));

    /// <summary>更新前の領域だけを検索し、置換後を再検索しません。</summary>
    /// <param name="value">領域。</param>
    /// <param name="mode">置換方法。</param>
    /// <param name="pattern">対象。</param>
    /// <param name="replacement">置換値。</param>
    /// <param name="expected">固定結果。</param>
    [Theory]
    [InlineData("AAAA", "first", "AA", "BB", "BBAA")]
    [InlineData("AAAAA", "all", "AA", "BB", "BBBBA")]
    [InlineData("AAAABAA", "leading", "AA", "BB", "BBBBBAA")]
    [InlineData("BAAAA", "leading", "AA", "BB", "BAAAA")]
    [InlineData("A  ", "characters", null, "Z", "ZZZ")]
    [InlineData("ABAB", "all", "AB", "AB", "ABAB")]
    public void ReplacementHasFixedLength(string value, string mode, string? pattern, string replacement, string expected) => Assert.Equal(expected, SourceCharacters.Replace(value, mode, pattern, replacement));

    /// <summary>最初の delimiter を除外し、SIZE 転送の既存 suffix を保持します。</summary>
    /// <param name="sending">送信値。</param>
    /// <param name="delimiter">値区切り。</param>
    /// <param name="pointer">開始位置。</param>
    /// <param name="expected">受信値。</param>
    /// <param name="next">更新位置。</param>
    /// <param name="overflow">あふれ。</param>
    [Theory]
    [InlineData("A,B,C", ",", 2, "aAZdef", 4, false)]
    [InlineData(",AB", ",", 1, "Zbcdef", 2, false)]
    [InlineData("ABC", "!", 1, "ABCZef", 5, false)]
    [InlineData("AB--CD--EF", "--", 1, "ABZdef", 4, false)]
    [InlineData("ABCDE,", ",", 1, "ABCDEZ", 7, false)]
    [InlineData("ABCDE,", ",", 3, "abABCD", 7, true)]
    [InlineData("A,", ",", 0, "abcdef", 0, true)]
    [InlineData("A,", ",", 7, "abcdef", 7, true)]
    public void ValueDelimiterKeepsSizeTransferSemantics(string sending, string delimiter, int pointer, string expected, int next, bool overflow)
    {
        var actual = SourceSupport.StringSize("abcdef", new Number(pointer), [SourceCharacters.Prefix(sending, delimiter), "Z"]);
        Assert.Equal(expected, actual.Value);
        Assert.Equal(new Number(next), actual.Pointer);
        Assert.Equal(overflow, actual.Overflow);
    }

    /// <summary>空 delimiter・異長置換・国別文字・未知丸めを成功扱いしません。</summary>
    [Fact]
    public void UnsupportedOperandsFail()
    {
        Assert.Throws<ArgumentException>(() => SourceCharacters.Prefix("ABC", string.Empty));
        Assert.Throws<ArgumentException>(() => SourceCharacters.Replace("AAAA", "all", "AA", "B"));
        Assert.Throws<NotSupportedException>(() => SourceCharacters.Tally("あ", "characters", null));
        Assert.Throws<ArgumentException>(() => BusinessValues.Decimal(new Number(1, 3), 2));
        Assert.Throws<ArgumentException>(() => BusinessValues.Array(new System.Text.Json.Nodes.JsonArray(), 2));
    }
}
