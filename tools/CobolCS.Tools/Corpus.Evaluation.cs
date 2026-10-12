using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CobolCS.Runtime;
using static CobolCS.Tools.Workspace;

namespace CobolCS.Tools;

/// <summary>解析・生成・ビルド・独立期待値の一致を別々に測定します。</summary>
public static partial class Corpus
{
    /// <summary>固定入力をケースごとに分離して製品CLIへ渡します。</summary>
    /// <param name="lockPath">固定マニフェスト。</param>
    /// <param name="sourceRoot">取得済み原文。</param>
    /// <param name="output">新規の測定先。</param>
    /// <param name="includeGpl">明示選択したGPL入力を含めるか。</param>
    /// <param name="build">生成物のビルド・実行も検査するか。</param>
    /// <param name="shardIndex">ゼロ始まりの分割番号。</param>
    /// <param name="shardCount">全ケースを重複なく分割する総数。</param>
    /// <returns>工程とケース別の全測定結果。</returns>
    public static JsonObject Evaluate(string lockPath, string sourceRoot, string output, bool includeGpl = false, bool build = false, int shardIndex = 0, int shardCount = 1)
    {
        if (shardCount is < 1 or > 16 || shardIndex < 0 || shardIndex >= shardCount)
        {
            throw new ArgumentException("Invalid corpus shard index/count");
        }

        lockPath = Path.GetFullPath(lockPath);
        sourceRoot = Path.GetFullPath(sourceRoot);
        output = Path.GetFullPath(output);
        var (locked, cases) = LoadCases(lockPath, sourceRoot, includeGpl);
        NewDirectory(output);
        var rows = new JsonArray();
        var summary = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["scope"] = locked["scope"]?.DeepClone(),
            ["upstream"] = locked["upstream"]?.DeepClone(),
            ["lockSha256"] = Digest(lockPath),
            ["includeGpl"] = includeGpl,
            ["expectedCases"] = cases.Length,
            ["expectedCaseIds"] = new JsonArray(cases.Select(c => (JsonNode?)JsonValue.Create(c.S("id"))).ToArray()),
            ["shardIndex"] = shardIndex,
            ["shardCount"] = shardCount,
            ["minimumGenerated"] = locked.I("minimumGenerated"),
            ["expectedExecutionIds"] = new JsonArray(cases.Where(c => c["execution"] is not null).Select(c => (JsonNode?)JsonValue.Create(c.S("id"))).ToArray()),
            ["compilerCommit"] = Command(["git", "rev-parse", "HEAD"]).S("stdout").Trim(),
            ["compilerSourceSha256"] = CompilerHash(),
            ["dotnetVersion"] = Command(["dotnet", "--version"]).S("stdout").Trim(),
            ["backend"] = "source",
            ["originalCobolComparison"] = "not-performed",
            ["cases"] = rows,
        };
        var selected = cases.Where((_, index) => index % shardCount == shardIndex).ToArray();
        Write(Path.Combine(output, "summary.json"), summary);
        foreach (var item in selected)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var work = Path.Combine(output, item.S("id"));
            Directory.CreateDirectory(work);
            var config = Path.Combine(work, "config.json");
            Write(config, new JsonObject { ["sourceFormat"] = item.S("sourceFormat") });
            var source = Confined(sourceRoot, item.S("source"));
            var options = new List<string> { "--config", config };
            foreach (var copy in item.A("copybookPaths"))
            {
                options.AddRange(["--copybook-path", Confined(sourceRoot, copy.GetValue<string>())]);
            }

            var phases = new JsonObject();
            var row = new JsonObject
            {
                ["id"] = item.S("id"),
                ["source"] = item.S("source"),
                ["sha256"] = item.S("sha256"),
                ["license"] = item.S("license"),
                ["intent"] = item["intent"]?.GetValue<string>() ?? "unreviewed",
                ["analyzed"] = false,
                ["analysisHasErrors"] = null,
                ["strictGenerated"] = false,
                ["scaffoldAnalyzed"] = false,
                ["scaffoldAnalysisHasErrors"] = null,
                ["scaffoldGenerated"] = false,
                ["built"] = false,
                ["fixtureExecuted"] = false,
                ["stubFixtureExecuted"] = false,
                ["matchedIndependentExpectation"] = false,
                ["matchedOriginalCobol"] = false,
                ["diagnosticCounts"] = new JsonObject(),
                ["categories"] = new JsonObject(),
                ["phases"] = phases,
            };
            JsonObject Phase(string name, IEnumerable<string> args)
            {
                var phaseTimer = System.Diagnostics.Stopwatch.StartNew();
                var result = Command(args);
                Write(Path.Combine(work, name + ".log.json"), result);
                phases[name] = new JsonObject { ["exitCode"] = result.I("exitCode"), ["timedOut"] = result.B("timedOut"), ["durationMs"] = phaseTimer.ElapsedMilliseconds };
                return result;
            }

