using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;

namespace CobolCS.Runtime;

/// <summary>固定ケースを実行し、生成レポートを変更せず実行別の証跡を保存します。</summary>
public sealed class ReferenceVerification
{
    private readonly JsonNode context;
    private readonly JsonNode inventory;
    private readonly string root;
    private readonly string executionDirectory;
    private readonly JsonArray results = [];
    private readonly object sync = new();

    /// <summary>Initializes a new instance of the <see cref="ReferenceVerification"/> class. 一回の試験の保存先を作ります。</summary>
    /// <param name="context">元ソース、設定、観測境界の識別情報。</param>
    /// <param name="inventory">生成時のケースファイル一覧、ハッシュと取込エラー。</param>
    /// <param name="root">コピーされた利用者ケースのディレクトリ。</param>
    /// <param name="resultsRoot">実行レポートの保存先。</param>
    public ReferenceVerification(JsonNode context, JsonNode inventory, string root, string resultsRoot)
    {
        this.context = context;
        this.inventory = inventory;
        this.root = root;
        executionDirectory = Path.Combine(resultsRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(executionDirectory);
        Save(Path.Combine(executionDirectory, "started.json"), new JsonObject { ["schemaVersion"] = "1.0", ["status"] = "running", ["startedAt"] = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture), ["context"] = context.DeepClone(), ["inventory"] = inventory.DeepClone() });
    }

    /// <summary>Gets TRX と照合する実行 ID です。</summary>
    public string ExecutionId => Path.GetFileName(executionDirectory);

    /// <summary>Gets 完了レポートのパスです。未完了時にはまだ存在しません。</summary>
    public string SummaryPath => Path.Combine(executionDirectory, "summary.json");

    /// <summary>取込失敗、欠落、重複、生成後の編集を検査するゲートです。</summary>
    /// <returns>不備の理由。一件でもあれば MSTest を失敗にします。</returns>
    public string CheckGate()
    {
        var errors = inventory.A("errors").Select(e => e.GetValue<string>()).ToList();
        var contractPath = Path.Combine(root, "../Contracts/full-state-v1.json");
        if (!File.Exists(contractPath) || ReferenceCase.Hash(File.ReadAllBytes(contractPath)) != context.S("contractFileHash"))
        {
            errors.Add("Missing/stale observation contract");
        }

        if (context["contractFiles"] is JsonObject contractFiles)
        {
            foreach (var (name, hash) in contractFiles)
            {
                var file = Path.Combine(root, "../Contracts", name);
                if (!File.Exists(file) || ReferenceCase.Hash(File.ReadAllBytes(file)) != hash!.GetValue<string>())
                {
                    errors.Add("Missing/stale business contract: " + name);
                }
            }
        }

        foreach (var item in inventory.A("cases"))
        {
            var path = Path.Combine(root, item.S("path"));
            if (!File.Exists(path) || ReferenceCase.Hash(File.ReadAllBytes(path)) != item.S("hash"))
            {
                errors.Add("Missing/stale registered case: " + item.S("path"));
            }
        }

        var registered = inventory.A("cases").Select(c => c.S("path")).ToHashSet(StringComparer.Ordinal);
        if (Directory.Exists(root))
        {
            foreach (var path in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (!relative.StartsWith("Data/", StringComparison.Ordinal) && !registered.Contains(relative))
                {
                    errors.Add("Unregistered case; regenerate: " + relative);
                }
            }
        }

        var text = string.Join('\n', errors);
        lock (sync)
        {
            Save(Path.Combine(executionDirectory, "gate.json"), new JsonObject { ["status"] = errors.Count == 0 ? "passed" : "failed", ["errors"] = new JsonArray(errors.Select(e => (JsonNode?)JsonValue.Create(e)).ToArray()) });
        }

        return text;
    }

