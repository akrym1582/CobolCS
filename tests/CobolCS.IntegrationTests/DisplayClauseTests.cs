using CobolCS.Cli;
using CobolCS.Frontend;
using CobolCS.Runtime;
using CobolCS.Tools;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CobolCS.IntegrationTests;

/// <summary>DISPLAYの改行指定をoperandと誤認せず、文単位の出力capture契約を維持します。</summary>
[TestClass]
public sealed class DisplayClauseTests
{
    /// <summary>NO ADVANCINGの両表記とEND-DISPLAYを保持し、生成物だけで3文の値を観測します。</summary>
    /// <returns>生成・ビルド・実行の完了。</returns>
    [TestMethod]
    public async Task CapturesStatementsWithoutInventingTerminalLineBreaks()
    {
        using var directory = new TestDirectory();
        var source = Path.Combine(directory.Path, "display.cbl");
        File.WriteAllText(source, "IDENTIFICATION DIVISION. PROGRAM-ID. DISPLAYCASE. PROCEDURE DIVISION. DISPLAY 'A ' WITH NO ADVANCING END-DISPLAY DISPLAY 'B' NO ADVANCING DISPLAY 'C' END-DISPLAY GOBACK.");
        File.WriteAllText(Path.Combine(directory.Path, "NuGet.Config"), "<configuration><packageSources><clear /></packageSources></configuration>");
        var ir = FrontendAnalyzer.Analyze(source);
        Assert.IsEmpty(ir.A("diagnostics"));
        var statements = ir.A("programs").Single().A("paragraphs").SelectMany(p => p.A("statements")).Where(s => s.S("kind") == "display").ToArray();
        Assert.HasCount(3, statements);
        Assert.IsTrue(statements[0].B("noAdvancing"));
        Assert.IsTrue(statements[1].B("noAdvancing"));
        Assert.IsNull(statements[2]["noAdvancing"]);
        Assert.IsTrue(statements.All(s => s.A("values").Count() == 1));
        var generated = Path.Combine(directory.Path, "generated");
        await ProcessRunner.Run("dotnet", [.. Workspace.Cli, "generate", source, "--output", generated]);
        Assert.IsEmpty(Workspace.Read(Path.Combine(generated, "reports/stubs.json")).AsArray());
        await ProcessRunner.Run("dotnet", [.. Workspace.Cli, "build", generated]);
        Directory.Delete(Path.Combine(generated, "reports"), true);
        var json = await ProcessRunner.Run("dotnet", [Path.Combine(generated, "bin/Debug/net10.0/CobolCS.Generated.dll"), "DISPLAYCASE"]);
        var output = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        CollectionAssert.AreEqual(new[] { "A ", "B", "C" }, output.A("display").Select(n => n.GetValue<string>()).ToArray());
        Assert.AreEqual("goback", output.S("signal"));
    }

    /// <summary>不完全な句やoperand欠落を正常なDISPLAYへ置換しません。</summary>
    /// <param name="statement">構文エラーを含む文。</param>
    [TestMethod]
    [DataRow("DISPLAY 'A' WITH ADVANCING")]
    [DataRow("DISPLAY 'A' NO")]
    [DataRow("DISPLAY WITH NO ADVANCING")]
    public void IncompleteClausesRemainErrors(string statement)
    {
        using var directory = new TestDirectory();
        var source = Path.Combine(directory.Path, "invalid.cbl");
        File.WriteAllText(source, $"IDENTIFICATION DIVISION. PROGRAM-ID. INVALIDCASE. PROCEDURE DIVISION. {statement}. GOBACK.");
        var ir = FrontendAnalyzer.Analyze(source);
        Assert.IsTrue(ir.A("diagnostics").Any(d => d.S("severity") == "error"));
    }
}
