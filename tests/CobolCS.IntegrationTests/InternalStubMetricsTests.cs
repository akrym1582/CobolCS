using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CobolCS.Cli;
using CobolCS.Frontend;
using CobolCS.Runtime;
using CobolCS.Tools;
using Xunit;

namespace CobolCS.IntegrationTests;

/// <summary>現在の到達証跡、固定位置件数、取得不足と旧実装保持を検証します。</summary>
public sealed class InternalStubMetricsTests
{
    /// <summary>未実行・実測停止・変更後の古い証跡を分け、native 化後の旧バイトを保持します。</summary>
    /// <returns>実 CLI の停止と再生成。</returns>
    [Fact]
    public async Task CurrentStopsAndUnusedUserImplementationsRemainDistinct()
    {
        using var folder = new TestDirectory();
        var source = Path.Combine(folder.Path, "inspect.cbl");
        var cobol = "IDENTIFICATION DIVISION. PROGRAM-ID. METRICS. DATA DIVISION. WORKING-STORAGE SECTION. 01 T PIC XX VALUE 'AB'. 01 N PIC 99 VALUE 5. PROCEDURE DIVISION. INSPECT T TALLYING N FOR ALL 'A' BEFORE 'Z' DISPLAY N GOBACK.";
        File.WriteAllText(source, cobol);
        var root = Path.Combine(folder.Path, "generated");
        var config = FrontendConfiguration.Parse("{\"testGeneration\":{\"maxAttempts\":1}}");
        Generator.Generate(FrontendAnalyzer.Analyze(source, config), root, "scaffold");
        var metrics = Workspace.Read(Path.Combine(root, "reports/internal-stub-summary.json"));
        Assert.Equal(1, metrics.A("groups").Single().I("unexecutedStatements"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ProcessRunner.Run("dotnet", [.. Workspace.Cli, "run", root, "--program", "METRICS"], folder.Path));
        metrics = Workspace.Read(Path.Combine(root, "reports/internal-stub-summary.json"));
        Assert.Equal(1, metrics.A("groups").Single().I("firstStopPrograms"));
        var stub = Workspace.Read(Path.Combine(root, "reports/stubs.json")).AsArray().Single()!;
        var path = Path.Combine(root, stub.S("file"));
        File.WriteAllText(path, Regex.Replace(File.ReadAllText(path), "throw new NotImplementedException\\([^\\n]+\\);", "return ControlResult.Continue;"));
        Assert.Equal(0, CompletionWorkflow.Prepare(root));
        metrics = Workspace.Read(Path.Combine(root, "reports/internal-stub-summary.json"));
        Assert.Equal(0, metrics.A("groups").Single().I("firstStopPrograms"));
        Assert.Equal(1, metrics.A("groups").Single().I("unexecutedStatements"));
        var bytes = File.ReadAllBytes(path);
        var helper = Path.Combine(root, "UserCode/Completion/legacy.cs");
        File.WriteAllText(helper, "// preserved helper\n");
        File.WriteAllText(source, cobol.Replace(" BEFORE 'Z'", string.Empty, StringComparison.Ordinal));
        Generator.Generate(FrontendAnalyzer.Analyze(source, config), root, "scaffold");
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal("// preserved helper\n", File.ReadAllText(helper));
        Assert.Empty(Workspace.Read(Path.Combine(root, "reports/stubs.json")).AsArray());
        Assert.Contains(Workspace.Read(Path.Combine(root, "reports/contract-changes.json")).AsArray(), c => c!.S("file") == stub.S("file") && c!.S("status") == "unused-preserved");
        _ = await ProcessRunner.Run("dotnet", ["build", "CobolCS.Generated.sln", "-m:1"], root);
        var result = J.Parse(await ProcessRunner.Run("dotnet", [Path.Combine(root, "bin/Debug/net10.0/CobolCS.Generated.dll"), "METRICS"], root));
        Assert.Equal("06", result.A("display").Single().GetValue<string>());
    }

    /// <summary>COPY 物理位置と展開件数を区別し、未取得を成功に含めません。</summary>
    [Fact]
    public void PhysicalCopyCountsAndUnacquiredScopeAreExplicit()
    {
        var statements = new JsonArray();
        foreach (var (program, id) in new[] { ("A", "s1"), ("A", "s2"), ("B", "s1") }) statements.Add(new JsonObject { ["programId"] = program, ["statementId"] = id, ["statementKind"] = "inspectOperations", ["reasonCode"] = "CHAR_INSPECT_CLAUSES", ["executionRole"] = "internal-logic", ["layoutUnknown"] = false, ["physicalId"] = "same-copy-location", ["observation"] = "unexecuted", ["firstStop"] = false });
        var scope = new JsonObject { ["expectedCases"] = 4, ["compilerSourceSha256"] = "fixed", ["lockSha256"] = "fixed", ["cases"] = new JsonArray(new JsonObject { ["id"] = "acquired", ["sha256"] = "fixed", ["internalStubs"] = new JsonObject { ["scope"] = new JsonObject { ["configHash"] = "fixed" }, ["statements"] = statements } }) };
        var summary = Corpus.InternalStubs(scope);
        var group = summary.A("groups").Single();
        Assert.Equal(3, group.I("statements"));
        Assert.Equal(1, group.I("physicalStatements"));
        Assert.Equal(2, group.I("programs"));
        Assert.Equal(0, group.I("firstStopPrograms"));
        Assert.Equal(3, summary["scope"]!.I("unmeasuredOrUnacquired"));
        Assert.DoesNotContain("raw", summary.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("text", summary.ToJsonString(), StringComparison.Ordinal);
    }
}
