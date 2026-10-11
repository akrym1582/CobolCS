using System.Text;
using System.Text.Json.Nodes;

namespace CobolCS.Frontend.Tests;

/// <summary>Unicode 列、UTF-8 バイト位置、改行と原文保持を検査します。</summary>
[TestClass]
public sealed class SourceFileTests
{
    /// <summary>基準 IR の原文と宣言範囲をバイト単位で再現します。</summary>
    /// <param name="id">改行形式を含む入力 ID。</param>
    [TestMethod]
    [DataRow("unicode-lf")]
    [DataRow("unicode-crlf")]
    public void SourceAndSpansMatchFrozenReference(string id)
    {
        var item = Baseline.Cases.Single(c => c["id"]!.GetValue<string>() == id);
        var ir = JsonNode.Parse(Baseline.Expected(item))!;
        var expected = ir["sources"]![0]!;
        var file = new SourceFile(expected["path"]!.GetValue<string>(), File.ReadAllBytes(System.IO.Path.Combine(Baseline.Root, item["input"]!.GetValue<string>())));
        Assert.IsEmpty(JsonComparison.Compare(id, expected.ToJsonString(), file.ToJson().ToJsonString()));
        foreach (var span in Spans(ir).Where(s => s["file"]!.GetValue<string>() == file.Path))
        {
            var bytes = Encoding.UTF8.GetBytes(file.Text);
            var start = Encoding.UTF8.GetCharCount(bytes.AsSpan(0, span["start"]!["byteOffset"]!.GetValue<int>()));
            var end = Encoding.UTF8.GetCharCount(bytes.AsSpan(0, span["end"]!["byteOffset"]!.GetValue<int>()));
            Assert.IsEmpty(JsonComparison.Compare(id, span.ToJsonString(), file.Span(start, end).ToJsonString()));
        }
    }

    /// <summary>非BMP文字を1列・4バイトとして扱い、ペア内部への位置指定を拒否します。</summary>
    [TestMethod]
    public void ScalarBoundariesAreDistinctFromUtf16()
    {
        var file = new SourceFile("unicode", Encoding.UTF8.GetBytes("日😀本\r\nX"));
        Assert.AreEqual(new SourcePosition(1, 3, 7), file.Position(3));
        Assert.AreEqual(new SourcePosition(2, 1, 12), file.Position(6));
        Assert.Throws<ArgumentOutOfRangeException>(() => file.Position(2));
    }

    /// <summary>不正UTF-8を置換文字で正常化しません。</summary>
    [TestMethod]
    public void InvalidUtf8IsRejected()
    {
        Assert.Throws<DecoderFallbackException>(() => new SourceFile("invalid", new byte[] { 0x61, 0xff }));
    }

    private static IEnumerable<JsonObject> Spans(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            if (obj.ContainsKey("file") && obj.ContainsKey("start") && obj.ContainsKey("end"))
            {
                yield return obj;
            }
            else
            {
                foreach (var span in obj.SelectMany(p => Spans(p.Value)))
                {
                    yield return span;
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var span in array.SelectMany(Spans))
            {
                yield return span;
            }
        }
    }
}
