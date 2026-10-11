using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CobolCS.Frontend;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>stage 内でビルド・別プロセス探索・固定再生を行い、実測だけを未レビューの MSTest へ昇格します。</summary>
internal static class BranchTestGeneration
{
    /// <summary>探索器の互換・決定性を識別する版です。</summary>
    internal const string Version = "1.0.0";

    /// <summary>SDK や未対応文の不足を通常ソース生成の失敗と混同しません。</summary>
    /// <param name="stage">既存利用者ファイルを保持した出力 stage。</param>
    /// <param name="ir">生成時だけ必要な IR。</param>
    /// <param name="sources">編集済み Stubs を呼ばない初期ソース。</param>
    /// <param name="manifest">生成判定の追加先。</param>
    internal static void Generate(string stage, JsonNode ir, SortedDictionary<string, string> sources, JsonNode manifest)
        => GenerateAsync(stage, ir, sources, manifest).GetAwaiter().GetResult();

    /// <summary>管理対象の内容から決定的な run を識別します。</summary>
    /// <param name="stage">出力先。</param>
    /// <param name="ir">解析結果。</param>
    /// <param name="sources">探索ビルド用の原型。</param>
    /// <param name="manifest">判定。</param>
    /// <returns>探索と再生の完了。</returns>
    private static async Task GenerateAsync(string stage, JsonNode ir, SortedDictionary<string, string> sources, JsonNode manifest)
    {
        var canonicalConfig = JsonNode.Parse(FrontendConfiguration.Parse(ir["config"]!.ToJsonString()).CanonicalJson)!;
        var settings = ir["config"]?["testGeneration"] ?? new JsonObject();
        var budget = settings.I("maxAttempts", 200);
        var seed = settings.I("seed", 1);
        var plans = J.Parse(sources["reports/branch-plans.json"]).AsArray();
        var probeRunner = ProbeRunner(plans, ir["config"]!, settings);
        var codeHash = Generator.Hash(Encoding.UTF8.GetBytes(string.Join("\n", sources.Where(p => p.Key.EndsWith(".cs", StringComparison.Ordinal) && !p.Key.StartsWith("Tests/", StringComparison.Ordinal) && !p.Key.StartsWith("UserCode/", StringComparison.Ordinal) && p.Key != "Api/GeneratedScenarioRegistration.g.cs" && p.Key != "Program.cs").Select(p => p.Key + "\n" + p.Value)) + "\nProgram.cs\n" + probeRunner));
        var runId = Generator.Hash(Encoding.UTF8.GetBytes(codeHash + Generator.Json(canonicalConfig) + Generator.Json(manifest["inputHashes"]!) + Version))[..16];
        var directory = "reports/test-generation/" + runId + "/";
        var summaryPath = Path.Combine(stage, directory + "summary.json");
        if (File.Exists(summaryPath))
        {
            var saved = J.Parse(File.ReadAllText(summaryPath));
            var reviewPath = Path.Combine(stage, "Tests/review.json");
            var currentReview = File.Exists(reviewPath) ? J.Parse(File.ReadAllText(reviewPath)) : new JsonObject { ["cases"] = new JsonArray() };
            var savedCases = J.Parse(File.ReadAllText(Path.Combine(stage, directory + "cases.json"))).AsArray();
            var intact = savedCases.All(c => currentReview.A("cases").Any(e => e.S("caseId") == c!.S("caseId") && e["files"] is JsonObject hashes && hashes.All(p => BoundFileMatches(stage, p))));
            manifest["judgments"]!["testGeneration"] = new JsonObject { ["status"] = "reused-preserving-user-edits-and-deletions", ["runId"] = runId, ["newCases"] = 0, ["preservedCases"] = saved.I("selectedCases", saved.I("generatedCases")), ["complete"] = saved.B("complete") && intact };
            Console.WriteLine($"Branch tests: preserved run {runId}, {saved.I("generatedCases")} cases; no user files rewritten.");
            return;
        }

        var catalog = J.Parse(sources["reports/branches.json"]).AsArray();
        var summary = new JsonObject { ["schemaVersion"] = 1, ["runId"] = runId, ["generatorVersion"] = Version, ["seed"] = seed, ["maxAttemptsPerProgram"] = budget, ["codeHash"] = codeHash, ["sourceHashes"] = manifest["inputHashes"]!.DeepClone(), ["config"] = canonicalConfig.DeepClone(), ["baselineKind"] = "csharp-characterization", ["reviewed"] = false, ["programs"] = new JsonArray(), ["limitations"] = "Finite heuristic exploration is neither unreachability proof, original COBOL equivalence, full path coverage nor MC/DC. Wall-clock exhaustion can vary across machines." };
        summary["invalidManualCandidates"] = new JsonArray(settings.A("candidates").Where(c => !plans.OfType<JsonObject>().Any(p => p.S("programId") == c.S("programId"))).Select(c => (JsonNode?)new JsonObject { ["programId"] = c.S("programId"), ["reason"] = "unknown-or-unselected-program" }).ToArray());
        var cases = new JsonArray();
        var newCases = 0;
        var reusedCases = 0;
        var trialReports = new JsonArray();
        var probeDirectory = stage + ".branch-probe";
        var stagedInputsChanged = false;
        var phase = "probe-build";
        var timer = Stopwatch.StartNew();
        var overallMs = settings.I("timeoutMs", 120000);
        try
        {
            Directory.CreateDirectory(probeDirectory);
            foreach (var (path, content) in sources.Where(p => !p.Key.StartsWith("Tests/", StringComparison.Ordinal) && !p.Key.StartsWith("UserCode/", StringComparison.Ordinal) && p.Key != "Api/GeneratedScenarioRegistration.g.cs" && p.Key != "Program.cs"))
            {
                Write(probeDirectory, path.StartsWith(".branch-probe/", StringComparison.Ordinal) ? "Probe/" + path[14..] : path, content);
            }

            Write(probeDirectory, "Program.cs", probeRunner);
            await ProcessRunner.Run("dotnet", ["build", "CobolCS.Generated.csproj", "-m:1", "--nologo"], probeDirectory, Math.Max(1, Math.Min(60, overallMs / 1000)));
            var dll = Path.Combine(probeDirectory, "bin/Debug/net10.0/CobolCS.Generated.dll");
            foreach (var plan in plans.OfType<JsonObject>())
            {
                var id = plan!.S("programId");
                phase = "exploring:" + id;
                var reached = new HashSet<string>(StringComparer.Ordinal);
                var partialReached = new HashSet<string>(StringComparer.Ordinal);
                var attempts = 0;
                var selected = 0;
                var createdForProgram = 0;
                var reusedForProgram = 0;
                var reasons = new HashSet<string>(StringComparer.Ordinal);
                var programSummary = new JsonObject { ["programId"] = id, ["attempts"] = 0, ["generatedCases"] = 0 };
                summary["programs"]!.AsArray().Add(programSummary);
                IEnumerable<JsonObject> candidates;
                try
                {
                    candidates = new BranchCandidates(plan).Create(budget, seed).ToArray();
                }
                catch (Exception error) when (error is NotSupportedException or KeyNotFoundException or InvalidOperationException)
                {
                    programSummary["blocked"] = error.Message;
                    continue;
                }

                var manual = settings.A("candidates").Where(c => c.S("programId") == id).Select(c => (JsonObject)c.DeepClone());
                candidates = manual.Concat(candidates);
                var duplicates = new HashSet<string>(StringComparer.Ordinal);
                foreach (var candidate in candidates)
                {
                    if (attempts >= budget || timer.ElapsedMilliseconds >= overallMs)
                    {
                        reasons.Add(attempts >= budget ? "budget-exhausted" : "overall-timeout");
                        break;
                    }

                    var normalized = candidate.DeepClone();
                    normalized.AsObject().Remove("inputPath");
                    normalized.AsObject().Remove("stateInjection");
                    normalized.AsObject().Remove("programId");
                    var key = Canonical(normalized);
                    if (!duplicates.Add(key))
                    {
                        continue;
                    }

                    if (candidate.S("inputPath") == "combined-leaf-candidates" && selected > 0 && catalog.OfType<JsonObject>().Where(c => c.S("programId") == id && c.B("measurable")).All(c => reached.Contains(c.S("branchId") + "/" + c.S("outcome"))))
                    {
                        break;
                    }

                    attempts++;
                    var trial = new JsonObject { ["programId"] = id, ["attempt"] = attempts, ["candidate"] = candidate.DeepClone(), ["completed"] = false };
                    trialReports.Add(trial);
                    try
                    {
                        ValidateCandidate(candidate, plan);
                        var observed = await Trial(dll, probeDirectory, id, candidate, ir["config"]!, overallMs - (int)timer.ElapsedMilliseconds);
                        trial["branchTrace"] = observed["branchTrace"]!.DeepClone();
                        if (!observed.B("completed") || observed["branchTrace"]!.B("Truncated"))
                        {
                            partialReached.UnionWith(observed["branchTrace"]!.A("Events").Select(e => e.S("BranchId") + "/" + e.S("Outcome")));
                            var reason = observed.S("reason") == string.Empty ? "trace-limit" : observed.S("reason");
                            trial["reason"] = reason;
                            reasons.Add(reason);
                            continue;
                        }

                        var covered = observed["branchTrace"]!.A("Events").Select(e => e.S("BranchId") + "/" + e.S("Outcome")).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                        trial["completed"] = true;
                        if (selected > 0 && covered.All(reached.Contains))
                        {
                            trial["reason"] = "duplicate-coverage";
                            continue;
                        }

                        var replay = new JsonObject { ["input"] = candidate["input"]!.DeepClone(), ["script"] = observed["script"]!.DeepClone() };
                        if (attempts + 2 > budget)
                        {
                            trial["reason"] = "replay-budget-exhausted";
                            reasons.Add("replay-budget-exhausted");
                            break;
                        }

                        attempts += 2;
                        trial["replayExecutions"] = 2;
                        var first = await Trial(dll, probeDirectory, id, replay, ir["config"]!, overallMs - (int)timer.ElapsedMilliseconds);
                        var second = await Trial(dll, probeDirectory, id, replay, ir["config"]!, overallMs - (int)timer.ElapsedMilliseconds);
                        if (!first.B("completed") || !second.B("completed") || !JsonNode.DeepEquals(first["observation"], observed["observation"]) || !JsonNode.DeepEquals(second["observation"], observed["observation"]) || !JsonNode.DeepEquals(first["branchTrace"], observed["branchTrace"]) || !JsonNode.DeepEquals(second["branchTrace"], observed["branchTrace"]))
                        {
                            trial["reason"] = "unstable-replay";
                            trial["observed"] = observed.DeepClone();
                            trial["firstReplay"] = first.DeepClone();
                            trial["secondReplay"] = second.DeepClone();
                            reasons.Add("unstable-replay");
                            continue;
                        }

                        var caseId = plan.S("name") + "_Branch_" + runId + "_" + selected.ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
                        var saved = SaveCase(stage, plan, runId, caseId, candidate, observed, manifest, catalog, codeHash);
                        caseId = saved.CaseId;
                        if (saved.Created)
                        {
                            newCases++;
                            createdForProgram++;
                        }
                        else
                        {
                            reusedCases++;
                            reusedForProgram++;
                        }

                        cases.Add(new JsonObject { ["programId"] = id, ["caseId"] = caseId, ["input"] = candidate["input"]!.DeepClone(), ["script"] = observed["script"]!.DeepClone(), ["branchTrace"] = observed["branchTrace"]!.DeepClone(), ["stateInjection"] = candidate["input"]?["InitialState"] is JsonObject { Count: > 0 }, ["inputPath"] = candidate["inputPath"]?.DeepClone(), ["baselineKind"] = "csharp-characterization", ["reviewed"] = false, ["observationHash"] = Generator.Hash(Encoding.UTF8.GetBytes(Generator.Json(observed["observation"]!))) });
                        reached.UnionWith(covered);
                        selected++;
                    }
                    catch (Exception error) when (error is InvalidOperationException or TimeoutException or ArgumentException or FormatException or JsonException or NotSupportedException)
                    {
                        var reason = error is TimeoutException ? "process-timeout" : "invalid-candidate: " + error.Message;
                        trial["reason"] = reason;
                        reasons.Add(reason);
                    }
                }

                var branches = catalog.OfType<JsonObject>().Where(c => c!.S("programId") == id).ToArray();
                programSummary["attempts"] = attempts;
                programSummary["selectedCases"] = selected;
                programSummary["generatedCases"] = createdForProgram;
                programSummary["reusedCases"] = reusedForProgram;
                programSummary["measurableBranches"] = branches.Count(c => c!.B("measurable"));
                programSummary["reachedBranches"] = branches.Count(c => c!.B("measurable") && reached.Contains(c.S("branchId") + "/" + c.S("outcome")));
                programSummary["partialOnlyOutcomes"] = new JsonArray(branches.Where(c => partialReached.Contains(c.S("branchId") + "/" + c.S("outcome")) && !reached.Contains(c.S("branchId") + "/" + c.S("outcome"))).Select(c => c.DeepClone()).ToArray());
                programSummary["unresolved"] = new JsonArray(branches.Where(c => !c!.B("measurable") || !reached.Contains(c.S("branchId") + "/" + c.S("outcome"))).Select(c =>
                {
                    var copy = c!.DeepClone();
                    copy["reason"] = !c.B("measurable") ? "blocked-unsupported-range" : partialReached.Contains(c.S("branchId") + "/" + c.S("outcome")) ? "observed-in-incomplete-execution; no-stable-regression-case" : UnresolvedReason(plan, c);
                    return copy;
                }).ToArray());
                programSummary["exclusions"] = new JsonArray(reasons.Order(StringComparer.Ordinal).Select(r => (JsonNode?)JsonValue.Create(r)).ToArray());
            }

            if (cases.Count != 0)
            {
                phase = "mstest-replay";
                var names = string.Join("|", cases.Select(c => "Name=" + c!.S("caseId")));
                var beforeReplay = Generator.Inventory(stage);
                try
                {
                    await ProcessRunner.Run("dotnet", ["test", "Tests/CobolCS.Generated.Tests.csproj", "-m:1", "--filter", names, "--logger", "trx;LogFileName=branch-replay.trx", "--results-directory", "TestResults"], stage, Math.Max(1, Math.Min(60, (overallMs - (int)timer.ElapsedMilliseconds) / 1000)));
                }
                finally
                {
                    if (!beforeReplay.SequenceEqual(Generator.Inventory(stage)))
                    {
                        stagedInputsChanged = true;
                        throw new IOException("Staged inputs changed during generated-test replay; original output preserved");
                    }
                }

                Adoption.ValidateResults(Path.Combine(stage, "TestResults/branch-replay.trx"), cases.Select(c => c!.S("caseId")).ToArray());
                summary["replayedMSTest"] = true;
            }
        }
        catch (Exception error) when (error is InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception or NotSupportedException or IOException)
        {
            if (stagedInputsChanged)
            {
                throw;
            }

            summary["blockedPhase"] = phase;
            summary["blocked"] = error.Message.Replace(probeDirectory, "<probe>", StringComparison.Ordinal).Replace(stage, "<stage>", StringComparison.Ordinal);
            summary["replayedMSTest"] = false;
        }
        finally
        {
            foreach (var path in Directory.EnumerateDirectories(stage, "*", SearchOption.AllDirectories).OrderByDescending(p => p.Length))
            {
                if (Path.GetFileName(path) is "bin" or "obj" or "TestResults")
                {
                    Directory.Delete(path, true);
                }
            }

            if (Directory.Exists(probeDirectory))
            {
                Directory.Delete(probeDirectory, true);
            }
        }

        summary["selectedCases"] = cases.Count;
        summary["generatedCases"] = newCases;
        summary["reusedCases"] = reusedCases;
        summary["preservedCases"] = File.Exists(Path.Combine(stage, "Tests/review.json")) ? J.Parse(File.ReadAllText(Path.Combine(stage, "Tests/review.json"))).A("cases").Count(c => c.S("generationRunId") != runId) : 0;
        summary["complete"] = summary.B("replayedMSTest") && !summary.A("invalidManualCandidates").Any() && plans.All(p => summary.A("programs").Any(s => s.S("programId") == p!.S("programId") && s.I("selectedCases") > 0 && s["blocked"] == null));
        Write(stage, directory + "summary.json", Generator.Json(summary));
        Write(stage, directory + "cases.json", Generator.Json(cases));
        Write(stage, directory + "trials.json", Generator.Json(trialReports));
        Write(stage, directory + "summary.md", "# Branch test generation\n\nC# characterization, reviewed=false. No original COBOL equivalence or unreachability proof.\n\n" + string.Join("\n", summary.A("programs").Select(p => $"- {p.S("programId")}: {p.I("attempts")} attempts, {p.I("generatedCases")} new cases, {p.I("reachedBranches")}/{p.I("measurableBranches")} measured outcomes. {p.S("blocked")}")) + "\n\n" + summary.S("blocked") + "\nWall-clock exhaustion can vary across machines. See trials.json and unresolved entries in summary.json. Existing tests and review state are preserved.\n");
        manifest["judgments"]!["testGeneration"] = new JsonObject { ["runId"] = runId, ["complete"] = summary.B("complete"), ["newCases"] = newCases, ["reusedCases"] = reusedCases, ["preservedCases"] = summary.I("preservedCases"), ["status"] = summary.B("complete") ? "characterization-generated-and-replayed" : "incomplete-see-report" };
        Console.WriteLine($"Branch tests: {newCases} new cases, {reusedCases} confirmed existing cases, generation {(summary.B("complete") ? "replayed" : "incomplete")}; {directory}summary.json");
    }

