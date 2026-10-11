namespace CobolCS.Frontend.Tests;

/// <summary>比較器自身の検出能力を、正例と意図的な変異で確認します。</summary>
[TestClass]
public sealed class JsonComparisonTests
{
    /// <summary>オブジェクトの順番と同一文字のエスケープだけは差異になりません。</summary>
    [TestMethod]
    public void PropertyOrderAndStringEscapingAreEquivalent()
    {
        Assert.IsEmpty(JsonComparison.Compare("unicode", "{\"a\":\"日本😀\",\"b\":[]}", "{\"b\":[],\"a\":\"\\u65e5本😀\"}"));
    }

    /// <summary>互換性上重要な変異が指定位置の差分として検出されます。</summary>
    /// <param name="expected">参照値。</param>
    /// <param name="actual">変異値。</param>
    /// <param name="path">期待する位置。</param>
    [TestMethod]
    [DataRow("{\"id\":\"s000001\"}", "{\"id\":\"s000002\"}", "/id")]
    [DataRow("{\"source\":{\"start\":{\"byteOffset\":3}}}", "{\"source\":{\"start\":{\"byteOffset\":1}}}", "/source/start/byteOffset")]
    [DataRow("{\"diagnostics\":[{\"severity\":\"error\"}]}", "{\"diagnostics\":[{\"severity\":\"review\"}]}", "/diagnostics/0/severity")]
    [DataRow("{\"configHash\":\"abc\"}", "{\"configHash\":\"def\"}", "/configHash")]
    [DataRow("{\"value\":\"1.00\"}", "{\"value\":\"1.0\"}", "/value")]
    [DataRow("{\"value\":1}", "{\"value\":1.0}", "/value")]
    [DataRow("{\"value\":1}", "{\"value\":\"1\"}", "/value")]
    [DataRow("{\"value\":null}", "{}", "/value")]
    [DataRow("{\"value\":[]}", "{}", "/value")]
    [DataRow("[1]", "[1,2]", "/1")]
    [DataRow("{\"a/b~c\":0}", "{\"a/b~c\":1}", "/a~1b~0c")]
    [DataRow("{\"frontendVersion\":\"0.2.2\"}", "{\"frontendVersion\":\"0.3.0\"}", "/frontendVersion")]
    public void MutationIsDetected(string expected, string actual, string path)
    {
        var result = JsonComparison.Compare("mutation", expected, actual);
        Assert.HasCount(1, result);
        Assert.AreEqual("mutation", result[0].InputId);
        Assert.AreEqual(path, result[0].Path);
        Assert.AreNotEqual(result[0].Expected, result[0].Actual);
    }

    /// <summary>配列の並び替えを検出します。</summary>
    [TestMethod]
    public void ArrayOrderIsSignificant()
    {
        Assert.HasCount(2, JsonComparison.Compare("order", "[1,2]", "[2,1]"));
    }

    /// <summary>重複キーによる情報欠落を受け入れません。</summary>
    [TestMethod]
    public void DuplicateKeysAreRejected()
    {
        Assert.Throws<ArgumentException>(() => JsonComparison.Compare("duplicate", "{\"x\":1,\"x\":2}", "{\"x\":2}"));
    }
}
