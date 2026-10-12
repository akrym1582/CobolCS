using System.Text;
using System.Text.Json.Nodes;
using CobolCS.Frontend;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>管理対象を検証して固定 stage で作業し、公開直前の競合検査と一式の入替えを共用します。</summary>
internal static class CompletionWorkflow
{
    /// <summary>利用者ソースを実際のコンパイル規則と同じ集合で束縛します。</summary>
    /// <param name="root">検査済み生成先。</param>
    /// <returns>テストを除いた全 Stubs と UserCode の相対パスと hash。</returns>
    internal static JsonObject CodeHashes(string root) => new(Generator.Inventory(root)
        .Where(p => p.Key.EndsWith(".cs", StringComparison.Ordinal) && (p.Key.StartsWith("Stubs/", StringComparison.Ordinal) || p.Key.StartsWith("UserCode/", StringComparison.Ordinal)))
        .Select(p => KeyValuePair.Create<string, JsonNode?>(p.Key, JsonValue.Create(p.Value))));

    /// <summary>静的な不足と有効な証跡のみを案内へ取り込み、コードは実行しません。</summary>
    /// <param name="root">生成先。</param>
    /// <returns>案内の保存完了。</returns>
    internal static int Prepare(string root) => Stage(root, (stage, manifest) =>
    {
        var inspection = BusinessCases.Inspect(stage);
        BusinessReview.Update(stage, inspection);
        CompletionWorklist.Update(stage, manifest, inspection);
        return 0;
    });

