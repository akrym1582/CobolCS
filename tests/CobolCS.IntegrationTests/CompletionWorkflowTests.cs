using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CobolCS.Cli;
using CobolCS.Frontend;
using CobolCS.Runtime;
using CobolCS.Tools;
using Xunit;

namespace CobolCS.IntegrationTests;

/// <summary>内部補完から再探索、業務期待値、採用後の改名までを実際の .NET 実行で確認します。</summary>
public sealed class CompletionWorkflowTests
{
    /// <summary>補完・fake 分離・来歴・削除保持・原子的失敗と、業務期待値の独立性を一連で検証します。</summary>
    /// <returns>生成・探索・採用・採用先だけのテスト。</returns>
    [Fact]
    public async Task CompletionToBusinessAdoptionAndRefactoring()
    {
        using var folder = new TestDirectory();
        var output = GeneratePayment(folder.Path);
        var initial = Summary(output);
        Assert.False(initial.B("complete"));
        Assert.Equal(0, initial.I("selectedCases"));
        var stub = Stubs(output).Single(s => s.S("executionRole") == "internal-logic");
        Assert.Equal("inspectOperations", stub["statement"]!.S("kind"));
        var hand = Path.Combine(output, "UserCode/Completion/work-items.json");
        var item = Worklist(output).A("items").Single(i => i.S("category") == "internal-implementation");
        File.WriteAllText(hand, Generator.Json(new JsonObject { ["schemaVersion"] = 1, ["items"] = new JsonArray(new JsonObject { ["itemId"] = item.S("itemId"), ["status"] = "done", ["notes"] = "Manual state is not runtime evidence" }) }));
        Assert.Equal(0, CompletionWorkflow.Prepare(output));
        item = Worklist(output).A("items").Single(i => i.S("itemId") == item.S("itemId"));
        Assert.Equal("done", item.S("manualStatus"));
        Assert.Equal("not-observed", item.S("observedStatus"));
        CompleteInspect(output);
        foreach (var external in Stubs(output).Where(s => s.S("executionRole") == "external-adapter"))
        {
            var path = Path.Combine(output, external.S("file"));
            File.WriteAllText(path, File.ReadAllText(path).Replace("throw new NotImplementedException", "throw new InvalidOperationException", StringComparison.Ordinal));
        }

        Assert.True(CompletionWorkflow.Explore(output, "PAYMENT", null) == 0, Summary(output).ToJsonString());
        var first = Summary(output);
        Assert.Equal("completed-internal", first.S("implementationMode"));
        Assert.True(first["userSourceHashes"]!.AsObject().ContainsKey("UserCode/Completion/CountLogic.cs"));
        Assert.True(first.A("programs").Single().I("reachedBranches") > 0);
        item = Worklist(output).A("items").Single(i => i.S("category") == "internal-implementation");
        Assert.Equal("reached-completed", item.S("observedStatus"));
        var baselineFiles = Directory.GetFiles(Path.Combine(output, "Tests/Baselines/BranchGenerated"), "*.json", SearchOption.AllDirectories).ToDictionary(p => Path.GetRelativePath(output, p), File.ReadAllBytes);
        var removed = baselineFiles.First();
        File.Delete(Path.Combine(output, removed.Key));
        Assert.Equal(0, CompletionWorkflow.Explore(output, "PAYMENT", null));
        Assert.Equal(first.S("runId"), Summary(output).S("runId"));
        Assert.False(File.Exists(Path.Combine(output, removed.Key)));
        File.WriteAllBytes(Path.Combine(output, removed.Key), removed.Value);
        var helper = Path.Combine(output, "UserCode/Completion/CountLogic.cs");
        File.AppendAllText(helper, "\n// Explicit helper change creates new provenance.\n");
        Assert.Equal(0, CompletionWorkflow.Prepare(output));
        Assert.Equal("stale", Worklist(output).A("items").Single(i => i.S("category") == "internal-implementation").S("observedStatus"));
        Assert.Equal(0, CompletionWorkflow.Explore(output, "PAYMENT", null));
        Assert.NotEqual(first.S("runId"), Summary(output).S("runId"));
        foreach (var pair in baselineFiles)
        {
            Assert.Equal(pair.Value, File.ReadAllBytes(Path.Combine(output, pair.Key)));
        }

        var invalid = Path.Combine(output, "UserCode/Completion/Invalid.cs");
        File.WriteAllText(invalid, "invalid C# source");
        var before = Generator.Inventory(output);
        Assert.Throws<InvalidOperationException>(() => CompletionWorkflow.Explore(output, null, null));
        Assert.Equal(before.Where(p => !CompletionWorkflow.IsRecord(p.Key)).ToArray(), Generator.Inventory(output).Where(p => !CompletionWorkflow.IsRecord(p.Key)).ToArray());
        var buildFailure = Workspace.Read(Directory.GetFiles(Path.Combine(output, "reports/executions"), "*.json").OrderDescending(StringComparer.Ordinal).First());
        Assert.Equal("failed", buildFailure.S("status"));
        Assert.NotNull(buildFailure["blocked"]);
        File.Delete(invalid);
        ConfigureBusiness(output);
        Assert.Equal(0, CompletionWorkflow.Prepare(output));
        await TestBusiness(output);
        ReviewAll(output);
        var adopted = Path.Combine(folder.Path, "adopted");
        var helperBytes = File.ReadAllBytes(helper);
        File.AppendAllText(helper, "\n// Review must be repeated.\n");
        Assert.Equal(1, await Adoption.Run(output, adopted));
        File.WriteAllBytes(helper, helperBytes);
        var businessCase = Path.Combine(output, "Tests/BusinessCases/payment.json");
        var caseBytes = File.ReadAllBytes(businessCase);
        File.Delete(businessCase);
        Assert.Equal(1, await Adoption.Run(output, adopted));
        File.WriteAllBytes(businessCase, caseBytes);
        Assert.Equal(0, await Adoption.Run(output, adopted));
        var businessFiles = Generator.Inventory(adopted).Where(p => p.Key.StartsWith("Tests/BusinessCases/", StringComparison.Ordinal) || p.Key.StartsWith("Tests/BusinessContracts/", StringComparison.Ordinal)).ToArray();
        var plan = Workspace.Read(Path.Combine(output, "reports/branch-plans.json"))[0]!;
        var feeMember = plan.A("symbols").Single(s => s.S("name") == "FEE").S("member");
        foreach (var path in Directory.GetFiles(adopted, "*.cs", SearchOption.AllDirectories).Where(p => !p.Contains("/Tests/", StringComparison.Ordinal)))
        {
            File.WriteAllText(path, File.ReadAllText(path).Replace(feeMember, "RefactoredFee", StringComparison.Ordinal));
        }

        Directory.Delete(output, true);
        await TestBusiness(adopted);
        var serviceAdapterPath = Path.Combine(adopted, "UserCode/Business/PaymentAdapter.cs");
        var previousAdapter = File.ReadAllText(serviceAdapterPath);
        var servicePath = Path.Combine(adopted, "UserCode/Business/RefactoredPaymentService.cs");
        File.WriteAllText(servicePath, File.ReadAllText(Path.Combine(Workspace.Root, "tests/fixtures/completion/RefactoredPaymentService.cs.txt")));
        File.WriteAllText(serviceAdapterPath, File.ReadAllText(Path.Combine(Workspace.Root, "tests/fixtures/completion/RefactoredAdapter.cs.txt")));
        await TestBusiness(adopted);
        File.WriteAllText(serviceAdapterPath, previousAdapter);
        File.Delete(servicePath);
        Assert.Equal(businessFiles, Generator.Inventory(adopted).Where(p => p.Key.StartsWith("Tests/BusinessCases/", StringComparison.Ordinal) || p.Key.StartsWith("Tests/BusinessContracts/", StringComparison.Ordinal)).ToArray());
        var program = Directory.GetFiles(Path.Combine(adopted, "Programs"), "*.cs").Single();
        var original = File.ReadAllText(program);
        Assert.Contains("0.015", original);
        File.WriteAllText(program, original.Replace("0.015", "0.016", StringComparison.Ordinal));
        await Assert.ThrowsAsync<InvalidOperationException>(() => TestBusiness(adopted));
        File.WriteAllText(program, original);
        var adapterPath = Path.Combine(adopted, "UserCode/Business/PaymentAdapter.cs");
        var adapter = File.ReadAllText(adapterPath);
        File.WriteAllText(adapterPath, adapter.Replace("Argument0: response[\"code\"]!.GetValue<string>()", "Argument0: \"ER\"", StringComparison.Ordinal));
        await Assert.ThrowsAsync<InvalidOperationException>(() => TestBusiness(adopted));
        File.WriteAllText(adapterPath, adapter);
        var adoptedCase = Path.Combine(adopted, "Tests/BusinessCases/payment.json");
        var changedCase = Workspace.Read(adoptedCase);
        changedCase["expected"]!["result"]!["fee"] = "1.53";
        File.WriteAllText(adoptedCase, changedCase.ToJsonString());
        await Assert.ThrowsAsync<InvalidOperationException>(() => TestBusiness(adopted));
    }

