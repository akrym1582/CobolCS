using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CobolCS.Frontend;
using CobolCS.Runtime;
using static CobolCS.Tools.Workspace;

namespace CobolCS.Tools;

/// <summary>深掘りコーパスの工程と証跡をケースごとに分離します。</summary>
public static partial class DeepCorpus
{
    /// <summary>入力と実行版を検証し、指定工程の未完ケースだけを実行します。</summary>
    /// <param name="lockPath">固定台帳。</param>
    /// <param name="root">原文キャッシュ。</param>
    /// <param name="output">この版専用の保存先。</param>
    /// <param name="stage">analyze、generate、build。</param>
    /// <param name="cli">固定した製品CLI。</param>
    /// <param name="compilerRoot">CLIのビルド元を保存したリポジトリ。</param>
    /// <param name="offset">台帳順の開始位置。</param>
    /// <param name="count">対象件数。</param>
    /// <param name="timeout">各子プロセスの秒数上限。</param>
    public static void Evaluate(string lockPath, string root, string output, string stage, string cli, string compilerRoot, int offset = 0, int count = int.MaxValue, int timeout = 120)
    {
        if (stage is not ("analyze" or "generate" or "build") || offset < 0 || count < 1 || timeout < 1)
        {
            throw new ArgumentException("Invalid stage/range/timeout");
        }

        var locked = Read(lockPath);
        var cases = locked.A("cases").ToArray();
        if (locked.I("schemaVersion") != 1 || cases.Length != locked.I("expectedCases") || cases.Select(c => c.S("id")).Distinct(StringComparer.Ordinal).Count() != cases.Length)
        {
            throw new ArgumentException("Invalid inventory schema/count/IDs");
        }

        if (Digest(Corpus.Confined(root, "compact-selection/catalog.json")) != locked.S("catalogSha256") || Digest(Path.Combine(Root, "tasks/deep-corpus/files.tsv.gz")) != locked.S("filesLockSha256"))
        {
            throw new ArgumentException("Catalog/files lock changed");
        }

        VerifyAvailableFiles(root);
        foreach (var item in cases)
        {
            var input = Corpus.Confined(root, item.S("source"));
            if (!Regex.IsMatch(item.S("id"), @"\Adeep-[0-9a-f]{16}\z") || (File.Exists(input) && Digest(input) != item.S("sha256")) || FrontendConfiguration.Parse(item["config"]!.ToJsonString()).Hash != item.S("configSha256"))
            {
                throw new ArgumentException("Invalid case ID/input/config hash");
            }

            foreach (var pair in item["copybookHashes"]!.AsObject())
            {
                if (Digest(Corpus.Confined(root, pair.Key)) != pair.Value!.GetValue<string>())
                {
                    throw new ArgumentException("COPY hash mismatch");
                }
            }
        }

        var identity = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["lockSha256"] = Digest(lockPath),
            ["compilerSourceSha256"] = SourceHash(Path.Combine(compilerRoot, "src")),
            ["compilerBinarySha256"] = BinaryHash(Path.GetDirectoryName(Path.GetFullPath(cli))!),
            ["evaluatorSourceSha256"] = SourceHash(Path.Combine(Root, "tools/CobolCS.Tools")),
            ["backend"] = "source",
            ["mode"] = "scaffold",
            ["timeoutSeconds"] = timeout,
        };
        Directory.CreateDirectory(output);
        var identityPath = Path.Combine(output, "identity.json");
        if (File.Exists(identityPath) && !JsonNode.DeepEquals(Read(identityPath), identity))
        {
            throw new ArgumentException("Checkpoint compiler/config/lock identity mismatch; use a new output directory");
        }

