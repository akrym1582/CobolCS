using System.Text.Json.Nodes;
using CobolCS.Cli;
using CobolCS.Frontend;
using CobolCS.Runtime;
using CobolCS.Tools;
using Xunit;

namespace CobolCS.IntegrationTests;

/// <summary>未対応文でもIRで確定する更新先を比較候補から落としません。</summary>
public sealed class UnsupportedCandidateTests
{
    /// <summary>UNSTRINGの受信・補助出力とINSPECTの書込み方向を保持します。</summary>
    /// <returns>比較雛形を含む生成プロジェクトのビルド。</returns>
    [Fact]
    public async Task StringOperationsKeepExplicitOutputs()
    {
        using var folder = new TestDirectory();
        var output = Generate(folder.Path, "DATA DIVISION. WORKING-STORAGE SECTION. 01 INPUT-TEXT PIC X(10). 01 EDITED PIC X(10). 01 DEST PIC X(10). 01 DELIM PIC X. 01 CNT PIC 99. 01 PTR PIC 99. 01 TALLY PIC 99.", "UNSTRING INPUT-TEXT DELIMITED BY ',' INTO DEST DELIMITER IN DELIM COUNT IN CNT WITH POINTER PTR TALLYING IN TALLY END-UNSTRING INSPECT INPUT-TEXT TALLYING CNT FOR ALL 'A' INSPECT EDITED REPLACING ALL 'A' BY 'B' INSPECT EDITED CONVERTING 'A' TO 'B' GOBACK.");
        var candidates = Candidates(output);
        Assert.Equal(new[] { "EDITED", "DEST", "DELIM", "CNT", "PTR", "TALLY" }.OrderBy(item => item, StringComparer.Ordinal), KnownNames(candidates).OrderBy(item => item, StringComparer.Ordinal));
        Assert.Equal(4, candidates.Where(c => c.S("status") == "manual-unknown").ToArray().Count());
        var count = candidates.Single(c => c.S("name") == "CNT");
        Assert.Equal(2, count["updates"]!.AsArray().Count());
        var test = File.ReadAllText(Directory.GetFiles(Path.Combine(output, "Tests/Cases"), "*.cs").Single());
        foreach (var name in KnownNames(candidates))
        {
            Assert.Contains("expected " + name, test);
        }

        await ProcessRunner.Run("dotnet", ["build", Path.Combine(output, "CobolCS.Generated.sln"), "-m:1"], folder.Path);
    }

    /// <summary>WRITE FROMはレコードを、READ INTOはレコードと受信項目を候補にします。</summary>
    /// <returns>比較雛形を含む生成プロジェクトのビルド。</returns>
    [Fact]
    public async Task FileTransfersRespectWriteDirection()
    {
        using var folder = new TestDirectory();
        var output = Generate(folder.Path, "ENVIRONMENT DIVISION. INPUT-OUTPUT SECTION. FILE-CONTROL. SELECT F ASSIGN TO 'F' FILE STATUS ST. DATA DIVISION. FILE SECTION. FD F. 01 REC. 05 TEXT-VALUE PIC X(3). WORKING-STORAGE SECTION. 01 ST PIC XX. 01 INPUT-TEXT PIC X(3). 01 DEST PIC X(3).", "WRITE REC FROM INPUT-TEXT REWRITE REC FROM INPUT-TEXT READ F INTO DEST GOBACK.");
        var candidates = Candidates(output);
        Assert.Equal(new[] { "TEXT-VALUE OF REC", "ST", "DEST" }.OrderBy(item => item, StringComparer.Ordinal), KnownNames(candidates).OrderBy(item => item, StringComparer.Ordinal));
        var record = candidates.Single(c => c.S("name") == "TEXT-VALUE OF REC");
        Assert.Equal(3, record["updates"]!.AsArray().Count());
        await ProcessRunner.Run("dotnet", ["build", Path.Combine(output, "CobolCS.Generated.sln"), "-m:1"], folder.Path);
    }

