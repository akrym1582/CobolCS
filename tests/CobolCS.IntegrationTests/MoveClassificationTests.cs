using System.Text.Json.Nodes;
using CobolCS.Cli;
using CobolCS.Frontend;
using CobolCS.Runtime;
using CobolCS.Tools;
using Xunit;

namespace CobolCS.IntegrationTests;

/// <summary>合成宣言の独立期待値で MOVE スタブ理由と候補条件を検証します。</summary>
public sealed class MoveClassificationTests
{
    /// <summary>数値項目から固定長 X への文全体候補に PIC と位置を保存します。</summary>
    /// <returns>未補完スタブへの到達が失敗することの確認。</returns>
    [Fact]
    public async Task NumericDisplayCandidatePreservesDeclarationAndPosition()
    {
        using var folder = new TestDirectory();
        var report = Generate(folder, "01 N PIC S9(3)V99. 01 X1 PIC X(8). 01 X2 PIC X(4).", "MOVE N TO X1 X2", fullGeneration: true);
        var row = report.A("statements").Single();
        var classification = row["moveClassification"]!;
        Assert.Equal("MOVE_TYPE", row.S("reasonCode"));
        Assert.Equal("numeric-to-text", classification.S("primaryReason"));
        Assert.Equal(new[] { "numeric-to-text" }, classification.A("reasons").Select(r => r.GetValue<string>()));
        Assert.Equal("numeric-display-to-pic-x", classification.S("candidate"));
        Assert.Equal("requires-semantic-review", classification.S("candidateStatus"));
        var details = classification["candidateDetails"]!;
        Assert.Equal("S9(3)V99", details["sending"]!.S("pic"));
        Assert.Equal(5, details["sending"]!.I("digits"));
        Assert.Equal(2, details["sending"]!.I("scale"));
        Assert.True(details["sending"]!.B("signed"));
        Assert.Equal(new[] { 8, 4 }, details.A("receivers").Select(r => r.I("length")));
        Assert.Equal(row.S("statementId"), details.S("statementId"));
        Assert.Equal(row.S("file"), details["source"]!.S("file"));
        Assert.Equal(1, report["moveSummary"]!.I("candidateStatements"));
        Assert.Equal(1, report["moveSummary"]!.I("statements"));
        Assert.DoesNotContain("MOVE N TO X1", report.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains("MOVE classification", InternalStubMetrics.Markdown(report), StringComparison.Ordinal);
        Assert.Contains("NotImplementedException", File.ReadAllText(Directory.GetFiles(Path.Combine(folder.Path, "generated", "Stubs"), "Statement_*.cs", SearchOption.AllDirectories).Single()), StringComparison.Ordinal);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ProcessRunner.Run("dotnet", [.. Workspace.Cli, "run", Path.Combine(folder.Path, "generated"), "--program", "CLASSIFY"], folder.Path));
    }

    /// <summary>複数受信先を一文として数え、数値受信先が混ざれば候補から外します。</summary>
    [Fact]
    public void MixedTargetsKeepReasonButRejectCandidate()
    {
        using var folder = new TestDirectory();
        var report = Generate(folder, "01 N PIC 999. 01 X1 PIC X(8). 01 N2 PIC 999.", "MOVE N TO X1 N2");
        var classification = report.A("statements").Single()["moveClassification"]!;
        Assert.Equal("numeric-to-text", classification.S("primaryReason"));
        Assert.Null(classification["candidate"]);
        Assert.Equal(1, report["moveSummary"]!.I("statements"));
    }

    /// <summary>別理由の受信先が混ざる MOVE は文数を増やさず両理由を保持します。</summary>
    [Fact]
    public void MultipleTargetReasonsHaveOnePrimaryAndTwoOverlappingCounts()
    {
        using var folder = new TestDirectory();
        var report = Generate(folder, "01 N PIC 99. 01 X1 PIC XX. 01 G. 05 CHILD PIC XX.", "MOVE N TO X1 G");
        var classification = report.A("statements").Single()["moveClassification"]!;
        Assert.Equal("group-storage", classification.S("primaryReason"));
        Assert.Equal(new[] { "group-storage", "numeric-to-text" }, classification.A("reasons").Select(r => r.GetValue<string>()));
        Assert.Equal(1, report["moveSummary"]!.I("statements"));
        Assert.Equal(1, report["moveSummary"]!["reasons"]!.I("group-storage"));
        Assert.Equal(1, report["moveSummary"]!["reasons"]!.I("numeric-to-text"));
    }