    /// <summary>単純な上書きと未探索を、到達不能の証明から区別します。</summary>
    /// <param name="plan">文列。</param>
    /// <param name="branch">未到達枝。</param>
    /// <returns>有限探索の説明。</returns>
    private static string UnresolvedReason(JsonNode plan, JsonNode branch)
    {
        var condition = branch["condition"];
        if (condition != null && Validation.Walk(condition).Any(n => n.S("kind") == "function"))
        {
            return "unsupported-inverse-expression; finite-search-not-found";
        }

        var ids = Validation.Walk(condition).Where(n => n.S("kind") == "reference").Select(n => n.S("symbolId")).ToHashSet(StringComparer.Ordinal);
        var overwritten = Validation.Walk(plan["paragraphs"]).Any(s => s.S("kind") == "move" && s["value"]?.S("kind") is "literal" or "figurative" && s.A("targets").Any(t => ids.Contains(t.S("symbolId"))));
        return overwritten ? "constant-overwrite; finite-search-not-found" : "finite-search-not-found; may-be-contradictory-or-budget-limited";
    }

    /// <summary>手動応答の未知 ID と数値でない値を拒否します。</summary>
    /// <param name="candidate">手動または自動候補。</param>
    /// <param name="plan">型情報。</param>
    private static void ValidateCandidate(JsonNode candidate, JsonNode plan)
    {
        foreach (var pair in candidate["responseValues"]?.AsObject() ?? new JsonObject())
        {
            if (!plan.A("responseItems").Any(i => i.GetValue<string>() == pair.Key))
            {
                throw new ArgumentException("Item is not a supported dependency output: " + pair.Key);
            }

            var symbol = plan.A("symbols").SingleOrDefault(s => s.S("id") == pair.Key && s.B("known")) ?? throw new ArgumentException("Unknown response item " + pair.Key);
            var value = pair.Value!.GetValue<string>();
            if (symbol["pic"]!.S("category") == "numeric")
            {
                ValidateNumeric(symbol, value);
            }
        }

        foreach (var section in new[] { "InitialState", "Arguments" })
        {
            foreach (var pair in candidate["input"]?[section]?.AsObject() ?? new JsonObject())
            {
                var symbol = plan.A("symbols").SingleOrDefault(s => s.S("member") == pair.Key);
                if (symbol?["pic"]?.S("category") == "numeric" && pair.Value is JsonValue raw && raw.TryGetValue<string>(out var number))
                {
                    ValidateNumeric(symbol, number);
                }
            }
        }

        var path = candidate.S("inputPath");
        if (path.StartsWith("accept:", StringComparison.Ordinal))
        {
            var accepts = Validation.Walk(plan["paragraphs"]).Where(n => n.S("kind") == "acceptInput").ToArray();
            var index = Array.FindIndex(accepts, a => "accept:" + a.S("id") == path);
            if (index >= 0)
            {
                var symbol = plan.A("symbols").Single(s => s.S("id") == accepts[index]["target"]!.S("symbolId"));
                if (symbol["pic"]?.S("category") == "numeric")
                {
                    ValidateNumeric(symbol, candidate["input"]!.A("AcceptInputs").ElementAt(index).GetValue<string>());
                }
            }
        }

        foreach (var pair in candidate["responseModes"]?.AsObject() ?? new JsonObject())
        {
            if (!Validation.Walk(plan["paragraphs"]).Any(n => n.S("kind") == "external" && "Dependency_" + n.S("id") == pair.Key) || pair.Value!.GetValue<int>() is < 0 or > 3)
            {
                throw new ArgumentException("Unknown response mode " + pair.Key);
            }
        }

        if (candidate["responseMode"] != null && candidate.I("responseMode") is < 0 or > 3)
        {
            throw new ArgumentException("Invalid responseMode");
        }
    }

