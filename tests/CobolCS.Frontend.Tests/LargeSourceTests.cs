using System.Text;

namespace CobolCS.Frontend.Tests;

/// <summary>文字数に比例する軽量位置保持と、宣言原文の正確な取得を検証します。</summary>
[TestClass]
public sealed class LargeSourceTests
{
    /// <summary>固定形式の空行や短い行ではbyte範囲を行末へ制限し、次行の位置を保持します。</summary>
    [TestMethod]
    public void ShortFixedLinesClampByteOffsetsWithoutChangingColumns()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cobolcs-short-fixed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "input.cbl");
            const string source = "       IDENTIFICATION DIVISION.\n       PROGRAM-ID. SHORT-LINES.\n   \n\n       PROCEDURE DIVISION.\n       GOBACK.\n";
            File.WriteAllText(path, source, new UTF8Encoding(false));
            var ir = FrontendAnalyzer.Analyze(path, FrontendConfiguration.Parse("{\"sourceFormat\":\"fixed\"}"));
            var token = ir["programs"]!.AsArray().Single()!["tokens"]!.AsArray().Single(t => t!["text"]!.GetValue<string>() == "GOBACK")!;
            Assert.AreEqual(6, token["source"]!["start"]!["line"]!.GetValue<int>());
            Assert.AreEqual(8, token["source"]!["start"]!["column"]!.GetValue<int>());
            Assert.AreEqual(source.IndexOf("GOBACK", StringComparison.Ordinal), token["source"]!["start"]!["byteOffset"]!.GetValue<int>());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>大きいUnicodeリテラルでも元byte範囲を保ち、各文字へ位置辞書を展開しません。</summary>
    [TestMethod]
    public void LargeUnicodeLiteralRetainsExactSourceWithinAllocationBudget()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cobolcs-large-literal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var prefix = "IDENTIFICATION DIVISION.\nPROGRAM-ID. LARGE-TEXT.\nPROCEDURE DIVISION.\nDISPLAY ";
            var literal = "'" + string.Concat(Enumerable.Repeat("日😀", 8000)) + "'";
            var source = prefix + literal + ".\nGOBACK.\n";
            var path = Path.Combine(directory, "input.cbl");
            File.WriteAllText(path, source, new UTF8Encoding(false));
            var start = GC.GetAllocatedBytesForCurrentThread();
            var ir = FrontendAnalyzer.Analyze(path);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
            var program = ir["programs"]!.AsArray().Single()!;
            var token = program["tokens"]!.AsArray().Single(t => t!["text"]!.GetValue<string>() == literal)!;
            var location = token["source"]!;
            Assert.AreEqual(literal, location["raw"]!.GetValue<string>());
            Assert.AreEqual(Encoding.UTF8.GetByteCount(prefix), location["start"]!["byteOffset"]!.GetValue<int>());
            Assert.AreEqual(Encoding.UTF8.GetByteCount(prefix + literal), location["end"]!["byteOffset"]!.GetValue<int>());
            Assert.AreEqual(9, location["start"]!["column"]!.GetValue<int>());
            Assert.AreEqual(16011, location["end"]!["column"]!.GetValue<int>());
            Assert.IsLessThan(256L * 1024 * 1024, allocated, "Large literal allocated " + allocated + " bytes");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>多数の宣言で原文全体の再encodeを繰り返さず、最後の宣言も原位置へ結び付けます。</summary>
    [TestMethod]
    public void ThousandDeclarationsRetainSourceWithinAllocationBudget()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cobolcs-many-fields-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = new StringBuilder("IDENTIFICATION DIVISION.\nPROGRAM-ID. MANY-FIELDS.\nDATA DIVISION.\nWORKING-STORAGE SECTION.\n01 ROOT-GROUP.\n");
            for (var index = 0; index < 1000; index++)
            {
                source.AppendLine($"  05 FIELD-{index:D4} PIC X VALUE SPACE.");
            }

            source.AppendLine("PROCEDURE DIVISION.\nGOBACK.");
            var path = Path.Combine(directory, "input.cbl");
            File.WriteAllText(path, source.ToString(), new UTF8Encoding(false));
            var start = GC.GetAllocatedBytesForCurrentThread();
            var ir = FrontendAnalyzer.Analyze(path);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
            var symbols = ir["programs"]!.AsArray().Single()!["symbols"]!.AsArray();
            Assert.HasCount(1001, symbols);
            var last = symbols.Single(s => s!["name"]!.GetValue<string>() == "FIELD-0999")!;
            StringAssert.Contains(last["source"]!["raw"]!.GetValue<string>(), "05 FIELD-0999 PIC X VALUE SPACE.");
            Assert.AreEqual(1005, last["source"]!["start"]!["line"]!.GetValue<int>());
            Assert.IsLessThan(384L * 1024 * 1024, allocated, "Declarations allocated " + allocated + " bytes");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