    /// <summary>外部未対応と未知 EXEC は編集されても探索用内部処理として実行しません。</summary>
    [Fact]
    public void UnknownAndUnsupportedExternalStayBlocked()
    {
        using var folder = new TestDirectory();
        var path = Path.Combine(folder.Path, "unsafe.cbl");
        File.WriteAllText(path, "IDENTIFICATION DIVISION. PROGRAM-ID. UNSAFE. DATA DIVISION. WORKING-STORAGE SECTION. 01 X PIC 9. PROCEDURE DIVISION. CALL 'SYNTHETIC' USING BY REFERENCE X X END-CALL EXEC CICS RETURN END-EXEC IF X = 1 DISPLAY 'BAD' END-IF GOBACK.");
        var output = Path.Combine(folder.Path, "generated");
        Generator.Generate(FrontendAnalyzer.Analyze(path, Configuration()), output, "scaffold");
        Assert.Contains(Stubs(output), s => s.S("executionRole") == "external-unsupported");
        foreach (var stub in Stubs(output))
        {
            ReplaceThrow(output, stub, "return ControlResult.Continue;");
        }

        Assert.Equal(1, CompletionWorkflow.Explore(output, null, null));
        Assert.Equal(0, Summary(output).I("selectedCases"));
        Assert.Contains("external-unsupported", Summary(output).ToJsonString());
        var config = Path.Combine(folder.Path, "invalid.json");
        File.WriteAllText(config, "{\"profile\":\"portable-test\",\"testGeneration\":{\"seed\":2}}");
        Assert.Throws<ArgumentException>(() => CompletionWorkflow.Explore(output, null, config));
        Assert.Throws<ArgumentException>(() => CompletionWorkflow.Explore(output, "MISSING", null));
    }