    /// <summary>PIC に厳密に入らない数値候補を、切り捨てた逆算解として扱わず拒否します。</summary>
    /// <param name="symbol">PIC が既知の基本項目。</param>
    /// <param name="value">精密な候補文字列。</param>
    private static void ValidateNumeric(JsonNode symbol, string value)
    {
        var number = Number.Parse(value);
        var pic = symbol["pic"]!;
        var units = number.Units(pic.I("scale"));
        if (number != new Number(units, System.Numerics.BigInteger.Pow(10, pic.I("scale"))) || System.Numerics.BigInteger.Abs(units) >= System.Numerics.BigInteger.Pow(10, pic.I("digits")) || (!pic.B("signed") && number.Numerator.Sign < 0))
        {
            throw new ArgumentException("Invalid candidate outside exact PIC range/scale: " + symbol.S("id") + "=" + value);
        }
    }

    /// <summary>同期呼出しも別プロセスの制限で回収します。</summary>
    /// <param name="dll">ビルド済み探索 executable。</param>
    /// <param name="directory">作業領域。</param>
    /// <param name="id">対象プログラム。</param>
    /// <param name="candidate">固定入力または探索候補。</param>
    /// <param name="config">既存制限。</param>
    /// <param name="remainingMs">全体の残り時間。</param>
    /// <returns>完走または途中 trace。</returns>
    private static async Task<JsonNode> Trial(string dll, string directory, string id, JsonNode candidate, JsonNode config, int remainingMs)
    {
        if (remainingMs <= 0)
        {
            throw new TimeoutException("Overall exploration timeout");
        }

        Write(directory, "candidate.json", Generator.Json(candidate));
        var output = await ProcessRunner.Run("dotnet", [dll, id, "candidate.json"], directory, Math.Max(1, Math.Min(((config.I("timeoutMs", 10000) + 999) / 1000) + 1, (remainingMs + 999) / 1000)), maxOutputCharacters: 1048576);
        if (Encoding.UTF8.GetByteCount(output) > 1048576)
        {
            throw new NotSupportedException("observation-output-limit");
        }

        return J.Parse(output);
    }

