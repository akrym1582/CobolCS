using System.Text.Json.Nodes;
using CobolCS.Cli;
using CobolCS.Frontend;
using CobolCS.Runtime;
using CobolCS.Tools;
using Xunit;

namespace CobolCS.IntegrationTests;

/// <summary>許容解析のCLI契約、独立C#ビルド、未知部分の到達失敗と編集保持を検証します。</summary>
public sealed class TolerantGenerationTests
{
    /// <summary>復旧した文・宣言・ヘッダーを含むC#を独立ビルドできます。</summary>
    /// <param name="source">独立したCOBOL入力。</param>
    /// <returns>独立生成とビルドが完了するタスク。</returns>
    [Theory]
    [InlineData("IDENTIFICATION DIVISION. PROGRAM-ID. RECOVER. PROCEDURE DIVISION. MYSTERY 'x' DISPLAY 'after' GOBACK.")]
    [InlineData("IDENTIFICATION DIVISION. PROGRAM-ID. RECOVER. DATA DIVISION. WORKING-STORAGE SECTION. 01 BAD PIC 9 VALUE 3 MYSTERY. 01 GOOD PIC 9 VALUE 7. PROCEDURE DIVISION. MOVE 1 TO BAD DISPLAY GOOD GOBACK.")]
    [InlineData("IDENTIFICATION DIVISION. PROGRAM-ID. RECOVER. ENVIRONMENT DIVISION. CONFIGURATION SECTION. SPECIAL-NAMES. CURRENCY SIGN IS 'XX'. PROCEDURE DIVISION. DISPLAY 'after' GOBACK.")]
    [InlineData("IDENTIFICATION DIVISION. PROGRAM-ID. RECOVER. DATA DIVISION. WORKING-STORAGE SECTION. 01 G. 05 BAD PIC 9 VALUE 3 MYSTERY. 05 SIBLING PIC 9. 01 GOOD PIC 9 VALUE 7. PROCEDURE DIVISION. MOVE 1 TO SIBLING DISPLAY GOOD GOBACK.")]
    [InlineData("IDENTIFICATION DIVISION. PROGRAM-ID. RECOVER. DATA DIVISION. WORKING-STORAGE SECTION. 01 N PIC 9. PROCEDURE DIVISION. IF N = 1 IF MISSING = 1 MOVE 1 TO N ELSE MOVE 2 TO N END-IF ELSE MOVE 3 TO N END-IF DISPLAY N GOBACK.")]
    [InlineData("IDENTIFICATION DIVISION. PROGRAM-ID. RECOVER. DATA DIVISION. FILE SECTION. FD MISSING. PROCEDURE DIVISION. DISPLAY 'after' GOBACK.")]
    public async Task RecoveredSourceBuildsAndKeepsReviewEvidence(string source)
    {
        using var directory = new TestDirectory();
        var input = Path.Combine(directory.Path, "sample.cbl");
        var output = Path.Combine(directory.Path, "generated");
        File.WriteAllText(input, source);
        var config = Path.Combine(directory.Path, "config.json");
        File.WriteAllText(config, "{\"testGeneration\":{\"maxAttempts\":2}}");
        await ProcessRunner.Run("dotnet", [.. Workspace.Cli, input, "-o", output, "--config", config]);
        var manifest = Generator.ReadManifest(output);
        Assert.Equal("tolerant", manifest.S("syntaxRecovery"));
        Assert.True(manifest["judgments"]!.B("generated"));
        Assert.True(manifest["judgments"]!.B("needsReview"));
        Assert.Equal("tolerant", J.Parse(File.ReadAllText(Path.Combine(output, "reports/ir.json"))).S("syntaxRecovery"));
        await ProcessRunner.Run("dotnet", [.. Workspace.Cli, "build", output]);
        Generator.Verify(output, Generator.ReadManifest(output));
    }