            string[] CliArgs(params string[] args) => new[] { "dotnet" }.Concat(Workspace.Cli).Concat(args).ToArray();
            var analyzed = Path.Combine(work, "analysis");
            Phase("analyze", CliArgs("analyze", source, "--output", analyzed).Concat(options));
            if (File.Exists(Path.Combine(analyzed, "manifest.json")))
            {
                row["analyzed"] = Read(Path.Combine(analyzed, "manifest.json"))["judgments"]!.B("analyzed");
                var diagnostics = Read(Path.Combine(analyzed, "reports/diagnostics.json")).AsArray().Select(d => d!).ToArray();
                row["analysisHasErrors"] = diagnostics.Any(d => d.S("severity") == "error");
                row["diagnosticCounts"] = Counts(diagnostics.Select(d => d.S("code")));
                row["categories"] = Counts(diagnostics.Select(d => Classification(d.S("code"))));
            }

            var generated = Path.Combine(work, "strict");
            var result = Phase("strict", CliArgs("generate", source, "--output", generated, "--backend", "source", "--mode", "strict").Concat(options));
            row["strictGenerated"] = result.I("exitCode") == 0 && File.Exists(Path.Combine(generated, "CobolCS.Generated.csproj"));
            if (!row.B("strictGenerated"))
            {
                generated = Path.Combine(work, "scaffold");
                result = Phase("scaffold", CliArgs("generate", source, "--output", generated, "--backend", "source", "--mode", "scaffold").Concat(options));
                row["scaffoldGenerated"] = result.I("exitCode") == 0 && File.Exists(Path.Combine(generated, "CobolCS.Generated.csproj"));
                if (File.Exists(Path.Combine(generated, "manifest.json")))
                {
                    row["scaffoldAnalyzed"] = Read(Path.Combine(generated, "manifest.json"))["judgments"]!.B("analyzed");
                    row["scaffoldAnalysisHasErrors"] = Read(Path.Combine(generated, "reports/diagnostics.json")).AsArray().Any(d => d!.S("severity") == "error");
                }
            }

            if (File.Exists(Path.Combine(generated, "reports/stubs.json")))
            {
                var stubs = Read(Path.Combine(generated, "reports/stubs.json")).AsArray();
                row["stubKinds"] = Counts(stubs.Select(s => s!["statement"]?["kind"]?.GetValue<string>() ?? s["kind"]?.GetValue<string>() ?? "unknown"));
                row["stubCount"] = stubs.Count;
            }

