using System.Text.Json.Nodes;
using CobolCS.Cli;
using CobolCS.Frontend;
using CobolCS.Runtime;
using CobolCS.Tools;
using Xunit;

namespace CobolCS.IntegrationTests;

/// <summary>非引用プログラム名の大小文字を同一視し、引用名とプログラム境界の拒否を維持します。</summary>
public sealed class EndProgramNameTests
{
    /// <summary>名前の種類に応じた照合と未対応の連結構造の拒否を検査します。</summary>
    /// <param name="declaration">宣言名。</param>
    /// <param name="ending">終了名と後続トークン。</param>
    /// <param name="valid">対応する単一プログラムかどうか。</param>
    [Theory]
    [InlineData("Mixed", "mIxEd.", true)]
    [InlineData("Mixed", "Mixed.", true)]
    [InlineData("Mixed", "Other.", false)]
    [InlineData("\"Mixed\"", "Mixed.", true)]
    [InlineData("\"Mixed\"", "\"Mixed\".", true)]
    [InlineData("\"Mixed\"", "\"mixed\".", false)]
    [InlineData("'Mixed'", "mixed.", false)]
    [InlineData("Mixed", "Mixed. IDENTIFICATION DIVISION. PROGRAM-ID. SECOND. PROCEDURE DIVISION. GOBACK. END PROGRAM SECOND.", false)]
    [InlineData("Mixed", "Mixed..", false)]
    public void NamesAndBoundaries(string declaration, string ending, bool valid)
    {
        using var folder = new TestDirectory();
        var ir = Analyze(folder.Path, declaration, ending);
        Assert.Equal(valid ? 1 : 0, ir.A("programs").Count());
        Assert.Equal(!valid, ir.A("diagnostics").Any(d => d.S("code") == "PARSE_ERROR"));
        if (valid)
        {
            Assert.Equal("Mixed", ir["programs"]![0]!.S("id"));
        }
    }

    /// <summary>大小文字差だけの単一プログラムを生成・ビルドし、その表示結果を確認します。</summary>
    /// <returns>生成プログラムの実行。</returns>
    [Fact]
    public async Task CaseInsensitiveEndProgramBuildsAndRuns()
    {
        using var folder = new TestDirectory();
        var output = Path.Combine(folder.Path, "generated");
        var ir = Analyze(folder.Path, "Mixed", "mIxEd.");
        Generator.Generate(ir, output, "strict");
        var result = await ProcessRunner.Run("dotnet", [.. Workspace.Cli, "run", output], folder.Path);
        var actual = JsonNode.Parse(result)!;
        Assert.Equal("OK", actual["display"]![0]!.GetValue<string>());
        Assert.Equal("goback", actual.S("signal"));
    }

    private static JsonObject Analyze(string root, string declaration, string ending)
    {
        var input = Path.Combine(root, "mixed.cbl");
        File.WriteAllText(input, $"IDENTIFICATION DIVISION. PROGRAM-ID. {declaration}. PROCEDURE DIVISION. DISPLAY 'OK' GOBACK. END PROGRAM {ending}");
        return FrontendAnalyzer.Analyze(input);
    }
}