    /// <summary>探索 executable は出力に配布せず、保存テストは固定 Replay API だけを使います。</summary>
    /// <param name="plans">型情報。</param>
    /// <param name="config">既存実行制限。</param>
    /// <param name="settings">trace 上限。</param>
    /// <returns>一試行 runner。</returns>
    private static string ProbeRunner(JsonArray plans, JsonNode config, JsonNode settings)
    {
        var code = new StringBuilder("// <auto-generated/>\nusing System.Text.Json.Nodes;\nusing CobolCS.Generated;\nvar plan = JsonNode.Parse(File.ReadAllText(args[1]))!;\nvar options = new ExecutionOptions { EnableBranchTrace = true, MaxSteps = " + config.I("maxSteps", 1000000) + ", TimeoutMs = " + config.I("timeoutMs", 10000) + ", MaxBranchEvents = " + settings.I("maxTraceEvents", 10000) + " };\nvar json = new JsonObject();\ntry\n{\nswitch (args[0])\n{\n");
        foreach (var plan in plans.OfType<JsonObject>())
        {
            var name = plan!.S("name");
            code.AppendLine($"case {JsonSerializer.Serialize(plan.S("programId"))}:\n{{\nvar probe = new {name}BranchProbe(plan);\nvar result = plan[\"script\"] == null ? new {name}Executor().Run(Characterization.Read<{name}Input>(plan[\"input\"]!.ToJsonString()), probe, options) : {name}Replay.Run(plan[\"input\"]!.ToJsonString(), plan[\"script\"]!.ToJsonString(), options);\njson[\"completed\"] = true;\njson[\"observation\"] = JsonNode.Parse(Characterization.Observation(result));\njson[\"branchTrace\"] = JsonNode.Parse(Characterization.Serialize(result.BranchTrace));\njson[\"script\"] = plan[\"script\"]?.DeepClone() ?? probe.Script;\nbreak;\n}}");
        }

        code.AppendLine("default: throw new ArgumentException(\"Unknown program\");\n}\n}\ncatch (Exception error)\n{\njson[\"completed\"] = false;\njson[\"reason\"] = error is ExecutionException execution && execution.InnerException is NotImplementedException ? \"blocked-internal-stub: \" + error.Message : error.GetType().Name + \": \" + error.Message;\njson[\"branchTrace\"] = JsonNode.Parse(Characterization.Serialize(error.Data[\"BranchTrace\"] as BranchSnapshot ?? BranchSnapshot.Empty));\n}\nConsole.WriteLine(json.ToJsonString());");
        return code.ToString();
    }

