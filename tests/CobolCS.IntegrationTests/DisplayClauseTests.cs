using CobolCS.Cli;
using CobolCS.Frontend;
using CobolCS.Runtime;
using CobolCS.Tools;
using Xunit;

namespace CobolCS.IntegrationTests;

/// <summary>DISPLAYの改行指定をoperandと誤認せず、文単位の出力capture契約を維持します。</summary>
public sealed class DisplayClauseTests
{
    /// <summary>NO ADVANCINGの両表記とEND-DISPLAYを保持し、生成物だけで3文の値を観測します。</summary>
    /// <returns>生成・ビルド・実行の完了。</returns>
    [Fact]
    public async Task CapturesStatementsWithoutInventingTerminalLineBreaks()
    {
        using var directory = new TestDirectory();
        var source = Path.Combine(directory.Path, "display.cbl");
        File.WriteAllText(source, "IDENTIFICATION DIVISION. PROGRAM-ID. DISPLAYCASE. PROCEDURE DIVISION. DISPLAY 'A ' WITH NO ADVANCING END-DISPLAY DISPLAY 'B' NO ADVANCING DISPLAY 'C' END-DISPLAY GOBACK.");
        File.WriteAllText(Path.Combine(directory.Path, "NuGet.Config"), "<configuration><packageSources><clear /></packageSources></configuration>");
        var ir = FrontendAnalyzer.Analyze(source);
        Assert.Empty(ir.A("diagnostics"));
        var statements = ir.A("programs").Single().A("paragraphs").SelectMany(p => p.A("statements")).Where(s => s.S("kind") == "display").ToArray();
        Assert.Equal(3, statements.Count());
        Assert.True(statements[0].B("noAdvancing"));
        Assert.True(statements[1].B("noAdvancing"));
        Assert.Null(statements[2]["noAdvancing"]);
        Assert.True(statements.All(s => s.A("values").Count() == 1));
        var generated = Path.Combine(directory.Path, "generated");
        await ProcessRunner.Run("dotnet", [.. Workspace.Cli, "generate", source, "--output", generated]);
        Assert.Empty(Workspace.Read(Path.Combine(generated, "reports/stubs.json")).AsArray());
        await ProcessRunner.Run("dotnet", [.. Workspace.Cli, "build", generated]);
        Directory.Delete(Path.Combine(generated, "reports"), true);
        var json = await ProcessRunner.Run("dotnet", [Path.Combine(generated, "bin/Debug/net10.0/CobolCS.Generated.dll"), "DISPLAYCASE"]);
        var output = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        Assert.Equal(new[] { "A ", "B", "C" }, output.A("display").Select(n => n.GetValue<string>()).ToArray());
        Assert.Equal("goback", output.S("signal"));
    }

    /// <summary>不完全な句やoperand欠落を正常なDISPLAYへ置換しません。</summary>
    /// <param name="statement">構文エラーを含む文。</param>
    [Theory]
    [InlineData("DISPLAY 'A' WITH ADVANCING")]
    [InlineData("DISPLAY 'A' NO")]
    [InlineData("DISPLAY WITH NO ADVANCING")]
    public void IncompleteClausesRemainErrors(string statement)
    {
        using var directory = new TestDirectory();
        var source = Path.Combine(directory.Path, "invalid.cbl");
        File.WriteAllText(source, $"IDENTIFICATION DIVISION. PROGRAM-ID. INVALIDCASE. PROCEDURE DIVISION. {statement}. GOBACK.");
        var ir = FrontendAnalyzer.Analyze(source);
        Assert.Contains(ir.A("diagnostics"), d => d.S("severity") == "error");
    }
}
