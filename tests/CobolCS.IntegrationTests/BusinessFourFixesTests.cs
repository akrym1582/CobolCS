using System.Text.Json.Nodes;
using System.Xml.Linq;
using CobolCS.Cli;
using CobolCS.Frontend;
using CobolCS.Runtime;
using CobolCS.Tools;
using Xunit;

namespace CobolCS.IntegrationTests;

/// <summary>診断・実体照合・ケース分離・実 runner の証跡を合成データで検証します。</summary>
public sealed class BusinessFourFixesTests
{
    /// <summary>合成採取バイトを検査し、欠落・変更・危険パス・重複 ID を有効件数から除外します。</summary>
    /// <returns>通常ファイル種別とリンクの検査。</returns>
    [Fact]
    public async Task InspectionCollectsErrorsAndValidatesReferenceBytes()
    {
        using var folder = new TestDirectory();
        Configure(folder.Path, ["valid"]);
        var data = Path.Combine(folder.Path, "Tests/BusinessCases/Data/capture.json");
        Directory.CreateDirectory(Path.GetDirectoryName(data)!);
        File.WriteAllText(data, "{\"synthetic\":true}");
        var path = Path.Combine(folder.Path, "Tests/BusinessCases/0.json");
        var item = Workspace.Read(path);
        item["baselineKind"] = "cobol-reference";
        item["provenance"] = new JsonObject { ["processor"] = "synthetic hash fixture, not COBOL capture", ["version"] = "1", ["method"] = "fixed synthetic bytes", ["conditions"] = new JsonObject(), ["fileHashes"] = new JsonObject { ["capture.json"] = BusinessCases.FileHash(data).ToUpperInvariant() } };
        File.WriteAllText(path, item.ToJsonString());
        Assert.Single(BusinessCases.Load(folder.Path));
        Directory.CreateDirectory(Path.Combine(folder.Path, "UserCode/Business"));
        var legacy = Path.Combine(folder.Path, "UserCode/Business/BusinessRegistration.cs");
        File.WriteAllText(legacy, "public static void Register(IDictionary < string , IBusinessCaseAdapter > adapters) { throw new Exception(); }");
        Assert.Contains(BusinessCases.Inspect(folder.Path).Diagnostics, d => d.Code == "legacy-registration" && d.Reason.Contains("() => new PaymentAdapter()", StringComparison.Ordinal));
        File.Delete(legacy);
        File.WriteAllText(Path.Combine(folder.Path, "Tests/BusinessCases/broken.json"), "{");
        var unfilled = item.DeepClone();
        unfilled["caseId"] = "unfilled";
        unfilled["expected"] = null;
        File.WriteAllText(Path.Combine(folder.Path, "Tests/BusinessCases/unfilled.json"), unfilled.ToJsonString());
        var inspection = BusinessCases.Inspect(folder.Path);
        Assert.Single(inspection.Cases);
        Assert.Equal(2, inspection.Diagnostics.Count);
        var malformedId = Path.Combine(folder.Path, "Tests/BusinessCases/malformed-id.json");
        File.WriteAllText(malformedId, item.ToJsonString().Replace("\"caseId\":\"valid\"", "\"caseId\":\"\\uD800\"", StringComparison.Ordinal));
        Assert.Contains(BusinessCases.Inspect(folder.Path).Diagnostics, d => d.Path.EndsWith("malformed-id.json", StringComparison.Ordinal));
        File.Delete(malformedId);
        var blockedContract = Workspace.Read(Path.Combine(folder.Path, "Tests/BusinessContracts/test.json"));
        blockedContract["contractId"] = "blocked-v1";
        blockedContract["result"]!["fields"] = new JsonObject();
        var blockedPath = Path.Combine(folder.Path, "Tests/BusinessContracts/blocked.json");
        File.WriteAllText(blockedPath, blockedContract.ToJsonString());
        foreach (var contractId in new[] { "blocked-v1", "missing-v1" })
        {
            var blockedCase = item.DeepClone();
            blockedCase["caseId"] = contractId;
            blockedCase["contractId"] = contractId;
            File.WriteAllText(Path.Combine(folder.Path, "Tests/BusinessCases/" + contractId + ".json"), blockedCase.ToJsonString());
        }

        inspection = BusinessCases.Inspect(folder.Path);
        Assert.Single(inspection.Cases);
        Assert.Contains(inspection.Diagnostics, d => d.Category == "business-contract-error");
        Assert.Contains(inspection.Diagnostics, d => d.CaseId == "blocked-v1" && d.Code == "blocked-contract" && d.Reason.Contains("blocked.json", StringComparison.Ordinal));
        Assert.Contains(inspection.Diagnostics, d => d.CaseId == "missing-v1" && d.Code == "blocked-contract");
        File.Delete(blockedPath);
        foreach (var contractId in new[] { "blocked-v1", "missing-v1" })
        {
            File.Delete(Path.Combine(folder.Path, "Tests/BusinessCases/" + contractId + ".json"));
        }

        File.AppendAllText(data, " ");
        Assert.Throws<ArgumentException>(() => BusinessCases.Load(folder.Path));
        Assert.Contains(BusinessCases.Inspect(folder.Path).Diagnostics, d => d.Category == "reference-data-error");
        File.Delete(data);
        Assert.Contains(BusinessCases.Inspect(folder.Path).Diagnostics, d => d.Category == "reference-data-error");
        foreach (var relative in new[] { "../capture.json", "/capture.json", "C:/capture.json", "\\\\server/file", "a//b", "a/./b", "a\\b", "a\0b" })
        {
            item["provenance"]!["fileHashes"] = new JsonObject { [relative] = new string('a', 64) };
            File.WriteAllText(path, item.ToJsonString());
            Assert.Contains(BusinessCases.Inspect(folder.Path).Diagnostics, d => d.Category == "reference-data-error");
        }

        File.WriteAllText(data, "{}");
        item["provenance"]!["fileHashes"] = new JsonObject { ["capture.json"] = BusinessCases.FileHash(data) };
        File.WriteAllText(path, item.ToJsonString());
        File.Copy(path, Path.Combine(folder.Path, "Tests/BusinessCases/duplicate.json"));
        inspection = BusinessCases.Inspect(folder.Path);
        Assert.Empty(inspection.Cases);
        Assert.Equal(2, inspection.Diagnostics.Count(d => d.Code == "duplicate-case-id"));
        File.Delete(Path.Combine(folder.Path, "Tests/BusinessCases/duplicate.json"));
        File.Delete(data);
        File.CreateSymbolicLink(data, Path.Combine(folder.Path, "Tests/BusinessContracts/test.json"));
        Assert.Throws<IOException>(() => BusinessCases.Inspect(folder.Path));
        File.Delete(data);
        var parentLink = Path.Combine(folder.Path, "Tests/BusinessCases/Data/link");
        Directory.CreateSymbolicLink(parentLink, Path.Combine(folder.Path, "Tests/BusinessContracts"));
        item["provenance"]!["fileHashes"] = new JsonObject { ["link/test.json"] = BusinessCases.FileHash(Path.Combine(folder.Path, "Tests/BusinessContracts/test.json")) };
        File.WriteAllText(path, item.ToJsonString());
        Assert.Throws<IOException>(() => BusinessCases.Inspect(folder.Path));
        Directory.Delete(parentLink);
        item["provenance"]!["fileHashes"] = new JsonObject { ["capture.json"] = new string('a', 64) };
        File.WriteAllText(path, item.ToJsonString());
        _ = await ProcessRunner.Run("mkfifo", [data], folder.Path);
        Assert.Contains(BusinessCases.Inspect(folder.Path).Diagnostics, d => d.Category == "reference-data-error");
    }

