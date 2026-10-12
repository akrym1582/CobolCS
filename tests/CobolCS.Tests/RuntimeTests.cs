using System.Numerics;
using CobolCS.Runtime;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace CobolCS.Tests;

/// <summary>COBOL 数値の厳密演算と文字列の格納境界を検証します。</summary>
public sealed class RuntimeTests
{
    /// <summary>浮動小数点の誤差を含まずに小数を加算します。</summary>
    [Fact]
    public void DecimalAdditionIsExact() => Assert.Equal("0.3", (Number.Parse("0.1") + Number.Parse("0.2")).Decimal(2));

    /// <summary>機械整数の範囲を超えた桁上がりも保持します。</summary>
    [Fact]
    public void AdditionPreservesLargeIntegers() => Assert.Equal("100000000000000000000000000000000", (Number.Parse("99999999999999999999999999999999") + new Number(1)).Decimal(0));

    /// <summary>除算の中間値を丸めずに乗算へ渡します。</summary>
    [Fact]
    public void DivisionRetainsTheExactRationalValue() => Assert.Equal("1", (new Number(1) / new Number(3) * new Number(3)).Decimal(3));

    /// <summary>PIC 格納時の丸めと負数のゼロ方向への切り捨てを検証します。</summary>
    /// <param name="value">入力する十進値。</param>
    /// <param name="rounded">格納時に四捨五入するか。</param>
    /// <param name="expected">小数点以下二桁を整数化した期待値。</param>
    [Theory]
    [InlineData("-1.235", true, "-124")]
    [InlineData("-1.239", false, "-123")]
    [InlineData("1.235", true, "124")]
    public void UnitsRespectStorageRounding(string value, bool rounded, string expected) => Assert.Equal(expected, Number.Parse(value).Units(2, rounded).ToString());

    /// <summary>負の累乗で逆数を正確に求めます。</summary>
    [Fact]
    public void NegativePowerProducesTheReciprocal() => Assert.Equal("0.125", Number.Parse("2").Pow(-3).Decimal(3));

    /// <summary>文字列の左右寄せと切り詰めの向きを検証します。</summary>
    /// <param name="value">格納する文字列。</param>
    /// <param name="length">格納先の幅。</param>
    /// <param name="right">右寄せするか。</param>
    /// <param name="expected">空白を含む格納結果。</param>
    [Theory]
    [InlineData("AB", 4, false, "AB  ")]
    [InlineData("ABCDE", 3, true, "CDE")]
    [InlineData("AB", 4, true, "  AB")]
    public void StorageFitsTheRequestedWidth(string value, int length, bool right, string expected) => Assert.Equal(expected, Storage.Fit(value, length, right));

    /// <summary>極小値の有効桁を表示時にも保持します。</summary>
    [Fact]
    public void ExactDecimalPreservesSmallValues() => Assert.Equal("0.000000000000000000000000000001", Number.Parse("0.000000000000000000000000000001").ExactDecimal());

    /// <summary>ゼロ除算を正常な値へ変換しません。</summary>
    [Fact]
    public void DivisionByZeroFails() => Assert.Throws<DivideByZeroException>(() => new Number(1) / new Number(0));

    /// <summary>巨大な負の分母を正規化しても分子の符号を保持します。</summary>
    [Fact]
    public void LargeNegativeDenominatorMovesTheSignToTheNumerator()
    {
        var huge = BigInteger.Pow(10, 80);
        Assert.Equal(-huge, new Number(huge, -BigInteger.One).Numerator);
    }

    /// <summary>ゼロ値は巨大な負の分母でも標準形の分母一へ正規化します。</summary>
    [Fact]
    public void ZeroNumeratorNormalizesTheDenominator() => Assert.Equal(BigInteger.One, new Number(0, -BigInteger.Pow(10, 80)).Denominator);

    /// <summary>最小の符号付き整数を正確に保持します。</summary>
    [Fact]
    public void MinimumIntegerRetainsItsSign() => Assert.Equal(new BigInteger(long.MinValue), new Number(long.MinValue).Numerator);

    /// <summary>分子と分母が両方負の値を既約な正の分数へ正規化します。</summary>
    [Fact]
    public void DoubleNegativeFractionIsReduced()
    {
        var value = new Number(-6, -8);
        Assert.Equal(new BigInteger(3), value.Numerator);
        Assert.Equal(new BigInteger(4), value.Denominator);
    }

    /// <summary>分子もゼロである場合を含めてゼロの分母を拒否します。</summary>
    [Fact]
    public void ZeroDenominatorFails() => Assert.Throws<DivideByZeroException>(() => new Number(BigInteger.Zero, BigInteger.Zero));

    /// <summary>元の全3249組合せで巨大分数の加減算、既約性、比較の符号を検証します。</summary>
    /// <param name="denominator">一から九までの分母。</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void LargeFractionsPreserveArithmeticAndCanonicalForm(int denominator)
    {
        var huge = BigInteger.Pow(10, 80);
        for (var left = -9; left <= 9; left++)
        {
            for (var right = -9; right <= 9; right++)
            {
                var a = new Number(huge + left, denominator);
                var b = new Number(huge + right, denominator);
                void FractionEqual(Number actual, BigInteger numerator, BigInteger divisor)
                {
                    Assert.Equal(numerator * actual.Denominator, actual.Numerator * divisor);
                    Assert.Equal(BigInteger.One, BigInteger.GreatestCommonDivisor(actual.Numerator, actual.Denominator));
                }

                FractionEqual(a + b, (2 * huge) + left + right, denominator);
                FractionEqual(a - b, left - right, denominator);
                Assert.Equal(Math.Sign(left.CompareTo(right)), Math.Sign(a.CompareTo(b)));
            }
        }
    }
}