    /// <summary>符号なし整数と小数も候補の型条件を保持します。</summary>
    /// <param name="pic">送信側 PIC。</param>
    /// <param name="scale">期待する小数桁数。</param>
    [Theory]
    [InlineData("99", 0)]
    [InlineData("99V9", 1)]
    public void CandidateSeparatesUnsignedScales(string pic, int scale)
    {
        using var folder = new TestDirectory();
        var report = Generate(folder, "01 N PIC " + pic + ". 01 X1 PIC XX.", "MOVE N TO X1");
        var details = report.A("statements").Single()["moveClassification"]!["candidateDetails"]!;
        Assert.False(details["sending"]!.B("signed"));
        Assert.Equal(scale, details["sending"]!.I("scale"));
        Assert.Single(report["moveSummary"]!.A("signedScaleGroups"));
    }

    /// <summary>数値リテラルは原因になっても、基本項目候補には含めません。</summary>
    [Fact]
    public void NumericLiteralIsNotCandidate()
    {
        using var folder = new TestDirectory();
        var report = Generate(folder, "01 X1 PIC X(8).", "MOVE 1 TO X1");
        Assert.Equal("numeric-to-text", report.A("statements").Single()["moveClassification"]!.S("primaryReason"));
        Assert.Null(report.A("statements").Single()["moveClassification"]!["candidate"]);
    }

    /// <summary>値の型とレイアウトに基づく主理由の優先順を確認します。</summary>
    /// <param name="declarations">合成宣言。</param>
    /// <param name="move">MOVE 文。</param>
    /// <param name="primary">独立に指定した最優先理由。</param>
    [Theory]
    [InlineData("01 G. 05 N PIC 99. 01 X1 PIC XX.", "MOVE G TO X1", "group-storage")]
    [InlineData("01 E PIC ZZ9. 01 X1 PIC XX.", "MOVE E TO X1", "edited-pic")]
    [InlineData("01 N PIC 99. 01 X1 PIC XX.", "MOVE FUNCTION MYSTERY(N) TO X1", "unsupported-expression")]
    [InlineData("01 N PIC 99. 01 ALIAS REDEFINES N PIC 99. 01 X1 PIC XX.", "MOVE ALIAS TO X1", "alias-layout")]
    [InlineData("01 N PIC 99. 01 X1 PIC XX.", "MOVE N TO X1", "numeric-to-text")]
    public void CausesAreClassifiedFromStructure(string declarations, string move, string primary)
    {
        using var folder = new TestDirectory();
        var report = Generate(folder, declarations, move);
        Assert.Equal(primary, report.A("statements").Single()["moveClassification"]!.S("primaryReason"));
        Assert.Equal(1, report["moveSummary"]!.I("statements"));
    }

    /// <summary>DISPLAY 以外の格納と別途 SIGN 句は候補から除外します。</summary>
    /// <param name="declarations">数値側の宣言。</param>
    [Theory]
    [InlineData("01 N PIC S99 USAGE COMP. 01 X1 PIC XX.")]
    [InlineData("01 N PIC S99 SIGN IS LEADING SEPARATE. 01 X1 PIC XX.")]
    [InlineData("01 N PIC 99. 01 X1 PIC XX JUSTIFIED RIGHT.")]
    public void UnsupportedStorageIsNotCandidate(string declarations)
    {
        using var folder = new TestDirectory();
        var report = Generate(folder, declarations, "MOVE N TO X1");
        Assert.Null(report.A("statements").Single()["moveClassification"]!["candidate"]);
    }

    /// <summary>欠落 COPY の参照を既知の数値型と推測しません。</summary>
    [Fact]
    public void MissingCopyKeepsUnknownLayout()
    {
        using var folder = new TestDirectory();
        var source = Path.Combine(folder.Path, "input.cbl");
        File.WriteAllText(source, "IDENTIFICATION DIVISION. PROGRAM-ID. CLASSIFY. DATA DIVISION. WORKING-STORAGE SECTION. COPY 'missing.cpy'. 01 X1 PIC XX. PROCEDURE DIVISION. MOVE N TO X1 GOBACK.");
        var ir = FrontendAnalyzer.Analyze(source, allowMissingCopy: true);
        Assert.Contains(ir.A("diagnostics"), d => d.S("code").Contains("COPY", StringComparison.Ordinal));
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        SourceBackend.Create(ir, files, new JsonArray());
        var report = InternalStubMetrics.Create(ir, J.Parse(files["reports/stubs.json"]).AsArray());
        var move = report.A("statements").Single(s => s.S("statementKind") == "move");
        Assert.Equal("unknown-layout", move["moveClassification"]!.S("primaryReason"));
        Assert.Null(move["moveClassification"]!["candidate"]);
    }