    /// <summary>毎ケース初期状態と非同期破棄を使い、factory・実行・契約不一致でも後続を実行します。</summary>
    /// <returns>独立ケースと破棄の検査。</returns>
    [Fact]
    public async Task FactoriesIsolateStateAndDisposeOnEveryOutcome()
    {
        using var folder = new TestDirectory();
        Configure(folder.Path, ["a", "A", "c"]);
        foreach (var reverse in new[] { false, true })
        {
            var execution = new BusinessExecution(folder.Path);
            execution.VerifyIntegrity();
            var created = new List<StatefulAdapter>();
            foreach (var id in reverse ? new[] { "c", "A", "a" } : new[] { "a", "A", "c" })
            {
                await execution.Run(BusinessExecution.CaseKey(id), factories => factories.Add("test-v1", () =>
                {
                    var adapter = new StatefulAdapter();
                    created.Add(adapter);
                    return adapter;
                }));
            }

            Assert.Equal(3, created.Count);
            Assert.All(created, a => Assert.True(a.AsyncDisposed && !a.SyncDisposed));
            var summary = Summary(folder.Path, execution.ExecutionId);
            Assert.True(summary.B("complete"));
            Assert.True(summary.B("passed"));
        }

        foreach (var failure in new[] { "factory", "null", "mismatch", "execute", "duplicate", "noop", "incomplete", "timeout", "cancel" })
        {
            var execution = new BusinessExecution(folder.Path);
            execution.VerifyIntegrity();
            var adapter = new StatefulAdapter { Mode = failure };
            await Assert.ThrowsAnyAsync<Exception>(() => execution.Run(BusinessExecution.CaseKey("a"), factories =>
            {
                factories.Add("test-v1", () => failure == "factory" ? throw new InvalidOperationException("factory failure") : failure == "null" ? null! : adapter);
                if (failure == "duplicate")
                {
                    factories.Add("test-v1", () => adapter);
                }
            }));
            Assert.Equal(failure is "factory" or "null" or "duplicate" ? false : true, adapter.AsyncDisposed);
            await execution.Run(BusinessExecution.CaseKey("A"), f => f.Add("test-v1", () => new StatefulAdapter()));
            await execution.Run(BusinessExecution.CaseKey("c"), f => f.Add("test-v1", () => new StatefulAdapter()));
            var summary = Summary(folder.Path, execution.ExecutionId);
            Assert.True(summary.B("complete"));
            Assert.False(summary.B("passed"));
            Assert.Equal(2, summary.I("passedCases"));
        }
    }

