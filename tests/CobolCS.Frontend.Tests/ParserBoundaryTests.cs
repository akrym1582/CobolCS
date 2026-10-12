using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace CobolCS.Frontend.Tests;

/// <summary>既存remaining18スイートの実入力を固定し、C#解析器だけで完全IRを検査します。</summary>
public sealed class ParserBoundaryTests
{
    /// <summary>Gets 境界ケースのID。</summary>
    public static IEnumerable<object[]> Inputs => Cases().Select(c => new object[] { c["id"]!.GetValue<string>() });

    /// <summary>条件分岐、COPY、宣言復旧、引用符などの固定期待値を比較します。</summary>
    /// <param name="id">固定ケースID。</param>
    [Theory]
    [MemberData(nameof(Inputs))]
    public void CompleteBoundaryIrMatchesReference(string id)
    {
        var item = Cases().Single(c => c["id"]!.GetValue<string>() == id);
        var folder = Path.Combine(Baseline.Root, "tests/migration/parser-cases", id);
        foreach (var (file, hash) in item["files"]!.AsObject())
        {
            Assert.Equal(hash!.GetValue<string>(), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder, file)))));
        }

        var expected = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "expected.json")))!.AsObject();
        var actual = FrontendAnalyzer.Analyze(Path.Combine(folder, "sample.cbl"), FrontendConfiguration.Parse(File.ReadAllText(Path.Combine(folder, "config.json"))), allowMissingCopy: item["scaffold"]!.GetValue<bool>());
        Assert.Equal("0.2.2", expected["frontendVersion"]!.GetValue<string>());
        Assert.Equal(FrontendAnalyzer.Version, actual["frontendVersion"]!.GetValue<string>());
        expected["frontendVersion"] = FrontendAnalyzer.Version;
        var diff = JsonComparison.Compare(id, expected.ToJsonString(), actual.ToJsonString());
        Assert.True(!diff.Any(), string.Join("\n", diff.Take(10)));
    }

    private static IEnumerable<JsonObject> Cases() => JsonNode.Parse(File.ReadAllText(Path.Combine(Baseline.Root, "tests/migration/parser-cases/manifest.json")))!["cases"]!.AsArray().Select(c => c!.AsObject());
}
