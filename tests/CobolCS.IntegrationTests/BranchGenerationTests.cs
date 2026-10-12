using System.Text.Json.Nodes;
using CobolCS.Cli;
using CobolCS.Frontend;
using CobolCS.Runtime;
using CobolCS.Tools;
using Xunit;

namespace CobolCS.IntegrationTests;

/// <summary>通常 generate だけで、到達確認から固定 xUnit と採用まで接続する受入試験です。</summary>
public sealed class BranchGenerationTests
{
    /// <summary>入れ子グループの応答境界を探索し、固定再生と採用後の独立実行を確認します。</summary>
    /// <returns>生成および採用した xUnit の実行。</returns>
    [Fact]
    public async Task GroupCallExploresLeafResponsesAndAdopts()
    {
        using var folder = new TestDirectory();
        var output = Path.Combine(folder.Path, "generated");
        var source = Path.Combine(Workspace.Root, "tests/fixtures/branch-generation/group-call.cbl");
        Generator.Generate(FrontendAnalyzer.Analyze(source), output, "scaffold");
        var plan = Workspace.Read(Path.Combine(output, "reports/branch-plans.json"))[0]!;
        var amount = plan.A("symbols").Single(s => s.S("name") == "RESULT-AMOUNT").S("id");
        Assert.Contains(plan.A("responseItems"), n => n.GetValue<string>() == amount);
        var trials = Workspace.Read(Path.Combine(Path.GetDirectoryName(ReportPath(output))!, "trials.json")).AsArray();
        foreach (var value in new[] { "99.99", "100.00", "100.01" })
        {
            Assert.True(trials.Any(t => t!["candidate"]?["responseValues"]?[amount]?.GetValue<string>() == value && t.B("completed")), value);
        }

        Assert.True(Report(output).B("complete"), Report(output).ToJsonString());
        var events = Cases(output).SelectMany(c => c!["branchTrace"]!.A("Events")).Where(e => e.S("BranchId").EndsWith(":if", StringComparison.Ordinal)).Select(e => e.S("Outcome")).ToArray();
        Assert.All(new[] { "true", "false" }, item => Assert.Contains(item, events));
        var reviewPath = Path.Combine(output, "Tests/review.json");
        var review = Workspace.Read(reviewPath);
        foreach (var entry in review.A("cases"))
        {
            var path = entry["files"]!.AsObject().Single(p => p.Key.StartsWith("Tests/Baselines/", StringComparison.Ordinal)).Key;
            var baseline = Workspace.Read(Path.Combine(output, path));
            var response = baseline["script"]![0]!["response"]!["Argument0"]!;
            var value = response.AsObject().Single(p => p.Key.Contains(amount, StringComparison.Ordinal)).Value!;
            var number = new Number(System.Numerics.BigInteger.Parse(value.S("numerator")), System.Numerics.BigInteger.Parse(value.S("denominator")));
            Assert.Equal(number.CompareTo(new Number(100)) >= 0 ? "HIGH" : "LOW", baseline["expected"]!.A("Display").Single().GetValue<string>());
            Assert.True(JsonNode.DeepEquals(baseline["script"]![0]!["request"], baseline["expected"]!["Dependencies"]![0]!["request"]));
            Assert.True(JsonNode.DeepEquals(value, baseline["expected"]!["Snapshot"]!.AsObject().Single(p => p.Key.Contains(amount, StringComparison.Ordinal)).Value));
            baseline["reviewReason"] = "Independent synthetic group specification: returned RESULT-AMOUNT determines HIGH iff >=100; exact response and display checked.";
            File.WriteAllText(Path.Combine(output, path), Generator.Json(baseline));
            entry["reviewed"] = true;
            entry["files"]![path] = Generator.Hash(File.ReadAllBytes(Path.Combine(output, path)));
        }

        File.WriteAllText(reviewPath, Generator.Json(review));
        var adopted = Path.Combine(folder.Path, "adopted");
        await ProcessRunner.Run("dotnet", [.. Workspace.Cli, "adopt", output, "-o", adopted], folder.Path, 180);
        Directory.Delete(output, true);
        await ProcessRunner.Run("dotnet", ["test", Path.Combine(adopted, "Tests/CobolCS.Generated.Tests.csproj"), "-m:1"], folder.Path);
    }

