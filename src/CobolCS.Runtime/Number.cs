using System.Globalization;
using System.Numerics;

namespace CobolCS.Runtime;
/// <summary>
/// COBOL の金額などを、二進浮動小数点の誤差を混ぜずに計算するための数値です。
/// たとえば 1 / 3 は分数のまま保持し、受け取り側の PIC に格納するときだけ桁を揃えます。
/// </summary>
public readonly record struct Number : IComparable<Number>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Number"/> struct.
    /// 分数としての計算結果を受け取り、符号と約分を揃えて、後の計算でも同じ規則を使えるようにします。
    /// </summary>
    /// <param name="numerator">分子。負の値も指定でき、金額の増減などを表します。</param>
    /// <param name="denominator">分母。ゼロ以外を指定してください。負の符号は分子へ移されます。</param>
    /// <exception cref="DivideByZeroException">分母にゼロを指定した場合。</exception>
    public Number(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.IsZero)
        {
            throw new DivideByZeroException();
        }

        if (denominator.Sign < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        if (denominator.IsOne || numerator.IsZero)
        {
            Numerator = numerator;
            Denominator = BigInteger.One;
            return;
        }

        var gcd = BigInteger.GreatestCommonDivisor(numerator, denominator);
        Numerator = gcd.IsOne ? numerator : numerator / gcd;
        Denominator = gcd.IsOne ? denominator : denominator / gcd;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Number"/> struct.
    /// 回数や添字などの整数を、小数と同じ精密な計算に参加させるための入口です。
    /// </summary>
    /// <param name="value">小数部分を持たない値。負数とゼロもそのまま表せます。</param>
    public Number(long value)
    {
        Numerator = value;
        Denominator = BigInteger.One;
    }

    /// <summary>
    /// Gets the numerator of the exact fraction.
    /// 分数の上側の整数で、値の符号もここに保持します。通常の整数型の桁数には制限されません。
    /// </summary>
    /// <value>約分後の分子。ゼロを表す場合は 0 です。</value>
    public BigInteger Numerator { get; }

    /// <summary>
    /// Gets the denominator of the exact fraction.
    /// 分数の下側の正の整数で、小数や割り算の途中結果を丸めずに保存するために使います。
    /// </summary>
    /// <value>約分後の分母。整数を表す場合は 1 です。</value>
    public BigInteger Denominator { get; }

    /// <summary>金額の加算などで、小数桁の異なる二つの値を途中で丸めずに足し合わせます。</summary>
    /// <param name="a">加算する一つ目の値。</param>
    /// <param name="b">加算する二つ目の値。</param>
    /// <returns>両方の値の正確な和。受け取り側の PIC に合わせた丸めは含みません。</returns>
    public static Number operator +(Number a, Number b) => new((a.Numerator * b.Denominator) + (b.Numerator * a.Denominator), a.Denominator * b.Denominator);

    /// <summary>残高からの減算などで、元の小数の精度を保ったまま差を求めます。</summary>
    /// <param name="a">差し引かれる側の値。</param>
    /// <param name="b">差し引く値。</param>
    /// <returns>負数も含めて表せる正確な差。</returns>
    public static Number operator -(Number a, Number b) => new((a.Numerator * b.Denominator) - (b.Numerator * a.Denominator), a.Denominator * b.Denominator);

    /// <summary>単価と数量の計算などで、受け取り項目へ格納する前の正確な積を求めます。</summary>
    /// <param name="a">掛ける一つ目の値。</param>
    /// <param name="b">掛ける二つ目の値。</param>
    /// <returns>途中の小数を切り捨てていない積。</returns>
    public static Number operator *(Number a, Number b) => new(a.Numerator * b.Numerator, a.Denominator * b.Denominator);

    /// <summary>割り算の商を分数のまま保持し、後から PIC の桁数に合わせて確定できるようにします。</summary>
    /// <param name="a">割られる値。</param>
    /// <param name="b">割る値。ゼロ以外を指定してください。</param>
    /// <returns>無限小数になる場合も正確に保持した商。</returns>
    /// <exception cref="DivideByZeroException">割る値がゼロの場合。</exception>
    public static Number operator /(Number a, Number b) => new(a.Numerator * b.Denominator, a.Denominator * b.Numerator);

    /// <summary>
    /// 解析器が出力する十進文字列を、浮動小数点へ変換することなく数値として読み取ります。
    /// 「0.1」を正確な 1 / 10 として扱い、端末の表示言語による小数点の違いは使用しません。
    /// </summary>
    /// <param name="text">符号と小数点を含められる十進表記。前後の空白は許容します。</param>
    /// <returns>文字列が表す値と数学的に等しい分数。</returns>
    /// <exception cref="FormatException">十進表記以外の文字や複数の小数点がある場合。</exception>
    public static Number Parse(string text)
    {
        text = text.Trim();
        var sign = text.StartsWith('-') ? -1 : 1;
        text = text.TrimStart('+', '-');
        var parts = text.Split('.');
        if (parts.Length > 2 || text.Length == 0 || parts.Any(p => p.Any(c => c < '0' || c > '9')))
        {
            throw new FormatException("Invalid decimal: " + text);
        }

        var scale = parts.Length == 2 ? parts[1].Length : 0;
        return new Number(sign * BigInteger.Parse(string.Concat(parts), CultureInfo.InvariantCulture), BigInteger.Pow(10, scale));
    }

    /// <summary>
    /// PIC に格納するために、小数点を取り除いた最小単位の整数を求めます。
    /// 小数 2 桁の PIC なら 1.23 を 123 とし、余分な桁は指定された丸め方で処理します。
    /// </summary>
    /// <param name="scale">受け取り側が保持する小数桁数。0 は整数への格納を意味します。</param>
    /// <param name="rounded">true は四捨五入、false はゼロ方向への切り捨てです。負数のちょうど半分も絶対値が大きい側へ丸めます。</param>
    /// <returns>指定した小数桁数に対応する整数。PIC の総桁数の検査は格納処理が別途行います。</returns>
    public BigInteger Units(int scale, bool rounded = false)
    {
        var numerator = Numerator * BigInteger.Pow(10, scale);
        var q = BigInteger.DivRem(numerator, Denominator, out var remainder);
        if (rounded && BigInteger.Abs(remainder) * 2 >= Denominator)
        {
            q += numerator.Sign;
        }

        return q;
    }

    /// <summary>
    /// 実行結果の JSON など、人が比較しやすい小数点付きの十進文字列を作ります。
    /// DISPLAY のゼロ埋めとは異なり、不要な小数末尾のゼロを取り除いて「1.20」を「1.2」とします。
    /// </summary>
    /// <param name="scale">文字列へ残せる小数桁数。それを超える桁はゼロ方向へ切り捨てます。</param>
    /// <returns>符号と小数点を必要に応じて含む、表示言語に依存しない文字列。</returns>
    public string Decimal(int scale)
    {
        var units = Units(scale);
        var sign = units.Sign < 0 ? "-" : string.Empty;
        var text = BigInteger.Abs(units).ToString(CultureInfo.InvariantCulture).PadLeft(scale + 1, '0');
        if (scale == 0)
        {
            return sign + text;
        }

        text = text.Insert(text.Length - scale, ".").TrimEnd('0').TrimEnd('.');
        return sign + text;
    }

    /// <summary>
    /// 小数への丸めを挟まず、COBOL の大小比較に使う数値の順序を判定します。
    /// 小数桁が異なる項目同士でも、分数としての値が等しければ同じ値と判断します。
    /// </summary>
    /// <param name="other">比較先の数値。</param>
    /// <returns>この値が小さい場合は負数、等しい場合は 0、大きい場合は正数。</returns>
    public int CompareTo(Number other) => Denominator == other.Denominator
        ? Numerator.CompareTo(other.Numerator)
        : (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);

    /// <summary>
    /// 値を一切切り捨てずに十進文字列へ戻します。
    /// 1 / 3 のように小数が無限に続く値は、先に PIC の桁数で確定させる必要があるため拒否します。
    /// </summary>
    /// <returns>元の値を正確に表し、不要な末尾のゼロを含まない十進文字列。</returns>
    /// <exception cref="InvalidOperationException">有限の桁数の十進小数で表せない場合。</exception>
    public string ExactDecimal()
    {
        var denominator = Denominator;
        var twos = 0;
        var fives = 0;
        while (denominator % 2 == 0)
        {
            denominator /= 2;
            twos++;
        }

        while (denominator % 5 == 0)
        {
            denominator /= 5;
            fives++;
        }

        if (denominator != 1)
        {
            throw new InvalidOperationException("Non-terminating decimal requires a PIC assignment");
        }

        return Decimal(Math.Max(twos, fives));
    }

    /// <summary>COBOL の単項マイナスに使う、同じ大きさで符号だけが反対の数値を求めます。</summary>
    /// <returns>正数なら負数、負数なら正数。ゼロはゼロのままです。</returns>
    public Number Negate() => new(-Numerator, Denominator);

    /// <summary>ABS 関数などで使用する、符号を除いた数値の大きさを求めます。</summary>
    /// <returns>元の値と同じ大きさの、ゼロ以上の値。</returns>
    public Number Abs() => new(BigInteger.Abs(Numerator), Denominator);

    /// <summary>COBOL のべき乗式を、指数が整数の場合に丸めなしで計算します。</summary>
    /// <param name="exponent">繰り返し掛ける回数。負の指数は逆数のべき乗、0 は 1 を表します。</param>
    /// <returns>元の値の指定された整数乗。</returns>
    /// <exception cref="DivideByZeroException">ゼロの値に負の指数を指定した場合。</exception>
    public Number Pow(int exponent) => exponent >= 0 ? new(BigInteger.Pow(Numerator, exponent), BigInteger.Pow(Denominator, exponent)) : new(BigInteger.Pow(Denominator, -exponent), BigInteger.Pow(Numerator, -exponent));
}
