using System.Text.Json;
using System.Text.Json.Nodes;
using CobolCS.Cli;
using CobolCS.Frontend;
using CobolCS.Runtime;
using CobolCS.Tools;
using Xunit;

namespace CobolCS.IntegrationTests;

/// <summary>深いCOBOL文のIRを製品の出力、読込、生成実行まで渡します。</summary>
public sealed class JsonDepthTests
{
    /// <summary>32段IFと300項ORを独立の表示期待値で検証し、両backendのIR読込を通します。</summary>
    /// <param name="backend">生成backend。</param>
    /// <param name="longCondition">長い左結合OR式を使う場合true。</param>
    [Theory]
    [InlineData("source", false)]
    [InlineData("interpreter", false)]
    [InlineData("source", true)]
    [InlineData("interpreter", true)]
    public void NestedIfAndLongConditionGenerateAndRun(string backend, bool longCondition)
    {
        using var folder = new TestDirectory();
        File.WriteAllText(Path.Combine(folder.Path, "NuGet.Config"), "<configuration><packageSources><clear /></packageSources></configuration>");
        var input = Path.Combine(folder.Path, "deep.cbl");
        var body = longCondition
            ? "IF " + string.Join(" OR ", Enumerable.Range(1, 300).Select(i => "FLAG = " + i)) + "\nDISPLAY 'nested-ok'\nEND-IF\n"
            : string.Concat(Enumerable.Repeat("IF FLAG = 300\n", 32)) + "DISPLAY 'nested-ok'\n" + string.Concat(Enumerable.Repeat("END-IF\n", 32));
        File.WriteAllText(input, "IDENTIFICATION DIVISION.\nPROGRAM-ID. DEEP-IF.\nDATA DIVISION.\nWORKING-STORAGE SECTION.\n01 FLAG PIC 999 VALUE 300.\nPROCEDURE DIVISION.\n" + body + "GOBACK.\n");
        var ir = FrontendAnalyzer.Analyze(input);
        var output = Path.Combine(folder.Path, backend);
        Generator.Generate(ir, output, "strict", backend);
        var saved = J.Parse(File.ReadAllText(Path.Combine(output, "reports/ir.json")));
        Assert.True(JsonNode.DeepEquals(ir, saved));
        Generator.Generate(ir, output, "strict", backend);
        var build = Workspace.Command(["dotnet", "build", Path.Combine(output, "CobolCS.Generated.csproj"), "--configfile", Path.Combine(folder.Path, "NuGet.Config"), "-m:1", "--nologo"]);
        Assert.True(build.I("exitCode") == 0, build.S("stdout") + build.S("stderr"));
        var run = Workspace.Command(["dotnet", Path.Combine(output, "bin/Debug/net10.0/CobolCS.Generated.dll"), "DEEP-IF"]);
        Assert.True(run.I("exitCode") == 0, run.S("stderr"));
        var result = J.Parse(run.S("stdout"));
        Assert.Single(result["display"]!.AsArray());
        Assert.Equal("nested-ok", result["display"]![0]!.GetValue<string>());
    }

    /// <summary>512段を読み書きでき、513段は境界を明示して拒否します。</summary>
    [Fact]
    public void JsonDepthHasAnExplicitFiniteBoundary()
    {
        var accepted = new string('[', 512) + "0" + new string(']', 512);
        var node = J.Parse(accepted);
        Assert.True(JsonNode.DeepEquals(node, J.Parse(Generator.Json(node))));
        var rejected = new string('[', 513) + "0" + new string(']', 513);
        Assert.Contains("512", Assert.ThrowsAny<JsonException>(() => J.Parse(rejected)).Message);
        Assert.Contains("512", Assert.ThrowsAny<InvalidOperationException>(() => Generator.Json(new JsonArray(node))).Message);
    }
}
