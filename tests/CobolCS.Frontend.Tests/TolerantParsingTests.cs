using System.Text;
using System.Text.Json.Nodes;

namespace CobolCS.Frontend.Tests;

/// <summary>未知範囲の保持、親の境界、失敗した解析状態の取り消しを独立した最小入力で検証します。</summary>
public sealed class TolerantParsingTests
{
    private const string Header = "IDENTIFICATION DIVISION. PROGRAM-ID. RECOVER. DATA DIVISION. WORKING-STORAGE SECTION. 01 N PIC 9 VALUE 1. PROCEDURE DIVISION. ";

    /// <summary>未知文の前後を保持し、未知範囲にtokenと診断を結び付けます。</summary>
    /// <param name="unknown">未知文または不正な既知文。</param>
    [Theory]
    [InlineData("MYSTERY 'END-IF' N")]
    [InlineData("MYSTERY\n '日本語'\n N")]
    [InlineData("MOVE TO N")]
    public void UnknownStatementPreservesFollowingKnownStatements(string unknown)
    {
        var ir = Analyze(Header + "MOVE 2 TO N. " + unknown + " DISPLAY N GOBACK.");
        var program = Program(ir);
        var statements = Statements(program).ToArray();
        Assert.Equal(new[] { "move", "sentenceBoundary", "unknown", "display", "halt", "sentenceBoundary" }, statements.Select(n => Text(n, "kind")));
        var node = statements[2];
        Assert.Equal(unknown, Text(node["source"]!, "raw"));
        Assert.NotEmpty(node["tokens"]!.AsArray());
        Assert.Equal("DISPLAY", Text(node["recovery"]!, "boundary"));
        var diagnostic = ir["diagnostics"]!.AsArray().Single(d => Text(d!, "code") == "UNSUPPORTED_STATEMENT")!;
        Assert.Equal("RECOVER", Text(diagnostic, "programId"));
        Assert.Equal(Text(node, "id"), Text(diagnostic, "statementId"));
        Assert.DoesNotContain(ir["diagnostics"]!.AsArray(), d => Text(d!, "code") is "PARSE_ERROR" or "UNCONSUMED_TOKEN");
        Assert.Equal("tolerant", Text(ir, "syntaxRecovery"));
    }

    /// <summary>未知文が外側のELSEを消費せず、thenとelseの所属を保ちます。</summary>
    [Fact]
    public void RecoveryPreservesOuterElse()
    {
        var ir = Analyze(Header + "IF N = 1 MYSTERY N ELSE DISPLAY 'else' END-IF DISPLAY 'after' GOBACK.");
        var conditional = Statements(Program(ir)).First();
        Assert.Equal("if", Text(conditional, "kind"));
        Assert.Equal("unknown", Text(conditional["then"]!.AsArray().Single()!, "kind"));
        Assert.Equal("display", Text(conditional["else"]!.AsArray().Single()!, "kind"));
        Assert.Equal("display", Text(Statements(Program(ir)).Skip(1).First(), "kind"));
    }

    /// <summary>異なる種類の入れ子を含む失敗ブロックを丸ごと保持します。</summary>
    [Fact]
    public void BrokenBlockDoesNotLeakNestedStatements()
    {
        var body = "IF MISSING = 1 EVALUATE N WHEN 1 DISPLAY 'inside' END-EVALUATE END-IF DISPLAY 'after' GOBACK.";
        var ir = Analyze(Header + body);
        var statements = Statements(Program(ir)).ToArray();
        Assert.Equal(new[] { "unknown", "display", "halt", "sentenceBoundary" }, statements.Select(n => Text(n, "kind")));
        Assert.Contains("DISPLAY 'inside' END-EVALUATE END-IF", Text(statements[0]["source"]!, "raw"), StringComparison.Ordinal);
    }

