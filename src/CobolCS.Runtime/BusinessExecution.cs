using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace CobolCS.Runtime;

/// <summary>実行ごとに開始・ケース別・集約証跡を保存します。発見時には作成しません。</summary>
public sealed class BusinessExecution
{
    /// <summary>実行単位の保存先。</summary>
    private readonly string directory;

    /// <summary>開始時の登録と入力の束縛。</summary>
    private readonly JsonObject started;

    /// <summary>同じ実行内の終端結果。共有 static 状態を使いません。</summary>
    private readonly Dictionary<string, JsonNode> outcomes = new(StringComparer.Ordinal);

    /// <summary>集合検査が成功した場合だけ完了を許します。</summary>
    private bool integrity;

    /// <summary>Initializes a new instance of the <see cref="BusinessExecution"/> class. 開始時に入力・契約・採取元・実装を固定します。</summary>
    /// <param name="root">生成または採用先。</param>
    /// <param name="executionId">adopt と共有する新しい ID。通常実行では省略します。</param>
    public BusinessExecution(string root, string? executionId = null)
    {
        Root = root;
        ExecutionId = executionId ?? Guid.NewGuid().ToString("N");
        if (!Guid.TryParseExact(ExecutionId, "N", out _))
        {
            throw new ArgumentException("Invalid business execution ID");
        }

        if (File.Exists(Path.Combine(root, "CobolCS.Generated.csproj")) && !File.Exists(Path.Combine(root, "Testing/BusinessHistory.json")))
        {
            throw new IOException("Missing business registration history");
        }

        Inspection = BusinessCases.Inspect(root);
        directory = Path.Combine(root, "TestResults/business", ExecutionId);
        BusinessCases.SafeFile(directory);
        if (Directory.Exists(directory))
        {
            throw new IOException("Business execution ID already exists");
        }

        Directory.CreateDirectory(Path.Combine(directory, "cases"));
        started = new JsonObject
        {
            ["schemaVersion"] = 2,
            ["executionId"] = ExecutionId,
            ["startedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["caseIds"] = new JsonArray(Inspection.Cases.Select(c => (JsonNode?)JsonValue.Create(c.Case["caseId"]!.GetValue<string>())).ToArray()),
            ["inputHashes"] = InputHashes(root),
            ["implementationHashes"] = ImplementationHashes(root),
            ["diagnostics"] = new JsonArray(Inspection.Diagnostics.Select(d => (JsonNode?)new JsonObject { ["path"] = d.Path, ["caseId"] = d.CaseId, ["code"] = d.Code, ["reason"] = d.Reason, ["pointer"] = d.Pointer }).ToArray()),
        };
        Save("started.json", started);
        Summarize();
    }

    /// <summary>Gets この実行の元ディレクトリ。</summary>
    public string Root { get; }

    /// <summary>Gets 全行と adopt で共有する識別子。</summary>
    public string ExecutionId { get; }

    /// <summary>Gets 実行開始時の静的登録。</summary>
    public BusinessInspection Inspection { get; }

    /// <summary>任意の ID を安全で衝突しにくい行識別子へ変換します。元 ID は記録に保持します。</summary>
    /// <param name="caseId">大文字小文字を区別する ID。</param>
    /// <returns>完全な SHA-256。</returns>
    public static string CaseKey(string caseId) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(caseId)));

    /// <summary>ケース・契約・採取元・レビュー・履歴を内容 hash で束縛します。</summary>
    /// <param name="root">検査対象。</param>
    /// <returns>安全な相対パス別 hash。</returns>
    public static JsonObject InputHashes(string root) => Hashes(root, p => p.StartsWith("Tests/BusinessCases/", StringComparison.Ordinal) || p.StartsWith("Tests/BusinessContracts/", StringComparison.Ordinal) || p is "Tests/review.json" or "Testing/BusinessHistory.json");

    /// <summary>通常の実行ソースを束縛し、採取データを実装へ算入しません。</summary>
    /// <param name="root">検査対象。</param>
    /// <returns>実行時に参照する C# とプロジェクトの hash。</returns>
    public static JsonObject ImplementationHashes(string root) => Hashes(root, p => (p.EndsWith(".cs", StringComparison.Ordinal) && !p.StartsWith("Tests/BusinessCases/", StringComparison.Ordinal)) || p.EndsWith(".csproj", StringComparison.Ordinal) || p.EndsWith(".props", StringComparison.Ordinal) || p == "Tests/xunit.runner.json");

    /// <summary>ケース集合、履歴、診断を独立のテストで検査します。</summary>
    public void VerifyIntegrity()
    {
        if (Inspection.Diagnostics.Count != 0)
        {
            throw new ArgumentException(string.Join("\n", Inspection.Diagnostics.Select(d => d.Path + ": " + d.Reason)));
        }

        VerifyCurrent();
        integrity = true;
        Summarize();
    }

    /// <summary>ローカル登録から毎ケース一度生成し、例外・不一致時も破棄して失敗を伝えます。</summary>
    /// <param name="caseKey">発見時に固定した安全な ID。</param>
    /// <param name="register">利用者の factory 登録。発見時には呼びません。</param>
    /// <returns>非同期破棄の完了。</returns>
    public async Task Run(string caseKey, Action<IDictionary<string, Func<IBusinessCaseAdapter>>> register)
    {
        if (caseKey == "unconfigured" && Inspection.Cases.Count == 0)
        {
            Console.WriteLine("Business cases unconfigured; independent validation count = 0.");
            return;
        }

        var item = Inspection.Cases.Single(c => CaseKey(c.Case["caseId"]!.GetValue<string>()) == caseKey);
        var id = item.Case["caseId"]!.GetValue<string>();
        var outcome = new JsonObject
        {
            ["schemaVersion"] = 2,
            ["executionId"] = ExecutionId,
            ["caseId"] = id,
            ["caseKey"] = caseKey,
            ["programId"] = item.Case["programId"]!.DeepClone(),
            ["contractId"] = item.Case["contractId"]!.DeepClone(),
            ["caseHash"] = BusinessCases.FileHash(Path.Combine(Root, item.Path)),
            ["inputHash"] = CaseKey(item.Case["input"]!.ToJsonString()),
            ["status"] = "started",
            ["startedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
        };
        Save("cases/" + caseKey + ".json", outcome);
        var phase = "registration";
        try
        {
            if (outcomes.ContainsKey(id))
            {
                throw new InvalidOperationException("Duplicate business execution " + id);
            }

            VerifyCurrent();
            VerifyReview(item.Path, id);
            var factories = new Dictionary<string, Func<IBusinessCaseAdapter>>(StringComparer.Ordinal);
            register(factories);
            if (!factories.TryGetValue(item.Case["contractId"]!.GetValue<string>(), out var factory))
            {
                throw new InvalidOperationException("Missing business adapter factory");
            }

            phase = "factory";
            var adapter = factory() ?? throw new InvalidOperationException("Business factory returned null");
            try
            {
                phase = "execution";
                BusinessCases.Execute(item.Case, item.Contract, adapter);
            }
            finally
            {
                var precedingPhase = phase;
                phase = "disposal";
                if (adapter is IAsyncDisposable asynchronous)
                {
                    await asynchronous.DisposeAsync();
                }
                else if (adapter is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                phase = precedingPhase;
            }

            VerifyCurrent();
            outcome["status"] = "passed";
        }
        catch (Exception error)
        {
            outcome["status"] = "failed";
            var message = error.Message;
            outcome["failure"] = new JsonObject
            {
                ["reasonCode"] = error is OperationCanceledException ? "cancelled" : error is TimeoutException ? "timeout" : error.Data["BusinessReason"] is string reason ? reason : error.Data["BusinessExpected"] is string ? "comparison-failed" : phase + "-failed",
                ["exceptionType"] = error.GetType().FullName,
                ["pointer"] = error.Data["BusinessPointer"] as string ?? string.Empty,
                ["message"] = message[..Math.Min(4096, message.Length)],
                ["expected"] = error.Data["BusinessExpected"] as string,
                ["actual"] = error.Data["BusinessActual"] as string,
                ["omittedCharacters"] = Math.Max(0, message.Length - 4096) + (error.Data["BusinessOmittedCharacters"] as int? ?? 0),
            };
            throw;
        }
        finally
        {
            outcome["finishedAtUtc"] = DateTimeOffset.UtcNow.ToString("O");
            outcomes[id] = outcome;
            Save("cases/" + caseKey + ".json", outcome);
            Summarize();
        }
    }

    /// <summary>ビルド成果物を除外し、リンクを経由せずファイルを読む共通の束縛処理。</summary>
    private static JsonObject Hashes(string root, Func<string, bool> include)
    {
        var result = new JsonObject();
        void Visit(string folder)
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(folder).Order(StringComparer.Ordinal))
            {
                BusinessCases.SafeFile(path);
                if (Directory.Exists(path))
                {
                    if (Path.GetRelativePath(root, path).Replace('\\', '/').StartsWith("Tests/BusinessCases/Data/", StringComparison.Ordinal) || Path.GetFileName(path) is not ("bin" or "obj" or "TestResults" or ".git" or ".branch-probe"))
                    {
                        Visit(path);
                    }
                }
                else
                {
                    var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                    if (include(relative))
                    {
                        try
                        {
                            result[relative] = BusinessCases.FileHash(path);
                        }
                        catch (ArgumentException error) when (relative.StartsWith("Tests/BusinessCases/Data/", StringComparison.Ordinal) && error.Message.StartsWith("reference-data-error:", StringComparison.Ordinal))
                        {
                            result[relative] = "not-a-regular-file";
                        }
                    }
                }
            }
        }

        Visit(root);
        return result;
    }

    /// <summary>レビューやコピー後にも最新バイトを照合します。</summary>
    private void VerifyCurrent()
    {
        if (!JsonNode.DeepEquals(started["inputHashes"], InputHashes(Root)) || !JsonNode.DeepEquals(started["implementationHashes"], ImplementationHashes(Root)))
        {
            throw new InvalidOperationException("Business inputs or implementation changed during execution");
        }
    }

    /// <summary>採用後は実装のリファクタリングを許し、ケース・契約・採取元のレビューを維持します。</summary>
    private void VerifyReview(string path, string id)
    {
        if (!File.Exists(Path.Combine(Root, "provenance/cobolcs-origin.json")))
        {
            return;
        }

        var review = BusinessComparison.Parse(File.ReadAllText(Path.Combine(Root, "Tests/review.json")));
        var entry = review["cases"]!.AsArray().Single(c => c?["caseKind"]?.GetValue<string>() == "business" && c["caseId"]!.GetValue<string>() == id)!;
        if (entry["reviewed"]?.GetValue<bool>() != true || entry["files"]?[path] == null)
        {
            throw new InvalidOperationException("Business contract requires explicit review");
        }

        foreach (var pair in entry["files"]!.AsObject())
        {
            if (Path.IsPathRooted(pair.Key) || pair.Key.Contains('\\') || pair.Key.Contains(':') || pair.Key.Split('/').Any(p => p is ".." or "." or ""))
            {
                throw new InvalidOperationException("Unsafe business review path");
            }

            var file = Path.Combine(Root, pair.Key);
            BusinessCases.SafeFile(file);
            if (BusinessCases.FileHash(file) != pair.Value!.GetValue<string>())
            {
                throw new InvalidOperationException("Business review is stale: " + pair.Key);
            }
        }
    }

    /// <summary>終端結果が全 ID 分ある場合だけ完了とします。失敗の完了は成功ではありません。</summary>
    private void Summarize()
    {
        var complete = integrity && outcomes.Count == Inspection.Cases.Count && outcomes.Values.All(c => c["status"]!.GetValue<string>() is "passed" or "failed");
        Save("summary.json", new JsonObject
        {
            ["schemaVersion"] = 2,
            ["executionId"] = ExecutionId,
            ["complete"] = complete,
            ["passed"] = complete && outcomes.Count > 0 && outcomes.Values.All(c => c["status"]!.GetValue<string>() == "passed"),
            ["configuredCases"] = Inspection.Cases.Count,
            ["passedCases"] = outcomes.Values.Count(c => c["status"]!.GetValue<string>() == "passed"),
            ["cases"] = new JsonArray(outcomes.Values.Select(c => c.DeepClone()).ToArray()),
        });
    }

    /// <summary>単一の行だけが書くファイルを原子的に置換します。</summary>
    private void Save(string relative, JsonNode node)
    {
        var path = Path.Combine(directory, relative);
        BusinessCases.SafeFile(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, node.ToJsonString());
        File.Move(temporary, path, overwrite: true);
    }
}