    /// <summary>実際の VSTest で三行を発見し、比較失敗後も二成功を残し、ID と TRX を厳密に対応させます。</summary>
    /// <returns>生成先の実 runner と採用検証。</returns>
    [Fact]
    public async Task GeneratedTheoryRowsAndAdoptionUseCurrentExecution()
    {
        using var folder = new TestDirectory();
        var root = Generate(folder.Path);
        var ids = new[] { "同じ接頭辞 \"一\"", "同じ接頭辞 \"二\"", "同じ接頭辞 \"三\"" };
        Configure(root, ids);
        var dataPath = Path.Combine(root, "Tests/BusinessCases/Data/capture.json");
        Directory.CreateDirectory(Path.GetDirectoryName(dataPath)!);
        File.WriteAllText(dataPath, "{\"syntheticHashFixture\":true}");
        File.WriteAllText(Path.Combine(root, "Tests/BusinessCases/Data/reference.cs"), "Reference data is never C# source");
        var referencePath = Path.Combine(root, "Tests/BusinessCases/0.json");
        var reference = Workspace.Read(referencePath);
        reference["baselineKind"] = "cobol-reference";
        reference["provenance"] = new JsonObject { ["processor"] = "synthetic hash fixture, not COBOL capture", ["version"] = "1", ["method"] = "fixed synthetic bytes", ["conditions"] = new JsonObject(), ["fileHashes"] = new JsonObject { ["capture.json"] = BusinessCases.FileHash(dataPath) } };
        foreach (var name in new[] { "bin", "obj", "TestResults" })
        {
            var capture = Path.Combine(root, "Tests/BusinessCases/Data", name, "capture.json");
            Directory.CreateDirectory(Path.GetDirectoryName(capture)!);
            File.WriteAllText(capture, "{\"synthetic\":true}");
            reference["provenance"]!["fileHashes"]![name + "/capture.json"] = BusinessCases.FileHash(capture);
        }

        File.WriteAllText(referencePath, reference.ToJsonString());
        var dataBytes = File.ReadAllBytes(dataPath);
        var unusedData = Path.Combine(root, "Tests/BusinessCases/Data/deleted.txt");
        File.WriteAllText(unusedData, "user reference");
        File.Delete(unusedData);
        Generator.Generate(FrontendAnalyzer.Analyze(Path.Combine(folder.Path, "simple.cbl"), FrontendConfiguration.Parse("{\"testGeneration\":{\"maxAttempts\":4}}")), root, "scaffold");
        Assert.Equal(dataBytes, File.ReadAllBytes(dataPath));
        Assert.False(File.Exists(unusedData));
        var adapterPath = Path.Combine(root, "UserCode/Business/TestAdapter.cs");
        var adapterSource = """
            using System.Text.Json.Nodes;
            using CobolCS.Runtime;

            namespace CobolCS.Generated;

            /// <summary>ケースごとに作る合成境界。</summary>
            public sealed class TestAdapter : IBusinessCaseAdapter, IAsyncDisposable
            {
                /// <summary>ケース内の呼出し数。</summary>
                private int calls;

                /// <summary>Gets 合成契約。</summary>
                public string ContractId => "test-v1";

                /// <summary>ケース内の状態だけを観測。</summary>
                /// <param name="input">正式入力。</param>
                /// <returns>初回の値。</returns>
                public BusinessObservation Execute(BusinessCaseInput input) => new(new JsonObject { ["count"] = (++calls).ToString() }, new JsonArray(), "ok");

                /// <summary>合成リソースの破棄。</summary>
                /// <returns>完了。</returns>
                public ValueTask DisposeAsync() => ValueTask.CompletedTask;
            }
            """;
        File.WriteAllText(adapterPath, adapterSource + "\n");
        var registration = Path.Combine(root, "UserCode/Business/BusinessRegistration.cs");
        File.WriteAllText(registration, File.ReadAllText(registration).Replace("// adapters.Add(\"payment-v1\", () => new PaymentAdapter());", "adapters.Add(\"test-v1\", () => new TestAdapter());", StringComparison.Ordinal));
        CompletionWorkflow.Prepare(root);
        File.Delete(dataPath);
        Assert.Equal(0, CompletionWorkflow.Prepare(root));
        Assert.Contains(Workspace.Read(Path.Combine(root, "reports/completion-worklist.json")).A("items"), i => i.S("category") == "reference-data-error");
        File.WriteAllBytes(dataPath, dataBytes);
        var preflightId = Guid.NewGuid().ToString("N");
        await Test(root, preflightId);
        var middleId = Summary(root, preflightId).A("cases").ElementAt(1).S("caseId");
        var path = Path.Combine(root, BusinessCases.Load(root).Single(c => c.Case.S("caseId") == middleId).Path);
        var bad = Workspace.Read(path);
        bad["expected"]!["result"]!["count"] = "2";
        File.WriteAllText(path, bad.ToJsonString());
        var executionId = Guid.NewGuid().ToString("N");
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Test(root, executionId));
        Assert.True(File.Exists(Path.Combine(root, "TestResults/business", executionId, "summary.json")), failure.Message);
        var summary = Summary(root, executionId);
        Assert.True(summary.B("complete"));
        Assert.Equal(2, summary.I("passedCases"));
        Assert.False(summary.B("passed"));
        Assert.Equal(new[] { "passed", "failed", "passed" }, summary.A("cases").Select(c => c.S("status")));
        var trxPath = Path.Combine(root, "TestResults", executionId, "business.trx");
        var trx = XDocument.Load(trxPath);
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        var results = trx.Descendants(ns + "UnitTestResult").ToArray();
        var definitions = trx.Descendants(ns + "UnitTest").ToArray();
        Assert.Equal(4, results.Length);
        Assert.Equal(1, results.Count(r => (string?)r.Attribute("outcome") == "Failed"));
        SaveVerification(root, executionId, trxPath, failure.Message);
        foreach (var id in ids.Where(id => id != middleId))
        {
            BusinessTrxResult.Validate(results, definitions, BusinessExecution.CaseKey(id));
        }