    /// <summary>同じ種類の親子で終端が曖昧な場合、内側のELSEを外側のnative本文へ漏らしません。</summary>
    /// <param name="body">未知条件の内側にある既知文。</param>
    [Theory]
    [InlineData("MOVE 1 TO N")]
    [InlineData("CONTINUE")]
    public void AmbiguousNestedIfStubsWholeParent(string body)
    {
        var ir = Analyze(Header + "IF N = 1 IF MISSING = 1 " + body + " ELSE MOVE 2 TO N END-IF ELSE MOVE 3 TO N END-IF DISPLAY N GOBACK.");
        var statements = Statements(Program(ir)).ToArray();
        Assert.Equal(new[] { "unknown", "display", "halt", "sentenceBoundary" }, statements.Select(n => Text(n, "kind")));
        Assert.Contains("ELSE MOVE 3 TO N END-IF", Text(statements[0]["source"]!, "raw"), StringComparison.Ordinal);
        Assert.Contains("Ambiguous nested IF", Text(statements[0], "reason"), StringComparison.Ordinal);
    }

    /// <summary>後続段落とEND PROGRAMを復旧範囲の外に保ちます。</summary>
    /// <param name="condition">既知または不明な条件。</param>
    [Theory]
    [InlineData("MISSING = 1")]
    [InlineData("N = 1")]
    public void MissingTerminatorPreservesParagraphAndProgramBoundary(string condition)
    {
        var ir = Analyze(Header + "IF " + condition + " DISPLAY 'inside'\n NEXT-PARA. DISPLAY 'after' GOBACK. END PROGRAM RECOVER.");
        var paragraphs = Program(ir)["paragraphs"]!.AsArray();
        Assert.Equal(new[] { "$entry", "NEXT-PARA" }, paragraphs.Select(p => Text(p!, "name")));
        Assert.Equal("unknown", Text(paragraphs[0]!["statements"]!.AsArray().Single()!, "kind"));
        Assert.Equal("display", Text(paragraphs[1]!["statements"]!.AsArray().First()!, "kind"));
        Assert.Empty(ir["rejectedTokens"]!.AsArray());
    }

    /// <summary>失敗した親の中で作られたCALL依存と特殊項目を取り消します。</summary>
    [Fact]
    public void FailedParentRollsBackDependenciesAndSymbols()
    {
        var ir = Analyze(Header + "IF N = 1 CALL 'INNER' END-CALL MOVE 1 TO RETURN-CODE END-PERFORM. DISPLAY N GOBACK.");
        var program = Program(ir);
        Assert.Empty(program["dependencies"]!.AsArray());
        Assert.DoesNotContain(program["symbols"]!.AsArray(), s => Text(s!, "name") == "RETURN-CODE");
        Assert.Contains(Statements(program), s => Text(s, "kind") == "display");
        Assert.DoesNotContain(ir["diagnostics"]!.AsArray(), d => Text(d!, "code") == "PARSE_ERROR");
    }

    /// <summary>未知句の前に読み取れたPICとVALUEを確定型へ昇格させません。</summary>
    [Fact]
    public void FailedDeclarationDiscardsPartialAttributes()
    {
        var source = "IDENTIFICATION DIVISION. PROGRAM-ID. RECOVER. DATA DIVISION. WORKING-STORAGE SECTION.\n01 G.\n 05 BAD PIC 9 VALUE 3 MYSTERY.\n 05 SIBLING PIC 9.\n01 GOOD PIC 9 VALUE 7.\nPROCEDURE DIVISION. DISPLAY GOOD GOBACK.";
        var ir = Analyze(source);
        var symbols = Program(ir)["symbols"]!.AsArray();
        var bad = symbols.Single(s => Text(s!, "name") == "BAD")!;
        Assert.True(bad["stub"]!.GetValue<bool>());
        Assert.Null(bad["pic"]);
        Assert.Null(bad["initial"]);
        Assert.Equal("unknown", Text(bad, "layoutStatus"));
        Assert.True(symbols.Single(s => Text(s!, "name") == "G")!["stub"]!.GetValue<bool>());
        Assert.False(symbols.Single(s => Text(s!, "name") == "GOOD")!["stub"]!.GetValue<bool>());
        Assert.Equal("display", Text(Statements(Program(ir)).First(), "kind"));
    }