    /// <summary>実測された入力・応答・期待値とケースコードを hash で束縛します。</summary>
    /// <param name="stage">保存先。</param>
    /// <param name="plan">プログラム型。</param>
    /// <param name="runId">決定的な run。</param>
    /// <param name="caseId">TRX と一致するメソッド名。</param>
    /// <param name="candidate">固定入力。</param>
    /// <param name="observed">二回再生で一致した観測値。</param>
    /// <param name="manifest">出典。</param>
    /// <param name="catalog">原文位置。</param>
    /// <param name="codeHash">実行したコード。</param>
    /// <returns>重複を除いたケース ID と新規作成の有無。</returns>
    private static (string CaseId, bool Created) SaveCase(string stage, JsonNode plan, string runId, string caseId, JsonNode candidate, JsonNode observed, JsonNode manifest, JsonArray catalog, string codeHash)
    {
        var baselinePath = $"Tests/Baselines/BranchGenerated/{runId}/{caseId}.json";
        var codePath = $"Tests/Cases/BranchGenerated/{runId}/{caseId}.g.cs";
        var review = J.Parse(File.ReadAllText(Path.Combine(stage, "Tests/review.json")));
        foreach (var existing in review.A("cases").Where(c => c.S("programId") == plan.S("programId") && c.S("caseKind") == "branch-generated-characterization"))
        {
            var hashes = existing["files"]!.AsObject();
            if (!hashes.All(p => File.Exists(Path.Combine(stage, p.Key)) && Generator.Hash(File.ReadAllBytes(Path.Combine(stage, p.Key))) == p.Value!.GetValue<string>()))
            {
                continue;
            }

            var existingPath = hashes.Single(p => p.Key.StartsWith("Tests/Baselines/", StringComparison.Ordinal)).Key;
            var previous = J.Parse(File.ReadAllText(Path.Combine(stage, existingPath)));
            if (JsonNode.DeepEquals(previous["input"], candidate["input"]) && JsonNode.DeepEquals(previous["script"], observed["script"]) && JsonNode.DeepEquals(previous["expected"], observed["observation"]))
            {
                return (existing.S("caseId"), false);
            }
        }

        if (!review.A("cases").Any(c => c.S("generationRunId") == runId) && review.A("cases").Where(c => c.S("caseKind") == "branch-generated-characterization").Select(c => c.S("generationRunId")).Distinct(StringComparer.Ordinal).Count() >= 4)
        {
            throw new IOException("Retained generated-run limit (4): review and organize existing user cases or generate into a new directory; no user tests overwritten");
        }

        if (review.A("cases").Any(c => c.S("caseId") == caseId) || File.Exists(Path.Combine(stage, codePath)) || File.Exists(Path.Combine(stage, baselinePath)))
        {
            throw new IOException("Generated case collision: " + caseId);
        }

        var baseline = new JsonObject { ["baselineKind"] = "csharp-characterization", ["reviewReason"] = "UNREVIEWED: current generated C# observation; synthetic external responses", ["reviewed"] = false, ["sourceHashes"] = manifest["inputHashes"]!.DeepClone(), ["toolVersion"] = manifest.S("toolVersion"), ["generatorVersion"] = Version, ["generationRunId"] = runId, ["codeHash"] = codeHash, ["input"] = candidate["input"]!.DeepClone(), ["script"] = observed["script"]!.DeepClone(), ["expected"] = observed["observation"]!.DeepClone(), ["stateInjection"] = candidate["input"]?["InitialState"] is JsonObject { Count: > 0 } };
        var targets = observed["branchTrace"]!.A("Events").Select(e => e.S("BranchId") + "/" + e.S("Outcome")).ToHashSet(StringComparer.Ordinal);
        var comments = string.Join("\n", catalog.OfType<JsonObject>().Where(c => targets.Contains(c!.S("branchId") + "/" + c.S("outcome"))).Select(c => "// Measured " + c!.S("branchId") + "/" + c.S("outcome") + " " + c["source"]!.S("file") + ":" + c["source"]!["start"]!.I("line")));
        var code = "// <auto-generated/>\nusing System.Text.Json.Nodes;\nusing Microsoft.VisualStudio.TestTools.UnitTesting;\nnamespace CobolCS.Generated.Tests;\n/// <summary>現在の C# 挙動の固定。元 COBOL の独立期待値ではありません。</summary>\n[TestClass]\npublic sealed class " + caseId + "Tests\n{\n/// <summary>固定入力・順序付き応答・表示・状態・引数更新を比較します。</summary>\n[TestMethod]\npublic void " + caseId + "()\n{\n" + comments + "\nReview.RequireCharacterization(" + JsonSerializer.Serialize(plan.S("programId")) + ", " + JsonSerializer.Serialize(caseId) + ");\nvar baseline = JsonNode.Parse(File.ReadAllText(Path.Combine(Review.Root(), " + JsonSerializer.Serialize(baselinePath) + ")))!;\nvar result = " + plan.S("name") + "Replay.Run(baseline[\"input\"]!.ToJsonString(), baseline[\"script\"]!.ToJsonString());\nAssert.IsFalse(result.HasNoOpCalls);\nAssert.AreEqual(Characterization.Normalize(baseline[\"expected\"]!.ToJsonString()), Characterization.Observation(result));\n}\n}\n";
        Write(stage, baselinePath, Generator.Json(baseline));
        Write(stage, codePath, SourceFormatting.Indent(code));
        review["cases"]!.AsArray().Add(new JsonObject { ["programId"] = plan.S("programId"), ["caseId"] = caseId, ["caseKind"] = "branch-generated-characterization", ["generationRunId"] = runId, ["generatorVersion"] = Version, ["baselineKind"] = "csharp-characterization", ["reviewed"] = false, ["callStubMode"] = "Throw", ["hasNoOpCalls"] = false, ["files"] = new JsonObject { [baselinePath] = Generator.Hash(File.ReadAllBytes(Path.Combine(stage, baselinePath))), [codePath] = Generator.Hash(File.ReadAllBytes(Path.Combine(stage, codePath))) } });
        Write(stage, "Tests/review.json", Generator.Json(review));
        var provenance = "// <auto-generated/>\nnamespace CobolCS.Generated;\npublic static class Characterization_" + runId + "\n{\npublic static string Files(string programId, string caseId) => (programId, caseId) switch\n{\n" + string.Join("\n", review.A("cases").Where(c => c.S("generationRunId") == runId).Select(c => "(" + JsonSerializer.Serialize(c.S("programId")) + ", " + JsonSerializer.Serialize(c.S("caseId")) + ") => " + JsonSerializer.Serialize(c["files"]!.ToJsonString()) + ",")) + "\n_ => throw new ArgumentException(\"Unknown generated case\"),\n};\n}\n";
        Write(stage, "Testing/Characterization_" + runId + ".g.cs", SourceFormatting.Indent(SourceBackend.DocumentPublic(provenance)));
        return (caseId, true);
    }

