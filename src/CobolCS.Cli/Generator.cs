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
    public const string Version = "0.1.0";

    /// <summary>採用した CobolTS 解析器の固定コミットです。解析結果の違いを調べる際の出発点になります。</summary>
    public const string CobolTsCommit = "3e42de61fa8e0e7747e37cb17a48732ac962e38d";

    /// <summary>台帳や診断を人が読みやすい段落付きの JSON にし、ファイル間で同じ表記規則を使用します。</summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
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
    /// <returns>入力と生成物のハッシュ、および工程別の判定を持つ台帳。</returns>
    /// <exception cref="IOException">生成先に手編集・管理外ファイル・危険なパスがある場合。</exception>
    public static JsonNode Generate(JsonNode ir, string output, string mode)
    {
        output = Path.GetFullPath(output);
        SafePath(output);
        var diagnostics = Validation.Check(ir);
        var errors = diagnostics.Any(d => d!.S("severity") == "error");
        var generated = mode != "analyze" && (mode == "scaffold" || !errors);
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["reports/ir.json"] = Json(ir),
            ["reports/diagnostics.json"] = Json(diagnostics),
            ["reports/diagnostics.md"] = string.Join('\n', diagnostics.Select(d => $"- {d!.S("code")}: {d!.S("message")} ({d!["source"]?.S("file")}:{d!["source"]?["start"]?.I("line")})")) + "\n",
            ["reports/dependencies.json"] = Json(new JsonArray(ir.A("programs").Select(p => (JsonNode?)new JsonObject { ["program"] = p.S("id"), ["dependencies"] = p["dependencies"]!.DeepClone() }).ToArray())),
            ["reports/ai-brief.md"] = "# CobolCS review\n\nReview reports/diagnostics.json and reports/ir.json. Change frontend/compiler/runtime, regenerate and run regression tests. Build and fixture success do not establish business equivalence.\n",
        };
        if (generated)
        {
            files["CobolCS.Generated.csproj"] = GeneratedCode.CreateProject();
            files["Directory.Build.props"] = "<Project />\n";
            foreach (var path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "runtime"), "*.cs"))
            {
                files["Runtime/" + Path.GetFileName(path)] = File.ReadAllText(path);
            }

            var index = 0;
            foreach (var program in ir.A("programs"))
            {
                files[$"Programs/Program{index:D4}.cs"] = GeneratedCode.CreateProgram(index, program, errors);
                index++;
            }

            files["Program.cs"] = GeneratedCode.CreateRunner(ir);
            files["LICENSE"] = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "licenses/CobolCS.txt"));
        }

        var artifactHashes = new JsonObject();
        foreach (var (path, content) in files)
            artifactHashes[path] = Hash(Encoding.UTF8.GetBytes(content));
        var inputs = new JsonObject();
        foreach (var input in ir.A("sources"))
        {
            inputs[input.S("path")] = input.S("sha256");
        }

        var manifest = new JsonObject
        {
            ["toolVersion"] = Version,
            ["cobolTsCommit"] = CobolTsCommit,
            ["pythonBolCommit"] = ir.S("upstreamCommit"),
            ["configHash"] = ir.S("configHash"),
            ["inputHashes"] = inputs,
            ["artifacts"] = artifactHashes,
            ["programs"] = new JsonArray(ir.A("programs").Select(p => (JsonNode?)JsonValue.Create(p.S("id"))).ToArray()),
            ["judgments"] = new JsonObject
            {
                ["analyzed"] = !ir.A("diagnostics").Any(d => d.S("code") is "PARSE_ERROR" or "INPUT_DECODE"),
                ["generated"] = generated,
                ["built"] = false,
                ["executedWithFixtures"] = false,
                ["matchedBaseline"] = false,
                ["needsReview"] = diagnostics.Count != 0,
            },
        };
        files["manifest.json"] = Json(manifest);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
        {
            var previous = ReadManifest(output);
            Verify(output, previous);
            foreach (var directory in Directory.EnumerateDirectories(output, "*", SearchOption.AllDirectories))
            {
                SafePath(directory);
            }

            foreach (var file in Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories))
            {
                SafePath(file);
                var relative = Path.GetRelativePath(output, file).Replace('\\', '/');
                if (relative.StartsWith("bin/") || relative.StartsWith("obj/"))
                {
                    continue;
                }

                if (relative != "manifest.json" && previous["artifacts"]?[relative] == null)
                {
                    throw new IOException("Unmanaged file prevents regeneration: " + relative);
                }
            }
        }

        var stage = output + ".stage-" + Guid.NewGuid().ToString("N");
        var backup = output + ".backup-" + Guid.NewGuid().ToString("N");
        try
        {
            foreach (var (path, content) in files)
            {
                var target = Path.Combine(stage, path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, content, new UTF8Encoding(false));
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
}