        Assert.Throws<InvalidOperationException>(() => BusinessReview.ValidateExecution(root, ids, executionId, trxPath));
        bad["expected"]!["result"]!["count"] = "1";
        File.WriteAllText(path, bad.ToJsonString());
        var next = Guid.NewGuid().ToString("N");
        await Test(root, next);
        trxPath = Path.Combine(root, "TestResults", next, "business.trx");
        BusinessReview.ValidateExecution(root, ids, next, trxPath);
        Assert.Throws<InvalidOperationException>(() => BusinessReview.ValidateExecution(root, ids, executionId, trxPath));
        var terminalPath = Path.Combine(root, "TestResults/business", next, "cases", BusinessExecution.CaseKey(ids[0]) + ".json");
        var terminalBytes = File.ReadAllBytes(terminalPath);
        var terminal = Workspace.Read(terminalPath);
        terminal["executionId"] = executionId;
        File.WriteAllText(terminalPath, terminal.ToJsonString());
        Assert.Throws<InvalidOperationException>(() => BusinessReview.ValidateExecution(root, ids, next, trxPath));
        File.Delete(terminalPath);
        Assert.Throws<InvalidOperationException>(() => BusinessReview.ValidateExecution(root, ids, next, trxPath));
        File.WriteAllBytes(terminalPath, terminalBytes);
        Assert.Throws<InvalidOperationException>(() => BusinessReview.ValidateExecution(root, ids, Guid.NewGuid().ToString("N"), trxPath));
        bad["input"]!["id"] = "changed";
        File.WriteAllText(path, bad.ToJsonString());
        Assert.Throws<InvalidOperationException>(() => BusinessReview.ValidateExecution(root, ids, next, trxPath));
        bad["input"]!["id"] = middleId;
        File.WriteAllText(path, bad.ToJsonString());

