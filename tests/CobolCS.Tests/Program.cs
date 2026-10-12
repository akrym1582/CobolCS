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

// 独立比較は精度、末尾空白、欠落、null、順序を保つ。
Equal(ReferenceCase.Compare(J.Parse("{\"N\":\"99999999999999999999999999999999\"}"), J.Parse("{\"N\":\"99999999999999999999999999999998\"}")).B("matched").ToString(), "False");
Equal(ReferenceCase.Compare(J.Parse("{\"T\":\"A \"}"), J.Parse("{\"T\":\"A\"}")).B("matched").ToString(), "False");
Equal(ReferenceCase.Compare(J.Parse("{\"A\":null}"), J.Parse("{}")).B("matched").ToString(), "False");
Equal(ReferenceCase.Compare(J.Parse("[\"1\",\"2\"]"), J.Parse("[\"2\",\"1\"]")).B("matched").ToString(), "False");
Equal(ReferenceCase.Compare(J.Parse("[\"1\"]"), J.Parse("[\"1\",\"2\"]")).B("matched").ToString(), "False");
Equal(ReferenceCase.Compare(J.Parse("{\"x\":\"1\",\"y\":\"2\"}"), J.Parse("{\"y\":\"2\",\"x\":\"1\"}")).B("matched").ToString(), "True");
Equal(ReferenceCase.ReviewHash(J.Parse("{\"a\":\"1\",\"review\":{\"approved\":false}}")), ReferenceCase.ReviewHash(J.Parse("{\"review\":{\"approved\":true},\"a\":\"1\"}")));
try
{
    ReferenceCase.Parse("{\"caseId\":\"one\",\"caseId\":\"two\"}");
    throw new Exception("Duplicate JSON keys were accepted");
}
catch (InvalidDataException)
{
    assertions++;
}

var manyExpected = new System.Text.Json.Nodes.JsonArray(Enumerable.Range(0, 60).Select(_ => (System.Text.Json.Nodes.JsonNode?)System.Text.Json.Nodes.JsonValue.Create("expected")).ToArray());
var manyActual = new System.Text.Json.Nodes.JsonArray(Enumerable.Range(0, 60).Select(_ => (System.Text.Json.Nodes.JsonNode?)System.Text.Json.Nodes.JsonValue.Create("actual")).ToArray());
var bounded = ReferenceCase.Compare(manyExpected, manyActual);
Equal(bounded.I("differenceCount").ToString(), "60");
Equal(bounded.I("omittedCount").ToString(), "10");
Console.WriteLine($"Runtime and reference comparison: {assertions} assertions passed");
