using System.Text;
using System.Text.Json.Nodes;
using CobolCS.Cli;
using CobolCS.Frontend;
using CobolCS.Runtime;
using CobolCS.Tools;
using Xunit;

namespace CobolCS.IntegrationTests;

/// <summary>日本語コーパスで見つかった字句・継続・参照変更を独自の最小再現で検証します。</summary>
public sealed class JapaneseParserTests
{
    /// <summary>複合不正入力の最初の位置を原バイト順に報告し、COPY の位置も失いません。</summary>
    /// <param name="hex">拡張と不完全符号の組合せ。</param>
    /// <param name="copy">COPY に配置する場合は true。</param>
    [Theory]
    [InlineData("874081", false)]
    [InlineData("ed4081", false)]
    [InlineData("8081", false)]
    [InlineData("874081", true)]
    [InlineData("ed4081", true)]
    [InlineData("8081", true)]
    public void CompoundDecodeErrorsKeepFirstOriginalPosition(string hex, bool copy)
    {
        using var directory = new TestDirectory();
        var source = Path.Combine(directory.Path, "source.cbl");
        var broken = copy ? "broken.cpy" : "source.cbl";
        if (copy)
        {
            File.WriteAllText(source, "IDENTIFICATION DIVISION. PROGRAM-ID. DECODE. DATA DIVISION. WORKING-STORAGE SECTION. COPY 'broken.cpy'. PROCEDURE DIVISION. GOBACK.");
        }

        var prefix = CodePagesEncodingProvider.Instance.GetEncoding(932)!.GetBytes("*> 日本語\n");
        var bytes = prefix.Concat(Convert.FromHexString(hex)).ToArray();
        File.WriteAllBytes(Path.Combine(directory.Path, broken), bytes);
        foreach (var encoding in new[] { "shift_jis", "cp932" })
        {
            var offset = prefix.Length + (encoding == "cp932" ? (hex.Length / 2) - 1 : 0);
            var direct = Assert.Throws<DecoderFallbackException>(() => SourceCodec.Decode(bytes, encoding));
            Assert.Equal(offset, direct.Index);
            var ir = FrontendAnalyzer.Analyze(source, FrontendConfiguration.Parse(new JsonObject { ["sourceEncoding"] = encoding }.ToJsonString()));
            var error = ir.A("diagnostics").Single(d => d.S("code") == "INPUT_DECODE");
            Assert.DoesNotContain(ir.A("diagnostics"), d => d.S("code") is "COPY_ERROR" or "EMPTY_SOURCE");
            var span = error["source"]!;
            Assert.Equal(broken, span.S("file"));
            Assert.Equal(offset, span["start"]!.I("byteOffset"));
            Assert.Equal(2, span["start"]!.I("line"));
            Assert.Equal(encoding == "cp932" ? 2 : 1, span["start"]!.I("column"));
            Assert.Equal(copy, span.A("copyStack").Any());
            Assert.Equal(Convert.ToHexStringLower(direct.BytesUnknown!), span.S("raw"));
            var original = ir.A("sources").Single(s => s.S("path") == broken);
            Assert.Equal(bytes, Convert.FromHexString(original.S("bytesHex")));
            Assert.Equal(Generator.Hash(bytes), original.S("sha256"));
        }
    }

