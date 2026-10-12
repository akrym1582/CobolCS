using CobolCS.Frontend;

namespace CobolCS.Frontend.Tests;

/// <summary>探索の追加設定と従来の正準設定 hash の互換を検査します。</summary>
public sealed class BranchGenerationConfigurationTests
{
    /// <summary>任意設定だけを追加し、無制限予算と未知キーを拒否します。</summary>
    [Fact]
    public void OptionalConfigurationKeepsLegacyDefaultsAndRejectsInvalidBudgets()
    {
        var original = FrontendConfiguration.Parse();
        var explicitDefault = FrontendConfiguration.Parse("{}");
        Assert.Equal(original.Hash, explicitDefault.Hash);
        var configured = FrontendConfiguration.Parse("{\"testGeneration\":{\"maxAttempts\":200,\"seed\":1,\"candidates\":[]}}");
        Assert.NotEqual(original.Hash, configured.Hash);
        foreach (var settings in new[] { "{\"maxAttempts\":201}", "{\"maxAttempts\":0}", "{\"seed\":-1}", "{\"seed\":1.5}", "{\"extra\":1}", "{\"maxTraceEvents\":0}", "{\"timeoutMs\":0}", "{\"candidates\":[{\"programId\":\"P\",\"input\":{},\"extra\":true}]}" })
        {
            Assert.ThrowsAny<ArgumentException>(() => FrontendConfiguration.Parse("{\"testGeneration\":" + settings + "}"));
        }
    }
}
