namespace CobolCS.Frontend.Tests;

/// <summary>IR比較用の正準化がデータ差分を消さないことを確認します。</summary>
[TestClass]
public sealed class CanonicalJsonTests
{
    /// <summary>オブジェクトキー順だけを無視し、Unicodeと制御文字を固定形式にします。</summary>
    [TestMethod]
    public void KeysAndEscapesAreCanonical()
    {
        Assert.AreEqual("{\"a\":\"\\u65e5\\ud83d\\ude00\\u007f\\n\",\"z\":null}", CanonicalJson.Serialize("{\"z\":null,\"a\":\"日😀\\u007f\\n\"}"));
        Assert.AreEqual(CanonicalJson.Hash("{\"a\":1,\"b\":2}"), CanonicalJson.Hash("{\"b\":2, \"a\":1}"));
        Assert.AreEqual("{\"\\ue000\":1,\"\\ud800\\udc00\":2}", CanonicalJson.Serialize("{\"𐀀\":2,\"\uE000\":1}"));
    }

    /// <summary>配列順、数値表記、nullと欠落、真偽と文字列の違いは残します。</summary>
    /// <param name="left">左のJSON。</param>
    /// <param name="right">異なる右のJSON。</param>
    [TestMethod]
    [DataRow("[1,2]", "[2,1]")]
    [DataRow("1", "1.0")]
    [DataRow("{}", "{\"a\":null}")]
    [DataRow("true", "\"true\"")]
    public void ValuesAreNotNormalized(string left, string right) => Assert.AreNotEqual(CanonicalJson.Hash(left), CanonicalJson.Hash(right));
}