            if (build && (row.B("strictGenerated") || row.B("scaffoldGenerated")))
            {
                result = Phase("build", CliArgs("build", generated));
                row["built"] = result.I("exitCode") == 0;
                row["buildDiagnosticCodes"] = new JsonArray(Regex.Matches(result.S("stdout") + result.S("stderr"), @"error (CS\d+|SA\d+|MSB\d+|NU\d+):").Select(m => m.Groups[1].Value).Distinct().Order(StringComparer.Ordinal).Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());
                if (row.B("built") && item["execution"] is { } execution)
                {
                    var fixture = Path.Combine(work, "fixture.json");
                    Write(fixture, execution["fixture"]!);
                    if (execution.A("adapters").Any())
                    {
                        var failure = Phase("before-adapter", CliArgs("run", generated, "--program", execution.S("program"), "--fixture", fixture));
                        row["unsetAdapterFailed"] = failure.I("exitCode") != 0 && failure.S("stderr").Contains(execution.S("expectedUnsetFailure"), StringComparison.Ordinal);
                        var edits = ApplyAdapters(generated, execution, Path.GetDirectoryName(lockPath)!);
                        var mode = row.B("strictGenerated") ? "strict" : "scaffold";
                        var regenerated = Phase("regenerate-edited", CliArgs("generate", source, "--backend", "source", "--mode", mode, "--output", generated).Concat(options));
                        row["editedStubsPreserved"] = regenerated.I("exitCode") == 0 && edits.All(e => Digest(Path.Combine(generated, e.Key)) == e.Value!.GetValue<string>());
                        if (!row.B("editedStubsPreserved"))
                        {
                            throw new ArgumentException("Edited adapter preservation failed");
                        }
                    }

                    result = Phase("fixture", CliArgs("run", generated, "--program", execution.S("program"), "--fixture", fixture));
                    row["fixtureExecuted"] = result.I("exitCode") == 0;
                    row["stubFixtureExecuted"] = row.B("fixtureExecuted") && execution.A("adapters").Any() && row.B("unsetAdapterFailed");
                    if (row.B("fixtureExecuted"))
                    {
                        var actual = JsonNode.Parse(result.S("stdout"))!;
                        row["matchedIndependentExpectation"] = JsonNode.DeepEquals(actual, Read(Confined(Path.GetDirectoryName(lockPath)!, execution.S("expectedFile"))));
                        var portable = Path.Combine(work, "portable");
                        CopyPortable(generated, portable);
                        result = Phase("standalone-build", ["dotnet", "build", Path.Combine(portable, "CobolCS.Generated.csproj"), "-m:1", "--nologo"]);
                        row["builtWithoutReports"] = result.I("exitCode") == 0;
                        if (row.B("builtWithoutReports"))
                        {
                            result = Phase("standalone-run", ["dotnet", Path.Combine(portable, "bin/Debug/net10.0/CobolCS.Generated.dll"), execution.S("program"), fixture]);
                            row["executedWithoutReports"] = result.I("exitCode") == 0 && JsonNode.DeepEquals(JsonNode.Parse(result.S("stdout")), actual);
                        }
                    }
                }
                else if (row.B("built") && row.I("stubCount") > 0 && Read(Path.Combine(generated, "manifest.json")).A("programs").FirstOrDefault() is { } program)
                {
                    result = Phase("unedited-stub-run", ["dotnet", Path.Combine(generated, "bin/Debug/net10.0/CobolCS.Generated.dll"), program.GetValue<string>()]);
                    row["uneditedExecutionFailed"] = result.I("exitCode") != 0;
                }
            }

            var internalReport = Path.Combine(generated, "reports/internal-stub-summary.json");
            if (File.Exists(internalReport))
            {
                row["internalStubs"] = Read(internalReport);
            }

