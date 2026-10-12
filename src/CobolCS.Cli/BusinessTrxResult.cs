using System.Xml.Linq;

namespace CobolCS.Cli;

/// <summary>xUnit 2.9.3 / VSTest 3.1.5 の安全な文字列行と TRX の対応をこの境界へ閉じ込めます。</summary>
internal static class BusinessTrxResult
{
    /// <summary>部分一致を使わず、行名・定義・実行 ID・クラス・共通メソッドを照合します。</summary>
    /// <param name="results">今回の結果。</param>
    /// <param name="definitions">今回の定義。</param>
    /// <param name="caseKey">元 ID の完全 SHA-256。表示名のエスケープや省略に依存しません。</param>
    internal static void Validate(XElement[] results, XElement[] definitions, string caseKey)
    {
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        var name = "BusinessCase_Run(keyPrefix: \"" + caseKey[..32] + "\", keySuffix: \"" + caseKey[32..] + "\")";
        var matches = results.Where(r => (string?)r.Attribute("testName") == name).ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException("Missing or ambiguous business TRX row " + caseKey + "; names=" + string.Join(", ", results.Select(r => (string?)r.Attribute("testName"))));
        }

        var result = matches[0];
        var testId = (string?)result.Attribute("testId");
        var candidates = definitions.Where(d => (string?)d.Attribute("id") == testId).ToArray();
        var definition = candidates.Length == 1 ? candidates[0] : null;
        var method = definition?.Element(ns + "TestMethod");
        if (string.IsNullOrEmpty(testId) || (string?)result.Attribute("outcome") != "Passed" || (string?)definition?.Attribute("name") != name || (string?)method?.Attribute("name") != "BusinessCase_Run" || (string?)method?.Attribute("className") != "CobolCS.Generated.Tests.BusinessCasesTests" || string.IsNullOrEmpty((string?)result.Attribute("executionId")) || (string?)definition?.Element(ns + "Execution")?.Attribute("id") != (string?)result.Attribute("executionId"))
        {
            throw new InvalidOperationException("Unproven business TRX definition " + caseKey);
        }
    }
}