    /// <summary>探索中の原本編集を公開直前に検出し、ビルド済み stage を元成果物へ混在させません。</summary>
    /// <returns>競合した探索の終了。</returns>
    [Fact]
    public async Task ConcurrentUserEditRefusesPublication()
    {
        using var folder = new TestDirectory();
        var output = GeneratePayment(folder.Path);
        CompleteInspect(output);
        var manifest = File.ReadAllBytes(Path.Combine(output, "manifest.json"));
        var exploring = Task.Run(() => CompletionWorkflow.Explore(output, null, null));
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!Directory.GetDirectories(folder.Path, "generated.stage-*").Any() && timer.Elapsed < TimeSpan.FromSeconds(30))
        {
            await Task.Delay(20);
        }

        Assert.True(Directory.GetDirectories(folder.Path, "generated.stage-*").Any());
        File.AppendAllText(Path.Combine(output, "UserCode/Completion/CountLogic.cs"), "\n// Concurrent user edit.\n");
        await Assert.ThrowsAsync<IOException>(async () => _ = await exploring);
        Assert.Equal(manifest, File.ReadAllBytes(Path.Combine(output, "manifest.json")));
        Assert.False(Directory.GetDirectories(folder.Path, "generated.stage-*").Any());
    }

    /// <summary>静的状態は各試行で隔離し、無限ループと非決定値は固定期待値へ昇格しません。</summary>
    [Fact]
    public void StaticStateTimeoutAndUnstableReplayAreNotBusinessSuccess()
    {
        using var folder = new TestDirectory();
        var output = GeneratePayment(folder.Path);
        CompleteInspect(output);
        var helper = Path.Combine(output, "UserCode/Completion/CountLogic.cs");
        File.WriteAllText(helper, File.ReadAllText(helper).Replace("public static class CountLogic\n{", "public static class CountLogic\n{\n    private static int calls;\n", StringComparison.Ordinal).Replace("public static Number Count(string value) => new(value.Split('Z')[0].Count(c => c == 'A'));", "public static Number Count(string value) => new(++calls);", StringComparison.Ordinal));
        Assert.Equal(1, CompletionWorkflow.Explore(output, null, null));
        Assert.Equal(0, Summary(output).I("selectedCases"));
        var trials = Workspace.Read(Path.Combine(output, "reports/test-generation/" + Summary(output).S("runId") + "/trials.json"));
        Assert.True(trials.AsArray().All(t => t!["reason"]?.GetValue<string>() != "unstable-replay"));
        var stub = Stubs(output).Single(s => s.S("executionRole") == "internal-logic");
        ReplaceBody(output, stub, "while (true)\n        {\n            System.Threading.Thread.SpinWait(1000);\n        }");
        var config = Path.Combine(folder.Path, "explore.json");
        File.WriteAllText(config, "{\"testGeneration\":{\"maxAttempts\":3,\"timeoutMs\":120000}}");
        Assert.Equal(1, CompletionWorkflow.Explore(output, null, config));
        Assert.Equal(0, Summary(output).I("selectedCases"));
        Assert.Contains("process-timeout", Summary(output).ToJsonString());
        ReplaceBody(output, stub, "value_f000002.Value = new Number(System.Security.Cryptography.RandomNumberGenerator.GetInt32(1, 99));\n        return ControlResult.Continue;");
        File.WriteAllText(config, "{\"testGeneration\":{\"maxAttempts\":18,\"timeoutMs\":120000}}");
        Assert.Equal(1, CompletionWorkflow.Explore(output, null, config));
        Assert.Equal(0, Summary(output).I("selectedCases"));
        Assert.Contains("unstable-replay", Summary(output).ToJsonString());
    }

    /// <summary>実行失敗は構造化位置を残し、prepare は実行せず最新失敗・古い証跡を案内します。</summary>
    /// <returns>CLI の実際の実行記録確認。</returns>
    [Fact]
    public async Task RunFailureEvidenceAndPrepareDoNotExecuteScenarios()
    {
        using var folder = new TestDirectory();
        var output = GeneratePayment(folder.Path);
        var fixture = Path.Combine(folder.Path, "fixture.json");
        File.WriteAllText(fixture, "{\"inputs\":[\"101\"]}");
        await Assert.ThrowsAsync<InvalidOperationException>(() => ProcessRunner.Run("dotnet", [.. Workspace.Cli, "run", output, "--fixture", fixture], folder.Path));
        var record = Workspace.Read(Directory.GetFiles(Path.Combine(output, "reports/executions"), "*.json").Single());
        Assert.Equal("failed", record.S("status"));
        Assert.Equal("s000001", record["failure"]!["source"]!.S("StatementId"));
        Assert.Equal("reached-failed", Worklist(output).A("items").Single(i => i.S("category") == "internal-implementation").S("observedStatus"));
        var source = Path.Combine(output, "UserCode/Completion/Unbuildable.cs");
        File.WriteAllText(source, "not valid C#");
        Assert.Equal(0, CompletionWorkflow.Prepare(output));
        Assert.Equal("stale", Worklist(output).A("items").Single(i => i.S("category") == "internal-implementation").S("observedStatus"));
        File.Delete(source);
    }

    /// <summary>FILE正常・EOF・書出し、SQL正常・no-dataを再探索でも固定 fake で実行します。</summary>
    [Fact]
    public void FileAndSqlReplayNeverCallEditedExternalStubs()
    {
        using var folder = new TestDirectory();
        var sources = Path.Combine(folder.Path, "sources");
        Directory.CreateDirectory(sources);
        var fixture = Path.Combine(Workspace.Root, "tests/fixtures/branch-generation");
        File.Copy(Path.Combine(fixture, "sql.cbl"), Path.Combine(sources, "sql.cbl"));
        var fileSource = File.ReadAllText(Path.Combine(fixture, "file.cbl")).Replace("DISPLAY N GOBACK.", "WRITE REC CLOSE F DISPLAY N GOBACK.", StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(sources, "file.cbl"), fileSource);
        var output = Path.Combine(folder.Path, "generated");
        Generator.Generate(FrontendAnalyzer.Analyze(sources, Configuration()), output, "scaffold");
        foreach (var stub in Stubs(output))
        {
            Assert.Equal("external-adapter", stub.S("executionRole"));
            var path = Path.Combine(output, stub.S("file"));
            File.WriteAllText(path, File.ReadAllText(path).Replace("throw new NotImplementedException", "throw new InvalidOperationException", StringComparison.Ordinal));
        }

        Assert.Equal(0, CompletionWorkflow.Explore(output, null, null));
        var summary = Summary(output);
        Assert.Equal(2, summary.A("programs").ToArray().Count());
        var cases = Workspace.Read(Path.Combine(output, "reports/test-generation/" + summary.S("runId") + "/cases.json"));
        var scripts = cases.AsArray().SelectMany(c => c!.A("script")).ToArray();
        Assert.Contains(scripts, s => s["response"]!.S("StatusCode") == "100");
        Assert.Contains(scripts, s => s["response"]!.S("StatusCode") == "10");
        Assert.Contains(scripts, s => s["request"]!.S("Operation") == "write");
    }

    /// <summary>再生成は補助コード・業務 JSON のバイトと削除を保持し、旧形式は理由付きで再生成へ案内します。</summary>
    [Fact]
    public void RegenerationPreservesBusinessFilesHelpersAndDeletedTemplates()
    {
        using var folder = new TestDirectory();
        var output = GeneratePayment(folder.Path);
        CompleteInspect(output);
        ConfigureBusiness(output);
        CompletionWorkflow.Prepare(output);
        var before = Generator.Inventory(output).Where(p => Generator.IsUserFile(p.Key)).ToArray();
        var template = Path.Combine(output, "Tests/Templates/Business/case.json.txt");
        File.Delete(template);
        before = before.Where(p => p.Key != "Tests/Templates/Business/case.json.txt").ToArray();
        Generator.Generate(FrontendAnalyzer.Analyze(Path.Combine(Workspace.Root, "tests/fixtures/completion/payment-completion.cbl"), Configuration()), output, "scaffold");
        Assert.Equal(before, Generator.Inventory(output).Where(p => Generator.IsUserFile(p.Key)).ToArray());
        var executionPath = Path.Combine(output, "Api/Execution.g.cs");
        File.WriteAllText(executionPath, File.ReadAllText(executionPath).Replace("public bool IsExploration", "public bool LegacyExploration", StringComparison.Ordinal));
        var manifest = Generator.ReadManifest(output);
        manifest["artifacts"]!["Api/Execution.g.cs"] = Generator.Hash(File.ReadAllBytes(executionPath));
        Generator.SaveManifest(output, manifest);
        var error = Assert.Throws<IOException>(() => CompletionWorkflow.Explore(output, null, null));
        Assert.Contains("Legacy output", error.Message);
        var legacyStub = Stubs(output).Single(s => s.S("executionRole") == "internal-logic").DeepClone().AsObject();
        legacyStub.Remove("executionRole");
        Assert.Equal(StubRoles.Binding(Stubs(output).Single(s => s.S("executionRole") == "internal-logic")), StubRoles.Binding(legacyStub));
    }

    /// <summary>COPY とレイアウト不足を入力補完と区別し、静的 prepare で元位置・編集先を保持します。</summary>
    [Fact]
    public void MissingCopyAndUnknownLayoutHaveStructuredWorkItems()
    {
        using var folder = new TestDirectory();
        var source = Path.Combine(folder.Path, "missing.cbl");
        File.WriteAllText(source, "IDENTIFICATION DIVISION. PROGRAM-ID. MISSINGCOPY.\nDATA DIVISION. LINKAGE SECTION.\nCOPY \"absent.cpy\".\nCOPY \"other.cpy\".\n01 KNOWN PIC X.\nPROCEDURE DIVISION USING MISSING KNOWN. DISPLAY KNOWN GOBACK.");
        var output = Path.Combine(folder.Path, "generated");
        Generator.Generate(FrontendAnalyzer.Analyze(source, Configuration(), allowMissingCopy: true), output, "scaffold");
        CompletionWorkflow.Prepare(output);
        var items = Worklist(output).A("items").ToArray();
        var missing = items.Where(i => i.S("category") == "missing-copy").ToArray();
        Assert.Equal(2, missing.Count());
        Assert.NotEqual(missing[0].S("itemId"), missing[1].S("itemId"));
        Assert.True(missing.All(i => i.S("confirmCommand").StartsWith("cobolcs ", StringComparison.Ordinal)));
        Assert.Contains(items, i => i.S("category") == "unknown-layout");
        Assert.True(items.All(i => i.S("category") != "required-input"), "An unparsed program has no known formal inputs.");
        var paymentRoot = Path.Combine(folder.Path, "payment");
        Directory.CreateDirectory(paymentRoot);
        var payment = GeneratePayment(paymentRoot);
        Assert.Contains(Worklist(payment).A("items"), i => i!.S("category") == "required-input");
        foreach (var item in items)
        {
            Assert.NotNull(item["edit"]);
            Assert.False(string.IsNullOrWhiteSpace(item.S("confirmCommand")));
        }
    }

    /// <summary>同じ採用 stage の記録でも、0件・skip・開始のみ・必須欠落を成功として扱いません。</summary>
    [Fact]
    public void BusinessExecutionGateRejectsZeroSkippedAndIncompleteRecords()
    {
        using var folder = new TestDirectory();
        Directory.CreateDirectory(Path.Combine(folder.Path, "TestResults"));
        var path = Path.Combine(folder.Path, "TestResults/business-execution.json");
        foreach (var status in new[] { "skipped", "started", "failed" })
        {
            File.WriteAllText(path, new JsonObject { ["complete"] = true, ["cases"] = new JsonArray(new JsonObject { ["caseId"] = "Required", ["status"] = status }) }.ToJsonString());
            Assert.Throws<InvalidOperationException>(() => BusinessReview.ValidateExecution(folder.Path, ["Required"]));
        }

        File.WriteAllText(path, "{\"complete\":true,\"cases\":[]}");
        Assert.Throws<InvalidOperationException>(() => BusinessReview.ValidateExecution(folder.Path, ["Required"]));
        File.WriteAllText(path, "{\"complete\":false,\"cases\":[{\"caseId\":\"Required\",\"status\":\"passed\"}]}");
        Assert.Throws<InvalidOperationException>(() => BusinessReview.ValidateExecution(folder.Path, ["Required"]));
        File.WriteAllText(path, "{\"complete\":true,\"cases\":[{\"caseId\":\"Required\",\"status\":\"passed\"}]}");
        Assert.Throws<InvalidOperationException>(() => BusinessReview.ValidateExecution(folder.Path, ["Required"]));
    }

    /// <summary>共通探索予算と既定の時間制限を保ち、合成処理の文数を制限します。</summary>
    /// <returns>合成入力の設定。</returns>
    private static FrontendConfiguration Configuration() => FrontendConfiguration.Parse("{\"maxSteps\":1000,\"timeoutMs\":10000,\"testGeneration\":{\"maxAttempts\":24,\"timeoutMs\":120000}}");

    /// <summary>独立した合成 COBOL を生成します。</summary>
    /// <param name="root">試験ごとの保存先。</param>
    /// <returns>管理された生成先。</returns>
    private static string GeneratePayment(string root)
    {
        var output = Path.Combine(root, "generated");
        Generator.Generate(FrontendAnalyzer.Analyze(Path.Combine(Workspace.Root, "tests/fixtures/completion/payment-completion.cbl"), Configuration()), output, "scaffold");
        return output;
    }

    /// <summary>管理されたスタブ一覧。</summary>
    /// <param name="output">生成先。</param>
    /// <returns>役割別の編集先。</returns>
    private static JsonNode[] Stubs(string output) => Workspace.Read(Path.Combine(output, "reports/stubs.json")).AsArray().Select(s => s!).ToArray();

    /// <summary>直近 run の探索結果。</summary>
    /// <param name="output">生成先。</param>
    /// <returns>管理レポート。</returns>
    private static JsonNode Summary(string output)
    {
        var run = Workspace.Read(Path.Combine(output, "manifest.json"))["judgments"]!["testGeneration"]!.S("runId");
        return Workspace.Read(Path.Combine(output, "reports/test-generation/" + run + "/summary.json"));
    }

    /// <summary>手動状態と実測が分離した案内。</summary>
    /// <param name="output">生成先。</param>
    /// <returns>最新案内。</returns>
    private static JsonNode Worklist(string output) => Workspace.Read(Path.Combine(output, "reports/completion-worklist.json"));

    /// <summary>INSPECT の明示補完とコンパイルされる補助コード。</summary>
    /// <param name="output">生成先。</param>
    private static void CompleteInspect(string output)
    {
        var stub = Stubs(output).Single(s => s.S("executionRole") == "internal-logic");
        ReplaceThrow(output, stub, "value_f000002.Value = CountLogic.Count(value_f000001.Value);\n        return ControlResult.Continue;");
        var helperSource = """
            using CobolCS.Runtime;

            namespace CobolCS.Generated;

            /// <summary>合成仕様の文字 A を数える内部処理。</summary>
            public static class CountLogic
            {
                /// <summary>INSPECT TALLYING FOR ALL A BEFORE Z の合成補完です。</summary>
                /// <param name="value">検査する文字列。</param>
                /// <returns>一致した個数。</returns>
                public static Number Count(string value) => new(value.Split('Z')[0].Count(c => c == 'A'));
            }
            """ + "\n";
        File.WriteAllText(Path.Combine(output, "UserCode/Completion/CountLogic.cs"), helperSource);
    }

    /// <summary>初期例外だけを利用者の明示補完へ差し替えます。</summary>
    /// <param name="output">生成先。</param>
    /// <param name="stub">契約。</param>
    /// <param name="body">試験用の補完。</param>
    private static void ReplaceThrow(string output, JsonNode stub, string body)
    {
        var path = Path.Combine(output, stub.S("file"));
        File.WriteAllText(path, Regex.Replace(File.ReadAllText(path), "throw new NotImplementedException\\([^\\n]+\\);", body));
    }

    /// <summary>同じ契約の内部処理だけを負の試験用に変更します。</summary>
    /// <param name="output">生成先。</param>
    /// <param name="stub">契約。</param>
    /// <param name="body">timeout または非決定処理。</param>
    private static void ReplaceBody(string output, JsonNode stub, string body)
    {
        var path = Path.Combine(output, stub.S("file"));
        var source = File.ReadAllText(path);
        var start = source.IndexOf("    {", source.IndexOf("    public ", StringComparison.Ordinal), StringComparison.Ordinal);
        File.WriteAllText(path, source[..start] + "    {\n        " + body + "\n    }\n}\n");
    }

    /// <summary>生成 API へのマッピングだけを型情報から作り、手計算済み JSON はコピーします。</summary>
    /// <param name="output">生成先。</param>
    private static void ConfigureBusiness(string output)
    {
        var fixtures = Path.Combine(Workspace.Root, "tests/fixtures/completion");
        Directory.CreateDirectory(Path.Combine(output, "Tests/BusinessCases"));
        Directory.CreateDirectory(Path.Combine(output, "Tests/BusinessContracts"));
        File.Copy(Path.Combine(fixtures, "business-case.json"), Path.Combine(output, "Tests/BusinessCases/payment.json"));
        File.Copy(Path.Combine(fixtures, "business-contract.json"), Path.Combine(output, "Tests/BusinessContracts/payment.json"));
        var plan = Workspace.Read(Path.Combine(output, "reports/branch-plans.json"))[0]!;
        var method = Stubs(output).Single(s => s.S("executionRole") == "external-adapter").S("method");
        var adapter = File.ReadAllText(Path.Combine(fixtures, "Adapter.cs.txt")).Replace("@N@", plan.S("name"), StringComparison.Ordinal).Replace("@M@", method, StringComparison.Ordinal).Replace("@FEE@", plan.A("symbols").Single(s => s.S("name") == "FEE").S("member"), StringComparison.Ordinal).Replace("@CODE@", plan.A("symbols").Single(s => s.S("name") == "RESPONSE-CODE").S("member"), StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(output, "UserCode/Business/PaymentAdapter.cs"), adapter);
        var registration = Path.Combine(output, "UserCode/Business/BusinessRegistration.cs");
        File.WriteAllText(registration, File.ReadAllText(registration).Replace("// adapters.Add(\"payment-v1\", () => new PaymentAdapter());", "adapters.Add(\"payment-v1\", () => new PaymentAdapter());", StringComparison.Ordinal));
    }

    /// <summary>業務比較だけを実行し、内部 snapshot の比較から独立させます。</summary>
    /// <param name="output">生成または採用先。</param>
    /// <returns>実際の xUnit 実行。</returns>
    private static async Task TestBusiness(string output) => _ = await ProcessRunner.Run("dotnet", ["test", "Tests/CobolCS.Generated.Tests.csproj", "-m:1", "--filter", "Category=Business"], output, 180);

    /// <summary>手計算の業務期待値と C# 挙動固定を区別してレビューします。</summary>
    /// <param name="output">生成先。</param>
    private static void ReviewAll(string output)
    {
        var manifest = Generator.ReadManifest(output);
        var path = Path.Combine(output, "Tests/review.json");
        var review = Workspace.Read(path);
        foreach (var entry in review.A("cases"))
        {
            foreach (var baseline in entry["files"]!.AsObject().Where(p => p.Key.StartsWith("Tests/Baselines/", StringComparison.Ordinal)).ToArray())
            {
                var file = Path.Combine(output, baseline.Key);
                var value = Workspace.Read(file);
                value["reviewReason"] = "Reviewed synthetic C# behavior with fixed responses; remains characterization, not specification.";
                File.WriteAllText(file, Generator.Json(value));
                entry["files"]![baseline.Key] = Generator.Hash(File.ReadAllBytes(file));
            }

            entry["reviewed"] = true;
            entry["implementationHashes"] = CompletionWorkflow.CodeHashes(output);
        }

        review["cases"]!.AsArray().Add(new JsonObject { ["programId"] = "PAYMENT", ["caseId"] = "Payment_RoundingBoundary", ["caseKind"] = "business", ["baselineKind"] = "specification", ["reviewed"] = true, ["hasNoOpCalls"] = false, ["callStubMode"] = "Throw", ["sourceHashes"] = manifest["inputHashes"]!.DeepClone(), ["configHash"] = manifest.S("configHash"), ["contractHashes"] = manifest["contractHashes"]!.DeepClone(), ["implementationHashes"] = BusinessReview.ImplementationHashes(output), ["files"] = new JsonObject { ["Tests/BusinessCases/payment.json"] = Generator.Hash(File.ReadAllBytes(Path.Combine(output, "Tests/BusinessCases/payment.json"))), ["Tests/BusinessContracts/payment.json"] = Generator.Hash(File.ReadAllBytes(Path.Combine(output, "Tests/BusinessContracts/payment.json"))) } });
        File.WriteAllText(path, Generator.Json(review));
    }
}
