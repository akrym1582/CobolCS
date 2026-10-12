namespace CobolCS.Frontend.Tests;

/// <summary>IR比較用の正準化がデータ差分を消さないことを確認します。</summary>
public sealed class CanonicalJsonTests
{
    /// <summary>オブジェクトキー順だけを無視し、Unicodeと制御文字を固定形式にします。</summary>
    [Fact]
    public void KeysAndEscapesAreCanonical()
    {
        Assert.Equal("{\"a\":\"\\u65e5\\ud83d\\ude00\\u007f\\n\",\"z\":null}", CanonicalJson.Serialize("{\"z\":null,\"a\":\"日😀\\u007f\\n\"}"));
        Assert.Equal(CanonicalJson.Hash("{\"a\":1,\"b\":2}"), CanonicalJson.Hash("{\"b\":2, \"a\":1}"));
        Assert.Equal("{\"\\ue000\":1,\"\\ud800\\udc00\":2}", CanonicalJson.Serialize("{\"𐀀\":2,\"\uE000\":1}"));
    }

    /// <summary>配列順、数値表記、nullと欠落、真偽と文字列の違いは残します。</summary>
    /// <param name="left">左のJSON。</param>
    /// <param name="right">異なる右のJSON。</param>
    [Theory]
    [InlineData("[1,2]", "[2,1]")]
    [InlineData("1", "1.0")]
    [InlineData("{}", "{\"a\":null}")]
    [InlineData("true", "\"true\"")]
    public void ValuesAreNotNormalized(string left, string right) => Assert.NotEqual(CanonicalJson.Hash(left), CanonicalJson.Hash(right));
}
