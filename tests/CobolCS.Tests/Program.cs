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