    /// <summary>分割を跨いだ同一文は重複排除し、別ケースは同じ ID でも残します。</summary>
    [Fact]
    public void SplitReportsPreserveDistinctCasesAndClassification()
    {
        JsonObject Statement(string caseId) => new()
        {
            ["caseId"] = caseId,
            ["programId"] = "P",
            ["statementId"] = "s1",
            ["statementKind"] = "move",
            ["physicalId"] = "same-position",
            ["moveClassification"] = new JsonObject { ["primaryReason"] = "numeric-to-text", ["reasons"] = new JsonArray("numeric-to-text") },
        };
        var first = new JsonObject { ["statements"] = new JsonArray(Statement("a")) };
        var second = new JsonObject { ["statements"] = new JsonArray(Statement("b"), Statement("b")) };
        var merged = InternalStubMetrics.Aggregate([first, second], new JsonObject());
        Assert.Equal(2, merged["moveSummary"]!.I("statements"));
        Assert.Equal(2, merged["moveSummary"]!["primaryReasons"]!.I("numeric-to-text"));
        Assert.Equal(2, merged.A("statements").Count());
        var corpus = new JsonObject { ["expectedCases"] = 2, ["cases"] = new JsonArray(new JsonObject { ["id"] = "a", ["sha256"] = "hash-a", ["internalStubs"] = first.DeepClone() }, new JsonObject { ["id"] = "b", ["sha256"] = "hash-b", ["internalStubs"] = second.DeepClone() }) };
        var evaluated = Corpus.InternalStubs(corpus);
        Assert.Equal(2, evaluated["moveSummary"]!.I("statements"));
        Assert.Equal(2, evaluated["moveSummary"]!["primaryReasonGroups"]!.AsArray().Single()!.I("programs"));
    }

    /// <summary>旧結果の分類欠落をゼロ件や other とみなさず、重複も除きます。</summary>
    [Fact]
    public void LegacyAndShardDuplicatesRemainExplicit()
    {
        var statement = new JsonObject { ["caseId"] = "one", ["programId"] = "P", ["statementId"] = "s1", ["statementKind"] = "move", ["reasonCode"] = "MOVE_TYPE", ["physicalId"] = "place" };
        var summary = InternalStubMetrics.Aggregate([new JsonObject { ["statements"] = new JsonArray(statement.DeepClone(), statement.DeepClone()) }], new JsonObject());
        Assert.Equal(1, summary["moveSummary"]!.I("statements"));
        Assert.Equal(1, summary["moveSummary"]!.I("classificationUnavailable"));
        Assert.Equal(1, summary["moveSummary"]!["primaryReasons"]!.I("classification-unavailable"));
        Assert.Contains("classification-unavailable", InternalStubMetrics.Markdown(summary), StringComparison.Ordinal);
    }

    /// <summary>同じ合成 COBOL を解析・生成してスタブ測定を読む。</summary>
    /// <param name="folder">一時生成先。</param>
    /// <param name="declarations">基本項目の独立宣言。</param>
    /// <param name="move">検査する MOVE 文。</param>
    /// <param name="fullGeneration">生成先レポートと編集可能スタブまで確認する場合は true。</param>
    /// <returns>生成先の位置別測定。</returns>
    private static JsonNode Generate(TestDirectory folder, string declarations, string move, bool fullGeneration = false)
    {
        var source = Path.Combine(folder.Path, "input.cbl");
        File.WriteAllText(source, "IDENTIFICATION DIVISION. PROGRAM-ID. CLASSIFY. DATA DIVISION. WORKING-STORAGE SECTION. " + declarations + " PROCEDURE DIVISION. " + move + " GOBACK.");
        var config = FrontendConfiguration.Parse("{\"testGeneration\":{\"maxAttempts\":1}}");
        var ir = FrontendAnalyzer.Analyze(source, config);
        Assert.DoesNotContain(ir.A("diagnostics"), d => d.S("code") is "PARSE_ERROR" or "EMPTY_SOURCE");
        if (fullGeneration)
        {
            Generator.Generate(ir, Path.Combine(folder.Path, "generated"), "scaffold");
            return Workspace.Read(Path.Combine(folder.Path, "generated", "reports/internal-stub-summary.json"));
        }

        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        SourceBackend.Create(ir, files, new JsonArray());
        return InternalStubMetrics.Create(ir, J.Parse(files["reports/stubs.json"]).AsArray());
    }
}
