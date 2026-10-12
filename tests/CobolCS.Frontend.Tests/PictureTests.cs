using System.Text.Json.Nodes;

namespace CobolCS.Frontend.Tests;

/// <summary>上流 PIC の保存した期待値を Python に依存せず検証します。</summary>
public sealed class PictureTests
{
    /// <summary>Gets 上流実装で固定した PIC 入力。</summary>
    public static IEnumerable<object[]> Inputs => JsonNode.Parse(File.ReadAllText(Path.Combine(Baseline.Root, "tests/migration/pictures.json")))!.AsArray().Select(n => new object[] { n!.ToJsonString() });

    /// <summary>展開・カテゴリ・長さ・小数・符号を比較します。</summary>
    /// <param name="json">入力と固定期待値。</param>
    [Theory]
    [MemberData(nameof(Inputs))]
    public void PictureMatchesUpstream(string json)
    {
        var item = JsonNode.Parse(json)!;
        var result = PictureParser.Parse(item["input"]!.GetValue<string>());
        Assert.Equal(item["raw"]!.GetValue<string>(), result.Raw);
        Assert.Equal(item["expanded"]!.GetValue<string>(), result.Expanded);

        // Upstream's enum auto() values are 1..5; declarations use lower-case enum names.
        string[] categories = ["numeric", "alphanumeric", "alphabetic", "edited", "unknown"];
        Assert.Equal(categories[item["category"]!.GetValue<int>() - 1], result.Category);
        Assert.Equal(item["size"]!.GetValue<int>(), result.Size);
        Assert.Equal(item["decimals"]!.GetValue<int>(), result.Decimals);
        Assert.Equal(item["signed"]!.GetValue<bool>(), result.Signed);
    }
}