    /// <summary>内部補完を明示的に取り込み、外部経路を fake に固定した既存探索器を使います。</summary>
    /// <param name="root">source 生成先。</param>
    /// <param name="programId">省略時は全プログラム。</param>
    /// <param name="configPath">探索運用設定だけを持つ JSON。</param>
    /// <returns>完了は 0、不完全は 1。入力・ビルド不正は例外として CLI が 2 を返します。</returns>
    internal static int Explore(string root, string? programId, string? configPath) => Stage(root, (stage, manifest) =>
    {
        if (!manifest["judgments"]!.B("generated"))
        {
            throw new IOException("Explore requires generated source; regenerate after resolving diagnostics.");
        }

        if (!File.ReadAllText(Path.Combine(stage, "Api/Execution.g.cs")).Contains("IsExploration", StringComparison.Ordinal))
        {
            throw new IOException("Legacy output lacks the protected exploration router. Regenerate with this version, preserving user edits, or generate into a new directory and port them explicitly.");
        }

        if (programId != null && !manifest.A("programs").Any(p => p.GetValue<string>() == programId))
        {
            throw new ArgumentException("Unknown or rejected program: " + programId);
        }

        var ir = J.Parse(File.ReadAllText(Path.Combine(stage, "reports/ir.json")));
        if (configPath != null)
        {
            var requested = J.Parse(File.ReadAllText(configPath)).AsObject();
            if (requested.Count != 1 || requested["testGeneration"] is not JsonObject)
            {
                throw new ArgumentException("Explore config accepts only testGeneration; semantic, PIC, dialect and mode changes require generate.");
            }

            ir["config"]!["testGeneration"] = requested["testGeneration"]!.DeepClone();
            _ = FrontendConfiguration.Parse(ir["config"]!.ToJsonString());
        }

        var regenerated = new SortedDictionary<string, string>(StringComparer.Ordinal);
        SourceBackend.Create(ir, regenerated, new JsonArray());
        var sources = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Generator.Inventory(stage).Keys.Where(p => p.EndsWith(".cs", StringComparison.Ordinal) || p.EndsWith(".csproj", StringComparison.Ordinal) || p.EndsWith(".props", StringComparison.Ordinal) || p is "reports/branch-plans.json" or "reports/branches.json" or ".editorconfig"))
        {
            sources[path] = File.ReadAllText(Path.Combine(stage, path));
        }

        foreach (var (path, content) in regenerated.Where(p => p.Key.StartsWith(".branch-probe/", StringComparison.Ordinal)))
        {
            sources[path] = content;
        }

        var start = StartRecord(stage, manifest, "explore", programId ?? "all", new JsonObject { ["testGeneration"] = ir["config"]!["testGeneration"]?.DeepClone() });
        try
        {
            BranchTestGeneration.Generate(stage, ir, sources, manifest, "completed-internal", programId);
        }
        catch (Exception error)
        {
            var failed = J.Parse(File.ReadAllText(Path.Combine(stage, start)));
            failed["status"] = "failed";
            failed["blocked"] = error.Message.Replace(stage + ".branch-probe", "<probe>", StringComparison.Ordinal).Replace(stage, "<stage>", StringComparison.Ordinal);
            failed["failure"] = new JsonObject { ["exceptionType"] = error.GetType().Name, ["source"] = null };
            error.Data["CompletionRecordPath"] = start;
            error.Data["CompletionRecordContent"] = Generator.Json(failed);
            throw;
        }

        var generation = manifest["judgments"]!["testGeneration"]!;
        var run = generation.S("runId");
        var summary = J.Parse(File.ReadAllText(Path.Combine(stage, "reports/test-generation/" + run + "/summary.json")));
        var trials = J.Parse(File.ReadAllText(Path.Combine(stage, "reports/test-generation/" + run + "/trials.json")));
        var record = J.Parse(File.ReadAllText(Path.Combine(stage, start)));
        record["status"] = summary.B("complete") ? "completed" : "incomplete";
        record["generationRunId"] = run;
        record["trials"] = trials.DeepClone();
        record["blocked"] = summary["blocked"]?.DeepClone();
        Write(stage, start, Generator.Json(record));
        var inspection = BusinessCases.Inspect(stage);
        BusinessReview.Update(stage, inspection);
        CompletionWorklist.Update(stage, manifest, inspection);
        return summary.B("complete") ? 0 : 1;
    });

    /// <summary>実行開始だけを記録し、異常終了を成功と取り違えません。</summary>
    /// <param name="root">ローカル生成先。</param>
    /// <param name="manifest">検証済み台帳。</param>
    /// <param name="kind">run または explore。</param>
    /// <param name="program">対象。</param>
    /// <param name="input">入力・シナリオ名・設定と hash。</param>
    /// <returns>独立記録の相対パス。</returns>
    internal static string StartRecord(string root, JsonNode manifest, string kind, string program, JsonNode input)
    {
        var path = "reports/executions/" + DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffffff", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N") + ".json";
        Write(root, path, Generator.Json(new JsonObject
        {
            ["schemaVersion"] = 1,
            ["executionId"] = Path.GetFileNameWithoutExtension(path),
            ["kind"] = kind,
            ["programId"] = program,
            ["status"] = "started",
            ["startedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["fingerprint"] = Fingerprint(root, manifest),
            ["userSourceHashes"] = CodeHashes(root),
            ["input"] = input.DeepClone(),
        }));
        return path;
    }

    /// <summary>生成本文・正式契約・補完コードを現在の実測と結び付けます。</summary>
    /// <param name="root">出力先。</param>
    /// <param name="manifest">入力と意味設定。</param>
    /// <returns>絶対ディレクトリを含まない内容 hash。</returns>
    internal static string Fingerprint(string root, JsonNode manifest)
    {
        var code = Generator.Inventory(root).Where(p => p.Key.EndsWith(".cs", StringComparison.Ordinal) && !p.Key.StartsWith("Tests/", StringComparison.Ordinal) && !p.Key.StartsWith("Testing/Characterization_", StringComparison.Ordinal));
        return Generator.Hash(Encoding.UTF8.GetBytes(string.Join('\n', code.Select(p => p.Key + ":" + p.Value)) + Generator.Json(manifest["inputHashes"]!) + manifest.S("configHash")));
    }

    /// <summary>証跡だけに許可するパス。生成管理台帳やユーザー編集領域へ混在させません。</summary>
    /// <param name="path">相対パス。</param>
    /// <returns>独立 JSON 記録の安全なパスの場合は true。</returns>
    internal static bool IsRecord(string path) => path.StartsWith("reports/executions/", StringComparison.Ordinal) && path.EndsWith(".json", StringComparison.Ordinal) && path.Split('/').Length == 3 && !path.Contains('\\') && !path.Split('/').Any(p => p is "." or ".." or "");

    /// <summary>子プロセスが完了できなかった場合も最新失敗を保存し、案内の hash を更新します。</summary>
    /// <param name="root">検査済み出力先。</param>
    /// <param name="manifest">実行判定を持つ台帳。</param>
    /// <param name="path">開始時に作った記録。</param>
    /// <param name="error">プロセス起動・ビルド・timeout の異常。</param>
    internal static void FinishRun(string root, JsonNode manifest, string path, Exception? error)
    {
        var record = J.Parse(File.ReadAllText(Path.Combine(root, path)));
        if (record.S("status") == "started")
        {
            record["status"] = error == null ? "completed" : "failed";
            record["failure"] = error == null ? null : new JsonObject { ["exceptionType"] = error.GetType().Name, ["source"] = null };
            record["blocked"] = error?.Message;
        }

        if (record.S("fingerprint") != Fingerprint(root, manifest))
        {
            record["inputsChangedDuringExecution"] = true;
        }

        Write(root, path, Generator.Json(record));
        CompletionWorklist.Update(root, manifest);
        foreach (var report in new[] { "reports/completion-worklist.json", "reports/completion-worklist.md", "reports/internal-stub-summary.json", "reports/internal-stub-summary.md" })
        {
            if (File.Exists(Path.Combine(root, report)))
            {
                manifest["artifacts"]![report] = Generator.Hash(File.ReadAllBytes(Path.Combine(root, report)));
            }
        }

        Generator.SaveManifest(root, manifest);
    }

    /// <summary>相対パスへ UTF-8 で保存します。</summary>
    /// <param name="root">検査済み stage。</param>
    /// <param name="path">内部で選んだ相対パス。</param>
    /// <param name="content">保存内容。</param>
    internal static void Write(string root, string path, string content)
    {
        var target = Path.Combine(root, path);
        Generator.SafePath(target);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, content, new UTF8Encoding(false));
    }

    /// <summary>許可した領域を検査し、stage の内容だけを原子的に公開します。</summary>
    /// <param name="root">元の生成先。</param>
    /// <param name="action">stage だけを変更する処理。</param>
    /// <returns>処理結果の終了コード。</returns>
    internal static int Stage(string root, Func<string, JsonNode, int> action)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (File.Exists(Path.Combine(root, "provenance/cobolcs-origin.json")))
        {
            throw new IOException("Adopted solution is user-owned; use dotnet test. Explore and regenerate are unsupported.");
        }

        var manifest = Generator.ReadManifest(root);
        Generator.Verify(root, manifest);
        if (manifest.S("backend") != "source" || manifest.I("manifestSchemaVersion") != 2)
        {
            throw new IOException("Prepare/explore requires schema 2 source output; regenerate into a new directory.");
        }

        var before = Generator.Inventory(root);
        foreach (var path in before.Keys)
        {
            if (path != "manifest.json" && manifest["artifacts"]?[path] == null && !Generator.IsUserFile(path) && !IsRecord(path))
            {
                throw new IOException("Unmanaged file: " + path);
            }
        }

        var stage = root + ".stage-" + Guid.NewGuid().ToString("N");
        var backup = root + ".backup-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(stage);
            foreach (var path in before.Keys)
            {
                var target = Path.Combine(stage, path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(Path.Combine(root, path), target);
            }

            var result = action(stage, manifest);
            manifest["artifacts"] = new JsonObject(Generator.Inventory(stage).Where(p => p.Key != "manifest.json" && !Generator.IsUserFile(p.Key) && !IsRecord(p.Key)).Select(p => KeyValuePair.Create<string, JsonNode?>(p.Key, JsonValue.Create(p.Value))));
            Generator.SaveManifest(stage, manifest);
            if (!before.SequenceEqual(Generator.Inventory(root)))
            {
                throw new IOException("Output changed during completion operation; original files preserved.");
            }

            Directory.Move(root, backup);
            try
            {
                Directory.Move(stage, root);
            }
            catch
            {
                Directory.Move(backup, root);
                throw;
            }

            Directory.Delete(backup, true);
            return result;
        }
        catch (Exception error)
        {
            // 失敗した stage の成果物を公開せず、独立した失敗記録だけを保存します。
            if (error.Data["CompletionRecordPath"] is string recordPath && IsRecord(recordPath) && error.Data["CompletionRecordContent"] is string content && before.SequenceEqual(Generator.Inventory(root)))
            {
                Write(root, recordPath, content);
            }

            throw;
        }
        finally
        {
            if (Directory.Exists(stage))
            {
                Directory.Delete(stage, true);
            }
        }
    }
}