    /// <summary>独立した固定入力で実行し、比較結果を原文 ID と共に保存します。</summary>
    /// <param name="path">生成時に登録した相対パス。</param>
    /// <param name="run">生成された業務命令列の実行入口。</param>
    /// <returns>失敗理由。空文字列なら当該ケースの完全比較が一致しました。</returns>
    public string Execute(string path, Func<string, JsonNode, JsonArray, JsonObject> run)
    {
        var record = new JsonObject { ["schemaVersion"] = "1.0", ["path"] = path, ["status"] = "infrastructure-failed", ["sourceHashes"] = context["inputHashes"]!.DeepClone(), ["configHash"] = context.S("configHash"), ["contractHash"] = null };
        var trace = new JsonArray();
        var failure = string.Empty;
        try
        {
            var registered = inventory.A("cases").Single(c => c.S("path") == path);
            var bytes = File.ReadAllBytes(Path.Combine(root, path));
            record["caseHash"] = ReferenceCase.Hash(bytes);
            if (record.S("caseHash") != registered.S("hash"))
            {
                throw new InvalidDataException("Stale case: regenerate without overwriting expected values");
            }

            var value = ReferenceCase.Parse(Encoding.UTF8.GetString(bytes));
            ReferenceCase.Validate(value, context, root);
            record["caseId"] = value.S("caseId");
            record["programId"] = value.S("programId");
            record["origin"] = value.S("origin");
            record["required"] = value.B("required");
            record["referenceExecution"] = value["evidence"]?["referenceExecution"]?.DeepClone();
            record["syntheticCapture"] = value["evidence"]!.B("syntheticCapture");
            record["syntheticDependencies"] = value["evidence"]!.B("syntheticDependencies");
            record["fixedResponseConditions"] = value["input"]!.A("externalResponses").Any();
            record["scope"] = "This input, reviewed observation contract and fixed-response conditions only; no all-input equivalence or physical storage compatibility";
            var contract = ReferenceCase.ResolveContract(value, context);
            record["contractHash"] = value["contract"]!.S("hash");
            record["conditions"] = value["evidence"]!["conditions"]!.DeepClone();
            record["inputBoundary"] = contract == null ? "initial-state-injection" : "reviewed-business-input";
            var fixture = contract == null ? value["input"]! : BusinessObservation.Input(value["input"]!, contract);
            var actual = run(value.S("programId"), fixture, trace);
            if (contract != null)
            {
                actual = BusinessObservation.Observe(actual, contract);
                record["businessKeys"] = contract["observations"]!.DeepClone();
            }

            var comparison = contract == null ? ReferenceCase.Compare(value["expected"], actual) : BusinessObservation.Compare(value["expected"]!, actual, contract);
            record["comparison"] = comparison;
            record["status"] = comparison.B("matched") ? "matched" : "mismatched";
            if (!comparison.B("matched"))
            {
                failure = value.S("caseId") + ": " + comparison.ToJsonString();
            }
        }
        catch (Exception error)
        {
            failure = error.Message;
            record["error"] = failure;
        }
        finally
        {
            record["trace"] = trace;
            record["traceComplete"] = record.S("status") is "matched" or "mismatched";
            lock (sync)
            {
                results.Add(record);
                Save(Path.Combine(executionDirectory, ReferenceCase.Hash(Encoding.UTF8.GetBytes(path)) + ".json"), record);
            }
        }

        return failure;
    }

    /// <summary>生成 C# の自己観測を回帰として再生します。独立検証へ昇格しません。</summary>
    /// <param name="programId">対象プログラム。</param>
    /// <param name="run">固定 API の実行入口。</param>
    /// <returns>失敗理由。自己観測のみの一致は COBOL 同値の証明ではありません。</returns>
    public string Characterize(string programId, Func<string, JsonNode, JsonArray, JsonObject> run)
    {
        var item = inventory.A("characterization").Single(c => c.S("programId") == programId);
        var record = new JsonObject { ["caseId"] = "characterization:" + programId, ["programId"] = programId, ["origin"] = "csharp-characterization", ["status"] = "infrastructure-failed" };
        var trace = new JsonArray();
        var failure = string.Empty;
        try
        {
            if (item["error"] != null)
            {
                throw new InvalidOperationException(item.S("error"));
            }

            var actual = run(programId, new JsonObject { ["fields"] = new JsonObject() }, trace);
            var comparison = ReferenceCase.Compare(item["expected"], actual);
            record["comparison"] = comparison;
            record["status"] = comparison.B("matched") ? "matched" : "mismatched";
            if (!comparison.B("matched"))
            {
                failure = comparison.ToJsonString();
            }
        }
        catch (Exception error)
        {
            failure = error.Message;
            record["error"] = failure;
        }
        finally
        {
            record["trace"] = trace;
            record["traceComplete"] = record.S("status") is "matched" or "mismatched";
            lock (sync)
            {
                results.Add(record);
                Save(Path.Combine(executionDirectory, "characterization-" + ReferenceCase.Hash(Encoding.UTF8.GetBytes(programId)) + ".json"), record);
            }
        }

        return failure;
    }

