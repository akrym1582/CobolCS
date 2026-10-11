using System.Numerics;

namespace CobolCS.Runtime;

/// <summary>直接生成した C# が使用する、IR を受け取らない格納処理です。</summary>
public static class SourceSupport
{
    /// <summary>SIZE 転送で既存領域を保持し、実際に格納した文字数だけポインタを進めます。</summary>
    /// <param name="receiver">固定長の受信領域。</param>
    /// <param name="pointer">1 始まりの位置。</param>
    /// <param name="sendings">順序付き送信値。</param>
    /// <returns>更新値、ポインタ、overflow の有無。</returns>
    public static (string Value, Number Pointer, bool Overflow) StringSize(string receiver, Number pointer, IReadOnlyList<string> sendings)
    {
        if (pointer.CompareTo(new Number(1)) < 0 || pointer.CompareTo(new Number(receiver.Length)) > 0)
        {
            return (receiver, pointer, true);
        }

        var position = CobolValue.Integer(pointer) - 1;
        var buffer = receiver.ToCharArray();
        foreach (var sending in sendings)
        {
            foreach (var character in sending)
            {
                if (position == buffer.Length)
                {
                    return (new string(buffer), new Number(position + 1), true);
                }

                buffer[position++] = character;
            }
        }

        return (new string(buffer), new Number(position + 1), false);
    }

    /// <summary>公開値境界で未指定文字列と PIC 超過を拒否します。</summary>
    /// <param name="value">明示入力。</param>
    /// <param name="length">宣言文字数。</param>
    /// <param name="national">国別文字の既存規則。</param>
    /// <returns>検証済みの固定長値。</returns>
    public static string BoundaryText(string value, int length, bool national)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > length)
        {
            throw new ArgumentException("Text exceeds PIC length");
        }

        return TextValue(value, length, national, false);
    }

    /// <summary>数値の桁数・符号・丸めを格納境界で適用します。</summary>
    /// <param name="value">計算値。</param>
    /// <param name="digits">総桁数。</param>
    /// <param name="scale">小数桁数。</param>
    /// <param name="signed">符号付きかどうか。</param>
    /// <param name="rounded">丸めを行うかどうか。</param>
    /// <param name="sizeError">桁あふれ時に通知するかどうか。</param>
    /// <returns>格納可能な値。</returns>
    public static Number NumberValue(object value, int digits, int scale, bool signed, bool rounded = false, bool sizeError = false)
    {
        var number = CobolValue.Numeric(value);
        if (number.Denominator.IsZero)
        {
            throw new ArgumentException("Uninitialized Number");
        }

        var units = number.Units(scale, rounded);
        var limit = BigInteger.Pow(10, digits);
        if (sizeError && (BigInteger.Abs(units) >= limit || (!signed && units.Sign < 0)))
        {
            throw new SizeError("Numeric receiving field");
        }

        units %= limit;
        return new Number(signed ? units : BigInteger.Abs(units), BigInteger.Pow(10, scale));
    }

    /// <summary>固定長文字列の範囲を検査し、空白埋めと切り詰めを行います。</summary>
    /// <param name="value">入力値。</param>
    /// <param name="length">文字数。</param>
    /// <param name="national">BMP 文字を許す場合は true。</param>
    /// <param name="right">右寄せの場合は true。</param>
    /// <returns>格納する文字列。</returns>
    public static string TextValue(object value, int length, bool national = false, bool right = false)
    {
        var text = CobolValue.Text(value);
        if (national ? text.Any(char.IsSurrogate) : text.Any(c => c > 127))
        {
            throw new NotSupportedException(national ? "PIC N supports BMP characters only" : "Portable PIC X/A supports ASCII only; use PIC N");
        }

        return Fit(text, length, right);
    }

    /// <summary>文字種の検査と独立した固定長の切り詰め・空白埋めです。</summary>
    /// <param name="value">元の文字列。</param>
    /// <param name="length">受信項目の長さ。</param>
    /// <param name="right">右寄せかどうか。</param>
    /// <returns>指定長の文字列。</returns>
    public static string Fit(string value, int length, bool right) => value.Length >= length ? right ? value[^length..] : value[..length] : right ? value.PadLeft(length) : value.PadRight(length);

    /// <summary>1 始まりの添字を範囲検査して配列位置へ変換します。</summary>
    /// <param name="value">COBOL 添字。</param>
    /// <param name="length">表の要素数。</param>
    /// <returns>0 始まりの位置。</returns>
    public static int Index(object value, int length)
    {
        var index = CobolValue.Integer(value);
        return index >= 1 && index <= length ? index - 1 : throw new IndexOutOfRangeException("COBOL subscript is outside 1.." + length);
    }

    /// <summary>参照変更の開始位置と長さを検査します。</summary>
    /// <param name="value">元の項目。</param>
    /// <param name="start">1 始まりの開始位置。</param>
    /// <param name="length">部分文字数。</param>
    /// <returns>部分文字列。</returns>
    public static string Slice(string value, int start, int length)
    {
        if (start < 1 || length < 1 || start - 1 > value.Length - length)
        {
            throw new IndexOutOfRangeException("Reference modification");
        }

        return value.Substring(start - 1, length);
    }

    /// <summary>未変更領域を保持して参照変更へ格納します。</summary>
    /// <param name="original">元の値。</param>
    /// <param name="start">1 始まりの開始位置。</param>
    /// <param name="length">部分文字数。</param>
    /// <param name="value">送信値。</param>
    /// <returns>更新後の全体値。</returns>
    public static string Replace(string original, int start, int length, object value)
    {
        _ = Slice(original, start, length);
        return original[..(start - 1)] + TextValue(value, length, national: true) + original[(start - 1 + length)..];
    }

    /// <summary>数値 PIC の符号と暗黙小数点を反映した DISPLAY を作ります。</summary>
    /// <param name="value">格納値。</param>
    /// <param name="digits">桁数。</param>
    /// <param name="scale">小数桁数。</param>
    /// <param name="signed">符号表示を行うかどうか。</param>
    /// <returns>ゼロ埋めした表記。</returns>
    public static string Display(Number value, int digits, int scale, bool signed) => (signed ? value.Numerator.Sign < 0 ? "-" : "+" : string.Empty) + BigInteger.Abs(value.Units(scale)).ToString().PadLeft(digits, '0');

    /// <summary>値の種類を保持して COBOL のクラス条件を判定します。</summary>
    /// <param name="value">評価値。</param>
    /// <param name="category">検査するクラス。</param>
    /// <returns>クラスが一致する場合は true。</returns>
    public static bool IsClass(object value, string category)
    {
        if (category == "numeric" && value is Number)
        {
            return true;
        }

        var text = CobolValue.Text(value);
        return text.Length > 0 && category switch
        {
            "numeric" => text.All(c => c is >= '0' and <= '9'),
            "alphabetic" => text.All(c => c == ' ' || c is >= 'A' and <= 'Z' || c is >= 'a' and <= 'z'),
            "upper" => text.All(c => c == ' ' || c is >= 'A' and <= 'Z'),
            "lower" => text.All(c => c == ' ' || c is >= 'a' and <= 'z'),
            _ => false,
        };
    }
}
