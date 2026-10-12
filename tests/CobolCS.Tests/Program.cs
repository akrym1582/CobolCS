using System.Numerics;
using CobolCS.Runtime;

var assertions = 0;
void Equal(string actual, string expected)
{
    assertions++;
    if (actual != expected)
    {
        throw new Exception($"Expected {expected}, actual {actual}");
    }
}

Equal((Number.Parse("0.1") + Number.Parse("0.2")).Decimal(2), "0.3");
Equal((Number.Parse("99999999999999999999999999999999") + new Number(1)).Decimal(0), "100000000000000000000000000000000");
Equal((new Number(1) / new Number(3) * new Number(3)).Decimal(3), "1");
Equal(Number.Parse("-1.235").Units(2, rounded: true).ToString(), "-124");
Equal(Number.Parse("-1.239").Units(2).ToString(), "-123");
Equal(Number.Parse("1.235").Units(2, rounded: true).ToString(), "124");
Equal(Number.Parse("2").Pow(-3).Decimal(3), "0.125");
Equal(Storage.Fit("AB", 4, false), "AB  ");
Equal(Storage.Fit("ABCDE", 3, true), "CDE");
Equal(Number.Parse("0.000000000000000000000000000001").ExactDecimal(), "0.000000000000000000000000000001");
Equal(Storage.Fit("AB", 4, true), "  AB");
var huge = BigInteger.Pow(10, 80);
Equal(new Number(huge, -BigInteger.One).Numerator.ToString(), (-huge).ToString());
Equal(new Number(0, -huge).Denominator.ToString(), "1");
Equal(new Number(long.MinValue).Numerator.ToString(), long.MinValue.ToString());
Equal(new Number(-6, -8).Numerator.ToString(), "3");
Equal(new Number(-6, -8).Denominator.ToString(), "4");
try
{
    _ = new Number(BigInteger.Zero, BigInteger.Zero);
    throw new Exception("Zero denominator was accepted");
}
catch (DivideByZeroException)
{
    assertions++;
}

for (var denominator = 1; denominator <= 9; denominator++)
{
    for (var left = -9; left <= 9; left++)
    {
        for (var right = -9; right <= 9; right++)
        {
            var a = new Number(huge + left, denominator);
            var b = new Number(huge + right, denominator);
            void FractionEqual(Number actual, BigInteger numerator, BigInteger divisor)
            {
                Equal((actual.Numerator * divisor).ToString(), (numerator * actual.Denominator).ToString());
                Equal(BigInteger.GreatestCommonDivisor(actual.Numerator, actual.Denominator).ToString(), "1");
            }

            FractionEqual(a + b, (2 * huge) + left + right, denominator);
            FractionEqual(a - b, left - right, denominator);
            Equal(Math.Sign(a.CompareTo(b)).ToString(), Math.Sign(left.CompareTo(right)).ToString());
        }
    }
}

try
{
    _ = new Number(1) / new Number(0);
    throw new Exception("Division by zero was accepted");
}
catch (DivideByZeroException)
{
    assertions++;
}

Console.WriteLine($"Runtime: {assertions} assertions passed");
