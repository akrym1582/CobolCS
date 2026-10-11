using System.Globalization;
using System.Numerics;

namespace CobolCS.Runtime;

/// <summary>Exact rational arithmetic; PIC assignment is the only rounding boundary.</summary>
public readonly record struct Number : IComparable<Number>
{
    public BigInteger Numerator { get; }
    public BigInteger Denominator { get; }
    public Number(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.IsZero) throw new DivideByZeroException();
        if (denominator.Sign < 0) { numerator = -numerator; denominator = -denominator; }
        var gcd = BigInteger.GreatestCommonDivisor(numerator, denominator);
        Numerator = numerator / gcd; Denominator = denominator / gcd;
    }
    public Number(long value) : this(value, BigInteger.One) { }
    public static Number Parse(string text)
    {
        text = text.Trim();
        var sign = text.StartsWith('-') ? -1 : 1;
        text = text.TrimStart('+', '-');
        var parts = text.Split('.');
        if (parts.Length > 2 || text.Length == 0 || parts.Any(p => p.Any(c => c < '0' || c > '9')))
            throw new FormatException("Invalid decimal: " + text);
        var scale = parts.Length == 2 ? parts[1].Length : 0;
        return new Number(sign * BigInteger.Parse(string.Concat(parts), CultureInfo.InvariantCulture), BigInteger.Pow(10, scale));
    }
    public BigInteger Units(int scale, bool rounded = false)
    {
        var numerator = Numerator * BigInteger.Pow(10, scale);
        var q = BigInteger.DivRem(numerator, Denominator, out var remainder);
        if (rounded && BigInteger.Abs(remainder) * 2 >= Denominator) q += numerator.Sign;
        return q;
    }
    public string Decimal(int scale)
    {
        var units = Units(scale); var sign = units.Sign < 0 ? "-" : "";
        var text = BigInteger.Abs(units).ToString(CultureInfo.InvariantCulture).PadLeft(scale + 1, '0');
        if (scale == 0) return sign + text;
        text = text.Insert(text.Length - scale, ".").TrimEnd('0').TrimEnd('.');
        return sign + text;
    }
    public int CompareTo(Number other) => (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);
    public string ExactDecimal()
    {
        var denominator = Denominator; var twos = 0; var fives = 0;
        while (denominator % 2 == 0) { denominator /= 2; twos++; }
        while (denominator % 5 == 0) { denominator /= 5; fives++; }
        if (denominator != 1) throw new InvalidOperationException("Non-terminating decimal requires a PIC assignment");
        return Decimal(Math.Max(twos, fives));
    }
    public static Number operator +(Number a, Number b) => new(a.Numerator * b.Denominator + b.Numerator * a.Denominator, a.Denominator * b.Denominator);
    public static Number operator -(Number a, Number b) => new(a.Numerator * b.Denominator - b.Numerator * a.Denominator, a.Denominator * b.Denominator);
    public static Number operator *(Number a, Number b) => new(a.Numerator * b.Numerator, a.Denominator * b.Denominator);
    public static Number operator /(Number a, Number b) => new(a.Numerator * b.Denominator, a.Denominator * b.Numerator);
    public Number Negate() => new(-Numerator, Denominator);
    public Number Abs() => new(BigInteger.Abs(Numerator), Denominator);
    public Number Pow(int exponent) => exponent >= 0 ? new(BigInteger.Pow(Numerator, exponent), BigInteger.Pow(Denominator, exponent)) : new(BigInteger.Pow(Denominator, -exponent), BigInteger.Pow(Numerator, -exponent));
}
