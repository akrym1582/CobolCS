using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>
/// 解析した COBOL から独立した C# プロジェクトと、対応状況を確認するためのレポートを作ります。
/// 生成ファイルの記録と再生成前の検査を担当し、利用者の手編集を誤って上書きすることを防ぎます。
/// </summary>
public static class Generator
{
    /// <summary>台帳に記録する CobolCS の版です。同じ入力でも生成規則の版が違うことを追跡できるようにします。</summary>
    public const string Version = "0.3.0";

    /// <summary>採用した CobolTS 解析器の固定コミットです。解析結果の違いを調べる際の出発点になります。</summary>
    public const string CobolTsCommit = "3e42de61fa8e0e7747e37cb17a48732ac962e38d";

    /// <summary>台帳や診断を人が読みやすい段落付きの JSON にし、ファイル間で同じ表記規則を使用します。</summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        MaxDepth = J.MaxDepth,
    };

    /// <summary>レポートや台帳を保存するための、整形済み JSON 文字列を作ります。</summary>
    /// <param name="node">保存する JSON ツリー。</param>
    /// <returns>インデントと末尾の改行を含む文字列。単に画面へ表示する場合にも利用できます。</returns>
    public static string Json(JsonNode node) => node.ToJsonString(JsonOptions) + "\n";

    /// <summary>
    /// 生成物の内容を識別する SHA-256 を求めます。
    /// 前回生成した内容から変更されたかを調べるために使い、ファイル名や更新日時だけでは判断しません。
    /// </summary>
    /// <param name="bytes">記録または検査するファイルの実際のバイト列。</param>
    /// <returns>小文字の十六進数で表す 64 文字のハッシュ。</returns>
    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>
    /// 出力先やその親がシンボリックリンクでないことを確認します。
    /// 指定した生成先を操作したつもりで、リンク先の別の場所を書き換えてしまうことを防ぎます。
    /// </summary>
    /// <param name="path">作成・更新・検査するファイルまたはディレクトリのパス。</param>
    /// <exception cref="IOException">既存の出力パスや親にシンボリックリンクなどの再解析ポイントがある場合。</exception>
    public static void SafePath(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new IOException("Symlink output path refused: " + current);
            }

            current = Path.GetDirectoryName(current) ?? string.Empty;
        }
    }

    /// <summary>
    /// 生成先の台帳を読み取り、前回の生成ファイルや工程別の判定を調べられるようにします。
    /// 台帳を読むだけではファイル内容が一致するとは判断しないため、利用前にハッシュ検査も必要です。
    /// </summary>
    /// <param name="output">manifest.json がある生成プロジェクトのディレクトリ。</param>
    /// <returns>ハッシュ検査や工程別判定の更新に使用する台帳の JSON。</returns>
    public static JsonNode ReadManifest(string output)
    {
        SafePath(output);
        SafePath(Path.Combine(output, "manifest.json"));
        return J.Parse(File.ReadAllText(Path.Combine(output, "manifest.json")));
    }

    /// <summary>
    /// 台帳に載っている生成ファイルが、前回保存した内容のまま存在するかを確認します。
    /// 手編集や削除を発見した場合は、再生成や実行に進む前に停止します。
    /// </summary>
    /// <param name="output">台帳の各ファイル名を解決する生成プロジェクトのディレクトリ。</param>
    /// <param name="manifest">生成時のファイルパスとハッシュを持つ台帳。</param>
    /// <exception cref="IOException">パスが生成先の外へ逸脱する場合や、ファイルが変更・削除された場合。</exception>
    public static void Verify(string output, JsonNode manifest)
    {
        if (manifest["artifacts"] is not JsonObject artifacts)
        {
            throw new IOException("Invalid CobolCS manifest");
        }

        if (manifest.I("manifestSchemaVersion", 1) is not (1 or 2))
        {
            throw new IOException("Unsupported manifest schema. Generate into a new directory.");
        }

        if (artifacts.Select(p => p.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != artifacts.Count)
        {
            throw new IOException("Case-colliding manifest paths");
        }

        foreach (var (name, value) in artifacts)
        {
            if (Path.IsPathRooted(name) || name.Contains('\\') || name.Split('/').Any(p => p is ".." or "." or ""))
            {
                throw new IOException("Unsafe manifest artifact path");
            }

            var file = Path.Combine(output, name);
            SafePath(file);
            if (!File.Exists(file) || Hash(File.ReadAllBytes(file)) != value!.GetValue<string>())
            {
                throw new IOException("Generated file edited or removed: " + name);
            }
        }
    }

    /// <summary>
    /// ビルドや実行によって変わった工程別の判定を台帳へ保存します。
    /// 一時ファイルから置き換えることで、書き込み途中の JSON が次回の台帳として使われる可能性を減らします。
    /// </summary>
    /// <param name="output">台帳を更新する生成先ディレクトリ。</param>
    /// <param name="manifest">更新後の判定と既存のハッシュを含む台帳。</param>
    public static void SaveManifest(string output, JsonNode manifest)
    {
        var path = Path.Combine(output, "manifest.json");
        SafePath(path);
        var temporary = path + ".tmp";
        SafePath(temporary);
        File.WriteAllText(temporary, Json(manifest));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>
    /// 対応検査、レポートの作成、必要に応じた C# 生成を行い、完成した一式を生成先へ入れ替えます。
    /// strict の拒否時にも診断は残し、scaffold の未対応部分を仮の正常処理として実行させません。
    /// </summary>
    /// <param name="ir">frontend が出力した設定、ソース情報、プログラム、診断を含む IR。</param>
    /// <param name="output">成果物の保存先。既存の生成物は手編集がない場合だけ置き換えます。</param>
    /// <param name="mode">strict はエラー時にレポートだけ、scaffold は実行拒否の雛形、analyze は解析レポートだけを保存します。</param>
    /// <param name="backend">source は通常の C#、interpreter は従来の命令列を生成します。</param>
    /// <returns>入力と生成物のハッシュ、および工程別の判定を持つ台帳。</returns>
    /// <exception cref="IOException">生成先に手編集・管理外ファイル・危険なパスがある場合。</exception>
    public static JsonNode Generate(JsonNode ir, string output, string mode, string backend = "source")
    {
        output = Path.GetFullPath(output);
        SafePath(output);
        if (File.Exists(Path.Combine(output, "provenance/cobolcs-origin.json")))
        {
            throw new IOException("Adopted solution is user-owned. Translate into a new managed directory.");
        }

        var originalInventory = Inventory(output);
        var diagnostics = Validation.Check(ir, backend);
        if (backend is not ("source" or "interpreter"))
        {
            throw new ArgumentException("Invalid backend");
        }

        var errors = diagnostics.OfType<JsonObject>().Any(d => d!.S("severity") == "error");
        var fatal = diagnostics.OfType<JsonObject>().Any(d => d!.S("code") is "CS_PROFILE" or "CS_DUPLICATE" or "CS_NO_PROGRAMS");
        var generated = !fatal && mode != "analyze" && (mode == "scaffold" || !errors);
        var generatedPrograms = ir.A("programs").ToArray();
        var rejectedPrograms = new List<string>();
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["reports/ir.json"] = Json(ir),
            ["reports/diagnostics.json"] = Json(diagnostics),
            ["reports/diagnostics.md"] = string.Join('\n', diagnostics.OfType<JsonObject>().Select(d => $"- {d!.S("code")}: {d!.S("message")} ({d!["source"]?.S("file")}:{d!["source"]?["start"]?.I("line")})")) + "\n",
            ["reports/dependencies.json"] = Json(new JsonArray(ir.A("programs").Select(p => (JsonNode?)new JsonObject { ["program"] = p.S("id"), ["dependencies"] = p["dependencies"]!.DeepClone() }).ToArray())),
            ["reports/ai-brief.md"] = "# CobolCS review\n\nReview reports/diagnostics.json and reports/ir.json. Change frontend/compiler/runtime, regenerate and run regression tests. Build and fixture success do not establish business equivalence.\n",
        };
        if (backend == "source" && !fatal && ir.A("programs").Any())
        {
            SourceBackend.Create(ir, files, diagnostics);
            errors = diagnostics.OfType<JsonObject>().Any(d => d!.S("severity") == "error");
            if (mode == "strict")
            {
                rejectedPrograms.AddRange(generatedPrograms.Where(p => diagnostics.OfType<JsonObject>().Any(d => d!.S("severity") == "error" && (d.S("programId") == p.S("id") || (d.S("programId") == string.Empty && d["source"] != null && p.A("paragraphs").Any(paragraph => paragraph["source"]?.S("file") == d["source"]!.S("file")))))).Select(p => p.S("id")));
                generatedPrograms = generatedPrograms.Where(p => !rejectedPrograms.Contains(p.S("id"))).ToArray();
                foreach (var key in files.Keys.Where(k => !k.StartsWith("reports/", StringComparison.Ordinal)).ToArray())
                {
                    files.Remove(key);
                }

                if (generatedPrograms.Length != 0)
                {
                    var accepted = ir.DeepClone();
                    accepted["programs"] = new JsonArray(generatedPrograms.Select(p => p.DeepClone()).ToArray());
                    SourceBackend.Create(accepted, files, new JsonArray());
                }
            }

            generated = mode != "analyze" && generatedPrograms.Length != 0;
            if (!generated)
            {
                foreach (var key in files.Keys.Where(k => !k.StartsWith("reports/", StringComparison.Ordinal)).ToArray())
                {
                    files.Remove(key);
                }
            }
        }

        files["reports/diagnostics.json"] = Json(diagnostics);
        files["reports/diagnostics.md"] = string.Join('\n', diagnostics.OfType<JsonObject>().Select(d => $"- {d!.S("programId")} {d.S("statementId")} {d.S("code")}: {d.S("message")} ({d["source"]?.S("file")}:{d["source"]?["start"]?.I("line")})")) + "\n";
        if (generated)
        {
            files["CobolCS.Generated.csproj"] = backend == "source" ? GeneratedCode.CreateProject().Replace("    <EmbeddedResource Include=\"reports/ir.json\" LogicalName=\"CobolCS.IR\" />\n", string.Empty) : GeneratedCode.CreateProject();
            files["Directory.Build.props"] = "<Project />\n";
            if (backend == "source")
            {
                SourceBackend.Workflow(generatedPrograms, files);
                files["CobolCS.Generated.csproj"] = files["CobolCS.Generated.csproj"].Replace("<ItemGroup>", "<ItemGroup>\n    <Compile Remove=\"Tests/**/*.cs;**/obj/**/*.cs;**/bin/**/*.cs;**/TestResults/**/*.cs\" />", StringComparison.Ordinal);
                foreach (var path in files.Keys.Where(p => p.EndsWith(".g.cs", StringComparison.Ordinal)).ToArray())
                {
                    files[path] = SourceFormatting.Indent(SourceBackend.DocumentPublic(files[path]));
                }
            }

            foreach (var path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "runtime"), "*.cs"))
            {
                if (backend == "source" && Path.GetFileName(path) is not ("Number.cs" or "CobolValue.cs" or "SourceSupport.cs" or "SizeError.cs"))
                {
                    continue;
                }

                files["Runtime/" + Path.GetFileName(path)] = File.ReadAllText(path);
            }

            if (backend == "interpreter")
            {
                var index = 0;
                foreach (var program in ir.A("programs"))
                {
                    files[$"Programs/Program{index:D4}.cs"] = GeneratedCode.CreateProgram(index, program, errors);
                    index++;
                }

                files["Program.cs"] = GeneratedCode.CreateRunner(ir);
            }

            files["THIRD-PARTY-NOTICES.md"] = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md"));
            files["LICENSE"] = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "licenses/CobolCS.txt"));
        }

        var stubTemplateHashes = new JsonObject();
        var stubBindings = new JsonObject();
        if (backend == "source" && files.TryGetValue("reports/stubs.json", out var stubReport))
        {
            foreach (var stub in J.Parse(stubReport).AsArray())
            {
                stubBindings[stub!.S("file")] = Hash(Encoding.UTF8.GetBytes(Json(stub!)));
                var helper = Path.GetDirectoryName(stub!.S("file"))!.Replace('\\', '/') + "/NoOp_" + stub!.S("statementId") + ".cs";
                if (files.ContainsKey(helper))
                {
                    stubBindings[helper] = Hash(Encoding.UTF8.GetBytes(Json(stub!)));
                }
            }
        }

        foreach (var (path, content) in files.Where(p => p.Key.StartsWith("Stubs/", StringComparison.Ordinal)))
        {
            stubTemplateHashes[path] = Hash(Encoding.UTF8.GetBytes(content));
        }

        var userBindings = new JsonObject();
        var userTemplates = new JsonObject();
        foreach (var (path, content) in files.Where(p => IsUserFile(p.Key)))
        {
            userTemplates[path] = Hash(Encoding.UTF8.GetBytes(content));
            userBindings[path] = generatedPrograms.FirstOrDefault(p => path.Contains(SourceBackend.Identifier(p.S("id")), StringComparison.Ordinal))?.S("id");
        }

        var generatedTestHashes = files.Where(p => p.Key.StartsWith("Tests/Cases/", StringComparison.Ordinal)).ToDictionary(p => p.Key, p => Hash(Encoding.UTF8.GetBytes(p.Value)), StringComparer.Ordinal);
        JsonNode? previous = null;
        var editableContents = new Dictionary<string, byte[]>();
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
        {
            previous = ReadManifest(output);
            Verify(output, previous);
            var oldStubPath = Path.Combine(output, "reports/stubs.json");
            SafePath(oldStubPath);
            var oldStubs = previous.S("backend") == "source" && File.Exists(oldStubPath) ? J.Parse(File.ReadAllText(oldStubPath)).AsArray() : [];
            var oldIr = previous.S("backend") == "source" && File.Exists(Path.Combine(output, "reports/ir.json")) ? J.Parse(File.ReadAllText(Path.Combine(output, "reports/ir.json"))) : null;
            var newStubs = files.TryGetValue("reports/stubs.json", out var currentStubs) ? J.Parse(currentStubs).AsArray() : [];
            foreach (var directory in Directory.EnumerateDirectories(output, "*", SearchOption.AllDirectories))
            {
                SafePath(directory);
            }

            foreach (var file in Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories))
            {
                SafePath(file);
                var relative = Path.GetRelativePath(output, file).Replace('\\', '/');
                if (IsBuildFile(relative))
                {
                    continue;
                }

                if (previous.S("backend") == "source" && IsUserFile(relative))
                {
                    var oldStub = oldStubs.OfType<JsonObject>().FirstOrDefault(s => s.S("file") == relative || $"{Path.GetDirectoryName(s.S("file"))?.Replace('\\', '/')}/NoOp_{s.S("statementId")}.cs" == relative);
                    var oldBinding = previous["stubBindings"]?[relative]?.GetValue<string>() ?? (oldStub == null ? null : Hash(Encoding.UTF8.GetBytes(Json(oldStub))));
                    var newBinding = stubBindings[relative]?.GetValue<string>();
                    var edited = previous["stubTemplateHashes"]?[relative]?.GetValue<string>() != Hash(File.ReadAllBytes(file));
                    if (relative.StartsWith("Stubs/", StringComparison.Ordinal) && edited && newBinding != null && oldBinding != newBinding)
                    {
                        var newStub = newStubs.FirstOrDefault(s => s!.S("file") == oldStub?.S("file"));
                        if (oldStub != null && newStub != null && oldIr != null && CanUpgradeLayoutBinding(oldStub, newStub, oldIr, oldBinding))
                        {
                            oldBinding = newBinding;
                        }
                        else
                        {
                            throw new IOException("Edited stub binding changed: " + relative + ". Generate into a new directory and explicitly port the implementation after reviewing reports/stubs.json.");
                        }
                    }

                    if (newBinding == null && oldBinding != null)
                    {
                        stubBindings[relative] = oldBinding;
                    }

                    editableContents[relative] = File.ReadAllBytes(file);
                    if (generatedTestHashes.ContainsKey(relative) && previous["userTemplateHashes"]?[relative] == null)
                    {
                        // 保持した旧テストに、今回生成した雛形の来歴を後付けしません。
                        userTemplates[relative] = null;
                    }

                    files[relative] = string.Empty;
                    continue;
                }

                if (relative != "manifest.json" && previous["artifacts"]?[relative] == null)
                {
                    throw new IOException("Unmanaged file prevents regeneration: " + relative);
                }
            }
        }

        if ((previous?["userTemplateHashes"] ?? previous?["stubTemplateHashes"]) is JsonObject oldTemplates)
        {
            foreach (var (path, hash) in oldTemplates)
            {
                if (!IsUserFile(path) || Path.IsPathRooted(path) || path.Contains('\\') || path.Split('/').Any(p => p is ".." or "." or ""))
                {
                    throw new IOException("Unsafe user template path");
                }

                if (!File.Exists(Path.Combine(output, path)))
                {
                    files.Remove(path);
                }

                userTemplates[path] = hash?.DeepClone();
                userBindings[path] = previous?["userBindings"]?[path]?.DeepClone() ?? userBindings[path]?.DeepClone();
            }
        }

        var scenarioRegistrations = new JsonObject();
        if (files.TryGetValue("Api/GeneratedScenarioRegistration.g.cs", out var generatedRegistration))
        {
            foreach (var program in generatedPrograms)
            {
                var programName = SourceBackend.Identifier(program.S("id"));
                var registrations = new JsonArray();
                if (!files.ContainsKey($"UserCode/Scenarios/{programName}Scenarios.cs"))
                {
                    generatedRegistration = string.Join('\n', generatedRegistration.Split('\n').Where(line => !line.Contains(programName + "Scenarios.Sample", StringComparison.Ordinal)));
                }
                else
                {
                    registrations.Add("sample");
                }

                var scriptedPath = $"UserCode/Scenarios/{programName}ScriptedScenarios.cs";
                var previouslyRegistered = previous?["scenarioRegistrations"]?[program.S("id")]?.AsArray().Any(n => n?.GetValue<string>() == "sample-scripted") == true;
                if (!files.ContainsKey(scriptedPath) || (previous != null && !previouslyRegistered))
                {
                    generatedRegistration = string.Join('\n', generatedRegistration.Split('\n').Where(line => !line.Contains(programName + "ScriptedScenarios.Sample", StringComparison.Ordinal)));
                }
                else
                {
                    registrations.Add("sample-scripted");
                }

                scenarioRegistrations[program.S("id")] = registrations;
                if (previous != null && !previouslyRegistered)
                {
                    files["reports/execution-readiness.md"] += $"\n- `{program.S("id")}` migration: sample preserves the existing user file and its behavior. Omit the Executor.Run external argument to use edited Stubs. No meaning is inferred from arbitrary user C#. Review `{scriptedPath}` and register `registry.Register({JsonSerializer.Serialize(program.S("id"))}, \"sample-scripted\", {programName}ScriptedScenarios.Sample);` in UserCode/ScenarioRegistration.cs only after checking the class and method exist. Automatic scripted registration is withheld for existing outputs. Duplicate names are rejected: rename the user registration or remove the duplicate registration. Review files and hashes remain unchanged; explicitly include used Stubs, scenario inputs and response setup when reviewing.\n";
                }
            }

            files["Api/GeneratedScenarioRegistration.g.cs"] = generatedRegistration;
        }

        var orphans = new JsonArray(userBindings.Where(p => p.Value != null && !generatedPrograms.Any(program => program.S("id") == p.Value.GetValue<string>())).Select(p => (JsonNode?)new JsonObject { ["file"] = p.Key, ["programId"] = p.Value!.DeepClone(), ["action"] = "Preserved user file for removed or rejected program; review manually." }).ToArray());
        if (backend == "source")
        {
            files["reports/orphan-user-files.json"] = Json(orphans);
        }

        var contractHashes = new JsonObject();
        foreach (var (path, content) in files.Where(p => (p.Key.StartsWith("Contracts/", StringComparison.Ordinal) || p.Key.StartsWith("Models/", StringComparison.Ordinal) || p.Key.StartsWith("Api/", StringComparison.Ordinal))))
        {
            contractHashes[path] = Hash(Encoding.UTF8.GetBytes(content));
        }

        var changes = new JsonArray();
        if (previous?["contractHashes"] is JsonObject oldContracts)
        {
            foreach (var path in oldContracts.Select(p => p.Key).Union(contractHashes.Select(p => p.Key)).Order(StringComparer.Ordinal))
            {
                if (!JsonNode.DeepEquals(oldContracts[path], contractHashes[path]))
                {
                    changes.Add(new JsonObject { ["file"] = path, ["previousHash"] = oldContracts[path]?.DeepClone(), ["currentHash"] = contractHashes[path]?.DeepClone(), ["action"] = "Review the contract and implement the methods in reports/stubs.json; existing Stubs/*.cs were preserved without patching." });
                }
            }
        }

        if (backend == "source")
        {
            files["reports/contract-changes.json"] = Json(changes);
        }

        if (files.TryGetValue("reports/execution-readiness.json", out var readinessText))
        {
            var readiness = J.Parse(readinessText);
            foreach (var item in readiness.A("items"))
            {
                var edit = item.S("editFile");
                item["exampleStatus"] = !files.ContainsKey(edit) ? "deleted-user-file" : editableContents.ContainsKey(edit) ? "preserved-user-file; compare current contract and add missing example methods manually" : "generated";
                item["contractChangesReport"] = "reports/contract-changes.json";
            }

            foreach (var candidate in readiness.A("assertionCandidates"))
            {
                if (candidate.S("status") != "manual-unknown")
                {
                    var testFile = candidate.S("testFile");
                    var unchangedTemplate = editableContents.TryGetValue(testFile, out var retainedTest) && generatedTestHashes.TryGetValue(testFile, out var generatedTestHash) && previous?["userTemplateHashes"]?[testFile]?.GetValue<string>() == generatedTestHash && Hash(retainedTest) == generatedTestHash;
                    candidate["status"] = !files.ContainsKey(testFile) || (editableContents.ContainsKey(testFile) && !unchangedTemplate) ? "manual-existing-tests" : "generated-template";
                    if (candidate.S("status") == "manual-existing-tests")
                    {
                        var prefix = $"Static update candidate `{candidate.S("programId")}` `{candidate.S("symbolId")}` `{candidate.S("name")}`: ";
                        files["reports/execution-readiness.md"] = files["reports/execution-readiness.md"].Replace(prefix + "generated-template;", prefix + "manual-existing-tests;", StringComparison.Ordinal);
                        files["reports/execution-readiness.md"] += $"- `{candidate.S("programId")}` `{candidate.S("symbolId")}`: preserved or deleted user test `{testFile}`; manually review new assertion candidates. No test coverage or reviewed expectation is inferred.\n";
                    }
                }
            }

            files["reports/execution-readiness.json"] = Json(readiness);
        }

        var artifactHashes = new JsonObject();
        foreach (var (path, content) in files)
        {
            if (!IsUserFile(path))
            {
                artifactHashes[path] = Hash(Encoding.UTF8.GetBytes(content));
            }
        }

        var inputs = new JsonObject();
        foreach (var input in ir.A("sources"))
        {
            inputs[input.S("path")] = input.S("sha256");
        }

        var manifest = new JsonObject
        {
            ["manifestSchemaVersion"] = 2,
            ["scenarioRegistrations"] = scenarioRegistrations,
            ["userTemplateHashes"] = userTemplates,
            ["userBindings"] = userBindings,
            ["stubBindings"] = stubBindings,
            ["stubTemplateHashes"] = stubTemplateHashes,
            ["contractHashes"] = contractHashes,
            ["contractChanged"] = changes.Count != 0,
            ["backend"] = backend,
            ["mode"] = mode,
            ["editableFiles"] = new JsonArray(files.Keys.Where(IsUserFile).Select(k => (JsonNode?)JsonValue.Create(k)).ToArray()),
            ["toolVersion"] = Version,
            ["cobolTsCommit"] = CobolTsCommit,
            ["pythonBolCommit"] = ir.S("upstreamCommit"),
            ["configHash"] = ir.S("configHash"),
            ["inputHashes"] = inputs,
            ["artifacts"] = artifactHashes,
            ["programs"] = new JsonArray(generatedPrograms.Select(p => (JsonNode?)JsonValue.Create(p.S("id"))).ToArray()),
            ["rejectedPrograms"] = new JsonArray(rejectedPrograms.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()),
            ["judgments"] = new JsonObject
            {
                ["analyzed"] = !ir.A("diagnostics").Any(d => d.S("code") is "PARSE_ERROR" or "INPUT_DECODE"),
                ["generated"] = generated,
                ["strictRefused"] = mode == "strict" && errors,
                ["built"] = false,
                ["executedWithFixtures"] = false,
                ["matchedBaseline"] = false,
                ["needsReview"] = diagnostics.Count != 0,
            },
        };
        files["manifest.json"] = Json(manifest);
        var stage = output + ".stage-" + Guid.NewGuid().ToString("N");
        var backup = output + ".backup-" + Guid.NewGuid().ToString("N");
        try
        {
            foreach (var (path, content) in files)
            {
                var target = Path.Combine(stage, path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (editableContents.TryGetValue(path, out var edited))
                {
                    File.WriteAllBytes(target, edited);
                }
                else
                {
                    File.WriteAllText(target, content, new UTF8Encoding(false));
                }
            }

            if (!originalInventory.SequenceEqual(Inventory(output)))
            {
                throw new IOException("Output changed during generation; original files preserved.");
            }

            if (Directory.Exists(output))
            {
                Directory.Move(output, backup);
            }

            try
            {
                Directory.Move(stage, output);
            }
            catch
            {
                if (Directory.Exists(backup))
                {
                    Directory.Move(backup, output);
                }

                throw;
            }

            if (Directory.Exists(backup))
            {
                Directory.Delete(backup, recursive: true);
            }
        }
        finally
        {
            if (Directory.Exists(stage))
            {
                Directory.Delete(stage, recursive: true);
            }
        }

        return manifest;
    }

    /// <summary>指定された利用者所有領域だけを許可します。</summary>
    /// <param name="path">相対パス。</param>
    /// <returns>再生成で内容を保持する場合は true。</returns>
    internal static bool IsUserFile(string path) => path.StartsWith("Stubs/", StringComparison.Ordinal) || path.StartsWith("UserCode/", StringComparison.Ordinal) || path.StartsWith("Tests/Cases/", StringComparison.Ordinal) || path.StartsWith("Tests/Baselines/", StringComparison.Ordinal) || path.StartsWith("Fixtures/", StringComparison.Ordinal) || path == "Tests/review.json";

    /// <summary>全階層のビルド生成物をディレクトリ成分で除外します。</summary>
    /// <param name="path">相対パス。</param>
    /// <returns>配布しないビルド生成物の場合は true。</returns>
    internal static bool IsBuildFile(string path) => path.Split('/').SkipLast(1).Any(p => p is "bin" or "obj" or "TestResults");

    /// <summary>変更競合と危険なパスを検出する実ファイル一覧です。</summary>
    /// <param name="directory">検査対象。</param>
    /// <returns>パス順の内容ハッシュ。</returns>
    internal static SortedDictionary<string, string> Inventory(string directory)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(string current)
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(current))
            {
                SafePath(path);
                var relative = Path.GetRelativePath(directory, path).Replace('\\', '/');
                if (!names.Add(relative))
                {
                    throw new IOException("Case-colliding output path: " + relative);
                }

                if (Directory.Exists(path))
                {
                    if (Path.GetFileName(path) is not ("bin" or "obj" or "TestResults"))
                    {
                        Visit(path);
                    }
                }
                else
                {
                    result.Add(relative, Hash(File.ReadAllBytes(path)));
                }
            }
        }

        if (Directory.Exists(directory))
        {
            Visit(directory);
        }

        return result;
    }

    /// <summary>旧版の検証済み IR から同一レイアウトを確認できる場合だけ束縛を拡張します。</summary>
    /// <param name="oldStub">以前の管理対象契約。</param>
    /// <param name="newStub">新しい契約。</param>
    /// <param name="oldIr">以前の管理対象宣言。</param>
    /// <param name="oldBinding">以前の束縛 hash。</param>
    /// <returns>契約・原文・参照宣言がすべて不変の場合は true。</returns>
    private static bool CanUpgradeLayoutBinding(JsonNode oldStub, JsonNode newStub, JsonNode oldIr, string? oldBinding)
    {
        if (oldStub["referencedLayouts"] != null || newStub["referencedLayouts"] is not JsonArray layouts || oldBinding != Hash(Encoding.UTF8.GetBytes(Json(oldStub))))
        {
            return false;
        }

        var previousProgram = oldIr.A("programs").SingleOrDefault(p => p.S("id") == oldStub.S("programId"));
        if (previousProgram == null)
        {
            return false;
        }

        var previousLayouts = new JsonArray();
        foreach (var layout in layouts)
        {
            var symbol = previousProgram.A("symbols").SingleOrDefault(s => s.S("id") == layout!.S("id"));
            if (symbol == null)
            {
                return false;
            }

            previousLayouts.Add(symbol.DeepClone());
        }

        var upgraded = oldStub.DeepClone();
        upgraded["referencedLayouts"] = previousLayouts;
        return JsonNode.DeepEquals(upgraded, newStub);
    }
}