    /// <summary>手動応答のグループ葉・固定表・RETURNINGを許可し、未知・非参照・PIC違反を拒否します。</summary>
    [Fact]
    public void GroupResponseCandidatesValidateContractAndPic()
    {
        using var folder = new TestDirectory();
        var source = Path.Combine(folder.Path, "group.cbl");
        var cobol = """
            IDENTIFICATION DIVISION.
            PROGRAM-ID. GROUPMODES.
            DATA DIVISION.
            WORKING-STORAGE SECTION.
            01 CALL-RESULT.
               05 RESULT-DETAIL.
                  10 RESULT-AMOUNT PIC 9(5)V99.
                     88 IS-HIGH VALUE 100.00.
               05 RESULT-CODE PIC XX OCCURS 2 TIMES.
            01 CONTENT-ONLY PIC XX.
            01 VALUE-ONLY PIC 99.
            01 RETURN-VALUE PIC 99.
            PROCEDURE DIVISION.
                CALL 'SYNTHETIC' USING BY REFERENCE CALL-RESULT
                    BY CONTENT CONTENT-ONLY BY VALUE VALUE-ONLY
                    RETURNING RETURN-VALUE END-CALL
                IF RESULT-AMOUNT >= 100.00 DISPLAY 'HIGH'
                ELSE DISPLAY 'LOW' END-IF
                GOBACK.
            """;
        File.WriteAllText(source, cobol);
        var ir = FrontendAnalyzer.Analyze(source);
        var symbols = ir["programs"]![0]!.A("symbols").ToDictionary(s => s.S("name"), s => s.S("id"));
        JsonObject Candidate(string item, string value) => new() { ["programId"] = "GROUPMODES", ["input"] = new JsonObject { ["AcceptInputs"] = new JsonArray(), ["Arguments"] = new JsonObject(), ["InitialState"] = new JsonObject() }, ["responseValues"] = new JsonObject { [item] = value } };
        var valid = Candidate(symbols["RESULT-AMOUNT"], "100.00");
        valid["responseValues"]![symbols["RESULT-CODE"]] = "OK";
        valid["responseValues"]![symbols["RETURN-VALUE"]] = "5";
        var invalid = new[] { Candidate("unknown", "0"), Candidate(symbols["CALL-RESULT"], "0"), Candidate(symbols["IS-HIGH"], "0"), Candidate(symbols["CONTENT-ONLY"], "X"), Candidate(symbols["VALUE-ONLY"], "1"), Candidate(symbols["RESULT-AMOUNT"], "100000.00"), Candidate(symbols["RESULT-AMOUNT"], "100.001"), Candidate(symbols["RESULT-AMOUNT"], "-1"), Candidate(symbols["RESULT-CODE"], "LONG") };
        var config = FrontendConfiguration.Parse(new JsonObject { ["testGeneration"] = new JsonObject { ["candidates"] = new JsonArray(new[] { valid }.Concat(invalid).Select(c => (JsonNode?)c).ToArray()) } }.ToJsonString());
        var output = Path.Combine(folder.Path, "generated");
        Generator.Generate(FrontendAnalyzer.Analyze(source, config), output, "scaffold");
        var plan = Workspace.Read(Path.Combine(output, "reports/branch-plans.json"))[0]!;
        Assert.Equal(new[] { symbols["RESULT-AMOUNT"], symbols["RESULT-CODE"], symbols["RETURN-VALUE"] }.OrderBy(item => item, StringComparer.Ordinal), plan.A("responseItems").Select(n => n.GetValue<string>()).ToArray().OrderBy(item => item, StringComparer.Ordinal));
        var trials = Workspace.Read(Path.Combine(Path.GetDirectoryName(ReportPath(output))!, "trials.json")).AsArray();
        Assert.True(trials[0]!.B("completed"), trials[0]!.ToJsonString());
        foreach (var candidate in invalid)
        {
            var trial = trials.Single(t => JsonNode.DeepEquals(t!["candidate"], candidate))!;
            Assert.False(trial.B("completed"));
            Assert.StartsWith("invalid-candidate:", trial.S("reason"));
        }

        var baselinePath = Workspace.Read(Path.Combine(output, "Tests/review.json")).A("cases").First()["files"]!.AsObject().Single(p => p.Key.StartsWith("Tests/Baselines/", StringComparison.Ordinal)).Key;
        var baseline = Workspace.Read(Path.Combine(output, baselinePath));
        Assert.Equal("HIGH", baseline["expected"]!.A("Display").Single().GetValue<string>());
        var response = baseline["script"]![0]!["response"]!;
        var table = response["Argument0"]!.AsObject().Single(p => p.Key.Contains(symbols["RESULT-CODE"], StringComparison.Ordinal)).Value!.AsArray();
        Assert.Equal(new[] { "OK", "OK" }, table.Select(n => n!.GetValue<string>()).ToArray());
        Assert.Equal("5", response["Returning"]!.S("numerator"));
        Assert.True(Report(output).B("replayedMSTest"), Report(output).ToJsonString());
    }