    /// <summary>利用者のレビューから出力先外のファイルを読み出さないための束縛検査です。</summary>
    /// <param name="root">検査済み stage。</param>
    /// <param name="file">利用者所有の相対パスと hash。</param>
    /// <returns>安全なファイルが存在し、hash が一致する場合だけ true。</returns>
    private static bool BoundFileMatches(string root, KeyValuePair<string, JsonNode?> file)
    {
        var path = file.Key;
        if (!Generator.IsUserFile(path) || Path.IsPathRooted(path) || path.Contains('\\') || path.Split('/').Any(p => p is ".." or "." or ""))
        {
            throw new IOException("Unsafe review file path");
        }

        var target = Path.Combine(root, path);
        Generator.SafePath(target);
        return File.Exists(target) && Generator.Hash(File.ReadAllBytes(target)) == file.Value!.GetValue<string>();
    }

    /// <summary>JSON オブジェクトのキー順を入力同一性に含めない正準表現です。</summary>
    /// <param name="node">比較する候補。</param>
    /// <returns>順序を揃えた JSON。</returns>
    private static string Canonical(JsonNode node)
    {
        JsonNode? Sort(JsonNode? value) => value is JsonObject obj ? new JsonObject(obj.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, Sort(p.Value)))) : value is JsonArray array ? new JsonArray(array.Select(Sort).ToArray()) : value?.DeepClone();
        return Sort(node)!.ToJsonString();
    }

    /// <summary>探索 stage に UTF-8 の管理ファイルを書きます。</summary>
    /// <param name="directory">stage。</param>
    /// <param name="path">固定した安全な相対パス。</param>
    /// <param name="content">内容。</param>
    private static void Write(string directory, string path, string content)
    {
        var target = Path.Combine(directory, path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, content, new UTF8Encoding(false));
    }
}
