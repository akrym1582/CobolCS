using System.Text.Json.Nodes;
using CobolCS.Cli;
using CobolCS.Frontend;
using CobolCS.Runtime;
using CobolCS.Tools;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CobolCS.IntegrationTests;

/// <summary>通常 generate だけで、到達確認から固定 MSTest と採用まで接続する受入試験です。</summary>
[TestClass]
public sealed class BranchGenerationTests
{
    /// <summary>精密境界・逆算・複合条件・両制御経路と正常／異常外部応答を実測します。</summary>
    /// <returns>保存された全ケースの MSTest 実行。</returns>
    [TestMethod]
    public async Task OrdinaryGenerateExploresAndReplaysSyntheticPrograms()
    {
        using var folder = new TestDirectory();
        var output = Path.Combine(folder.Path, "generated");
        var config = Path.Combine(folder.Path, "config.json");
        File.WriteAllText(config, "{\"maxSteps\":2000,\"timeoutMs\":1000,\"testGeneration\":{\"maxAttempts\":60,\"timeoutMs\":120000}}");
        await ProcessRunner.Run("dotnet", [.. Workspace.Cli, "generate", Path.Combine(Workspace.Root, "tests/fixtures/branch-generation"), "--config", config, "-o", output], folder.Path, 180);
        var report = Report(output);
        Assert.IsTrue(report.B("replayedMSTest"), report.ToJsonString());
        var programs = report.A("programs").ToDictionary(p => p.S("programId"));
        foreach (var id in new[] { "BOUNDARY", "AFFINE", "DECIMALBOUND", "CONTROLCASE", "FLOWCASE", "BRANCHCALL", "BRANCHFILE", "BRANCHSQL", "SIZECASE", "OVERFLOWCASE" })
        {
            Assert.AreEqual(programs[id].I("measurableBranches"), programs[id].I("reachedBranches"), id + programs[id].ToJsonString());
            Assert.IsGreaterThan(0, programs[id].I("generatedCases"));
        }

        Assert.IsTrue(Cases(output).OfType<JsonObject>().Where(c => c.S("programId") == "EXITLOOPCASE").SelectMany(c => c["branchTrace"]!.A("Events")).Any(e => e.S("Outcome") == "one"));
        Assert.IsTrue(programs["LIMITCASE"].A("exclusions").Any(r => r.GetValue<string>().Contains("StepLimitException", StringComparison.Ordinal)));
        Assert.AreEqual(0, programs["BLOCKEDCASE"].I("generatedCases"));
        Assert.IsTrue(programs["BLOCKEDCASE"].A("exclusions").Any(r => r.GetValue<string>().Contains("blocked-internal-stub", StringComparison.Ordinal)));
        Assert.IsTrue(programs["OVERWRITECASE"].A("unresolved").Any(b => b.S("reason").Contains("overwrite", StringComparison.Ordinal)));
        Assert.IsTrue(programs.Values.All(p => p.I("attempts") <= 60));
        var trials = J.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(ReportPath(output))!, "trials.json")));
        void InputsInclude(string program, params string[] values)
        {
            var inputs = trials.AsArray().OfType<JsonObject>().Where(t => t.S("programId") == program).SelectMany(t => t["candidate"]!["input"]!.A("AcceptInputs")).Select(n => n.GetValue<string>()).ToArray();
            foreach (var value in values)
            {
                CollectionAssert.Contains(inputs, value, program);
            }
        }

        InputsInclude("BOUNDARY", "9999", "10000", "10001");
        InputsInclude("AFFINE", "1099", "1100", "1101");
        InputsInclude("DECIMALBOUND", "12.33", "12.34", "12.35");
        var review = Workspace.Read(Path.Combine(output, "Tests/review.json"));
        Assert.IsTrue(review.A("cases").All(c => !c.B("reviewed") && c.S("baselineKind") == "csharp-characterization"));
        Assert.IsNotEmpty(Directory.GetFiles(Path.Combine(output, "Tests/Templates"), "*.txt"));
        var cases = Cases(output).OfType<JsonObject>().ToArray();
        Assert.IsTrue(cases.Any(c => c.S("programId") == "BRANCHCALL" && c["script"]!.AsArray().Any(s => s!["response"]!.I("Outcome") == 1)));
        Assert.IsTrue(cases.Any(c => c.S("programId") == "BRANCHFILE" && c["script"]!.AsArray().Count == 2 && c["script"]![1]!["response"]!.S("StatusCode") == "10"));
        var sql = cases.Where(c => c.S("programId") == "BRANCHSQL").SelectMany(c => c["script"]!.AsArray()).Select(s => s!["response"]!.S("StatusCode")).ToArray();
        CollectionAssert.IsSubsetOf(new[] { "0", "100", "-911" }, sql);
        await ProcessRunner.Run("dotnet", ["test", Path.Combine(output, "Tests/CobolCS.Generated.Tests.csproj"), "--logger", "trx;LogFileName=acceptance.trx", "--results-directory", Path.Combine(output, "TestResults"), "-m:1"], folder.Path, 180);
        Assert.IsTrue(File.Exists(Path.Combine(output, "TestResults/acceptance.trx")));
    }

    /// <summary>同一条件の選択・命名と、編集・レビュー・削除の保持を検証します。</summary>
    [TestMethod]
    public void DeterministicRunsPreserveUserEvidenceAndDeletions()
    {
        using var folder = new TestDirectory();
        var ir = FrontendAnalyzer.Analyze(Path.Combine(Workspace.Root, "tests/fixtures/branch-generation/boundary.cbl"));
        var first = Path.Combine(folder.Path, "first");
        var second = Path.Combine(folder.Path, "second");
        Generator.Generate(ir, first, "scaffold");
        Generator.Generate(ir, second, "scaffold");
        Assert.IsTrue(JsonNode.DeepEquals(Cases(first), Cases(second)));
        Assert.AreEqual(Report(first).S("runId"), Report(second).S("runId"));
        var reviewPath = Path.Combine(first, "Tests/review.json");
        var review = Workspace.Read(reviewPath);
        review["cases"]![0]!["reviewed"] = true;
        File.WriteAllText(reviewPath, Generator.Json(review));
        var entry = review.A("cases").First();
        var path = entry["files"]!.AsObject().Single(p => p.Key.StartsWith("Tests/Cases/", StringComparison.Ordinal)).Key;
        File.AppendAllText(Path.Combine(first, path), "\n// 利用者の編集を保持する。\n");
        var bytes = File.ReadAllBytes(Path.Combine(first, path));
        var evidence = File.ReadAllBytes(reviewPath);
        var summary = File.ReadAllBytes(ReportPath(first));
        Generator.Generate(ir, first, "scaffold");
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(Path.Combine(first, path)));
        CollectionAssert.AreEqual(evidence, File.ReadAllBytes(reviewPath));
        CollectionAssert.AreEqual(summary, File.ReadAllBytes(ReportPath(first)));
        File.Delete(Path.Combine(first, path));
        Generator.Generate(ir, first, "scaffold");
        Assert.IsFalse(File.Exists(Path.Combine(first, path)));
        CollectionAssert.AreEqual(evidence, File.ReadAllBytes(reviewPath));
    }

    /// <summary>計測の有無で観測値が一致し、短い trace と不足／不一致／余剰 fake は成功証跡になりません。</summary>
    /// <returns>生成公開 API の独立した検証。</returns>
    [TestMethod]
    public async Task InstrumentationDoesNotChangeBehaviorAndReplayIsStrict()
    {
        using var folder = new TestDirectory();
        var output = Path.Combine(folder.Path, "generated");
        Generator.Generate(FrontendAnalyzer.Analyze(Path.Combine(Workspace.Root, "tests/fixtures/branch-generation/call.cbl")), output, "scaffold");
        var plan = Workspace.Read(Path.Combine(output, "reports/branch-plans.json"))[0]!;
        var name = plan.S("name");
        var entry = Workspace.Read(Path.Combine(output, "Tests/review.json"))["cases"]![0]!;
        var baselinePath = entry["files"]!.AsObject().Single(p => p.Key.StartsWith("Tests/Baselines/", StringComparison.Ordinal)).Key;
        var code = $$"""
            // <auto-generated/>
            using System.Text.Json.Nodes;
            using Microsoft.VisualStudio.TestTools.UnitTesting;
            namespace CobolCS.Generated.Tests;
            /// <summary>計測の独立性と固定応答の厳密さを検査します。</summary>
            [TestClass]
            public sealed class InstrumentationTests
            {
                /// <summary>観測値を比較し、不足・不一致・余剰応答を拒否します。</summary>
                [TestMethod]
                public void InstrumentationAndStrictScripts()
                {
                    var baseline = JsonNode.Parse(File.ReadAllText(Path.Combine(Review.Root(), "{{baselinePath}}")))!;
                    var input = baseline["input"]!.ToJsonString();
                    var script = baseline["script"]!.ToJsonString();
                    var off = {{name}}Replay.Run(input, script);
                    var on = {{name}}Replay.Run(input, script, new ExecutionOptions { EnableBranchTrace = true });
                    Assert.AreEqual(Characterization.Observation(off), Characterization.Observation(on));
                    Assert.HasCount(0, off.BranchTrace.Events);
                    Assert.IsGreaterThan(0, on.BranchTrace.Events.Count);
                    var limited = {{name}}Replay.Run(input, script, new ExecutionOptions { EnableBranchTrace = true, MaxBranchEvents = 1 });
                    Assert.IsTrue(limited.BranchTrace.Truncated);
                    Assert.HasCount(1, limited.BranchTrace.Events);
                    Assert.Throws<ExecutionException>(() => {{name}}Replay.Run(input, "[]"));
                    var bad = JsonNode.Parse(script)!.AsArray();
                    bad[0]!["request"]!["Target"] = "WRONG";
                    StringAssert.Contains(Assert.Throws<ExecutionException>(() => {{name}}Replay.Run(input, bad.ToJsonString())).Message, "Request mismatch");
                    bad = JsonNode.Parse(script)!.AsArray();
                    bad.Add(bad[0]!.DeepClone());
                    StringAssert.Contains(Assert.Throws<ExecutionException>(() => {{name}}Replay.Run(input, bad.ToJsonString())).Message, "Unused dependency responses");
                    var error = Assert.Throws<StepLimitException>(() => {{name}}Replay.Run(input, script, new ExecutionOptions { EnableBranchTrace = true, MaxSteps = 1 }));
                    Assert.IsNotNull(error.Data["BranchTrace"]);
                }
            }
            """;
        File.WriteAllText(Path.Combine(output, "Tests/Cases/Instrumentation.g.cs"), SourceFormattingForTest(code));
        await ProcessRunner.Run("dotnet", ["test", Path.Combine(output, "Tests/CobolCS.Generated.Tests.csproj"), "-m:1"], folder.Path);
    }

    /// <summary>未レビューの採用拒否、レビュー後の独立実行と、計算破壊検出を確認します。</summary>
    /// <returns>採用 stage と保存テストの実行。</returns>
    [TestMethod]
    public async Task AdoptionRequiresReviewAndSurvivesRefactoring()
    {
        using var folder = new TestDirectory();
        var output = Path.Combine(folder.Path, "generated");
        Generator.Generate(FrontendAnalyzer.Analyze(Path.Combine(Workspace.Root, "tests/fixtures/branch-generation/affine.cbl")), output, "scaffold");
        Assert.AreEqual(1, Workspace.Command(["dotnet", .. Workspace.Cli, "adopt", output, "-o", Path.Combine(folder.Path, "unreviewed")]).I("exitCode"));
        var reviewPath = Path.Combine(output, "Tests/review.json");
        var review = Workspace.Read(reviewPath);
        foreach (var entry in review.A("cases"))
        {
            var path = entry["files"]!.AsObject().Single(p => p.Key.StartsWith("Tests/Baselines/", StringComparison.Ordinal)).Key;
            var baseline = Workspace.Read(Path.Combine(output, path));
            var input = Number.Parse(baseline["input"]!.A("AcceptInputs").Single().GetValue<string>());
            var expected = input - new Number(100);
            var fields = baseline["expected"]!["Snapshot"]!.AsObject();
            var n = fields.Single(p => p.Key.Contains("_f000002", StringComparison.Ordinal)).Value!;
            Assert.AreEqual(expected.Numerator.ToString(), n.S("numerator"));
            Assert.AreEqual(expected.Denominator.ToString(), n.S("denominator"));
            baseline["reviewReason"] = "Independent synthetic specification: N equals accepted INPUT-AMOUNT minus 100; all saved values checked with exact Number arithmetic.";
            File.WriteAllText(Path.Combine(output, path), Generator.Json(baseline));
            entry["reviewed"] = true;
            entry["files"]![path] = Generator.Hash(File.ReadAllBytes(Path.Combine(output, path)));
        }

        File.WriteAllText(reviewPath, Generator.Json(review));
        var adopted = Path.Combine(folder.Path, "adopted");
        var originalCase = review.A("cases").First();
        var casePath = originalCase["files"]!.AsObject().Single(p => p.Key.StartsWith("Tests/Cases/", StringComparison.Ordinal)).Key;
        var duplicatePath = Path.Combine(output, "Tests/Cases/Collision.g.cs");
        File.WriteAllText(duplicatePath, File.ReadAllText(Path.Combine(output, casePath)).Replace(originalCase.S("caseId") + "Tests", "CollisionTests", StringComparison.Ordinal));
        var rejected = Workspace.Command(["dotnet", .. Workspace.Cli, "adopt", output, "-o", Path.Combine(folder.Path, "collision")], 180);
        Assert.AreEqual(1, rejected.I("exitCode"));
        StringAssert.Contains(rejected.S("stderr"), "duplicate TRX names");
        File.Delete(duplicatePath);
        await ProcessRunner.Run("dotnet", [.. Workspace.Cli, "adopt", output, "-o", adopted], folder.Path, 180);
        Directory.Delete(output, true);
        var program = Directory.GetFiles(Path.Combine(adopted, "Programs"), "*Program.g.cs").Single();
        var text = File.ReadAllText(program);
        var expression = "CobolValue.Numeric(state.CINPUT_002DAMOUNT_";
        var line = text.Split('\n').Single(l => l.Contains("var value =", StringComparison.Ordinal) && l.Contains(" - ", StringComparison.Ordinal));
        Assert.IsTrue(line.Contains(expression, StringComparison.Ordinal));
        var rhs = line[(line.IndexOf("=", StringComparison.Ordinal) + 1)..].Trim().TrimEnd(';');
        var refactored = text.Replace(rhs, "Calculate()", StringComparison.Ordinal);
        var position = refactored.LastIndexOf('}');
        refactored = refactored.Insert(position, "\n    private Number Calculate() => " + rhs + ";\n");
        File.WriteAllText(program, refactored);
        await ProcessRunner.Run("dotnet", ["test", Path.Combine(adopted, "Tests/CobolCS.Generated.Tests.csproj"), "-m:1"], folder.Path);
        File.WriteAllText(program, refactored.Replace(" - CobolValue.Numeric(", " + CobolValue.Numeric(", StringComparison.Ordinal));
        var result = Workspace.Command(["dotnet", "test", Path.Combine(adopted, "Tests/CobolCS.Generated.Tests.csproj"), "-m:1"]);
        Assert.AreEqual(1, result.I("exitCode"));
        StringAssert.Contains(result.S("stdout"), "Assert.AreEqual failed");
    }

    /// <summary>不完全な trace は正常テストへ昇格しません。</summary>
    [TestMethod]
    public void TraceLimitProducesAnIncompleteReport()
    {
        using var folder = new TestDirectory();
        var config = FrontendConfiguration.Parse("{\"testGeneration\":{\"maxTraceEvents\":1,\"maxAttempts\":20}}");
        var output = Path.Combine(folder.Path, "generated");
        Generator.Generate(FrontendAnalyzer.Analyze(Path.Combine(Workspace.Root, "tests/fixtures/branch-generation/control.cbl"), config), output, "scaffold");
        Assert.AreEqual(0, Report(output).I("generatedCases"));
        Assert.IsFalse(Report(output).B("complete"));
        Assert.IsTrue(Report(output).A("programs").Single().A("exclusions").Any(r => r.GetValue<string>() == "trace-limit"));
    }

    /// <summary>設定変更の再探索は同じ固定ケースを重複追加せず、確認済みとして再利用します。</summary>
    [TestMethod]
    public void DifferentSeedReusesEquivalentCasesWithoutChangingReview()
    {
        using var folder = new TestDirectory();
        var source = Path.Combine(Workspace.Root, "tests/fixtures/branch-generation/boundary.cbl");
        var output = Path.Combine(folder.Path, "generated");
        Generator.Generate(FrontendAnalyzer.Analyze(source), output, "scaffold");
        var review = File.ReadAllBytes(Path.Combine(output, "Tests/review.json"));
        var count = Directory.GetFiles(Path.Combine(output, "Tests/Cases"), "*.g.cs", SearchOption.AllDirectories).Length;
        var config = FrontendConfiguration.Parse("{\"testGeneration\":{\"seed\":2}}");
        Generator.Generate(FrontendAnalyzer.Analyze(source, config), output, "scaffold");
        CollectionAssert.AreEqual(review, File.ReadAllBytes(Path.Combine(output, "Tests/review.json")));
        Assert.HasCount(count, Directory.GetFiles(Path.Combine(output, "Tests/Cases"), "*.g.cs", SearchOption.AllDirectories));
        var reports = Directory.GetFiles(Path.Combine(output, "reports/test-generation"), "summary.json", SearchOption.AllDirectories).Select(Workspace.Read).ToArray();
        Assert.HasCount(2, reports);
        Assert.IsTrue(reports.Any(r => r.I("generatedCases") == 0 && r.I("reusedCases") == count && r.B("replayedMSTest")));
    }

    /// <summary>SDK が探索プロセスから見えなくても通常生成と未完了理由を残します。</summary>
    /// <returns>SDK 不足を再現した通常 generate。</returns>
    [TestMethod]
    public async Task MissingSdkLeavesOrdinarySourceAndIncompleteReport()
    {
        using var folder = new TestDirectory();
        var output = Path.Combine(folder.Path, "generated");
        var configuration = new DirectoryInfo(Path.GetDirectoryName(Workspace.Cli.Single())!).Parent!.Name;
        var appHost = Path.Combine(Workspace.Root, "src/CobolCS.Cli/bin", configuration, "net10.0/cobolcs");
        await ProcessRunner.Run("/bin/sh", ["-c", "PATH=/nonexistent exec \"$1\" generate \"$2\" -o \"$3\"", "sdk-probe", appHost, Path.Combine(Workspace.Root, "tests/fixtures/branch-generation/boundary.cbl"), output], folder.Path);
        Assert.IsTrue(File.Exists(Path.Combine(output, "CobolCS.Generated.csproj")));
        Assert.IsTrue(File.Exists(Path.Combine(output, "Tests/CobolCS.Generated.Tests.csproj")));
        Assert.IsFalse(Report(output).B("complete"));
        Assert.AreEqual(0, Report(output).I("generatedCases"));
        Assert.IsNotEmpty(Report(output).S("blocked"));
        Assert.IsFalse(Directory.Exists(Path.Combine(output, "bin")));
    }

    private static string SourceFormattingForTest(string text) => text + "\n";

    private static string ReportPath(string output) => Directory.GetFiles(Path.Combine(output, "reports/test-generation"), "summary.json", SearchOption.AllDirectories).Single();

    private static JsonNode Report(string output) => Workspace.Read(ReportPath(output));

    private static JsonArray Cases(string output) => J.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(ReportPath(output))!, "cases.json"))).AsArray();
}