    /// <summary>終了したケースと未実行を分離し、完了した実行 summary を原子的に保存します。</summary>
    /// <param name="assemblyPath">実行した生成コードのアセンブリ。</param>
    public void Complete(string assemblyPath)
    {
        lock (sync)
        {
            var counts = new JsonObject();
            foreach (var origin in new[] { "csharp-characterization", "specification", "cobol-reference" })
            {
                counts[origin] = new JsonObject { ["matched"] = results.Count(r => r!.S("origin") == origin && !r!.B("syntheticCapture") && r!.S("status") == "matched"), ["mismatched"] = results.Count(r => r!.S("origin") == origin && !r!.B("syntheticCapture") && r!.S("status") == "mismatched") };
            }

            var processors = new JsonArray();
            foreach (var group in results.Where(r => r!.S("origin") == "cobol-reference").GroupBy(r => (Processor: r!["referenceExecution"]?.S("processor"), Version: r!["referenceExecution"]?.S("version"), Options: r!["referenceExecution"]?.S("compileOptions"), Synthetic: r!.B("syntheticCapture"), Conditional: r!.B("syntheticDependencies") || r.B("fixedResponseConditions"), Conditions: ReferenceCase.ReviewHash(new JsonObject { ["conditions"] = r!["conditions"]?.DeepClone() }))))
            {
                processors.Add(new JsonObject { ["processor"] = group.Key.Processor, ["version"] = group.Key.Version, ["compileOptions"] = group.Key.Options, ["conditionsHash"] = group.Key.Conditions, ["syntheticDependencies"] = group.Key.Conditional, ["syntheticCapture"] = group.Key.Synthetic, ["matched"] = group.Count(r => r!.S("status") == "matched"), ["mismatched"] = group.Count(r => r!.S("status") == "mismatched"), ["conditional"] = group.Count(r => r!.B("syntheticDependencies") || r!.B("fixedResponseConditions")) });
            }

            var reached = new JsonArray();
            var covered = 0;
            foreach (var branch in context.A("branchCatalog"))
            {
                var observed = results.Where(r => r!.S("programId") == branch.S("programId")).SelectMany(r => r!.A("trace")).Where(t => t.I("instruction") == branch.I("instruction") && t["outcome"] != null).Select(t => t.B("outcome")).Distinct().ToArray();
                covered += observed.Length;
                reached.Add(new JsonObject { ["branch"] = branch.DeepClone(), ["reachedOutcomes"] = new JsonArray(observed.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray()) });
            }

            var missing = inventory.A("cases").Count(c => !results.Any(r => r!.S("path") == c.S("path"))) + inventory.A("characterization").Count(c => !results.Any(r => r!.S("origin") == "csharp-characterization" && r!.S("programId") == c.S("programId")));
            var gatePassed = File.Exists(Path.Combine(executionDirectory, "gate.json")) && J.Parse(File.ReadAllText(Path.Combine(executionDirectory, "gate.json"))).S("status") == "passed";
            var executedCodeArtifacts = new JsonObject();
            if (context["codeArtifacts"] is JsonObject artifacts)
            {
                foreach (var (path, _) in artifacts)
                {
                    var file = Path.GetFullPath(Path.Combine(root, "../..", path));
                    executedCodeArtifacts[path] = File.Exists(file) ? ReferenceCase.Hash(File.ReadAllBytes(file)) : null;
                }
            }

            var work = new JsonArray();
            foreach (var result in results.Where(r => r!.S("status") != "matched"))
            {
                var differences = result!["comparison"]?.A("differences").ToArray() ?? [];
                if (differences.Length == 0)
                {
                    work.Add(new JsonObject { ["caseId"] = result.S("caseId"), ["programId"] = result.S("programId"), ["ruleId"] = "verification-infrastructure-failure", ["reason"] = result.S("error"), ["status"] = "open" });
                }

                foreach (var difference in differences)
                {
                    work.Add(new JsonObject { ["caseId"] = result.S("caseId"), ["programId"] = result.S("programId"), ["statementId"] = difference["statementId"]?.DeepClone(), ["businessKey"] = difference["businessKey"]?.DeepClone(), ["path"] = difference.S("path"), ["ruleId"] = "reference-comparison-mismatch", ["status"] = "open" });
                }
            }

            Save(Path.Combine(executionDirectory, "work-items.json"), work);
            Save(Path.Combine(executionDirectory, "summary.json"), new JsonObject
            {
                ["schemaVersion"] = "1.0",
                ["completed"] = true,
                ["status"] = !gatePassed || missing != 0 || results.Any(r => r!.S("status") != "matched") ? "failed" : !inventory.A("cases").Any() ? "not-configured" : "matched",
                ["context"] = context.DeepClone(),
                ["caseInventory"] = inventory.DeepClone(),
                ["codeHash"] = ReferenceCase.Hash(File.ReadAllBytes(assemblyPath)),
                ["compiledSourceFingerprint"] = Assembly.LoadFrom(assemblyPath).GetCustomAttributes<AssemblyMetadataAttribute>().SingleOrDefault(a => a.Key == "CobolCS.SourceFingerprint")?.Value,
                ["executedCodeArtifacts"] = executedCodeArtifacts,
                ["countsByOrigin"] = counts,
                ["cobolReferenceByProcessor"] = processors,
                ["syntheticReferenceContractTests"] = results.Count(r => r!.B("syntheticCapture")),
                ["unexecuted"] = missing,
                ["branchCoverage"] = new JsonObject { ["reachedOutcomes"] = covered, ["measurableOutcomes"] = context.A("branchCatalog").Count() * 2, ["catalog"] = reached, ["unmeasurable"] = context["unmeasurableBranches"]?.DeepClone(), ["complete"] = results.All(r => r!.B("traceComplete")) },
                ["infrastructureFailures"] = results.Count(r => r!.S("status") == "infrastructure-failed"),
                ["cases"] = results.DeepClone(),
                ["completedAt"] = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                ["executionId"] = Path.GetFileName(executionDirectory),
                ["equivalence"] = "not-established",
            });
        }
    }

    private static void Save(string path, JsonNode value)
    {
        File.WriteAllText(path + ".tmp", value.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n");
        File.Move(path + ".tmp", path, overwrite: true);
    }
}
