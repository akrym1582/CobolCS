using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CobolCS.Frontend;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>stage 内でビルド・別プロセス探索・固定再生を行い、実測だけを未レビューの xUnit へ昇格します。</summary>
internal static class BranchTestGeneration
{
    /// <summary>探索器の互換・決定性を識別する版です。</summary>
    internal const string Version = "1.2.0";

    /// <summary>SDK や未対応文の不足を通常ソース生成の失敗と混同しません。</summary>
    /// <param name="stage">既存利用者ファイルを保持した出力 stage。</param>
    /// <param name="ir">生成時だけ必要な IR。</param>
    /// <param name="sources">編集済み Stubs を呼ばない初期ソース。</param>
    /// <param name="manifest">生成判定の追加先。</param>
    /// <param name="implementationMode">通常生成と明示的な内部補完実行を区別します。</param>
    /// <param name="programId">省略時は全プログラム。</param>
    internal static void Generate(string stage, JsonNode ir, SortedDictionary<string, string> sources, JsonNode manifest, string implementationMode = "generated-pristine", string? programId = null)
        => GenerateAsync(stage, ir, sources, manifest, implementationMode, programId).GetAwaiter().GetResult();

    /// <summary>管理対象の内容から決定的な run を識別します。</summary>
    /// <param name="stage">出力先。</param>
    /// <param name="ir">解析結果。</param>
    /// <param name="sources">探索ビルド用の原型。</param>
    /// <param name="manifest">判定。</param>
    /// <param name="implementationMode">実装の選択。</param>
    /// <param name="programId">対象の限定。</param>
    /// <returns>探索と再生の完了。</returns>
    private static async Task GenerateAsync(string stage, JsonNode ir, SortedDictionary<string, string> sources, JsonNode manifest, string implementationMode, string? programId)
    {
        var canonicalConfig = JsonNode.Parse(FrontendConfiguration.Parse(ir["config"]!.ToJsonString()).CanonicalJson)!;
        var executionConfig = Generator.Json(canonicalConfig);
        if (ir["syntaxRecovery"] != null)
        {
            canonicalConfig["syntaxRecovery"] = ir["syntaxRecovery"]!.DeepClone();
        }

        var settings = ir["config"]?["testGeneration"] ?? new JsonObject();
        var budget = settings.I("maxAttempts", 200);
        var seed = settings.I("seed", 1);
        var allPlans = J.Parse(sources["reports/branch-plans.json"]).AsArray();
        var plans = new JsonArray(allPlans.Where(p => programId == null || p!.S("programId") == programId).Select(p => p!.DeepClone()).ToArray());
        var completedInternal = implementationMode == "completed-internal";
        var userHashes = completedInternal ? CompletionWorkflow.CodeHashes(stage) : new JsonObject();
        bool Include(string path) => !path.StartsWith("Tests/", StringComparison.Ordinal) && !path.StartsWith("Testing/Characterization_", StringComparison.Ordinal) && (completedInternal || !path.StartsWith("UserCode/", StringComparison.Ordinal)) && (completedInternal || path != "Api/GeneratedScenarioRegistration.g.cs") && path != "Program.cs";
        var probeSources = sources.Where(p => Include(p.Key)).ToArray();
        var probeRunner = ProbeRunner(plans, ir["config"]!, settings, completedInternal);
        var codeHash = Generator.Hash(Encoding.UTF8.GetBytes(string.Join("\n", probeSources.Where(p => p.Key.EndsWith(".cs", StringComparison.Ordinal)).Select(p => p.Key + "\n" + p.Value)) + "\nProgram.cs\n" + probeRunner));
        var runId = Generator.Hash(Encoding.UTF8.GetBytes(codeHash + executionConfig + Generator.Json(manifest["inputHashes"]!) + Generator.Json(userHashes) + implementationMode + programId + Version))[..16];
        var directory = "reports/test-generation/" + runId + "/";
        var summaryPath = Path.Combine(stage, directory + "summary.json");
        var priorCases = new JsonArray();
        var provenance = ReadProvenance(stage, runId);
        var reviewFile = Path.Combine(stage, "Tests/review.json");
        var originalReview = File.Exists(reviewFile) ? File.ReadAllBytes(reviewFile) : null;
        var provenancePath = Path.Combine(stage, "Testing/Characterization_" + runId + ".g.cs");
        var originalProvenance = File.Exists(provenancePath) ? File.ReadAllBytes(provenancePath) : null;
        var createdIds = new List<string>();
        if (File.Exists(summaryPath))
        {
            var saved = J.Parse(File.ReadAllText(summaryPath));
            var reviewPath = Path.Combine(stage, "Tests/review.json");
            var currentReview = File.Exists(reviewPath) ? J.Parse(File.ReadAllText(reviewPath)) : new JsonObject { ["cases"] = new JsonArray() };
            var savedCases = J.Parse(File.ReadAllText(Path.Combine(stage, directory + "cases.json"))).AsArray();
            priorCases = (JsonArray)savedCases.DeepClone();
            var intact = savedCases.OfType<JsonObject>().All(c => currentReview.A("cases").Any(e => e.S("caseId") == c.S("caseId") && IntactCase(stage, e)));
            if (saved.B("complete") && (intact || completedInternal))
            {
                manifest["judgments"]!["testGeneration"] = new JsonObject { ["status"] = "reused-preserving-user-edits-and-deletions", ["runId"] = runId, ["newCases"] = 0, ["preservedCases"] = savedCases.Count, ["complete"] = true };
                Console.WriteLine($"Branch tests: preserved complete run {runId}, {savedCases.Count} cases.");
                return;
            }

            ArchiveAttempt(stage, directory);
        }

        var catalog = J.Parse(sources["reports/branches.json"]).AsArray();
        var summary = new JsonObject { ["schemaVersion"] = 1, ["runId"] = runId, ["generatorVersion"] = Version, ["implementationMode"] = implementationMode, ["userSourceHashes"] = userHashes.DeepClone(), ["contractHashes"] = manifest["contractHashes"]?.DeepClone(), ["seed"] = seed, ["maxAttemptsPerProgram"] = budget, ["codeHash"] = codeHash, ["sourceHashes"] = manifest["inputHashes"]!.DeepClone(), ["config"] = canonicalConfig.DeepClone(), ["baselineKind"] = "csharp-characterization", ["reviewed"] = false, ["programs"] = new JsonArray(), ["limitations"] = "Finite heuristic exploration is neither unreachability proof, original COBOL equivalence, full path coverage nor MC/DC. Wall-clock exhaustion can vary across machines." };
        summary["invalidManualCandidates"] = new JsonArray(settings.A("candidates").Where(c => !plans.OfType<JsonObject>().Any(p => p.S("programId") == c.S("programId"))).Select(c => (JsonNode?)new JsonObject { ["programId"] = c.S("programId"), ["reason"] = "unknown-or-unselected-program" }).ToArray());
        var cases = (JsonArray)priorCases.DeepClone();
        var newCases = 0;
        var reusedCases = 0;
        var trialReports = new JsonArray();
        var probeDirectory = stage + ".branch-probe";
        var stagedInputsChanged = false;
        var phase = "probe-build";
        var timer = Stopwatch.StartNew();
        var overallMs = settings.I("timeoutMs", 120000);
        var dll = Path.Combine(probeDirectory, "bin/Debug/net10.0/CobolCS.Generated.dll");
        var probes = new ProcessRunner.ProbeSession(dll, probeDirectory);
        var performance = new JsonObject { ["trialExecutions"] = 0, ["trialDurationMs"] = 0L };
        summary["performance"] = performance;
        async Task<JsonNode> Execute(string id, JsonNode candidate)
        {
            var trialTimer = Stopwatch.StartNew();
            performance["trialExecutions"] = performance.I("trialExecutions") + 1;
            try
            {
                var remainingMs = overallMs - (int)timer.ElapsedMilliseconds;
                var result = await probes.Trial(id, candidate, Math.Min(ir["config"]!.I("timeoutMs", 10000) + 1000, remainingMs));
                var metric = result.S("workerIsolation") == "verified-static-state" ? "reusedTypeExecutions" : "isolatedAssemblyExecutions";
                performance[metric] = performance.I(metric) + 1;
                return result;
            }
            finally
            {
                performance["trialDurationMs"] = performance["trialDurationMs"]!.GetValue<long>() + trialTimer.ElapsedMilliseconds;
            }
        }

        try
        {
            Directory.CreateDirectory(probeDirectory);
            foreach (var (path, content) in probeSources)
            {
                Write(probeDirectory, path.StartsWith(".branch-probe/", StringComparison.Ordinal) ? "Probe/" + path[14..] : path, content);
            }

            Write(probeDirectory, "Program.cs", probeRunner);
            var buildTimer = Stopwatch.StartNew();
            await ProcessRunner.Run("dotnet", ["build", "CobolCS.Generated.csproj", "-m:1", "--nologo"], probeDirectory, Math.Max(1, Math.Min(60, overallMs / 1000)));
            performance["probeBuildMs"] = buildTimer.ElapsedMilliseconds;
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
                        var observed = await Execute(id, candidate);
                        trial["branchTrace"] = observed["branchTrace"]!.DeepClone();
                        trial["internalTrace"] = observed["internalTrace"]?.DeepClone();
                        trial["failure"] = observed["failure"]?.DeepClone();
                        if (!observed.B("completed") || observed["branchTrace"]!.B("Truncated") || observed["observation"]?.B("HasNoOpCalls") == true)
                        {
                            partialReached.UnionWith(observed["branchTrace"]!.A("Events").Select(e => e.S("BranchId") + "/" + e.S("Outcome")));
                            var reason = observed.S("reason") == string.Empty ? observed["observation"]?.B("HasNoOpCalls") == true ? "no-op" : "trace-limit" : observed.S("reason");
                            var dependencyFailure = observed["failure"]?.A("dependencyTrace").LastOrDefault();
                            var sourceDependency = observed["failure"]?["source"]?.S("DependencyId");
                            if (!string.IsNullOrEmpty(sourceDependency) && dependencyFailure?["Source"]?.S("DependencyId") == sourceDependency && dependencyFailure!.I("Outcome") is 1 or 2 or 3)
                            {
                                reason = "synthetic-external-outcome: " + reason;
                            }

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
                        var first = await Execute(id, replay);
                        var second = await Execute(id, replay);
                        if (!first.B("completed") || !second.B("completed") || !JsonNode.DeepEquals(first["observation"], observed["observation"]) || !JsonNode.DeepEquals(second["observation"], observed["observation"]) || !JsonNode.DeepEquals(first["branchTrace"], observed["branchTrace"]) || !JsonNode.DeepEquals(second["branchTrace"], observed["branchTrace"]))
                        {
                            trial["reason"] = "unstable-replay";
                            trial["observed"] = observed.DeepClone();
                            trial["firstReplay"] = first.DeepClone();
                            trial["secondReplay"] = second.DeepClone();
                            reasons.Add("unstable-replay");
                            continue;
                        }

                        var prefix = plan.S("name") + "_Branch_" + runId + "_";
                        var review = J.Parse(File.ReadAllText(Path.Combine(stage, "Tests/review.json")));
                        var allocated = priorCases.Concat(review.A("cases")).Concat(provenance).Select(c => c!.S("caseId")).Where(c => c.StartsWith(prefix, StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
                        var number = 0;
                        while (allocated.Contains(prefix + number.ToString("D3", System.Globalization.CultureInfo.InvariantCulture)) || File.Exists(Path.Combine(stage, $"Tests/Cases/BranchGenerated/{runId}/{prefix}{number:D3}.g.cs")) || File.Exists(Path.Combine(stage, $"Tests/Baselines/BranchGenerated/{runId}/{prefix}{number:D3}.json")))
                        {
                            number++;
                        }

                        var caseId = prefix + number.ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
                        var saved = SaveCase(stage, plan, runId, caseId, candidate, observed, manifest, catalog, codeHash, priorCases, provenance, implementationMode, userHashes);
                        caseId = saved.CaseId;
                        if (saved.Created)
                        {
                            createdIds.Add(caseId);
                            newCases++;
                            createdForProgram++;
                        }
                        else if (saved.Confirmed)
                        {
                            reusedCases++;
                            reusedForProgram++;
                        }
                        else
                        {
                            trial["reason"] = "preserved-edited-or-deleted-case";
                            reasons.Add("preserved-edited-or-deleted-case");
                            reached.UnionWith(covered);
                            selected++;
                            continue;
                        }

                        if (!cases.Any(c => c!.S("caseId") == caseId))
                        {
                            cases.Add(new JsonObject { ["programId"] = id, ["caseId"] = caseId, ["input"] = candidate["input"]!.DeepClone(), ["script"] = observed["script"]!.DeepClone(), ["branchTrace"] = observed["branchTrace"]!.DeepClone(), ["internalTrace"] = observed["internalTrace"]?.DeepClone(), ["implementationMode"] = implementationMode, ["stateInjection"] = candidate["input"]?["InitialState"] is JsonObject { Count: > 0 }, ["inputPath"] = candidate["inputPath"]?.DeepClone(), ["baselineKind"] = "csharp-characterization", ["reviewed"] = false, ["observationHash"] = Generator.Hash(Encoding.UTF8.GetBytes(Generator.Json(observed["observation"]!))) });
                        }

                        reached.UnionWith(covered);
                        selected++;
                        await probes.DisposeAsync();
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
                var names = string.Join("|", cases.Select(c => "FullyQualifiedName~" + c!.S("caseId")));
                var beforeReplay = Generator.Inventory(stage);
                var replayTimer = Stopwatch.StartNew();
                try
                {
                    if (completedInternal)
                    {
                        phase = "mstest-build";
                        await ProcessRunner.Run("dotnet", ["build", "Tests/CobolCS.Generated.Tests.csproj", "-m:1"], stage, Math.Max(1, Math.Min(60, (overallMs - (int)timer.ElapsedMilliseconds) / 1000)));
                        phase = "mstest-replay";
                    }

                    var testArguments = new List<string> { "test", "Tests/CobolCS.Generated.Tests.csproj", "-m:1", "--filter", names, "--logger", "trx;LogFileName=branch-replay.trx", "--results-directory", "TestResults" };
                    if (completedInternal)
                    {
                        testArguments.AddRange(["--no-build", "--no-restore"]);
                    }

                    await ProcessRunner.Run("dotnet", testArguments, stage, Math.Max(1, Math.Min(60, (overallMs - (int)timer.ElapsedMilliseconds) / 1000)));
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
                performance["mstestReplayMs"] = replayTimer.ElapsedMilliseconds;
                summary["replayedMSTest"] = true;
            }
        }
        catch (Exception error) when (error is InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception or NotSupportedException or IOException)
        {
            if (stagedInputsChanged || (completedInternal && phase is "probe-build" or "mstest-build"))
            {
                throw;
            }

            summary["blockedPhase"] = phase;
            summary["blocked"] = error.Message.Replace(probeDirectory, "<probe>", StringComparison.Ordinal).Replace(stage, "<stage>", StringComparison.Ordinal);
            summary["replayedMSTest"] = false;
            if (completedInternal && phase == "mstest-replay")
            {
                foreach (var id in createdIds)
                {
                    File.Delete(Path.Combine(stage, $"Tests/Cases/BranchGenerated/{runId}/{id}.g.cs"));
                    File.Delete(Path.Combine(stage, $"Tests/Baselines/BranchGenerated/{runId}/{id}.json"));
                }

                if (originalReview == null)
                {
                    File.Delete(reviewFile);
                }
                else
                {
                    File.WriteAllBytes(reviewFile, originalReview);
                }

                if (originalProvenance == null)
                {
                    File.Delete(provenancePath);
                }
                else
                {
                    File.WriteAllBytes(provenancePath, originalProvenance);
                }

                cases = (JsonArray)priorCases.DeepClone();
                newCases = 0;
                foreach (var program in summary.A("programs"))
                {
                    program["generatedCases"] = 0;
                    program["selectedCases"] = priorCases.Count(c => c!.S("programId") == program.S("programId"));
                }

                summary["discardedProvisionalCases"] = createdIds.Count;
            }
        }
        finally
        {
            await probes.DisposeAsync();
            performance["workerStarts"] = probes.Starts;
            performance["totalDurationMs"] = timer.ElapsedMilliseconds;
            Console.Error.WriteLine("Branch performance: " + performance.ToJsonString());
            foreach (var key in new[] { "trialDurationMs", "probeBuildMs", "mstestReplayMs", "totalDurationMs" })
            {
                performance.Remove(key);
            }

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
        var otherCases = File.Exists(Path.Combine(stage, "Tests/review.json")) ? J.Parse(File.ReadAllText(Path.Combine(stage, "Tests/review.json"))).A("cases").Where(c => c.S("generationRunId") != runId).Select(c => c.S("caseId")) : [];
        summary["preservedCases"] = priorCases.Select(c => c!.S("caseId")).Concat(otherCases).Distinct(StringComparer.Ordinal).Count();
        summary["complete"] = summary.B("replayedMSTest") && !summary.A("invalidManualCandidates").Any() && plans.All(p => summary.A("programs").Any(s => s.S("programId") == p!.S("programId") && s.I("selectedCases") > 0 && s["blocked"] == null));
        if (completedInternal && summary.A("programs").Any(p => p.A("exclusions").Any(e => e.GetValue<string>() is not ("budget-exhausted" or "preserved-edited-or-deleted-case") && !e.GetValue<string>().StartsWith("synthetic-external-outcome:", StringComparison.Ordinal))))
        {
            summary["complete"] = false;
        }

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
            else if (value.Length > symbol["pic"]!.I("digits") || (symbol["pic"]!.S("category") != "national" && value.Any(c => c > 127)))
            {
                throw new ArgumentException("Invalid response text length/encoding: " + pair.Key);
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

    /// <summary>探索 executable は出力に配布せず、保存テストは固定 Replay API だけを使います。</summary>
    /// <param name="plans">型情報。</param>
    /// <param name="config">既存実行制限。</param>
    /// <param name="settings">trace 上限。</param>
    /// <param name="completedInternal">編集済み内部実装では未知外部経路を停止します。</param>
    /// <returns>一試行 runner。</returns>
    private static string ProbeRunner(JsonArray plans, JsonNode config, JsonNode settings, bool completedInternal)
    {
        var code = new StringBuilder(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Templates/BranchProbeWorker.cs.txt")));
        code.Append("\n/// <summary>一試行の生成コードを新しい読み込み領域で実行します。</summary>\npublic static class ProbeEntry\n{\n/// <summary>探索または固定再生の観測を返します。</summary>\n/// <param name=\"id\">対象プログラム。</param>\n/// <param name=\"candidateJson\">固定入力と応答。</param>\n/// <returns>完了または途中trace。</returns>\npublic static string Run(string id, string candidateJson)\n{\nvar plan = JsonNode.Parse(candidateJson)!;\nvar options = new ExecutionOptions { EnableBranchTrace = true, IsExploration = " + (completedInternal ? "true" : "false") + ", MaxSteps = " + config.I("maxSteps", 1000000) + ", TimeoutMs = " + config.I("timeoutMs", 10000) + ", MaxBranchEvents = " + settings.I("maxTraceEvents", 10000) + " };\nvar json = new JsonObject();\ntry\n{\nswitch (id)\n{\n");
        foreach (var plan in plans.OfType<JsonObject>())
        {
            var name = plan!.S("name");
            code.AppendLine($"case {JsonSerializer.Serialize(plan.S("programId"))}:\n{{\nvar probe = new {name}BranchProbe(plan);\nvar result = plan[\"script\"] == null ? new {name}Executor().Run(Characterization.Read<{name}Input>(plan[\"input\"]!.ToJsonString()), probe, options) : {name}Replay.Run(plan[\"input\"]!.ToJsonString(), plan[\"script\"]!.ToJsonString(), options);\njson[\"completed\"] = true;\njson[\"internalTrace\"] = JsonNode.Parse(Characterization.Serialize(result.InternalTrace));\njson[\"observation\"] = JsonNode.Parse(Characterization.Observation(result));\njson[\"branchTrace\"] = JsonNode.Parse(Characterization.Serialize(result.BranchTrace));\njson[\"script\"] = plan[\"script\"]?.DeepClone() ?? probe.Script;\nbreak;\n}}");
        }

        code.AppendLine("default: throw new ArgumentException(\"Unknown program\");\n}\n}\ncatch (Exception error)\n{\njson[\"completed\"] = false;\njson[\"internalTrace\"] = JsonNode.Parse(Characterization.Serialize(error.Data[\"InternalTrace\"] as IReadOnlyList<InternalStubEvent> ?? Array.Empty<InternalStubEvent>()));\njson[\"failure\"] = new JsonObject { [\"exceptionType\"] = error.GetType().Name, [\"source\"] = JsonNode.Parse(Characterization.Serialize((error as ExecutionException)?.SourceLocation ?? error.Data[\"SourceLocation\"] as SourceLocation)), [\"dependencyTrace\"] = JsonNode.Parse(Characterization.Serialize(error.Data[\"DependencyTrace\"] as IReadOnlyList<DependencyTrace> ?? Array.Empty<DependencyTrace>())) };\njson[\"reason\"] = error is ExecutionException execution && execution.InnerException is NotImplementedException ? \"blocked-internal-stub: \" + error.Message : error.GetType().Name + \": \" + error.Message;\njson[\"branchTrace\"] = JsonNode.Parse(Characterization.Serialize(error.Data[\"BranchTrace\"] as BranchSnapshot ?? BranchSnapshot.Empty));\n}\nreturn json.ToJsonString();\n}\n}");
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
    /// <param name="priorCases">前回の管理された固定入力・応答・観測証跡。</param>
    /// <param name="provenance">編集前の管理 hash と新規ケースの追記先。</param>
    /// <param name="implementationMode">実測した実装経路。</param>
    /// <param name="userHashes">コンパイルした全利用者ソース。</param>
    /// <returns>重複を除いたケース ID と新規作成の有無。</returns>
    private static (string CaseId, bool Created, bool Confirmed) SaveCase(string stage, JsonNode plan, string runId, string caseId, JsonNode candidate, JsonNode observed, JsonNode manifest, JsonArray catalog, string codeHash, JsonArray priorCases, JsonArray provenance, string implementationMode, JsonObject userHashes)
    {
        var baselinePath = $"Tests/Baselines/BranchGenerated/{runId}/{caseId}.json";
        var codePath = $"Tests/Cases/BranchGenerated/{runId}/{caseId}.g.cs";
        var review = J.Parse(File.ReadAllText(Path.Combine(stage, "Tests/review.json")));
        var previousCase = priorCases.OfType<JsonObject>().SingleOrDefault(c => c.S("programId") == plan.S("programId") && JsonNode.DeepEquals(c["input"], candidate["input"]) && JsonNode.DeepEquals(c["script"], observed["script"]) && c.S("observationHash") == Generator.Hash(Encoding.UTF8.GetBytes(Generator.Json(observed["observation"]!))));
        if (previousCase != null)
        {
            var entry = review.A("cases").SingleOrDefault(c => c.S("caseId") == previousCase.S("caseId"));
            var confirmed = entry != null && IntactCase(stage, entry);
            return (previousCase.S("caseId"), false, confirmed);
        }

        foreach (var existing in review.A("cases").Where(c => c.S("programId") == plan.S("programId") && c.S("caseKind") == "branch-generated-characterization" && (implementationMode != "completed-internal" || c.S("generationRunId") == runId)))
        {
            var hashes = existing["files"]!.AsObject();
            if (!IntactCase(stage, existing))
            {
                continue;
            }

            var existingPath = hashes.Single(p => p.Key.StartsWith("Tests/Baselines/", StringComparison.Ordinal)).Key;
            var previous = J.Parse(File.ReadAllText(Path.Combine(stage, existingPath)));
            if (JsonNode.DeepEquals(previous["input"], candidate["input"]) && JsonNode.DeepEquals(previous["script"], observed["script"]) && JsonNode.DeepEquals(previous["expected"], observed["observation"]))
            {
                return (existing.S("caseId"), false, true);
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

        var baseline = new JsonObject { ["baselineKind"] = "csharp-characterization", ["reviewReason"] = "UNREVIEWED: current generated C# observation; synthetic external responses", ["reviewed"] = false, ["sourceHashes"] = manifest["inputHashes"]!.DeepClone(), ["toolVersion"] = manifest.S("toolVersion"), ["generatorVersion"] = Version, ["generationRunId"] = runId, ["codeHash"] = codeHash, ["implementationMode"] = implementationMode, ["userSourceHashes"] = userHashes.DeepClone(), ["contractHashes"] = manifest["contractHashes"]?.DeepClone(), ["configHash"] = manifest.S("configHash"), ["internalTrace"] = observed["internalTrace"]?.DeepClone(), ["input"] = candidate["input"]!.DeepClone(), ["script"] = observed["script"]!.DeepClone(), ["expected"] = observed["observation"]!.DeepClone(), ["stateInjection"] = candidate["input"]?["InitialState"] is JsonObject { Count: > 0 } };
        var targets = observed["branchTrace"]!.A("Events").Select(e => e.S("BranchId") + "/" + e.S("Outcome")).ToHashSet(StringComparer.Ordinal);
        var comments = string.Join("\n", catalog.OfType<JsonObject>().Where(c => targets.Contains(c!.S("branchId") + "/" + c.S("outcome"))).Select(c => "// Measured " + c!.S("branchId") + "/" + c.S("outcome") + " " + c["source"]!.S("file") + ":" + c["source"]!["start"]!.I("line")));
        var code = "// <auto-generated/>\nusing System.Text.Json.Nodes;\nusing Xunit;\nnamespace CobolCS.Generated.Tests;\n/// <summary>現在の C# 挙動の固定。元 COBOL の独立期待値ではありません。</summary>\npublic sealed class " + caseId + "Tests\n{\n/// <summary>固定入力・順序付き応答・表示・状態・引数更新を比較します。</summary>\n[Fact]\n[Trait(\"Category\", \"Characterization\")]\npublic void " + caseId + "()\n{\n" + comments + "\nReview.RequireCharacterization(" + JsonSerializer.Serialize(plan.S("programId")) + ", " + JsonSerializer.Serialize(caseId) + ");\nvar baseline = JsonNode.Parse(File.ReadAllText(Path.Combine(Review.Root(), " + JsonSerializer.Serialize(baselinePath) + ")))!;\nvar result = " + plan.S("name") + "Replay.Run(baseline[\"input\"]!.ToJsonString(), baseline[\"script\"]!.ToJsonString());\nAssert.False(result.HasNoOpCalls);\nAssert.Equal(Characterization.Normalize(baseline[\"expected\"]!.ToJsonString()), Characterization.Observation(result));\n}\n}\n";
        Write(stage, baselinePath, Generator.Json(baseline));
        Write(stage, codePath, SourceFormatting.Indent(code));
        review["cases"]!.AsArray().Add(new JsonObject { ["programId"] = plan.S("programId"), ["caseId"] = caseId, ["caseKind"] = "branch-generated-characterization", ["implementationMode"] = implementationMode, ["implementationHashes"] = userHashes.DeepClone(), ["generationRunId"] = runId, ["generatorVersion"] = Version, ["baselineKind"] = "csharp-characterization", ["reviewed"] = false, ["callStubMode"] = "Throw", ["hasNoOpCalls"] = false, ["files"] = new JsonObject { [baselinePath] = Generator.Hash(File.ReadAllBytes(Path.Combine(stage, baselinePath))), [codePath] = Generator.Hash(File.ReadAllBytes(Path.Combine(stage, codePath))) } });
        Write(stage, "Tests/review.json", Generator.Json(review));
        provenance.Add(new JsonObject { ["programId"] = plan.S("programId"), ["caseId"] = caseId, ["files"] = review.A("cases").Single(c => c.S("caseId") == caseId)["files"]!.DeepClone() });
        var provenanceCode = "// <auto-generated/>\nnamespace CobolCS.Generated;\npublic static class Characterization_" + runId + "\n{\npublic static string Files(string programId, string caseId) => (programId, caseId) switch\n{\n" + string.Join("\n", provenance.OfType<JsonObject>().Select(c => "(" + JsonSerializer.Serialize(c.S("programId")) + ", " + JsonSerializer.Serialize(c.S("caseId")) + ") => " + JsonSerializer.Serialize(c["files"]!.ToJsonString()) + ",")) + "\n_ => throw new ArgumentException(\"Unknown generated case\"),\n};\n}\n";
        Write(stage, "Testing/Characterization_" + runId + ".g.cs", SourceFormatting.Indent(SourceBackend.DocumentPublic(provenanceCode)));
        return (caseId, true, true);
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

    /// <summary>既存形式の管理された C# 来歴を読み、利用者の review hash から再構築しません。</summary>
    /// <param name="stage">管理対象検査後の stage。</param>
    /// <param name="runId">同じ生成 run。</param>
    /// <returns>保存時の項目・ケース・ファイル hash。</returns>
    private static JsonArray ReadProvenance(string stage, string runId)
    {
        if (runId.Length != 16 || runId.Any(c => !char.IsAsciiHexDigit(c)))
        {
            throw new IOException("Invalid generation run in characterization provenance");
        }

        var result = new JsonArray();
        var path = Path.Combine(stage, "Testing/Characterization_" + runId + ".g.cs");
        if (!File.Exists(path))
        {
            return result;
        }

        foreach (var line in File.ReadLines(path).Where(l => l.TrimStart().StartsWith("(\"", StringComparison.Ordinal)))
        {
            var literals = System.Text.RegularExpressions.Regex.Matches(line, "\"(?:\\\\.|[^\"\\\\])*\"").Select(m => JsonSerializer.Deserialize<string>(m.Value)!).ToArray();
            if (literals.Length != 3)
            {
                throw new IOException("Invalid managed characterization provenance");
            }

            result.Add(new JsonObject { ["programId"] = literals[0], ["caseId"] = literals[1], ["files"] = J.Parse(literals[2]) });
        }

        return result;
    }

    /// <summary>他 run から再利用したケースも元 run の管理来歴で検査します。</summary>
    /// <param name="stage">検査済み stage。</param>
    /// <param name="entry">利用者所有のレビューエントリ。</param>
    /// <returns>元の来歴または明示的レビューに束縛された健全なケース。</returns>
    private static bool IntactCase(string stage, JsonNode entry)
    {
        var original = ReadProvenance(stage, entry.S("generationRunId")).OfType<JsonObject>().SingleOrDefault(p => p.S("caseId") == entry.S("caseId") && p.S("programId") == entry.S("programId"));
        return original != null && entry["files"] is JsonObject hashes && hashes.All(p => BoundFileMatches(stage, p)) && (entry.B("reviewed") || JsonNode.DeepEquals(original["files"], hashes));
    }

    /// <summary>再試行前の証跡を同じ run 配下の管理履歴へ保存します。</summary>
    /// <param name="stage">原子的に反映する stage。</param>
    /// <param name="directory">既存 run の相対ディレクトリ。</param>
    private static void ArchiveAttempt(string stage, string directory)
    {
        var index = 1;
        while (Directory.Exists(Path.Combine(stage, directory, "attempts", index.ToString("D4", System.Globalization.CultureInfo.InvariantCulture))))
        {
            index++;
        }

        var history = Path.Combine(stage, directory, "attempts", index.ToString("D4", System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(history);
        foreach (var file in new[] { "summary.json", "cases.json", "trials.json", "summary.md" })
        {
            File.Copy(Path.Combine(stage, directory, file), Path.Combine(history, file));
        }
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
