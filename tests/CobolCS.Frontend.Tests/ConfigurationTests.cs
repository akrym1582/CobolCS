using System.Text.Json.Nodes;

namespace CobolCS.Frontend.Tests;

/// <summary>固定した Python の設定・ハッシュ・拒否理由を Python なしで照合します。</summary>
public sealed class ConfigurationTests
{
    /// <summary>Gets 明示設定を持つ固定入力。</summary>
    public static IEnumerable<object[]> Inputs => Baseline.Cases.Where(c => c["config"] is not null).Select(c => new object[] { c["id"]!.GetValue<string>() });

    /// <summary>保存した期待値と入力を検証します。</summary>
    [Fact]
    public void FrozenInputsAreIntact() => Baseline.VerifyInputs();

    /// <summary>C# 設定検証が固定期待値と一致します。</summary>
    /// <param name="id">設定入力 ID。</param>
    [Theory]
    [MemberData(nameof(Inputs))]
    public void ConfigurationMatchesFrozenReference(string id)
    {
        var item = Baseline.Cases.Single(c => c["id"]!.GetValue<string>() == id);
        var json = File.ReadAllText(Path.Combine(Baseline.Root, item["config"]!.GetValue<string>()));
        if (item["exitCode"]!.GetValue<int>() != 0)
        {
            var exception = Assert.ThrowsAny<ArgumentException>(() => FrontendConfiguration.Parse(json));
            Assert.Equal(item["stderr"]!.GetValue<string>().TrimEnd('\n'), exception.Message);
            return;
        }

        var result = FrontendConfiguration.Parse(json);
        var expected = JsonNode.Parse(Baseline.Expected(item))!;
        Assert.Empty(JsonComparison.Compare(id, expected["config"]!.ToJsonString(), result.ToJson().ToJsonString()));
        Assert.True(result.Hash == expected["configHash"]!.GetValue<string>(), id);
    }

    /// <summary>IR コピーの変更が検証済み設定を変更しません。</summary>
    [Fact]
    public void ConfigurationCannotBeMutatedThroughJson()
    {
        var config = FrontendConfiguration.Parse();
        var hash = config.Hash;
        config.ToJson()["sourceFormat"] = "invalid";
        Assert.Equal(hash, config.Hash);
    }
}