            rows.Add(row);
            row["durationMs"] = timer.ElapsedMilliseconds;
            Write(Path.Combine(output, "summary.json"), summary);
            Console.Error.WriteLine($"Corpus shard {shardIndex + 1}/{shardCount}: {rows.Count}/{selected.Length} {item.S("id")} completed in {timer.ElapsedMilliseconds} ms; built={row.B("built")}");
        }

        var counts = new JsonObject();
        foreach (var key in new[] { "analyzed", "scaffoldAnalyzed", "strictGenerated", "scaffoldGenerated", "built", "fixtureExecuted", "stubFixtureExecuted", "matchedIndependentExpectation", "matchedOriginalCobol" })
        {
            counts[key] = rows.Count(r => r!.B(key));
        }

        counts["selected"] = selected.Length;
        summary["counts"] = counts;
        Write(Path.Combine(output, "summary.json"), summary);
        WriteInternalStubs(summary, output);
        return summary;
    }

    /// <summary>生成数の下限だけでなく、ビルドとレビュー済み実行の証拠を検証します。</summary>
    /// <param name="result">保存した測定結果。</param>
    /// <returns>全必須条件が満たされたか。</returns>
    public static bool Verify(JsonNode result)
    {
        var rows = result.A("cases").ToArray();
        var generated = rows.Where(r => r.B("strictGenerated") || r.B("scaffoldGenerated")).ToArray();
        var expected = result.A("expectedExecutionIds").Select(r => r.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        return result.I("shardCount", 1) == 1 && result.S("backend") == "source" && rows.Length == result.I("expectedCases") && result["counts"] is not null
            && rows.Select(r => r.S("id")).Distinct(StringComparer.Ordinal).Count() == rows.Length
            && generated.Length >= result.I("minimumGenerated") && generated.All(r => r.B("built"))
            && rows.Where(r => expected.Contains(r.S("id"))).All(r => r.B("matchedIndependentExpectation") && r.B("executedWithoutReports"))
            && rows.Where(r => r["unsetAdapterFailed"] is not null).All(r => r.B("unsetAdapterFailed") && r.B("stubFixtureExecuted") && r.B("editedStubsPreserved"))
            && expected.IsSubsetOf(rows.Select(r => r.S("id")));
    }

    private static JsonObject ApplyAdapters(string generated, JsonNode execution, string lockDirectory)
    {
        var stubs = Read(Path.Combine(generated, "reports/stubs.json")).AsArray();
        var edits = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var plan in execution.A("adapters"))
        {
            var matches = stubs.Where(s => s!.S("signature") == plan.S("signature") && Hash(Encoding.UTF8.GetBytes(s!["source"]!.S("raw"))) == plan.S("statementRawSha256")).ToArray();
            if (matches.Length != 1)
            {
                throw new ArgumentException("Adapter contract is missing or ambiguous");
            }

            var stub = matches[0]!;
            var path = Confined(generated, stub.S("file"));
            if (!stub.S("file").StartsWith("Stubs/", StringComparison.Ordinal) || edits.ContainsKey(path))
            {
                throw new ArgumentException("Duplicate or unmanaged adapter target");
            }

            var body = Confined(lockDirectory, plan.S("bodyFile"));
            if (Digest(body) != plan.S("bodySha256"))
            {
                throw new ArgumentException("Adapter body hash mismatch");
            }

            var pattern = new Regex(@"throw new NotImplementedException\([^\n]*\);", RegexOptions.CultureInvariant);
            var original = File.ReadAllText(path);
            if (pattern.Matches(original).Count != 1)
            {
                throw new ArgumentException("Expected one unimplemented adapter body");
            }

            edits[path] = pattern.Replace(original, _ => File.ReadAllText(body).Trim());
        }

        var hashes = new JsonObject();
        foreach (var (path, code) in edits)
        {
            File.WriteAllText(path, code);
            hashes[Path.GetRelativePath(generated, path)] = Digest(path);
        }

        return hashes;
    }

    private static string Classification(string code) => code.Contains("COPY", StringComparison.Ordinal) || code == "SOURCE_PATH_COLLISION" ? "copy"
        : code.StartsWith("INPUT_", StringComparison.Ordinal) ? "input"
        : code is "CS_STORAGE" or "CS_PIC" or "CS_FLOAT" or "CS_GROUP" or "CS_MOVE" or "CS_MODIFICATION" ? "declaration-layout"
        : code is "CS_CONTROL" or "CS_LOOP" ? "control"
        : code is "CS_EXTERNAL" or "EXTERNAL_DEPENDENCY" ? "external-dependency"
        : code is "PARSE_ERROR" or "CS_NO_PROGRAMS" or "UNKNOWN_HEADER" or "UNSUPPORTED_STATEMENT" or "CS_STATEMENT" ? "syntax" : "unclassified";

    private static JsonObject Counts(IEnumerable<string> values) => new(values.GroupBy(v => v, StringComparer.Ordinal).Select(g => KeyValuePair.Create<string, JsonNode?>(g.Key, JsonValue.Create(g.Count()))));

    private static string CompilerHash()
    {
        using var bytes = new MemoryStream();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(Root, "src"), "*", SearchOption.AllDirectories).Where(p => Path.GetExtension(p) is ".cs" or ".csproj").Where(p => !Path.GetRelativePath(Root, p).Split(Path.DirectorySeparatorChar).Any(s => s is "bin" or "obj")).Order(StringComparer.Ordinal))
        {
            bytes.Write(Encoding.UTF8.GetBytes(Path.GetRelativePath(Root, path).Replace('\\', '/')));
            bytes.Write(File.ReadAllBytes(path));
        }

        return Hash(bytes.ToArray());
    }

    private static void CopyPortable(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            var name = Path.GetFileName(entry);
            if (name is "bin" or "obj" or "reports" or "manifest.json")
            {
                continue;
            }

            if (Directory.Exists(entry))
            {
                CopyPortable(entry, Path.Combine(destination, name));
            }
            else
            {
                File.Copy(entry, Path.Combine(destination, name));
            }
        }
    }
}