    /// <summary>宣言末尾のピリオドがなくても次の宣言を飲み込みません。</summary>
    [Fact]
    public void MissingDeclarationPeriodPreservesNextDeclaration()
    {
        var source = "IDENTIFICATION DIVISION. PROGRAM-ID. RECOVER. DATA DIVISION. WORKING-STORAGE SECTION.\n01 BAD PIC 9 VALUE 3\n01 GOOD PIC 9 VALUE 7.\nPROCEDURE DIVISION. DISPLAY GOOD GOBACK.";
        var ir = Analyze(source);
        Assert.Equal(new[] { "BAD", "GOOD" }, Program(ir)["symbols"]!.AsArray().Select(s => Text(s!, "name")));
        Assert.True(Program(ir)["symbols"]![0]!["stub"]!.GetValue<bool>());
    }

    /// <summary>未知ヘッダー句と設定句の失敗を実行前の補完対象にします。</summary>
    /// <param name="clause">未知または不正なヘッダー。</param>
    [Theory]
    [InlineData("MYSTERY-HEADER IS 'x'.")]
    [InlineData("MYSTERY-HEADER IS 'x'")]
    [InlineData("DECIMAL-POINT IS DOT.")]
    [InlineData("CURRENCY SIGN IS 'XX'.")]
    public void BrokenHeaderPreservesProgramAndEntryStub(string clause)
    {
        var ir = Analyze("IDENTIFICATION DIVISION. PROGRAM-ID. RECOVER. ENVIRONMENT DIVISION. CONFIGURATION SECTION. SPECIAL-NAMES. " + clause + " PROCEDURE DIVISION. DISPLAY 'after' GOBACK.");
        var statements = Statements(Program(ir)).ToArray();
        Assert.Equal("unknown", Text(statements[0], "kind"));
        Assert.Contains(clause, Text(statements[0]["source"]!, "raw"), StringComparison.Ordinal);
        Assert.Equal("display", Text(statements[1], "kind"));
        Assert.Equal("dot", Text(ir["config"]!, "decimalPoint"));
        Assert.Equal("$", Text(ir["config"]!, "currencySymbol"));
    }

    /// <summary>未知のPROGRAM-ID属性とPROCEDURE USING句も補完対象として保持します。</summary>
    /// <param name="source">識別可能なprogramと未知の属性。</param>
    [Theory]
    [InlineData("IDENTIFICATION DIVISION. PROGRAM-ID. RECOVER IS SPECIAL. PROCEDURE DIVISION. DISPLAY 'after' GOBACK.")]
    [InlineData("IDENTIFICATION DIVISION. PROGRAM-ID. RECOVER. PROCEDURE DIVISION USING BY MYSTERY. DISPLAY 'after' GOBACK.")]
    [InlineData("IDENTIFICATION DIVISION. PROGRAM-ID. RECOVER. PROCEDURE DIVISION DISPLAY 'after' GOBACK.")]
    public void UnknownProgramAttributesKeepEntryStub(string source)
    {
        var ir = Analyze(source);
        Assert.Equal("RECOVER", Text(Program(ir), "id"));
        Assert.Equal("unknown", Text(Statements(Program(ir)).First(), "kind"));
        Assert.Contains(Statements(Program(ir)), n => Text(n, "kind") == "display");
    }

