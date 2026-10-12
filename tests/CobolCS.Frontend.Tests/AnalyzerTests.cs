using System.Text.Json.Nodes;

namespace CobolCS.Frontend.Tests;

/// <summary>固定Python参照IRに対してC#解析器の全出力を検証します。</summary>
public sealed class AnalyzerTests
{
    /// <summary>Gets 固定入力のID。</summary>
    public static IEnumerable<object[]> Inputs => Baseline.CaseIds();

    /// <summary>完全IRと入力拒否理由を比較し、実装バージョンだけを明示的な差異とします。</summary>
    /// <param name="id">固定入力ID。</param>
    [Theory]
    [MemberData(nameof(Inputs))]
    public void CompleteIrMatchesReference(string id)
    {
        var item = Baseline.Cases.Single(c => c["id"]!.GetValue<string>() == id);
        var input = Path.Combine(Baseline.Root, item["input"]!.GetValue<string>());
        var config = item["config"] is null ? null : File.ReadAllText(Path.Combine(Baseline.Root, item["config"]!.GetValue<string>()));
        var copies = item["copybookPaths"]!.AsArray().Select(p => Path.Combine(Baseline.Root, p!.GetValue<string>()));
        if (item["exitCode"]!.GetValue<int>() != 0)
        {
            var error = Assert.ThrowsAny<ArgumentException>(() => FrontendAnalyzer.Analyze(input, FrontendConfiguration.Parse(config), copies));
            Assert.Equal(item["stderr"]!.GetValue<string>().TrimEnd('\n'), error.Message);
            return;
        }

        var actual = FrontendAnalyzer.Analyze(input, FrontendConfiguration.Parse(config), copies);
        var expected = JsonNode.Parse(Baseline.Expected(item))!.AsObject();
        Assert.Equal("0.2.2", expected["frontendVersion"]!.GetValue<string>());
        Assert.Equal(FrontendAnalyzer.Version, actual["frontendVersion"]!.GetValue<string>());
        expected["frontendVersion"] = FrontendAnalyzer.Version;
        var differences = JsonComparison.Compare(id, expected.ToJsonString(), actual.ToJsonString());
        Assert.True(!differences.Any(), string.Join("\n", differences.Take(12)));
        Assert.Empty(JsonComparison.Compare(id, actual.ToJsonString(), FrontendAnalyzer.Analyze(input, FrontendConfiguration.Parse(config), copies).ToJsonString()));
    }
}