    /// <summary>未知文へ到達しない既知経路は独立期待値と一致し、到達時は明示的に失敗します。</summary>
    /// <returns>既知経路と未知到達の確認が完了するタスク。</returns>
    [Fact]
    public async Task KnownPathRunsAndUnknownPathThrows()
    {
        using var directory = new TestDirectory();
        var input = Path.Combine(directory.Path, "sample.cbl");
        var output = Path.Combine(directory.Path, "generated");
        File.WriteAllText(input, "IDENTIFICATION DIVISION. PROGRAM-ID. RECOVER. DATA DIVISION. WORKING-STORAGE SECTION. 01 N PIC 9 VALUE 0. PROCEDURE DIVISION. IF N = 1 MYSTERY N END-IF ADD 2 TO N DISPLAY N GOBACK.");
        var ir = FrontendAnalyzer.Analyze(input, FrontendConfiguration.Parse("{\"testGeneration\":{\"maxAttempts\":2}}"), syntaxRecovery: SyntaxRecoveryPolicy.Tolerant);
        Generator.Generate(ir, output, "scaffold", "source");
        await ProcessRunner.Run("dotnet", [.. Workspace.Cli, "build", output]);
        var assembly = Path.Combine(output, "bin/Debug/net10.0/CobolCS.Generated.dll");
        var observed = J.Parse(await ProcessRunner.Run("dotnet", [assembly, "RECOVER"]));
        Assert.Equal("2", observed["fields"]!.S("N"));
        Assert.Equal("goback", observed.S("signal"));
        Assert.Equal("2", Assert.Single(observed.A("display")).GetValue<string>());
        var fixture = Path.Combine(directory.Path, "fixture.json");
        File.WriteAllText(fixture, "{\"fields\":{\"N\":\"1\"}}");
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => ProcessRunner.Run("dotnet", [assembly, "RECOVER", fixture]));
        Assert.Contains("NotImplementedException", failure.Message, StringComparison.Ordinal);
        Assert.Contains("MYSTERY", File.ReadAllText(Directory.GetFiles(Path.Combine(output, "Programs"), "*Program.g.cs").Single()), StringComparison.Ordinal);
        var summary = J.Parse(File.ReadAllText(Path.Combine(output, "reports/test-generation", Generator.ReadManifest(output)["judgments"]!["testGeneration"]!.S("runId"), "summary.json")));
        Assert.Equal("tolerant", summary["config"]!.S("syntaxRecovery"));
    }

    /// <summary>strictは復旧済みprogramを拒否し、再生成は既存スタブの編集を保持します。</summary>
    [Fact]
    public void StrictRefusesRecoveredProgramAndRegenerationPreservesStubs()
    {
        using var directory = new TestDirectory();
        var input = Path.Combine(directory.Path, "sample.cbl");
        File.WriteAllText(input, "IDENTIFICATION DIVISION. PROGRAM-ID. RECOVER. PROCEDURE DIVISION. MYSTERY 'x' DISPLAY 'after' GOBACK.");
        var ir = FrontendAnalyzer.Analyze(input, FrontendConfiguration.Parse("{\"testGeneration\":{\"maxAttempts\":2}}"), syntaxRecovery: SyntaxRecoveryPolicy.Tolerant);
        var strictOutput = Path.Combine(directory.Path, "strict");
        var refused = Generator.Generate(ir, strictOutput, "strict", "source");
        Assert.True(refused["judgments"]!.B("strictRefused"));
        Assert.False(refused["judgments"]!.B("generated"));
        Assert.True(File.Exists(Path.Combine(strictOutput, "reports/ir.json")));
        var output = Path.Combine(directory.Path, "generated");
        Generator.Generate(ir, output, "scaffold", "source");
        var stub = Directory.GetFiles(Path.Combine(output, "Stubs"), "*.cs", SearchOption.AllDirectories).Single();
        var edited = File.ReadAllText(stub) + "\n// 利用者の補完を保持\n";
        File.WriteAllText(stub, edited);
        Generator.Generate(ir, output, "scaffold", "source");
        Assert.Equal(edited, File.ReadAllText(stub));
        Generator.Verify(output, Generator.ReadManifest(output));
    }

    /// <summary>再生成は既存の解析方針と探索runを引き継ぎ、明示指定でだけ切り替えます。</summary>
    /// <returns>両方針の来歴と再生成の確認が完了するタスク。</returns>
    [Fact]
    public async Task RegenerationKeepsPolicyUnlessExplicitlyChanged()
    {
        using var directory = new TestDirectory();
        var input = Path.Combine(directory.Path, "sample.cbl");
        var output = Path.Combine(directory.Path, "generated");
        var config = Path.Combine(directory.Path, "config.json");
        File.WriteAllText(input, "IDENTIFICATION DIVISION. PROGRAM-ID. RECOVER. PROCEDURE DIVISION. GOBACK.");
        File.WriteAllText(config, "{\"testGeneration\":{\"maxAttempts\":2}}");
        var initial = Generator.Generate(FrontendAnalyzer.Analyze(input, FrontendConfiguration.Parse(File.ReadAllText(config))), output, "scaffold", "source");
        var originalRun = initial["judgments"]!["testGeneration"]!.S("runId");
        await ProcessRunner.Run("dotnet", [.. Workspace.Cli, input, "-o", output, "--config", config]);
        var standard = Generator.ReadManifest(output);
        Assert.Null(standard["syntaxRecovery"]);
        Assert.Equal(originalRun, standard["judgments"]!["testGeneration"]!.S("runId"));
        await ProcessRunner.Run("dotnet", [.. Workspace.Cli, input, "-o", output, "--config", config, "--syntax-recovery", "tolerant"]);
        var tolerant = Generator.ReadManifest(output);
        var tolerantRun = tolerant["judgments"]!["testGeneration"]!.S("runId");
        Assert.Equal("tolerant", tolerant.S("syntaxRecovery"));
        Assert.Equal(originalRun, tolerantRun);
        var evidence = File.ReadAllBytes(Path.Combine(output, "reports/test-generation", originalRun, "summary.json"));
        await ProcessRunner.Run("dotnet", [.. Workspace.Cli, input, "-o", output, "--config", config]);
        Assert.Equal("tolerant", Generator.ReadManifest(output).S("syntaxRecovery"));
        Assert.Equal(tolerantRun, Generator.ReadManifest(output)["judgments"]!["testGeneration"]!.S("runId"));
        Assert.Equal(evidence, File.ReadAllBytes(Path.Combine(output, "reports/test-generation", originalRun, "summary.json")));
    }

    /// <summary>CLIの既定と明示Standard、strict、引数拒否を検証します。</summary>
    /// <returns>CLIの方針と引数検査が完了するタスク。</returns>
    [Fact]
    public async Task CliPoliciesAreExplicitAndValidated()
    {
        using var directory = new TestDirectory();
        var input = Path.Combine(directory.Path, "sample.cbl");
        File.WriteAllText(input, "IDENTIFICATION DIVISION. PROGRAM-ID. RECOVER. PROCEDURE DIVISION. MYSTERY 'x' GOBACK.");
        var analysis = Path.Combine(directory.Path, "analysis");
        await ProcessRunner.Run("dotnet", [.. Workspace.Cli, "analyze", input, "-o", analysis]);
        Assert.Equal("tolerant", Generator.ReadManifest(analysis).S("syntaxRecovery"));
        var standard = Path.Combine(directory.Path, "standard");
        await ProcessRunner.Run("dotnet", [.. Workspace.Cli, "analyze", input, "-o", standard, "--syntax-recovery", "standard"]);
        Assert.Null(Generator.ReadManifest(standard)["syntaxRecovery"]);
        var strict = Path.Combine(directory.Path, "strict");
        await Assert.ThrowsAsync<InvalidOperationException>(() => ProcessRunner.Run("dotnet", [.. Workspace.Cli, input, "-o", strict, "--mode", "strict", "--syntax-recovery", "tolerant"]));
        Assert.True(Generator.ReadManifest(strict)["judgments"]!.B("strictRefused"));
        foreach (var arguments in new[]
        {
            new[] { "--syntax-recovery", "invalid" },
            new[] { "--syntax-recovery", "tolerant", "--backend", "interpreter" },
            new[] { "--syntax-recovery" },
            new[] { "--syntax-recovery", "standard", "--syntax-recovery", "tolerant" },
        })
        {
            var rejected = Path.Combine(directory.Path, "rejected-" + Guid.NewGuid().ToString("N"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => ProcessRunner.Run("dotnet", [.. Workspace.Cli, input, "-o", rejected, .. arguments]));
            Assert.False(Directory.Exists(rejected));
        }
    }
}
