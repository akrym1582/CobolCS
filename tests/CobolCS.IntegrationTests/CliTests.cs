using System.Text.Json.Nodes;
using CobolCS.Cli;
using CobolCS.Runtime;
using CobolCS.Tools;
using Xunit;

namespace CobolCS.IntegrationTests;

/// <summary>実CLIを起動し、生成を既定操作とする引数と出力先の契約を検証します。</summary>
public sealed class CliTests
{
    /// <summary>cblだけで通常のC#を生成し、単一プログラムはID指定なしで実行できます。</summary>
    /// <returns>生成・ビルド・実行が完了するタスク。</returns>
    [Fact]
    public async Task DirectInputGeneratesAndRuns()
    {
        using var folder = new TestDirectory();
        var source = Path.Combine(folder.Path, "hello.cbl");
        File.WriteAllText(source, "IDENTIFICATION DIVISION. PROGRAM-ID. HELLO. PROCEDURE DIVISION. DISPLAY 'hello' GOBACK.");
        var result = await ProcessRunner.Run("dotnet", [.. Workspace.Cli, source], folder.Path);
        var output = Path.Combine(folder.Path, "hello-csharp");
        Assert.Contains(output, result);
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(output, "Programs"), "*.cs"));
        Assert.Equal("source", Workspace.Read(Path.Combine(output, "manifest.json")).S("backend"));
        var actual = Path.Combine(folder.Path, "actual.json");
        var run = await ProcessRunner.Run("dotnet", [.. Workspace.Cli, "run", output, "-o", actual], folder.Path);
        Assert.Equal("hello", JsonNode.Parse(run)!["display"]![0]!.GetValue<string>());
        Assert.Equal("hello", Workspace.Read(actual)["display"]![0]!.GetValue<string>());
        await ProcessRunner.Run("dotnet", [.. Workspace.Cli, "generate", source], folder.Path);
    }

    /// <summary>ディレクトリ入力と解析の既定出力先、COPY検索先の短縮指定を検証します。</summary>
    /// <returns>生成と解析が完了するタスク。</returns>
    [Fact]
    public async Task DirectoryAnalysisAndCopyPaths()
    {
        using var folder = new TestDirectory();
        var input = Path.Combine(Workspace.Root, "examples/COPYJP");
        await ProcessRunner.Run("dotnet", [.. Workspace.Cli, "generate", input + Path.DirectorySeparatorChar, "-I", folder.Path, "--copybook-path", Path.Combine(input, "copy")], folder.Path);
        Assert.True(File.Exists(Path.Combine(folder.Path, "COPYJP-csharp/CobolCS.Generated.csproj")));
        await ProcessRunner.Run("dotnet", [.. Workspace.Cli, "analyze", input, "-I", Path.Combine(input, "copy")], folder.Path);
        Assert.True(File.Exists(Path.Combine(folder.Path, "COPYJP-analysis/reports/diagnostics.json")));
        Assert.False(File.Exists(Path.Combine(folder.Path, "COPYJP-analysis/CobolCS.Generated.csproj")));
    }

    /// <summary>複数プログラムのID省略をビルド前に拒否し、選べるIDを案内します。</summary>
    /// <returns>生成と拒否検証が完了するタスク。</returns>
    [Fact]
    public async Task MultipleProgramsRequireSelection()
    {
        using var folder = new TestDirectory();
        var sources = Path.Combine(folder.Path, "sources");
        Directory.CreateDirectory(sources);
        foreach (var name in new[] { "ONE", "TWO" })
        {
            File.WriteAllText(Path.Combine(sources, name + ".cbl"), $"IDENTIFICATION DIVISION. PROGRAM-ID. {name}. PROCEDURE DIVISION. GOBACK.");
        }

        var output = Path.Combine(folder.Path, "generated");
        await ProcessRunner.Run("dotnet", [.. Workspace.Cli, sources, "-o", output], folder.Path);
        var result = Workspace.Command(["dotnet", .. Workspace.Cli, "run", output]);
        Assert.Equal(2, result.I("exitCode"));
        Assert.Contains("Specify --program", result.S("stderr"));
        Assert.Contains("ONE, TWO", result.S("stderr"));
        Assert.False(Directory.Exists(Path.Combine(output, "bin")));
    }

    /// <summary>ヘルプと版表示が入力なしで利用でき、誤ったオプションは生成前に拒否されます。</summary>
    [Fact]
    public void HelpAndInvalidArguments()
    {
        foreach (var command in new[] { "generate", "analyze", "build", "run", "adopt", "compare" })
        {
            var help = Workspace.Command(["dotnet", .. Workspace.Cli, command, "--help"]);
            Assert.Equal(0, help.I("exitCode"));
            Assert.Contains("cobolcs", help.S("stdout"));
        }

        Assert.Equal(0, Workspace.Command(["dotnet", .. Workspace.Cli, "--version"]).I("exitCode"));
        string[][] invalid =
        [
            ["generate"], ["sample.cbl", "-o"], ["sample.cbl", "-o", string.Empty],
            ["sample.cbl", "-o", "out", "--output", "out"],
            ["sample.cbl", "--scaffold", "--scaffold"], ["sample.cbl", "--unknown"],
            ["sample.cbl", "--out", "out"], ["analyze", "sample.cbl", "--scaffold"],
            ["compare", "actual.json"], ["--version", "sample.cbl"],
        ];
        foreach (var args in invalid)
        {
            var result = Workspace.Command(["dotnet", .. Workspace.Cli, .. args]);
            Assert.True(result.I("exitCode") == 2, string.Join(' ', args));
            Assert.False(string.IsNullOrWhiteSpace(result.S("stderr")));
        }
    }

    /// <summary>先頭がハイフンの入力とscaffold短縮指定でもC#を生成できます。</summary>
    /// <returns>生成とオプション競合の検査が完了するタスク。</returns>
    [Fact]
    public async Task EndOfOptionsAndScaffold()
    {
        using var folder = new TestDirectory();
        File.WriteAllText(Path.Combine(folder.Path, "-input.cbl"), "IDENTIFICATION DIVISION. PROGRAM-ID. TEST. PROCEDURE DIVISION. GOBACK.");
        var output = Path.Combine(folder.Path, "generated");
        await ProcessRunner.Run("dotnet", [.. Workspace.Cli, "--scaffold", "-o", output, "--", "-input.cbl"], folder.Path);
        Assert.True(File.Exists(Path.Combine(output, "CobolCS.Generated.csproj")));
        var source = Path.Combine(folder.Path, "-input.cbl");
        foreach (var options in new[] { new[] { "--scaffold", "--mode", "strict" }, new[] { "--mode", "analyze" } })
        {
            Assert.Equal(2, Workspace.Command(["dotnet", .. Workspace.Cli, source, .. options]).I("exitCode"));
        }
    }
}