    /// <summary>EOFと連続した未知範囲でも有限回で終了します。</summary>
    /// <param name="body">終端付近の未知文。</param>
    [Theory]
    [InlineData("MYSTERY N")]
    [InlineData("NEXT SENTENCE")]
    [InlineData("IF MISSING = 1 DISPLAY 'inside'")]
    [InlineData("MYSTERY N. OTHER N. GOBACK.")]
    public void EndOfInputKeepsUnknownRanges(string body)
    {
        var ir = Analyze(Header + body);
        Assert.Single(Program(ir)["paragraphs"]!.AsArray());
        Assert.Contains(Statements(Program(ir)), s => Text(s, "kind") == "unknown");
        Assert.DoesNotContain(ir["diagnostics"]!.AsArray(), d => Text(d!, "code") == "PARSE_ERROR");
    }

    /// <summary>PROGRAM-IDを特定できない入力は許容モードでも拒否します。</summary>
    [Fact]
    public void MissingProgramIdRemainsFatal()
    {
        var ir = Analyze("IDENTIFICATION DIVISION. PROCEDURE DIVISION. GOBACK.");
        Assert.Empty(ir["programs"]!.AsArray());
        Assert.Contains(ir["diagnostics"]!.AsArray(), d => Text(d!, "code") == "PARSE_ERROR");
    }

    /// <summary>従来APIの既定はStandardで、許容モードのメタデータを追加しません。</summary>
    [Fact]
    public void DefaultApiKeepsStandardPolicy()
    {
        var ir = Analyze(Header + "GOBACK.", SyntaxRecoveryPolicy.Standard);
        Assert.Null(ir["syntaxRecovery"]);
        Assert.Equal("halt", Text(Statements(Program(ir)).First(), "kind"));
    }

    /// <summary>固定形式と日本語COPYの復旧位置を原バイトと展開元で保持します。</summary>
    /// <param name="encoding">入力の文字コード。</param>
    [Theory]
    [InlineData("utf-8")]
    [InlineData("cp932")]
    [InlineData("shift_jis")]
    public void FixedFormatCopyPreservesSource(string encoding)
    {
        var directory = Path.Combine(Path.GetTempPath(), "cobolcs-tolerant-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "sample.cbl");
            var codec = encoding == "utf-8" ? Encoding.UTF8 : CodePagesEncodingProvider.Instance.GetEncoding(932)!;
            File.WriteAllText(source, "       IDENTIFICATION DIVISION.\n       PROGRAM-ID. RECOVER.\n       PROCEDURE DIVISION.\n           COPY 'part.cpy'.\n           DISPLAY 'after' GOBACK.\n", codec);
            File.WriteAllText(Path.Combine(directory, "part.cpy"), "           MYSTERY '日本語'\n", codec);
            var config = FrontendConfiguration.Parse(new JsonObject { ["sourceFormat"] = "fixed", ["sourceEncoding"] = encoding }.ToJsonString());
            var ir = FrontendAnalyzer.Analyze(source, config, syntaxRecovery: SyntaxRecoveryPolicy.Tolerant);
            var node = Statements(Program(ir)).First();
            Assert.Equal("unknown", Text(node, "kind"));
            Assert.Equal("part.cpy", Text(node["source"]!, "file"));
            Assert.Equal(1, node["source"]!["start"]!["line"]!.GetValue<int>());
            Assert.NotEmpty(node["source"]!["copyStack"]!.AsArray());
            Assert.Contains("日本語", Text(node["source"]!, "raw"), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static string Text(JsonNode node, string key) => node[key]?.GetValue<string>() ?? string.Empty;

    private static JsonNode Program(JsonNode ir) => Assert.Single(ir["programs"]!.AsArray())!;

    private static IEnumerable<JsonNode> Statements(JsonNode program) => program["paragraphs"]!.AsArray().SelectMany(p => p!["statements"]!.AsArray()).Select(n => n!);

    private static JsonObject Analyze(string source, SyntaxRecoveryPolicy policy = SyntaxRecoveryPolicy.Tolerant)
    {
        var path = Path.Combine(Path.GetTempPath(), "cobolcs-tolerant-" + Guid.NewGuid().ToString("N") + ".cbl");
        try
        {
            File.WriteAllText(path, source);
            return FrontendAnalyzer.Analyze(path, syntaxRecovery: policy);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