    /// <summary>不正本体を正常プログラムと同時に解析しても、位置と正常側を保持します。</summary>
    /// <param name="hex">前方拡張の有無を含む不正入力。</param>
    [Theory]
    [InlineData("874081")]
    [InlineData("ed4081")]
    [InlineData("8081")]
    [InlineData("81")]
    public void DirectoryDecodeFailurePreservesValidProgram(string hex)
    {
        using var directory = new TestDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "valid.cbl"), "IDENTIFICATION DIVISION. PROGRAM-ID. VALID. PROCEDURE DIVISION. GOBACK.");
        var prefix = CodePagesEncodingProvider.Instance.GetEncoding(932)!.GetBytes("*> 日本語\n");
        var bytes = prefix.Concat(Convert.FromHexString(hex)).ToArray();
        File.WriteAllBytes(Path.Combine(directory.Path, "broken.cbl"), bytes);
        var ir = FrontendAnalyzer.Analyze(directory.Path, FrontendConfiguration.Parse("{\"sourceEncoding\":\"shift_jis\"}"));
        Assert.Equal("VALID", ir.A("programs").Single().S("id"));
        var error = ir.A("diagnostics").Single(d => d.S("code") == "INPUT_DECODE");
        Assert.Equal("broken.cbl", error["source"]!.S("file"));
        Assert.Equal(prefix.Length, error["source"]!["start"]!.I("byteOffset"));
        Assert.Equal(2, error["source"]!["start"]!.I("line"));
        Assert.Equal(1, error["source"]!["start"]!.I("column"));
        Assert.Equal(bytes, Convert.FromHexString(ir.A("sources").Single(s => s.S("path") == "broken.cbl").S("bytesHex")));
        Assert.DoesNotContain(ir.A("diagnostics"), d => d.S("code") is "COPY_ERROR" or "EMPTY_SOURCE");
    }

    /// <summary>日本語名のハイフンを同名化せず、COPY・SECTION参照を両バックエンドで実行します。</summary>
    /// <param name="backend">独立生成する実行バックエンド。</param>
    /// <returns>生成・ビルド・実行の完了。</returns>
    [Theory]
    [InlineData("source")]
    [InlineData("interpreter")]
    public async Task DistinctHyphensAndCopyReferencesExecute(string backend)
    {
        // 独自再現。参考: opensource-cobol/i18n_utf8/user-defined-word/002、jp-compat/copy-leading-trailing/001。
        using var directory = new TestDirectory();
        var source = Path.Combine(directory.Path, "source.cbl");
        File.WriteAllText(source, "IDENTIFICATION DIVISION. PROGRAM-ID. Ｐ－試験. DATA DIVISION. WORKING-STORAGE SECTION. 01 同-名 PIC 9. 01 同－名 PIC 9. 01 同−名 PIC 9. COPY 'copy.cpy' REPLACING LEADING ==元－== BY ==新−==. PROCEDURE DIVISION. PERFORM Ｓ－初期化 GOBACK. Ｓ－初期化 SECTION. MOVE 1 TO 同-名 MOVE 2 TO 同－名 MOVE 3 TO 同−名 DISPLAY 同-名 同－名 同−名 新−値.");
        File.WriteAllText(Path.Combine(directory.Path, "copy.cpy"), "01 元－値 PIC 9 VALUE 4.");
        var ir = FrontendAnalyzer.Analyze(source);
        Assert.Empty(ir.A("diagnostics"));
        Assert.Equal(new[] { "同-名", "同－名", "同−名", "新−値" }, ir.A("programs").Single().A("symbols").Select(s => s.S("name")));
        var actual = await Execute(directory, source, "Ｐ－試験", backend);
        Assert.Equal(new[] { "1234" }, actual.A("display").Select(x => x.GetValue<string>()));
    }

    /// <summary>空のPROCEDURE DIVISIONを警告なしで独立ビルドし、空観測とfallthroughを返します。</summary>
    /// <param name="backend">生成・実行するバックエンド。</param>
    /// <returns>生成・ビルド・独立実行の完了。</returns>
    [Theory]
    [InlineData("source")]
    [InlineData("interpreter")]
    public async Task EmptyProcedureCompilesAndFallsThrough(string backend)
    {
        using var directory = new TestDirectory();
        var source = Path.Combine(directory.Path, "source.cbl");
        File.WriteAllText(source, "IDENTIFICATION DIVISION. PROGRAM-ID. EMPTY. PROCEDURE DIVISION. END PROGRAM EMPTY.");
        var result = await Execute(directory, source, "EMPTY", backend);
        Assert.Empty(result.A("display"));
        Assert.Equal("fallthrough", result.S("signal"));
    }

    /// <summary>原文字コードで日本語名を解決し、COPY置換範囲も原バイトから復元します。</summary>
    /// <param name="encoding">明示したソース文字コード。</param>
    /// <returns>生成・ビルド・実行の完了。</returns>
    [Theory]
    [InlineData("utf-8")]
    [InlineData("cp932")]
    [InlineData("shift_jis")]
    public async Task EncodedCopyKeepsOriginalBytesAndRuns(string encoding)
    {
        // 独自再現。参考: opensource-cobol/i18n_sjis/user-defined-word/004、mb-space/003。
        using var directory = new TestDirectory();
        var source = Path.Combine(directory.Path, "source.cbl");
        var codec = encoding == "utf-8" ? Encoding.UTF8 : CodePagesEncodingProvider.Instance.GetEncoding(932)!;
        File.WriteAllBytes(source, codec.GetBytes("IDENTIFICATION DIVISION. PROGRAM-ID. 日本語. DATA DIVISION. WORKING-STORAGE SECTION. COPY '商品.cpy' REPLACING 商品 BY 日本項目. PROCEDURE DIVISION. MOVE '東京' TO 日本項目 DISPLAY 日本項目 GOBACK."));
        var original = codec.GetBytes("01 商品 PIC N(3) VALUE '日本'.");
        File.WriteAllBytes(Path.Combine(directory.Path, "商品.cpy"), original);
        var config = new JsonObject { ["sourceEncoding"] = encoding };
        var ir = FrontendAnalyzer.Analyze(source, FrontendConfiguration.Parse(config.ToJsonString()));
        Assert.Empty(ir.A("diagnostics"));
        var book = ir.A("sources").Single(s => s.S("path") == "商品.cpy");
        Assert.Equal(original, Convert.FromHexString(book.S("bytesHex")));
        Assert.Equal(encoding, book.S("encoding"));
        var span = ir.A("programs").Single().A("symbols").Single()["source"]!;
        Assert.Equal("商品.cpy", span.S("file"));
        Assert.Equal("01 商品 PIC N(3) VALUE '日本'", span.S("raw").TrimEnd('.'));
        var actual = await Execute(directory, source, "日本語", "source", config);
        Assert.Equal(new[] { "東京 " }, actual.A("display").Select(x => x.GetValue<string>()));
    }

    /// <summary>固定形式の単語継続で名前と原文位置を保持し、UTF-8/CP932で実行します。</summary>
    /// <param name="encoding">固定形式の原文字コード。</param>
    /// <returns>生成・ビルド・実行の完了。</returns>
    [Theory]
    [InlineData("utf-8")]
    [InlineData("cp932")]
    public async Task FixedWordContinuationRetainsPositions(string encoding)
    {
        // 独自再現。参考: opensource-cobol/i18n_utf8/program-id/004。
        using var directory = new TestDirectory();
        var source = Path.Combine(directory.Path, "source.cob");
        var text = "       IDENTIFICATION DIVISION.\n       PROGRAM-ID. あいう   \n      -    えお.\n       DATA DIVISION.\n       WORKING-STORAGE SECTION.\n       01 日本語   \n      -    項目 PIC 9 VALUE 3.\n       PROCEDURE DIVISION.\n           DISPLAY 日本語\n      -    項目.\n           GOBACK.\n";
        File.WriteAllBytes(source, (encoding == "utf-8" ? Encoding.UTF8 : CodePagesEncodingProvider.Instance.GetEncoding(932)!).GetBytes(text));
        var config = new JsonObject { ["sourceEncoding"] = encoding, ["sourceFormat"] = "fixed", ["fixedColumnPolicy"] = "unicode-columns" };
        var ir = FrontendAnalyzer.Analyze(source, FrontendConfiguration.Parse(config.ToJsonString()));
        Assert.Empty(ir.A("diagnostics"));
        var program = ir.A("programs").Single();
        Assert.Equal("あいうえお", program.S("id"));
        Assert.Equal("日本語項目", program.A("symbols").Single().S("name"));
        var token = program.A("tokens").Single(t => t.S("text") == "あいうえお");
        Assert.Equal(2, token["source"]!["start"]!.I("line"));
        Assert.Equal(3, token["source"]!["end"]!.I("line"));
        var actual = await Execute(directory, source, "あいうえお", "source", config);
        Assert.Equal(new[] { "3" }, actual.A("display").Select(x => x.GetValue<string>()));
    }

    /// <summary>省略長を両実行器で末尾までとして読み書きし、空白・動的位置を検証します。</summary>
    /// <param name="backend">独立生成する実行バックエンド。</param>
    /// <returns>生成・ビルド・実行の完了。</returns>
    [Theory]
    [InlineData("source")]
    [InlineData("interpreter")]
    public async Task OmittedModificationLengthReadsAndWrites(string backend)
    {
        // 独自再現。参考: opensource-cobol/i18n_sjis/pic-n/012、pic-x/016。
        using var directory = new TestDirectory();
        var source = Path.Combine(directory.Path, "source.cbl");
        File.WriteAllText(source, "IDENTIFICATION DIVISION. PROGRAM-ID. SLICES. DATA DIVISION. WORKING-STORAGE SECTION. 01 日本項目 PIC N(5) VALUE '日本語仮名'. 01 ASCII項目 PIC X(5) VALUE 'ABCDE'. 01 位置 PIC 9 VALUE 3. PROCEDURE DIVISION. DISPLAY 日本項目(位置:) MOVE '京' TO 日本項目(2:) DISPLAY 日本項目 MOVE 'Z' TO ASCII項目(5:) DISPLAY ASCII項目(2:) GOBACK.");
        var actual = await Execute(directory, source, "SLICES", backend);
        Assert.Equal(new[] { "語仮名", "日京   ", "BCDZ" }, actual.A("display").Select(x => x.GetValue<string>()));
        Assert.Equal("日京   ", actual["fields"]!.S("日本項目"));
    }

    /// <summary>BY REFERENCEの省略長でも外部契約を保持したC#をビルドし、CALL実行は成功と扱いません。</summary>
    /// <returns>生成・コンパイルの完了。</returns>
    [Fact]
    public async Task OmittedLengthInCallReferenceBuildsWithoutInventingResponse()
    {
        using var directory = new TestDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "NuGet.Config"), "<configuration><packageSources><clear /></packageSources></configuration>");
        var source = Path.Combine(directory.Path, "source.cbl");
        File.WriteAllText(source, "IDENTIFICATION DIVISION. PROGRAM-ID. CALLSLICE. DATA DIVISION. WORKING-STORAGE SECTION. 01 項目 PIC N(3) VALUE '日本語'. PROCEDURE DIVISION. CALL 'FAKE' USING BY REFERENCE 項目(2:) GOBACK.");
        var config = Path.Combine(directory.Path, "config.json");
        File.WriteAllText(config, "{\"testGeneration\":{\"maxAttempts\":1}}");
        var generated = Path.Combine(directory.Path, "generated");
        await ProcessRunner.Run("dotnet", [.. Workspace.Cli, "generate", source, "--output", generated, "--config", config]);
        await ProcessRunner.Run("dotnet", [.. Workspace.Cli, "build", generated]);
        Assert.NotEmpty(Workspace.Read(Path.Combine(generated, "reports/dependencies.json")).AsArray());
    }

    /// <summary>不正COPYの実ファイル・原バイト位置・スタックを保持します。</summary>
    /// <param name="encoding">不正バイトを含むソース文字コード。</param>
    [Theory]
    [InlineData("utf-8")]
    [InlineData("cp932")]
    [InlineData("shift_jis")]
    public void InvalidCopyIsDiagnosedAtItsOriginalFile(string encoding)
    {
        using var directory = new TestDirectory();
        var source = Path.Combine(directory.Path, "source.cbl");
        File.WriteAllText(source, "IDENTIFICATION DIVISION. PROGRAM-ID. DECODE. DATA DIVISION. WORKING-STORAGE SECTION. COPY 'broken.cpy'. PROCEDURE DIVISION. GOBACK.");
        var codec = encoding == "utf-8" ? Encoding.UTF8 : CodePagesEncodingProvider.Instance.GetEncoding(932)!;
        var prefix = codec.GetBytes("01 日本 PIC N.\n");
        var original = prefix.Concat(new byte[] { encoding == "utf-8" ? (byte)0xff : (byte)0x81 }).ToArray();
        File.WriteAllBytes(Path.Combine(directory.Path, "broken.cpy"), original);
        var ir = FrontendAnalyzer.Analyze(source, FrontendConfiguration.Parse(new JsonObject { ["sourceEncoding"] = encoding }.ToJsonString()));
        var error = ir.A("diagnostics").Single(d => d.S("code") == "INPUT_DECODE");
        Assert.Equal("broken.cpy", error["source"]!.S("file"));
        Assert.Equal(prefix.Length, error["source"]!["start"]!.I("byteOffset"));
        Assert.Equal(2, error["source"]!["start"]!.I("line"));
        Assert.NotEmpty(error["source"]!.A("copyStack"));
        Assert.Equal(original, Convert.FromHexString(ir.A("sources").Single(s => s.S("path") == "broken.cpy").S("bytesHex")));
    }

    /// <summary>UPONをIRに保持し、未実装のデバイス出力を無視して実行させません。</summary>
    [Fact]
    public void DisplayUponIsStructuredAndExplicitlyUnsupported()
    {
        using var directory = new TestDirectory();
        var source = Path.Combine(directory.Path, "source.cbl");
        File.WriteAllText(source, "IDENTIFICATION DIVISION. PROGRAM-ID. ROUTE. PROCEDURE DIVISION. DISPLAY 'A' UPON CONSOLE WITH NO ADVANCING END-DISPLAY DISPLAY 'B' END-DISPLAY GOBACK.");
        var ir = FrontendAnalyzer.Analyze(source);
        Assert.Empty(ir.A("diagnostics"));
        var display = ir.A("programs").Single().A("paragraphs").SelectMany(p => p.A("statements")).First();
        Assert.Equal("CONSOLE", display.S("upon"));
        Assert.True(display.B("noAdvancing"));
        var error = Validation.Check(ir, "source").Single(d => d!.S("code") == "CS_DISPLAY_UPON")!;
        Assert.Contains("UPON CONSOLE", error["source"]!.S("raw"), StringComparison.Ordinal);
    }

    /// <summary>重複句で次の文を消さず、空入力と正常な空手続きを区別します。</summary>
    [Fact]
    public void InvalidDisplayAndEmptySourcesRemainVisible()
    {
        using var directory = new TestDirectory();
        var source = Path.Combine(directory.Path, "source.cbl");
        File.WriteAllText(source, "IDENTIFICATION DIVISION. PROGRAM-ID. INVALID. PROCEDURE DIVISION. DISPLAY 'A' NO ADVANCING NO ADVANCING DISPLAY 'B' GOBACK.");
        var ir = FrontendAnalyzer.Analyze(source);
        Assert.Contains(ir.A("diagnostics"), d => d.S("code") == "UNSUPPORTED_STATEMENT");
        Assert.Contains(ir.A("programs").Single().A("paragraphs").SelectMany(p => p.A("statements")), s => s.S("kind") == "display" && s.A("values").Single().S("value") == "B");
        File.WriteAllText(source, "IDENTIFICATION DIVISION. PROGRAM-ID. EMPTY. PROCEDURE DIVISION. END PROGRAM EMPTY.");
        var empty = FrontendAnalyzer.Analyze(source);
        Assert.Empty(empty.A("diagnostics"));
        Assert.Empty(empty.A("programs").Single().A("paragraphs"));
        File.WriteAllText(source, "\n*> comment\n");
        Assert.Contains(FrontendAnalyzer.Analyze(source).A("diagnostics"), d => d.S("code") == "EMPTY_SOURCE");
    }

    /// <summary>標準JISとWindowsの6文字の差をバイト値から検証します。</summary>
    /// <param name="code">原Shift_JISの2バイト値。</param>
    /// <param name="jis">標準JISのUnicode文字。</param>
    /// <param name="windows">Windows CP932のUnicode文字。</param>
    [Theory]
    [InlineData(0x8160, "〜", "～")]
    [InlineData(0x8161, "‖", "∥")]
    [InlineData(0x817c, "−", "－")]
    [InlineData(0x8191, "¢", "￠")]
    [InlineData(0x8192, "£", "￡")]
    [InlineData(0x81ca, "¬", "￢")]
    public void ShiftJisAndWindowsMappingsRemainDistinct(int code, string jis, string windows)
    {
        var bytes = new byte[] { (byte)(code >> 8), (byte)code };
        Assert.Equal(jis, SourceCodec.Decode(bytes, "shift_jis"));
        Assert.Equal(windows, SourceCodec.Decode(bytes, "cp932"));
        Assert.Equal(2, SourceCodec.ByteCount(jis, "shift_jis"));
        Assert.Equal(2, SourceCodec.ByteCount(windows, "cp932"));
    }

    /// <summary>Windows拡張をCP932では保持し、標準Shift_JISへ黙って受け入れません。</summary>
    /// <param name="hex">Windowsだけの原バイト。</param>
    [Theory]
    [InlineData("8740")]
    [InlineData("ed40")]
    [InlineData("8790")]
    [InlineData("fa40")]
    [InlineData("80")]
    public void ShiftJisRejectsWindowsExtensions(string hex)
    {
        var bytes = Convert.FromHexString(hex);
        Assert.NotEmpty(SourceCodec.Decode(bytes, "cp932"));
        Assert.Throws<DecoderFallbackException>(() => SourceCodec.Decode(bytes, "shift_jis"));
    }

    /// <summary>省略長と極端な境界で受信領域を壊さず、両文字列helperとdispatcherを検証します。</summary>
    [Fact]
    public void ReferenceModificationBoundsRejectInvalidReadsAndWrites()
    {
        Assert.Equal("本語", SourceSupport.Slice("日本語", 2, null));
        Assert.Equal("日京 ", SourceSupport.Replace("日本語", 2, null, "京"));
        Assert.Throws<IndexOutOfRangeException>(() => SourceSupport.Slice("日本語", 0, null));
        Assert.Throws<IndexOutOfRangeException>(() => SourceSupport.Replace("日本語", 4, null, "京"));
        Assert.Throws<IndexOutOfRangeException>(() => SourceSupport.Slice("日本語", 2, int.MaxValue));
        using var directory = new TestDirectory();
        var source = Path.Combine(directory.Path, "source.cbl");
        File.WriteAllText(source, "IDENTIFICATION DIVISION. PROGRAM-ID. BOUNDS. DATA DIVISION. WORKING-STORAGE SECTION. 01 項目 PIC N(3). PROCEDURE DIVISION. GOBACK.");
        var program = FrontendAnalyzer.Analyze(source).A("programs").Single();
        var storage = new Storage(program);
        var reference = new JsonObject { ["symbolId"] = program.A("symbols").Single().S("id"), ["subscripts"] = new JsonArray() };
        object Eval(JsonNode node) => Number.Parse(node.S("value"));
        storage.Set(reference, "日本語", Eval);
        reference["modification"] = new JsonObject { ["start"] = new JsonObject { ["value"] = "2" }, ["length"] = null };
        Assert.Equal("本語", storage.Get(reference, Eval));
        storage.Set(reference, "京", Eval);
        Assert.Equal("京 ", storage.Get(reference, Eval));
        reference["modification"]!["length"] = new JsonObject { ["value"] = int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        Assert.Throws<IndexOutOfRangeException>(() => storage.Get(reference, Eval));
        Assert.Throws<IndexOutOfRangeException>(() => storage.Set(reference, "京", Eval));
    }

    /// <summary>両バックエンドの生成と独立実行を検証し、探索や外部接続を成功根拠へ混ぜません。</summary>
    /// <param name="directory">ケース専用作業先。</param>
    /// <param name="source">原ソース。</param>
    /// <param name="program">実行プログラム名。</param>
    /// <param name="backend">生成バックエンド。</param>
    /// <param name="config">原文形式と文字コード。</param>
    /// <returns>独立アセンブリの観測結果。</returns>
    private static async Task<JsonNode> Execute(TestDirectory directory, string source, string program, string backend, JsonObject? config = null)
    {
        File.WriteAllText(Path.Combine(directory.Path, "NuGet.Config"), "<configuration><packageSources><clear /></packageSources></configuration>");
        config = config?.DeepClone().AsObject() ?? new JsonObject();
        config["testGeneration"] = new JsonObject { ["maxAttempts"] = 1 };
        var configuration = Path.Combine(directory.Path, "config.json");
        File.WriteAllText(configuration, config.ToJsonString());
        var generated = Path.Combine(directory.Path, "generated");
        await ProcessRunner.Run("dotnet", [.. Workspace.Cli, "generate", source, "--output", generated, "--backend", backend, "--mode", "strict", "--config", configuration]);
        await ProcessRunner.Run("dotnet", [.. Workspace.Cli, "build", generated]);
        var result = await ProcessRunner.Run("dotnet", [Path.Combine(generated, "bin/Debug/net10.0/CobolCS.Generated.dll"), program]);
        return JsonNode.Parse(result)!;
    }
}