    /// <summary>精密境界・逆算・複合条件・両制御経路と正常／異常外部応答を実測します。</summary>
    /// <returns>保存された全ケースの xUnit 実行。</returns>
    [Fact]
    public async Task OrdinaryGenerateExploresAndReplaysSyntheticPrograms()
    {
        using var folder = new TestDirectory();
        var output = Path.Combine(folder.Path, "generated");
        var config = Path.Combine(folder.Path, "config.json");
        File.WriteAllText(config, "{\"maxSteps\":2000,\"timeoutMs\":1000,\"testGeneration\":{\"maxAttempts\":60,\"timeoutMs\":120000}}");
        await ProcessRunner.Run("dotnet", [.. Workspace.Cli, "generate", Path.Combine(Workspace.Root, "tests/fixtures/branch-generation"), "--config", config, "-o", output], folder.Path, 180);
        var report = Report(output);
        Assert.True(report.B("replayedMSTest"), report.ToJsonString());
        var programs = report.A("programs").ToDictionary(p => p.S("programId"));
        foreach (var id in new[] { "BOUNDARY", "AFFINE", "DECIMALBOUND", "CONTROLCASE", "FLOWCASE", "BRANCHCALL", "BRANCHFILE", "BRANCHSQL", "SIZECASE", "OVERFLOWCASE" })
        {
            Assert.True(programs[id].I("reachedBranches") == programs[id].I("measurableBranches"), id + programs[id].ToJsonString());
            Assert.True(programs[id].I("generatedCases") > 0);
        }

        Assert.Contains(Cases(output).OfType<JsonObject>().Where(c => c.S("programId") == "EXITLOOPCASE").SelectMany(c => c["branchTrace"]!.A("Events")), e => e.S("Outcome") == "one");
        Assert.Contains(programs["LIMITCASE"].A("exclusions"), r => r.GetValue<string>().Contains("StepLimitException", StringComparison.Ordinal));
        Assert.Equal(0, programs["BLOCKEDCASE"].I("generatedCases"));
        Assert.Contains(programs["BLOCKEDCASE"].A("exclusions"), r => r.GetValue<string>().Contains("blocked-internal-stub", StringComparison.Ordinal));
        Assert.Contains(programs["OVERWRITECASE"].A("unresolved"), b => b.S("reason").Contains("overwrite", StringComparison.Ordinal));
        Assert.True(programs.Values.All(p => p.I("attempts") <= 60));
        var trials = J.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(ReportPath(output))!, "trials.json")));
        void InputsInclude(string program, params string[] values)
        {
            var inputs = trials.AsArray().OfType<JsonObject>().Where(t => t.S("programId") == program).SelectMany(t => t["candidate"]!["input"]!.A("AcceptInputs")).Select(n => n.GetValue<string>()).ToArray();
            foreach (var value in values)
            {
                Assert.Contains(value, inputs);
            }
        }

        InputsInclude("BOUNDARY", "9999", "10000", "10001");
        InputsInclude("AFFINE", "1099", "1100", "1101");
        InputsInclude("DECIMALBOUND", "12.33", "12.34", "12.35");
        var review = Workspace.Read(Path.Combine(output, "Tests/review.json"));
        Assert.True(review.A("cases").All(c => !c.B("reviewed") && c.S("baselineKind") == "csharp-characterization"));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(output, "Tests/Templates"), "*.txt"));
        var cases = Cases(output).OfType<JsonObject>().ToArray();
        Assert.Contains(cases, c => c.S("programId") == "BRANCHCALL" && c["script"]!.AsArray().Any(s => s!["response"]!.I("Outcome") == 1));
        Assert.Contains(cases, c => c.S("programId") == "BRANCHFILE" && c["script"]!.AsArray().Count == 2 && c["script"]![1]!["response"]!.S("StatusCode") == "10");
        var sql = cases.Where(c => c.S("programId") == "BRANCHSQL").SelectMany(c => c["script"]!.AsArray()).Select(s => s!["response"]!.S("StatusCode")).ToArray();
        Assert.All(new[] { "0", "100", "-911" }, item => Assert.Contains(item, sql));
        await ProcessRunner.Run("dotnet", ["test", Path.Combine(output, "Tests/CobolCS.Generated.Tests.csproj"), "--logger", "trx;LogFileName=acceptance.trx", "--results-directory", Path.Combine(output, "TestResults"), "-m:1"], folder.Path, 180);
        Assert.True(File.Exists(Path.Combine(output, "TestResults/acceptance.trx")));
    }

    /// <summary>同一条件の選択・命名と、編集・レビュー・削除の保持を検証します。</summary>
    [Fact]
    public void DeterministicRunsPreserveUserEvidenceAndDeletions()
    {
        using var folder = new TestDirectory();
        var ir = FrontendAnalyzer.Analyze(Path.Combine(Workspace.Root, "tests/fixtures/branch-generation/boundary.cbl"));
        var first = Path.Combine(folder.Path, "first");
        var second = Path.Combine(folder.Path, "second");
        Generator.Generate(ir, first, "scaffold");
        Generator.Generate(ir, second, "scaffold");
        Assert.True(JsonNode.DeepEquals(Cases(first), Cases(second)));
        Assert.Equal(Report(first).S("runId"), Report(second).S("runId"));
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
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(first, path)));
        Assert.Equal(evidence, File.ReadAllBytes(reviewPath));
        Assert.False(Report(first).B("complete"));
        Assert.Equal(summary, File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(ReportPath(first))!, "attempts/0001/summary.json")));
        File.Delete(Path.Combine(first, path));
        Generator.Generate(ir, first, "scaffold");
        Assert.False(File.Exists(Path.Combine(first, path)));
        Assert.Equal(evidence, File.ReadAllBytes(reviewPath));
    }

    /// <summary>計測の有無で観測値が一致し、短い trace と不足／不一致／余剰 fake は成功証跡になりません。</summary>
    /// <returns>生成公開 API の独立した検証。</returns>
    [Fact]
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
            using Xunit;
            namespace CobolCS.Generated.Tests;
            /// <summary>計測の独立性と固定応答の厳密さを検査します。</summary>
                        public sealed class InstrumentationTests
            {
                /// <summary>観測値を比較し、不足・不一致・余剰応答を拒否します。</summary>
                [Fact]
                public void InstrumentationAndStrictScripts()
                {
                    var baseline = JsonNode.Parse(File.ReadAllText(Path.Combine(Review.Root(), "{{baselinePath}}")))!;
                    var input = baseline["input"]!.ToJsonString();
                    var script = baseline["script"]!.ToJsonString();
                    var off = {{name}}Replay.Run(input, script);
                    var on = {{name}}Replay.Run(input, script, new ExecutionOptions { EnableBranchTrace = true });
                    Assert.Equal(Characterization.Observation(off), Characterization.Observation(on));
                    Assert.Empty(off.BranchTrace.Events);
                    Assert.True(on.BranchTrace.Events.Count > 0);
                    var limited = {{name}}Replay.Run(input, script, new ExecutionOptions { EnableBranchTrace = true, MaxBranchEvents = 1 });
                    Assert.True(limited.BranchTrace.Truncated);
                    Assert.Single(limited.BranchTrace.Events);
                    Assert.ThrowsAny<ExecutionException>(() => {{name}}Replay.Run(input, "[]"));
                    var bad = JsonNode.Parse(script)!.AsArray();
                    bad[0]!["request"]!["Target"] = "WRONG";
                    Assert.Contains("Request mismatch", Assert.ThrowsAny<ExecutionException>(() => {{name}}Replay.Run(input, bad.ToJsonString())).Message);
                    bad = JsonNode.Parse(script)!.AsArray();
                    bad.Add(bad[0]!.DeepClone());
                    Assert.Contains("Unused dependency responses", Assert.ThrowsAny<ExecutionException>(() => {{name}}Replay.Run(input, bad.ToJsonString())).Message);
                    var error = Assert.ThrowsAny<StepLimitException>(() => {{name}}Replay.Run(input, script, new ExecutionOptions { EnableBranchTrace = true, MaxSteps = 1 }));
                    Assert.NotNull(error.Data["BranchTrace"]);
                }
            }
            """;
        File.WriteAllText(Path.Combine(output, "Tests/Cases/Instrumentation.g.cs"), SourceFormattingForTest(code));
        await ProcessRunner.Run("dotnet", ["test", Path.Combine(output, "Tests/CobolCS.Generated.Tests.csproj"), "-m:1"], folder.Path);
    }

    /// <summary>未レビューの採用拒否、レビュー後の独立実行と、計算破壊検出を確認します。</summary>
    /// <returns>採用 stage と保存テストの実行。</returns>
    [Fact]
    public async Task AdoptionRequiresReviewAndSurvivesRefactoring()
    {
        using var folder = new TestDirectory();
        var output = Path.Combine(folder.Path, "generated");
        Generator.Generate(FrontendAnalyzer.Analyze(Path.Combine(Workspace.Root, "tests/fixtures/branch-generation/affine.cbl")), output, "scaffold");
        Assert.Equal(1, Workspace.Command(["dotnet", .. Workspace.Cli, "adopt", output, "-o", Path.Combine(folder.Path, "unreviewed")]).I("exitCode"));
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
            Assert.Equal(expected.Numerator.ToString(), n.S("numerator"));
            Assert.Equal(expected.Denominator.ToString(), n.S("denominator"));
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
        Assert.Equal(1, rejected.I("exitCode"));
        Assert.Contains("duplicate TRX names", rejected.S("stderr"));
        File.Delete(duplicatePath);
        await ProcessRunner.Run("dotnet", [.. Workspace.Cli, "adopt", output, "-o", adopted], folder.Path, 180);
        Directory.Delete(output, true);
        var program = Directory.GetFiles(Path.Combine(adopted, "Programs"), "*Program.g.cs").Single();
        var text = File.ReadAllText(program);
        var expression = "CobolValue.Numeric(state.CINPUT_002DAMOUNT_";
        var line = text.Split('\n').Single(l => l.Contains("var value =", StringComparison.Ordinal) && l.Contains(" - ", StringComparison.Ordinal));
        Assert.True(line.Contains(expression, StringComparison.Ordinal));
        var rhs = line[(line.IndexOf("=", StringComparison.Ordinal) + 1)..].Trim().TrimEnd(';');
        var refactored = text.Replace(rhs, "Calculate()", StringComparison.Ordinal);
        var position = refactored.LastIndexOf('}');
        refactored = refactored.Insert(position, "\n    private Number Calculate() => " + rhs + ";\n");
        File.WriteAllText(program, refactored);
        await ProcessRunner.Run("dotnet", ["test", Path.Combine(adopted, "Tests/CobolCS.Generated.Tests.csproj"), "-m:1"], folder.Path);
        File.WriteAllText(program, refactored.Replace(" - CobolValue.Numeric(", " + CobolValue.Numeric(", StringComparison.Ordinal));
        var result = Workspace.Command(["dotnet", "test", Path.Combine(adopted, "Tests/CobolCS.Generated.Tests.csproj"), "-m:1"]);
        Assert.Equal(1, result.I("exitCode"));
        Assert.Contains("Assert.Equal() Failure", result.S("stdout"));
    }

    /// <summary>不完全な trace は正常テストへ昇格しません。</summary>
    [Fact]
    public void TraceLimitProducesAnIncompleteReport()
    {
        using var folder = new TestDirectory();
        var config = FrontendConfiguration.Parse("{\"testGeneration\":{\"maxTraceEvents\":1,\"maxAttempts\":20}}");
        var output = Path.Combine(folder.Path, "generated");
        Generator.Generate(FrontendAnalyzer.Analyze(Path.Combine(Workspace.Root, "tests/fixtures/branch-generation/control.cbl"), config), output, "scaffold");
        Assert.Equal(0, Report(output).I("generatedCases"));
        Assert.False(Report(output).B("complete"));
        Assert.Contains(Report(output).A("programs").Single().A("exclusions"), r => r.GetValue<string>() == "trace-limit");
        var run = Report(output).S("runId");
        Generator.Generate(FrontendAnalyzer.Analyze(Path.Combine(Workspace.Root, "tests/fixtures/branch-generation/control.cbl"), config), output, "scaffold");
        Assert.Equal(run, Report(output).S("runId"));
        Assert.False(Report(output).B("complete"));
        Assert.Empty(Cases(output));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(ReportPath(output))!, "attempts/0001/summary.json")));
    }

    /// <summary>設定変更の再探索は同じ固定ケースを重複追加せず、確認済みとして再利用します。</summary>
    [Fact]
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
        Assert.Equal(review, File.ReadAllBytes(Path.Combine(output, "Tests/review.json")));
        Assert.Equal(count, Directory.GetFiles(Path.Combine(output, "Tests/Cases"), "*.g.cs", SearchOption.AllDirectories).Count());
        var reports = Directory.GetFiles(Path.Combine(output, "reports/test-generation"), "summary.json", SearchOption.AllDirectories).Select(Workspace.Read).ToArray();
        Assert.Equal(2, reports.Count());
        Assert.Contains(reports, r => r.I("generatedCases") == 0 && r.I("reusedCases") == count && r.B("replayedMSTest"));
        Generator.Generate(FrontendAnalyzer.Analyze(source, config), output, "scaffold");
        Assert.Equal(review, File.ReadAllBytes(Path.Combine(output, "Tests/review.json")));
        Assert.False(Directory.GetDirectories(Path.Combine(output, "reports/test-generation"), "attempts", SearchOption.AllDirectories).Any());
        var reviewPath = Path.Combine(output, "Tests/review.json");
        var changed = Workspace.Read(reviewPath);
        var entry = changed.A("cases").First();
        var path = entry["files"]!.AsObject().Single(p => p.Key.StartsWith("Tests/Cases/", StringComparison.Ordinal)).Key;
        File.AppendAllText(Path.Combine(output, path), "\n// UNREVIEWED CROSS-RUN EDIT\n");
        entry["files"]![path] = Generator.Hash(File.ReadAllBytes(Path.Combine(output, path)));
        File.WriteAllText(reviewPath, Generator.Json(changed));
        var before = File.ReadAllBytes(reviewPath);
        Generator.Generate(FrontendAnalyzer.Analyze(source, config), output, "scaffold");
        var retry = Directory.GetDirectories(Path.Combine(output, "reports/test-generation")).Select(d => Workspace.Read(Path.Combine(d, "summary.json"))).Single(r => !r.B("complete"));
        Assert.Equal(count, retry.I("preservedCases"));
        Assert.Equal(0, retry.I("generatedCases"));
        Assert.Equal(before, File.ReadAllBytes(reviewPath));
    }

    /// <summary>SDK が探索プロセスから見えなくても通常生成と未完了理由を残します。</summary>
    /// <returns>SDK 不足を再現した通常 generate。</returns>
    [Fact]
    public async Task MissingSdkLeavesOrdinarySourceAndIncompleteReport()
    {
        using var folder = new TestDirectory();
        var output = Path.Combine(folder.Path, "generated");
        var configuration = new DirectoryInfo(Path.GetDirectoryName(Workspace.Cli.Single())!).Parent!.Name;
        var appHost = Path.Combine(Workspace.Root, "src/CobolCS.Cli/bin", configuration, "net10.0/cobolcs");
        await ProcessRunner.Run("/bin/sh", ["-c", "PATH=/nonexistent exec \"$1\" generate \"$2\" -o \"$3\"", "sdk-probe", appHost, Path.Combine(Workspace.Root, "tests/fixtures/branch-generation/boundary.cbl"), output], folder.Path);
        Assert.True(File.Exists(Path.Combine(output, "CobolCS.Generated.csproj")));
        Assert.True(File.Exists(Path.Combine(output, "Tests/CobolCS.Generated.Tests.csproj")));
        Assert.False(Report(output).B("complete"));
        Assert.Equal(0, Report(output).I("generatedCases"));
        Assert.NotEmpty(Report(output).S("blocked"));
        Assert.False(Directory.Exists(Path.Combine(output, "bin")));
        var runId = Report(output).S("runId");
        await ProcessRunner.Run("dotnet", [.. Workspace.Cli, "generate", Path.Combine(Workspace.Root, "tests/fixtures/branch-generation/boundary.cbl"), "-o", output], folder.Path);
        Assert.Equal(runId, Report(output).S("runId"));
        Assert.True(Report(output).B("complete"), Report(output).ToJsonString());
        Assert.Equal(2, Cases(output).Count);
        await ProcessRunner.Run("dotnet", ["test", Path.Combine(output, "Tests/CobolCS.Generated.Tests.csproj"), "-m:1"], folder.Path);
    }

    /// <summary>最初のケース保存後の制御されたプロセス失敗から同一 run を再試行します。</summary>
    /// <returns>保持ケースと追加ケースの固定 xUnit。</returns>
    [Fact]
    public async Task PartialRunRetriesWithoutCaseCollisions()
    {
        using var folder = new TestDirectory();
        var output = Path.Combine(folder.Path, "generated");
        var source = Path.Combine(Workspace.Root, "tests/fixtures/branch-generation/boundary.cbl");
        await GenerateWithProcessBoundary(folder.Path, source, output, "after-first");
        var previous = Report(output).DeepClone();
        Assert.False(previous.B("complete"));
        Assert.Single(Cases(output));
        var entry = Workspace.Read(Path.Combine(output, "Tests/review.json")).A("cases").Single();
        var original = entry["files"]!.AsObject().ToDictionary(p => p.Key, p => File.ReadAllBytes(Path.Combine(output, p.Key)));
        var oldCases = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(ReportPath(output))!, "cases.json"));
        await ProcessRunner.Run("dotnet", [.. Workspace.Cli, "generate", source, "-o", output], folder.Path);
        Assert.Equal(previous.S("runId"), Report(output).S("runId"));
        Assert.True(Report(output).B("complete"), Report(output).ToJsonString());
        Assert.Equal(2, Cases(output).Count);
        Assert.Equal(1, Report(output).I("generatedCases"));
        Assert.Equal(1, Report(output).I("reusedCases"));
        Assert.Equal(1, Report(output).I("preservedCases"));
        Assert.Equal(oldCases, File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(ReportPath(output))!, "attempts/0001/cases.json")));
        foreach (var pair in original)
        {
            Assert.Equal(pair.Value, File.ReadAllBytes(Path.Combine(output, pair.Key)));
        }

        await ProcessRunner.Run("dotnet", ["test", Path.Combine(output, "Tests/CobolCS.Generated.Tests.csproj"), "-m:1"], folder.Path);
        var evidence = File.ReadAllBytes(ReportPath(output));
        Generator.Generate(FrontendAnalyzer.Analyze(source), output, "scaffold");
        Assert.Equal(evidence, File.ReadAllBytes(ReportPath(output)));
        Assert.Equal(2, Cases(output).Count);
    }

    /// <summary>部分保存ケースを編集した後の新規追加でも、元の来歴 hash を維持します。</summary>
    /// <returns>新規ケースの成功と、hashを取り直した未レビューケースの拒否。</returns>
    [Fact]
    public async Task RetryAppendsProvenanceWithoutRebindingEditedCases()
    {
        using var folder = new TestDirectory();
        var output = Path.Combine(folder.Path, "generated");
        var source = Path.Combine(Workspace.Root, "tests/fixtures/branch-generation/boundary.cbl");
        await GenerateWithProcessBoundary(folder.Path, source, output, "after-first");
        var reviewPath = Path.Combine(output, "Tests/review.json");
        var review = Workspace.Read(reviewPath);
        var entry = review.A("cases").Single();
        var code = entry["files"]!.AsObject().Single(p => p.Key.StartsWith("Tests/Cases/", StringComparison.Ordinal)).Key;
        var originalHash = entry["files"]![code]!.GetValue<string>();
        File.AppendAllText(Path.Combine(output, code), "\n// UNREVIEWED EDIT\n");
        var edited = File.ReadAllBytes(Path.Combine(output, code));
        var currentHash = Generator.Hash(edited);
        entry["files"]![code] = currentHash;
        File.WriteAllText(reviewPath, Generator.Json(review));
        var run = Report(output).S("runId");
        await ProcessRunner.Run("dotnet", [.. Workspace.Cli, "generate", source, "-o", output], folder.Path);
        Assert.Equal(run, Report(output).S("runId"));
        Assert.Equal(2, Cases(output).Count);
        Assert.Equal(1, Report(output).I("generatedCases"));
        Assert.False(Report(output).B("complete"));
        Assert.Equal(edited, File.ReadAllBytes(Path.Combine(output, code)));
        var provenance = File.ReadAllText(Path.Combine(output, "Testing/Characterization_" + run + ".g.cs"));
        Assert.Contains(originalHash, provenance);
        Assert.False(provenance.Contains(currentHash, StringComparison.Ordinal));
        var entries = Workspace.Read(reviewPath).A("cases").ToArray();
        Assert.True(JsonNode.DeepEquals(entry, entries.Single(c => c.S("caseId") == entry.S("caseId"))));
        var added = entries.Single(c => c.S("caseId") != entry.S("caseId"));
        await ProcessRunner.Run("dotnet", ["test", Path.Combine(output, "Tests/CobolCS.Generated.Tests.csproj"), "--filter", "FullyQualifiedName~" + added.S("caseId"), "-m:1"], folder.Path);
        var rejected = Workspace.Command(["dotnet", "test", Path.Combine(output, "Tests/CobolCS.Generated.Tests.csproj"), "--filter", "FullyQualifiedName~" + entry.S("caseId"), "-m:1"]);
        Assert.Equal(1, rejected.I("exitCode"));
        Assert.Contains("Characterization provenance mismatch", rejected.S("stdout"));
    }

    /// <summary>未完了出力の編集・レビュー・削除と元の来歴を再試行で保持します。</summary>
    /// <returns>編集済み baseline の来歴拒否と並行編集時の原子的保護。</returns>
    [Fact]
    public async Task IncompleteRetriesPreserveUserBytesAndOriginalProvenance()
    {
        using var folder = new TestDirectory();
        var output = Path.Combine(folder.Path, "generated");
        var source = Path.Combine(Workspace.Root, "tests/fixtures/branch-generation/control.cbl");
        await GenerateWithProcessBoundary(folder.Path, source, output, "xunit");
        Assert.False(Report(output).B("complete"));
        Assert.True(Cases(output).Count >= 3);
        var run = Report(output).S("runId");
        var reviewPath = Path.Combine(output, "Tests/review.json");
        var review = Workspace.Read(reviewPath);
        var entries = review.A("cases").ToArray();
        string Bound(JsonNode entry, string prefix) => entry["files"]!.AsObject().Single(p => p.Key.StartsWith(prefix, StringComparison.Ordinal)).Key;
        var editedCase = Bound(entries[0], "Tests/Cases/");
        File.AppendAllText(Path.Combine(output, editedCase), "\n// USER EDIT\n");
        var editedBaseline = Bound(entries[1], "Tests/Baselines/");
        var baseline = Workspace.Read(Path.Combine(output, editedBaseline));
        baseline["reviewReason"] = "UNREVIEWED: user changed evidence";
        File.WriteAllText(Path.Combine(output, editedBaseline), Generator.Json(baseline));
        entries[1]["files"]![editedBaseline] = Generator.Hash(File.ReadAllBytes(Path.Combine(output, editedBaseline)));
        entries[0]["reviewed"] = true;
        entries[0]["reviewReason"] = "Retain explicit human review annotation";
        var deleted = Bound(entries[2], "Tests/Cases/");
        File.Delete(Path.Combine(output, deleted));
        File.WriteAllText(reviewPath, Generator.Json(review));
        var originalFiles = new[] { editedCase, editedBaseline, "Tests/review.json", "Testing/Characterization_" + run + ".g.cs" }.ToDictionary(p => p, p => File.ReadAllBytes(Path.Combine(output, p)));
        await ProcessRunner.Run("dotnet", [.. Workspace.Cli, "generate", source, "-o", output], folder.Path);
        Assert.Equal(run, Report(output).S("runId"));
        Assert.False(Report(output).B("complete"));
        Assert.Equal(entries.Length, Cases(output).Count);
        Assert.False(File.Exists(Path.Combine(output, deleted)));
        foreach (var pair in originalFiles)
        {
            Assert.True(pair.Value.SequenceEqual(File.ReadAllBytes(Path.Combine(output, pair.Key))), pair.Key);
        }

        var rejected = Workspace.Command(["dotnet", "test", Path.Combine(output, "Tests/CobolCS.Generated.Tests.csproj"), "--filter", "FullyQualifiedName~" + entries[1].S("caseId"), "-m:1"]);
        Assert.Equal(1, rejected.I("exitCode"));
        Assert.Contains("Characterization provenance mismatch", rejected.S("stdout"));
        var manifest = Workspace.Read(Path.Combine(output, "manifest.json"));
        Assert.Equal(Report(output).B("complete"), manifest["judgments"]!["testGeneration"]!.B("complete"));
        var savedSummary = File.ReadAllBytes(ReportPath(output));
        var concurrent = await GenerateWithProcessBoundary(folder.Path, source, output, "concurrent", editedCase);
        Assert.Equal(2, concurrent);
        Assert.Equal(savedSummary, File.ReadAllBytes(ReportPath(output)));
        Assert.EndsWith("// CONCURRENT EDIT\n", File.ReadAllText(Path.Combine(output, editedCase)));
        var beforeStageMutation = Directory.GetFiles(output, "*", SearchOption.AllDirectories).ToDictionary(p => p, p => Generator.Hash(File.ReadAllBytes(p)));
        Assert.Equal(2, await GenerateWithProcessBoundary(folder.Path, source, output, "stage-edit"));
        Assert.Equal(beforeStageMutation.ToArray().OrderBy(item => item.Key, StringComparer.Ordinal), Directory.GetFiles(output, "*", SearchOption.AllDirectories).ToDictionary(p => p, p => Generator.Hash(File.ReadAllBytes(p))).ToArray().OrderBy(item => item.Key, StringComparer.Ordinal));
        var artifact = Path.Combine(Path.GetDirectoryName(ReportPath(output))!, "trials.json");
        File.AppendAllText(artifact, " ");
        Dictionary<string, string> Inventory() => Directory.GetFiles(output, "*", SearchOption.AllDirectories).ToDictionary(p => p, p => Generator.Hash(File.ReadAllBytes(p)));
        var inventory = Inventory();
        Assert.ThrowsAny<IOException>(() => Generator.Generate(FrontendAnalyzer.Analyze(source), output, "scaffold"));
        Assert.Equal(inventory.ToArray().OrderBy(item => item.Key, StringComparer.Ordinal), Inventory().ToArray().OrderBy(item => item.Key, StringComparer.Ordinal));
        File.Delete(artifact);
        inventory = Inventory();
        Assert.ThrowsAny<IOException>(() => Generator.Generate(FrontendAnalyzer.Analyze(source), output, "scaffold"));
        Assert.Equal(inventory.ToArray().OrderBy(item => item.Key, StringComparer.Ordinal), Inventory().ToArray().OrderBy(item => item.Key, StringComparer.Ordinal));
    }

    private static async Task<int> GenerateWithProcessBoundary(string folder, string source, string output, string mode, string? concurrentFile = null)
    {
        var wrapper = Path.Combine(folder, "process-boundary");
        Directory.CreateDirectory(wrapper);
        var dotnet = Workspace.Command(["/bin/sh", "-c", "command -v dotnet"]).S("stdout").Trim();
        string QuoteShell(string value) => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
        var script = "#!/bin/sh\n";
        if (mode == "after-first")
        {
            script += "case \"$1\" in */CobolCS.Generated.dll) stage=${PWD%.branch-probe}; if test -d \"$stage/Tests/Cases/BranchGenerated\" && test -n \"$(find \"$stage/Tests/Cases/BranchGenerated\" -name '*.g.cs' -print -quit)\"; then echo 'controlled failure after first saved case' >&2; exit 1; fi;; esac\n";
        }

        if (mode == "stage-edit")
        {
            script += "if test \"$1\" = test; then echo ' ' >> \"$PWD/Tests/review.json\"; echo 'controlled stage mutation' >&2; exit 1; fi\n";
        }
        else if (mode == "concurrent")
        {
            script += "if test \"$1\" = build; then echo '// CONCURRENT EDIT' >> " + QuoteShell(Path.Combine(output, concurrentFile!)) + "; echo 'controlled concurrent edit' >&2; exit 1; fi\n";
        }
        else
        {
            script += "if test \"$1\" = test; then echo 'controlled xUnit boundary failure' >&2; exit 1; fi\n";
        }

        script += "exec " + QuoteShell(dotnet) + " \"$@\"\n";
        var executable = Path.Combine(wrapper, "dotnet");
        File.WriteAllText(executable, script);
        await ProcessRunner.Run("/bin/chmod", ["+x", executable], folder);
        var configuration = new DirectoryInfo(Path.GetDirectoryName(Workspace.Cli.Single())!).Parent!.Name;
        var appHost = Path.Combine(Workspace.Root, "src/CobolCS.Cli/bin", configuration, "net10.0/cobolcs");
        var result = Workspace.Command(["/bin/sh", "-c", "PATH=\"$1:$PATH\" exec \"$2\" generate \"$3\" -o \"$4\"", "process-boundary", wrapper, appHost, source, output], 180);
        if (mode is not ("concurrent" or "stage-edit"))
        {
            Assert.True(result.I("exitCode") == 0, result.ToJsonString());
        }

        return result.I("exitCode");
    }

    private static string SourceFormattingForTest(string text) => text + "\n";

    private static string ReportPath(string output) => Directory.GetDirectories(Path.Combine(output, "reports/test-generation")).Select(d => Path.Combine(d, "summary.json")).Single();

    private static JsonNode Report(string output) => Workspace.Read(ReportPath(output));

    private static JsonArray Cases(string output) => J.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(ReportPath(output))!, "cases.json"))).AsArray();
}
