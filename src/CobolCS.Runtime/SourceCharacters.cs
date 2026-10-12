using System.Text;

namespace CobolCS.Runtime;

/// <summary>IR を使わず、固定長 ASCII の非重複走査を実行します。</summary>
public static class SourceCharacters
{
    /// <summary>最初の値区切りより前だけを転送対象にします。</summary>
    /// <param name="value">送信領域。</param>
    /// <param name="delimiter">空でない値区切り。</param>
    /// <returns>区切りを含まない接頭辞。</returns>
    public static string Prefix(string value, string delimiter)
    {
        Check(value);
        Check(delimiter);
        if (delimiter.Length == 0)
        {
            throw new ArgumentException("Empty STRING delimiter");
        }

        var index = value.IndexOf(delimiter, StringComparison.Ordinal);
        return index < 0 ? value : value[..index];
    }

    /// <summary>末尾空白を含む領域を走査し、既存 tally に加える個数を返します。</summary>
    /// <param name="value">固定長領域。</param>
    /// <param name="mode">characters、all、leading。</param>
    /// <param name="pattern">characters の場合だけ null。</param>
    /// <returns>整数の追加個数。</returns>
    public static Number Tally(string value, string mode, string? pattern)
    {
        Check(value);
        return new Number(Positions(value, mode, pattern).Count());
    }

    /// <summary>一致する非重複領域を同じ長さで置換し、未変更部分を保持します。</summary>
    /// <param name="value">固定長領域。</param>
    /// <param name="mode">characters、all、leading、first。</param>
    /// <param name="pattern">characters の場合だけ null。</param>
    /// <param name="replacement">同長の置換値。</param>
    /// <returns>同じ長さの領域。</returns>
    public static string Replace(string value, string mode, string? pattern, string replacement)
    {
        Check(value);
        Check(replacement);
        if (replacement.Length != (mode == "characters" ? 1 : pattern?.Length))
        {
            throw new ArgumentException("INSPECT replacement length");
        }

        var buffer = new StringBuilder(value);
        foreach (var position in Positions(value, mode, pattern))
        {
            for (var i = 0; i < replacement.Length; i++)
            {
                buffer[position + i] = replacement[i];
            }
        }

        return buffer.ToString();
    }

    /// <summary>対象は走査開始時の値であり、置換済み文字を再検索しません。</summary>
    private static IEnumerable<int> Positions(string value, string mode, string? pattern)
    {
        if (mode == "characters")
        {
            return Enumerable.Range(0, value.Length);
        }

        if (mode is not ("all" or "leading" or "first") || string.IsNullOrEmpty(pattern))
        {
            throw new ArgumentException("INSPECT mode or empty pattern");
        }

        Check(pattern);
        var positions = new List<int>();
        for (var i = 0; i <= value.Length - pattern.Length;)
        {
            if (value.AsSpan(i, pattern.Length).SequenceEqual(pattern.AsSpan()))
            {
                positions.Add(i);
                if (mode == "first")
                {
                    break;
                }

                i += pattern.Length;
            }
            else
            {
                if (mode == "leading")
                {
                    break;
                }

                i++;
            }
        }

        return positions;
    }

    /// <summary>国別文字を ASCII と同一視しません。</summary>
    private static void Check(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Any(c => c > 127))
        {
            throw new NotSupportedException("Fixed ASCII character operation required");
        }
    }
}