        // 名前や順序を変えても ID による照合は不変です。
        File.Move(path, Path.Combine(root, "Tests/BusinessCases/renamed.json"));
        var third = Guid.NewGuid().ToString("N");
        await Test(root, third);
        trxPath = Path.Combine(root, "TestResults", third, "business.trx");
        BusinessReview.ValidateExecution(root, ids, third, trxPath);
        var selectedId = Guid.NewGuid().ToString("N");
        await Test(root, selectedId, "DisplayName~" + BusinessExecution.CaseKey(ids[0])[..32] + "&DisplayName~" + BusinessExecution.CaseKey(ids[0])[32..], noBuild: true);
        Assert.Equal(1, Summary(root, selectedId).I("passedCases"));
        Assert.False(Summary(root, selectedId).B("complete"));
        var parallelIds = new[] { Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N") };
        await Task.WhenAll(parallelIds.Select(id => Test(root, id, noBuild: true)));
        Assert.All(parallelIds, id => Assert.True(Summary(root, id).B("passed")));
        File.WriteAllText(Path.Combine(root, "Tests/BusinessCases/broken.json"), "{");
        var diagnosticId = Guid.NewGuid().ToString("N");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Test(root, diagnosticId, noBuild: true));
        Assert.Equal(3, Summary(root, diagnosticId).I("passedCases"));
        Assert.False(Summary(root, diagnosticId).B("complete"));
        File.Delete(Path.Combine(root, "Tests/BusinessCases/broken.json"));
        var interruptSource = adapterSource.Replace("(++calls).ToString()", "Stop(++calls)", StringComparison.Ordinal);
        interruptSource = interruptSource[..interruptSource.LastIndexOf('}')] + "\n" + """
                /// <summary>合成 runner 中断。別プロセスのこの試験だけを終了します。</summary>
                /// <param name="value">未使用フィールドを作らず、ケース内の状態を読みます。</param>
                /// <returns>中断時には返りません。</returns>
                private static string Stop(int value)
                {
                    Environment.Exit(91);
                    return value.ToString();
                }
            }
            """;
        File.WriteAllText(adapterPath, interruptSource + "\n");
        var interruptedId = Guid.NewGuid().ToString("N");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Test(root, interruptedId));
        Assert.False(Summary(root, interruptedId).B("complete"));
        Assert.False(Summary(root, interruptedId).B("passed"));
        File.WriteAllText(adapterPath, adapterSource + "\n");
        await Test(root, Guid.NewGuid().ToString("N"));
        Review(root);
        var adopted = Path.Combine(folder.Path, "adopted");
        Assert.Equal(0, await Adoption.Run(root, adopted));
        Assert.Equal(dataBytes, File.ReadAllBytes(Path.Combine(adopted, "Tests/BusinessCases/Data/capture.json")));
        Assert.Equal(3, BusinessCases.Load(adopted).Count);
        foreach (var name in new[] { "bin", "obj", "TestResults" })
        {
            Assert.True(File.Exists(Path.Combine(adopted, "Tests/BusinessCases/Data", name, "capture.json")));
        }

        Directory.Delete(root, recursive: true);
        var adoptedAdapter = Path.Combine(adopted, "UserCode/Business/TestAdapter.cs");
        File.WriteAllText(adoptedAdapter, File.ReadAllText(adoptedAdapter).Replace("calls", "invocations", StringComparison.Ordinal));
        await Test(adopted, Guid.NewGuid().ToString("N"));
        File.WriteAllText(adoptedAdapter, File.ReadAllText(adoptedAdapter).Replace("(++invocations)", "(++invocations + 1)", StringComparison.Ordinal));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Test(adopted, Guid.NewGuid().ToString("N")));
    }

    /// <summary>prepare は不正ケースを集約し、履歴を保持し、正常な run の結果を書き換えません。</summary>
    /// <returns>通常 run の完了。</returns>
    [Fact]
    public async Task PreparePreservesRequiredHistoryAndRunResult()
    {
        using var folder = new TestDirectory();
        var root = Generate(folder.Path);
        Configure(root, ["required"]);
        Assert.Equal(0, CompletionWorkflow.Prepare(root));
        File.WriteAllText(Path.Combine(root, "Tests/BusinessCases/broken.json"), "{");
        var unfilled = Workspace.Read(Path.Combine(root, "Tests/BusinessCases/0.json"));
        unfilled["caseId"] = "unfilled";
        unfilled["expected"] = null;
        File.WriteAllText(Path.Combine(root, "Tests/BusinessCases/unfilled.json"), unfilled.ToJsonString());
        Assert.Equal(0, CompletionWorkflow.Prepare(root));
        var items = Workspace.Read(Path.Combine(root, "reports/completion-worklist.json")).A("items").ToArray();
        Assert.Equal(2, items.Count(i => i.S("category") == "business-case-error"));
        Assert.Contains(items, i => i.S("category") == "test-expectation" && i.S("reason").StartsWith("1 configured", StringComparison.Ordinal));
        _ = await ProcessRunner.Run("dotnet", [.. Workspace.Cli, "run", root], folder.Path);
        Assert.Equal(0, CompletionWorkflow.Explore(root, null, null));
        File.Delete(Path.Combine(root, "Tests/BusinessCases/0.json"));
        Assert.Equal(0, CompletionWorkflow.Prepare(root));
        Assert.Contains(Workspace.Read(Path.Combine(root, "reports/completion-worklist.json")).A("items"), i => i.S("reasonCode") == "required-case-missing");
        Assert.Contains("required", Workspace.Read(Path.Combine(root, "Testing/BusinessHistory.json")).A("requiredCaseIds").Select(i => i.GetValue<string>()));
        Assert.Throws<ArgumentException>(() => BusinessCases.Load(root));
        File.WriteAllText(Path.Combine(root, "Testing/BusinessHistory.json"), "{");
        Assert.Throws<IOException>(() => CompletionWorkflow.Prepare(root));
    }

    /// <summary>独立手計算に相当する合成ケースと型を保存します。実機比較と表示しません。</summary>
    private static void Configure(string root, string[] ids)
    {
        Directory.CreateDirectory(Path.Combine(root, "Tests/BusinessCases"));
        Directory.CreateDirectory(Path.Combine(root, "Tests/BusinessContracts"));
        var contractSource = """
            {"schemaVersion":1,"contractId":"test-v1","version":1,"reason":"Synthetic initial call count is one","input":{"type":"object","fields":{"id":{"type":"string"}}},"result":{"type":"object","fields":{"count":{"type":"number"}}},"termination":{"type":"string"},"operations":{}}
            """;
        File.WriteAllText(Path.Combine(root, "Tests/BusinessContracts/test.json"), contractSource);
        for (var index = 0; index < ids.Length; index++)
        {
            File.WriteAllText(Path.Combine(root, "Tests/BusinessCases/" + index + ".json"), new JsonObject
            {
                ["schemaVersion"] = 1,
                ["caseId"] = ids[index],
                ["programId"] = "SIMPLE",
                ["contractId"] = "test-v1",
                ["required"] = true,
                ["baselineKind"] = "specification",
                ["reviewReason"] = "New synthetic adapter must start with zero calls",
                ["provenance"] = new JsonObject { ["calculation"] = "0 + 1 = 1" },
                ["input"] = new JsonObject { ["id"] = ids[index] },
                ["responses"] = new JsonArray(),
                ["expected"] = new JsonObject { ["result"] = new JsonObject { ["count"] = "1" }, ["effects"] = new JsonArray(), ["termination"] = "ok" },
            }.ToJsonString());
        }
    }

    /// <summary>実 runner が実行する小さな source 出力。</summary>
    private static string Generate(string folder)
    {
        var source = Path.Combine(folder, "simple.cbl");
        File.WriteAllText(source, "IDENTIFICATION DIVISION. PROGRAM-ID. SIMPLE. PROCEDURE DIVISION. DISPLAY 'OK' GOBACK.");
        var root = Path.Combine(folder, "generated");
        Generator.Generate(FrontendAnalyzer.Analyze(source, FrontendConfiguration.Parse("{\"testGeneration\":{\"maxAttempts\":4}}")), root, "scaffold");
        return root;
    }

    /// <summary>今回の実行 ID を子プロセスだけに渡します。</summary>
    private static async Task Test(string root, string executionId, string filter = "Category=Business", bool noBuild = false)
    {
        var arguments = new List<string> { "test", "Tests/CobolCS.Generated.Tests.csproj", "-m:1", "--filter", filter, "--logger", "trx;LogFileName=business.trx", "--results-directory", "TestResults/" + executionId };
        if (noBuild)
        {
            arguments.AddRange(["--no-build", "--no-restore"]);
        }

        _ = await ProcessRunner.Run("dotnet", arguments, root, 180, environment: new Dictionary<string, string> { ["COBOLCS_BUSINESS_EXECUTION_ID"] = executionId });
    }

    /// <summary>実 runner と Runtime の共通形式を読みます。</summary>
    private static JsonNode Summary(string root, string executionId) => Workspace.Read(Path.Combine(root, "TestResults/business", executionId, "summary.json"));

    /// <summary>明示された検証保存先だけに、実 runner の失敗・成功三件の原本を残します。</summary>
    private static void SaveVerification(string root, string executionId, string trxPath, string output)
    {
        var targetRoot = Environment.GetEnvironmentVariable("COBOLCS_BUSINESS_VERIFICATION_ROOT");
        if (targetRoot == null)
        {
            return;
        }

        var target = Path.Combine(targetRoot, executionId);
        var source = Path.Combine(root, "TestResults/business", executionId);
        Directory.CreateDirectory(Path.Combine(target, "cases"));
        foreach (var path in Directory.GetFiles(source, "*.json", SearchOption.AllDirectories))
        {
            File.Copy(path, Path.Combine(target, Path.GetRelativePath(source, path)));
        }

        File.Copy(trxPath, Path.Combine(target, "runner.trx"));
        File.WriteAllText(Path.Combine(target, "runner-output.txt"), output);
    }

    /// <summary>合成 C# 挙動と独立業務期待値を別のレビュー行で束縛します。</summary>
    private static void Review(string root)
    {
        var manifest = Generator.ReadManifest(root);
        var path = Path.Combine(root, "Tests/review.json");
        var review = Workspace.Read(path);
        foreach (var entry in review.A("cases"))
        {
            foreach (var baseline in entry["files"]!.AsObject().Where(p => p.Key.StartsWith("Tests/Baselines/", StringComparison.Ordinal)).ToArray())
            {
                var file = Path.Combine(root, baseline.Key);
                var value = Workspace.Read(file);
                value["reviewReason"] = "Reviewed synthetic characterization, not original COBOL";
                File.WriteAllText(file, Generator.Json(value));
                entry["files"]![baseline.Key] = BusinessCases.FileHash(file);
            }

            entry["reviewed"] = true;
        }

        foreach (var item in BusinessCases.Load(root))
        {
            review["cases"]!.AsArray().Add(new JsonObject
            {
                ["programId"] = "SIMPLE",
                ["caseId"] = item.Case.S("caseId"),
                ["caseKind"] = "business",
                ["baselineKind"] = item.Case.S("baselineKind"),
                ["reviewed"] = true,
                ["hasNoOpCalls"] = false,
                ["callStubMode"] = "Throw",
                ["sourceHashes"] = manifest["inputHashes"]!.DeepClone(),
                ["configHash"] = manifest.S("configHash"),
                ["contractHashes"] = manifest["contractHashes"]!.DeepClone(),
                ["implementationHashes"] = BusinessReview.ImplementationHashes(root),
                ["files"] = new JsonObject { [item.Path] = BusinessCases.FileHash(Path.Combine(root, item.Path)), ["Tests/BusinessContracts/test.json"] = BusinessCases.FileHash(Path.Combine(root, "Tests/BusinessContracts/test.json")) },
            });
            foreach (var capture in BusinessCases.ReferencePaths(root, item.Case))
            {
                review["cases"]!.AsArray()[^1]!["files"]![capture] = BusinessCases.FileHash(Path.Combine(root, capture));
            }
        }

        File.WriteAllText(path, Generator.Json(review));
    }

    /// <summary>共有した場合に二ケース目で失敗する合成状態と、両破棄 API を持つ境界。</summary>
    private sealed class StatefulAdapter : IBusinessCaseAdapter, IDisposable, IAsyncDisposable
    {
        /// <summary>実行回数。</summary>
        private int calls;

        /// <summary>Gets or sets 合成失敗の種類。</summary>
        public string Mode { get; set; } = string.Empty;

        /// <summary>Gets a value indicating whether 同期破棄の観測。</summary>
        public bool SyncDisposed { get; private set; }

        /// <summary>Gets a value indicating whether 非同期破棄の観測。</summary>
        public bool AsyncDisposed { get; private set; }

        /// <summary>Gets 合成契約 ID。</summary>
        public string ContractId => Mode == "mismatch" ? "wrong" : "test-v1";

        /// <summary>状態を使う正常観測、または明示的な合成例外。</summary>
        public BusinessObservation Execute(BusinessCaseInput input)
        {
            if (Mode == "execute")
            {
                throw new InvalidOperationException("execution failure");
            }

            if (Mode == "timeout")
            {
                throw new TimeoutException();
            }

            if (Mode == "cancel")
            {
                throw new OperationCanceledException();
            }

            return new BusinessObservation(new JsonObject { ["count"] = (++calls).ToString() }, new JsonArray(), "ok", HasNoOpCalls: Mode == "noop", Completed: Mode != "incomplete");
        }

        /// <summary>同期破棄を記録。</summary>
        public void Dispose() => SyncDisposed = true;

        /// <summary>非同期破棄を優先したことを記録。</summary>
        public ValueTask DisposeAsync()
        {
            AsyncDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