        AtomicWrite(identityPath, identity);
        foreach (var item in cases.Skip(offset).Take(count))
        {
            var work = Path.Combine(output, item.S("id"));
            Directory.CreateDirectory(work);
            var resultPath = Path.Combine(work, stage + ".json");
            if (File.Exists(resultPath))
            {
                ValidateCheckpoint(Read(resultPath), item, identity);
                continue;
            }

            var previousStage = stage == "build" ? "generate" : "analyze";
            JsonNode? previous = null;
            if (stage != "analyze")
            {
                var previousPath = Path.Combine(work, previousStage + ".json");
                if (!File.Exists(previousPath))
                {
                    throw new ArgumentException("Required prior stage missing: " + item.S("id"));
                }

                previous = Read(previousPath);
                ValidateCheckpoint(previous, item, identity);
            }

            var row = new JsonObject
            {
                ["id"] = item.S("id"),
                ["inputSha256"] = item.S("sha256"),
                ["configSha256"] = item.S("configSha256"),
                ["identity"] = identity.DeepClone(),
                ["stage"] = stage,
                ["inputVerified"] = File.Exists(Corpus.Confined(root, item.S("source"))),
                ["attempted"] = false,
                ["analyzed"] = false,
                ["analysisHasErrors"] = null,
                ["generated"] = false,
                ["generatedPrograms"] = new JsonArray(),
                ["rejectedPrograms"] = new JsonArray(),
                ["built"] = false,
                ["nativeStatementCount"] = null,
                ["adapterRequiredCount"] = null,
                ["unsupportedStatementCount"] = null,
                ["unknownTokenCount"] = null,
                ["executionAttempted"] = false,
                ["executionPassed"] = false,
                ["independentExpectedMatched"] = false,
                ["referenceCompilerMatched"] = false,
                ["needsReview"] = true,
                ["oracle"] = null,
            };
            var config = Path.Combine(work, "config.json");
            Write(config, item["config"]!);
            var target = Path.Combine(work, stage == "analyze" ? "analysis" : "generated");
            var command = new List<string> { "dotnet", Path.GetFullPath(cli) };
            if (stage == "build")
            {
                command.AddRange(["build", target]);
            }
            else
            {
                command.AddRange([stage == "analyze" ? "analyze" : "generate", Corpus.Confined(root, item.S("source")), "--output", target, "--config", config]);
                if (stage == "generate")
                {
                    command.AddRange(["--backend", "source", "--mode", "scaffold"]);
                }

                foreach (var copy in item.A("copybookPaths"))
                {
                    command.AddRange(["--copybook-path", Corpus.Confined(root, copy.GetValue<string>())]);
                }
            }

            if (!row.B("inputVerified"))
            {
                row["status"] = "input-missing";
            }
            else if (stage == "build" && previous is not null && !previous.B("generated"))
            {
                row["status"] = "blocked-not-generated";
            }
            else
            {
                // 不正UTF-8も製品診断を測定しますが、構文未対応へは分類しません。
                row["attempted"] = true;
                var result = Command(command, timeout);
                result["command"] = Strings(command);
                AtomicWrite(Path.Combine(work, stage + ".log.json"), result);
                row["exitCode"] = result.I("exitCode");
                row["timedOut"] = result.B("timedOut");
                row["status"] = result.B("timedOut") ? "timeout" : item.S("encoding") == "unknown-non-utf8" ? "encoding-blocked" : result.I("exitCode") == 0 ? "completed" : "failed";
                row["built"] = stage == "build" && result.I("exitCode") == 0 && !result.B("timedOut");
                row["buildDiagnosticCodes"] = Strings(Regex.Matches(result.S("stdout") + result.S("stderr"), @"error (CS\d+|SA\d+|MSB\d+|NU\d+):").Select(m => m.Groups[1].Value).Distinct().Order(StringComparer.Ordinal));
                CaptureReports(row, target, stage);
            }

            AtomicWrite(resultPath, row);
            Console.WriteLine(item.S("id") + " " + stage + " " + row.S("status"));
        }
    }

    private static void ValidateCheckpoint(JsonNode row, JsonNode item, JsonNode identity)
    {
        if (row.S("id") != item.S("id") || row.S("inputSha256") != item.S("sha256") || row.S("configSha256") != item.S("configSha256") || !JsonNode.DeepEquals(row["identity"], identity))
        {
            throw new ArgumentException("Checkpoint input/compiler/config mismatch");
        }
    }

    private static void VerifyAvailableFiles(string root)
    {
        // COPY候補を含む固定取得ファイル全体を再検証し、依存だけの改ざんも拒否します。
        using var stream = File.OpenRead(Path.Combine(Root, "tasks/deep-corpus/files.tsv.gz"));
        using var gzip = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        while (reader.ReadLine() is { } line)
        {
            var fields = line.Split('\t');
            if (fields.Length != 4)
            {
                throw new ArgumentException("Invalid acquisition file lock");
            }

            var file = Corpus.Confined(root, fields[2]);
            if (File.Exists(file) && Digest(file) != fields[3])
            {
                throw new ArgumentException("Acquisition/COPY file hash mismatch: " + fields[2]);
            }
        }
    }

    private static string BinaryHash(string directory)
    {
        using var bytes = new MemoryStream();
        foreach (var file in Directory.EnumerateFiles(directory, "*.dll").Where(p => Path.GetFileName(p).StartsWith("CobolCS", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
        {
            bytes.Write(System.Text.Encoding.UTF8.GetBytes(Path.GetFileName(file) + "\0"));
            bytes.Write(File.ReadAllBytes(file));
        }

        return Hash(bytes.ToArray());
    }

    private static void CaptureReports(JsonObject row, string target, string stage)
    {
        var manifestPath = Path.Combine(target, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return;
        }

        var manifest = Read(manifestPath);
        row["analyzed"] = manifest["judgments"]!.B("analyzed");
        row["generated"] = stage != "analyze" && manifest["judgments"]!.B("generated") && File.Exists(Path.Combine(target, "CobolCS.Generated.csproj"));
        row["generatedPrograms"] = row.B("generated") ? manifest["programs"]!.DeepClone() : new JsonArray();
        row["rejectedPrograms"] = manifest["rejectedPrograms"]?.DeepClone() ?? new JsonArray();
        var diagnostics = Read(Path.Combine(target, "reports/diagnostics.json")).AsArray().Select(d => d!).ToArray();
        row["analysisHasErrors"] = diagnostics.Any(d => d.S("severity") == "error");
        row["diagnosticCounts"] = new JsonObject(diagnostics.GroupBy(d => d.S("code")).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => KeyValuePair.Create<string, JsonNode?>(g.Key, JsonValue.Create(g.Count()))));
        row["missingDependencies"] = Strings(diagnostics.Where(d => d.S("code").Contains("COPY", StringComparison.Ordinal) || d.S("code").Contains("INCLUDE", StringComparison.Ordinal)).Select(d => d.S("code")).Distinct());
        var ir = Read(Path.Combine(target, "reports/ir.json"));
        row["unknownTokenCount"] = ir.A("programs").SelectMany(p => p.A("tokens")).Concat(ir.A("rejectedTokens")).Count(t => t.S("ownerKind") == "unknown");
        row["unknownTokenCountEvidence"] = "ir-program-tokens-and-rejectedTokens-ownerKind";

        var mapPath = Path.Combine(target, "reports/source-map.json");
        if (File.Exists(mapPath))
        {
            var map = Read(mapPath).AsArray();
            row["nativeStatementCount"] = map.Count(n => n!.S("capability") == "native" && n!["statementId"] is not null);
        }

        var stubsPath = Path.Combine(target, "reports/stubs.json");
        if (File.Exists(stubsPath))
        {
            var stubs = Read(stubsPath).AsArray();
            row["stubCount"] = stubs.Count;
            row["adapterRequiredCount"] = stubs.Count(s => s!.S("capability") != "unsupported");
            row["unsupportedStatementCount"] = stubs.Count(s => s!.S("capability") == "unsupported");
            row["stubKinds"] = Strings(stubs.Select(s => s!["statement"]?.S("kind") ?? s!.S("kind")).Distinct().Order(StringComparer.Ordinal));
        }
    }
}
