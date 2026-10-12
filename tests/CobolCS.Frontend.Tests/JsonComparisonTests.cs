namespace CobolCS.Frontend.Tests;

/// <summary>比較器自身の検出能力を、正例と意図的な変異で確認します。</summary>
public sealed class JsonComparisonTests
{
    /// <summary>オブジェクトの順番と同一文字のエスケープだけは差異になりません。</summary>
    [Fact]
    public void PropertyOrderAndStringEscapingAreEquivalent()
    {
        Assert.Empty(JsonComparison.Compare("unicode", "{\"a\":\"日本😀\",\"b\":[]}", "{\"b\":[],\"a\":\"\\u65e5本😀\"}"));
    }

    /// <summary>互換性上重要な変異が指定位置の差分として検出されます。</summary>
    /// <param name="expected">参照値。</param>
    /// <param name="actual">変異値。</param>
    /// <param name="path">期待する位置。</param>
    [Theory]
    [InlineData("{\"id\":\"s000001\"}", "{\"id\":\"s000002\"}", "/id")]
    [InlineData("{\"source\":{\"start\":{\"byteOffset\":3}}}", "{\"source\":{\"start\":{\"byteOffset\":1}}}", "/source/start/byteOffset")]
    [InlineData("{\"diagnostics\":[{\"severity\":\"error\"}]}", "{\"diagnostics\":[{\"severity\":\"review\"}]}", "/diagnostics/0/severity")]
    [InlineData("{\"configHash\":\"abc\"}", "{\"configHash\":\"def\"}", "/configHash")]
    [InlineData("{\"value\":\"1.00\"}", "{\"value\":\"1.0\"}", "/value")]
    [InlineData("{\"value\":1}", "{\"value\":1.0}", "/value")]
    [InlineData("{\"value\":1}", "{\"value\":\"1\"}", "/value")]
    [InlineData("{\"value\":null}", "{}", "/value")]
    [InlineData("{\"value\":[]}", "{}", "/value")]
    [InlineData("[1]", "[1,2]", "/1")]
    [InlineData("{\"a/b~c\":0}", "{\"a/b~c\":1}", "/a~1b~0c")]
    [InlineData("{\"frontendVersion\":\"0.2.2\"}", "{\"frontendVersion\":\"0.3.0\"}", "/frontendVersion")]
    public void MutationIsDetected(string expected, string actual, string path)
    {
        var result = JsonComparison.Compare("mutation", expected, actual);
        Assert.Single(result);
        Assert.Equal("mutation", result[0].InputId);
        Assert.Equal(path, result[0].Path);
        Assert.NotEqual(result[0].Expected, result[0].Actual);
    }

    /// <summary>配列の並び替えを検出します。</summary>
    [Fact]
    public void ArrayOrderIsSignificant()
    {
        Assert.Equal(2, JsonComparison.Compare("order", "[1,2]", "[2,1]").Count());
    }

    /// <summary>重複キーによる情報欠落を受け入れません。</summary>
    [Fact]
    public void DuplicateKeysAreRejected()
    {
        Assert.ThrowsAny<ArgumentException>(() => JsonComparison.Compare("duplicate", "{\"x\":1,\"x\":2}", "{\"x\":2}"));
    }
}
