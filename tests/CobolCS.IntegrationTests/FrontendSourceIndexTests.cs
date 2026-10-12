using System.Text.Json.Nodes;
using CobolCS.Frontend.Parsing;
using Xunit;
using static CobolCS.Frontend.Parsing.Values;

namespace CobolCS.IntegrationTests;

/// <summary>診断索引の包含、同幅先行順、原文file境界と欠落位置を独立期待値で検証します。</summary>
public sealed class FrontendSourceIndexTests
{
    /// <summary>開始位置の並べ替えで同幅の選択順を変えず、最短の入れ子とfile境界を保ちます。</summary>
    [Fact]
    public void TestContainmentAndOriginalTieOrder()
    {
        var index = CreateIndex();
        Assert.Equal("inner", Text(index.Find(Location("a.cbl", 14, 15))[S("id")]));
        Assert.Equal("first", Text(index.Find(Location("a.cbl", 13, 17))[S("id")]));
        Assert.Equal("other-file", Text(index.Find(Location("b.cbl", 13, 17))[S("id")]));
        Assert.Equal("outer", Text(index.Find(Location("a.cbl", 0, 100))[S("id")]));
        Assert.Same(Nil, index.Find(Location("a.cbl", 101, 102)));
        Assert.Same(Nil, index.Find(Location("missing.cbl", 14, 15)));
        Assert.Equal("inner", Text(index.Find(Location("a.cbl", 14, 15))[S("id")]));
    }

    /// <summary>欠落位置を従来の-1とし、開始位置なしを一致に変えません。</summary>
    [Fact]
    public void TestPartialAndMissingLocations()
    {
        var index = CreateIndex();
        Assert.Same(Nil, index.Find(Location("a.cbl", null, 15)));
        Assert.Same(Nil, index.Find(Location("a.cbl", null, null)));
        Assert.Equal("first", Text(index.Find(Location("a.cbl", 13, null))[S("id")]));
        Assert.Same(Nil, index.Find(FromJson(new JsonObject())));
    }

    private static Parser.StatementSourceIndex CreateIndex() => new(ListOf(
        Statement("outer", "a.cbl", 0, 100),
        Statement("first", "a.cbl", 12, 22),
        Statement("second", "a.cbl", 10, 20),
        Statement("inner", "a.cbl", 14, 16),
        Statement("other-file", "b.cbl", 12, 22)));

    private static V Statement(string id, string file, int start, int end) => Put(Put(Dict(), S("id"), S(id)), S("source"), Location(file, start, end));

    private static V Location(string file, int? start, int? end)
    {
        var result = new JsonObject { ["file"] = file };
        if (start is not null)
        {
            result["start"] = new JsonObject { ["byteOffset"] = start.Value };
        }

        if (end is not null)
        {
            result["end"] = new JsonObject { ["byteOffset"] = end.Value };
        }

        return FromJson(result);
    }
}