    /// <summary>SORT入出力の転送先とSEARCH索引も未知領域とは別に残します。</summary>
    [Fact]
    public void SortTransfersAndSearchIndicesRemainVisible()
    {
        using var sort = new TestDirectory();
        var sortOutput = Generate(sort.Path, "ENVIRONMENT DIVISION. INPUT-OUTPUT SECTION. FILE-CONTROL. SELECT SORT-FILE ASSIGN TO 'S'. DATA DIVISION. FILE SECTION. SD SORT-FILE. 01 SORT-REC. 05 REC-TEXT PIC X(3). WORKING-STORAGE SECTION. 01 INPUT-TEXT PIC X(3). 01 DEST PIC X(3).", "RELEASE SORT-REC FROM INPUT-TEXT RETURN SORT-FILE INTO DEST AT END CONTINUE END-RETURN GOBACK.");
        var sortCandidates = Candidates(sortOutput);
        Assert.Equal(new[] { "REC-TEXT OF SORT-REC", "DEST" }.OrderBy(item => item, StringComparer.Ordinal), KnownNames(sortCandidates).OrderBy(item => item, StringComparer.Ordinal));
        Assert.Equal(2, sortCandidates.Where(c => c.S("status") == "manual-unknown").ToArray().Count());
        using var search = new TestDirectory();
        var searchOutput = Generate(search.Path, "DATA DIVISION. WORKING-STORAGE SECTION. 01 TAB OCCURS 3 INDEXED BY IDX. 05 ITEM PIC X. 01 POS PIC 9.", "SEARCH TAB VARYING POS AT END CONTINUE WHEN ITEM(IDX) = 'A' CONTINUE END-SEARCH GOBACK.");
        var searchCandidates = Candidates(searchOutput);
        Assert.Equal(new[] { "IDX", "POS" }.OrderBy(item => item, StringComparer.Ordinal), KnownNames(searchCandidates).OrderBy(item => item, StringComparer.Ordinal));
        Assert.Single(searchCandidates.Where(c => c.S("status") == "manual-unknown").ToArray());
    }

    /// <summary>CANCELのプログラム名は入力として扱い、別の代入文の候補は保持します。</summary>
    /// <param name="assignName">CANCEL前に名前のデータ項目へ代入するか。</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancelDoesNotUpdateItsProgramName(bool assignName)
    {
        using var folder = new TestDirectory();
        var body = (assignName ? "MOVE 'OTHER' TO PROGRAM-NAME " : string.Empty) + "CANCEL PROGRAM-NAME 'SECOND' GOBACK.";
        var output = Generate(folder.Path, "DATA DIVISION. WORKING-STORAGE SECTION. 01 PROGRAM-NAME PIC X(8) VALUE 'REMOTE'.", body);
        var candidates = Candidates(output);
        Assert.Single(candidates.Where(c => c.S("status") == "manual-unknown").ToArray());
        Assert.Equal(assignName ? new[] { "PROGRAM-NAME" } : Array.Empty<string>(), KnownNames(candidates));
        if (assignName)
        {
            var updates = candidates.Single(c => c.S("name") == "PROGRAM-NAME")["updates"]!.AsArray();
            Assert.Single(updates);
            Assert.Equal("move", updates[0]!.S("reason"));
        }
        else
        {
            var test = File.ReadAllText(Directory.GetFiles(Path.Combine(output, "Tests/Cases"), "*.cs").Single());
            Assert.False(test.Contains("expected PROGRAM-NAME", StringComparison.Ordinal));
        }
    }

    private static string Generate(string root, string declarations, string body)
    {
        File.WriteAllText(Path.Combine(root, "NuGet.Config"), "<configuration><packageSources><clear /></packageSources></configuration>");
        var input = Path.Combine(root, "unsupported.cbl");
        File.WriteAllText(input, "IDENTIFICATION DIVISION. PROGRAM-ID. UNSUPPORTED-TARGETS. " + declarations + " PROCEDURE DIVISION. " + body);
        var output = Path.Combine(root, "generated");
        Generator.Generate(FrontendAnalyzer.Analyze(input), output, "scaffold");
        ManualTemplateSupport.Prepare(output);
        return output;
    }

    private static JsonNode[] Candidates(string output) => Workspace.Read(Path.Combine(output, "reports/execution-readiness.json"))["assertionCandidates"]!.AsArray().Select(c => c!).ToArray();

    private static string[] KnownNames(JsonNode[] candidates) => candidates.Where(c => c.S("status") == "generated-template").Select(c => c.S("name")).ToArray();
}
