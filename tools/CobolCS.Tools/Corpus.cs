using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CobolCS.Runtime;
using static CobolCS.Tools.Workspace;

namespace CobolCS.Tools;

/// <summary>lockで固定した外部入力の取得・検証を行い、原文を製品やリポジトリへ取り込みません。</summary>
public static partial class Corpus
{
    /// <summary>配布されたアーカイブの固定識別子。</summary>
    public const string ArchiveSha256 = "ac73454b21c06b221c6f7bcaaa912376f68057ca51400dd6fc39070c3b5a1f40";

    /// <summary>Gets 対象の非GPLコーパス名。</summary>
    public static string[] Names => ["course", "fun", "carddemo", "bank", "db2"];

    /// <summary>正規な相対パスだけを許可し、リンクによるルート外への脱出も拒否します。</summary>
    /// <param name="root">入力のルート。</param>
    /// <param name="name">lockの相対名。</param>
    /// <returns>検証済みのパス。</returns>
    public static string Confined(string root, string name)
    {
        if (string.IsNullOrEmpty(name) || name.Contains('\\') || Path.IsPathRooted(name) || name.Split('/').Any(p => p is "" or "." or ".."))
        {
            throw new ArgumentException("Unsafe relative path: " + name);
        }

        var result = Path.Combine(root, name);
        var relative = Path.GetRelativePath(Resolve(root), Resolve(result));
        if (relative == ".." || relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            throw new ArgumentException("Path escapes root: " + name);
        }

        return result;
    }

    /// <summary>変換前に全選択入力とCOPY候補のバイトを検証します。</summary>
    /// <param name="lockPath">固定マニフェスト。</param>
    /// <param name="sourceRoot">取得済みソース。</param>
    /// <param name="includeGpl">明示的なGPL選択。</param>
    /// <returns>lockと検証済みケース。</returns>
    public static (JsonNode Lock, JsonNode[] Cases) LoadCases(string lockPath, string sourceRoot, bool includeGpl = false)
    {
        var locked = Read(lockPath);
        if (locked.I("schemaVersion") != 1)
        {
            throw new ArgumentException("Unsupported lock schema");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var cases = new List<JsonNode>();
        foreach (var item in locked.A("cases"))
        {
            var id = item.S("id");
            if (!Regex.IsMatch(id, @"\A[a-zA-Z0-9_-]+\z") || !seen.Add(id))
            {
                throw new ArgumentException("Invalid/duplicate case ID: " + id);
            }

            if ((item.B("optionalGpl") || item.S("license").Contains("GPL", StringComparison.OrdinalIgnoreCase)) && !includeGpl)
            {
                continue;
            }

            if (item.S("sourceFormat") is not ("fixed" or "free"))
            {
                throw new ArgumentException("Explicit sourceFormat required: " + id);
            }

            var source = Confined(sourceRoot, item.S("source"));
            if (!new[] { ".cbl", ".cob", ".cobol", ".sqb" }.Contains(Path.GetExtension(source).ToLowerInvariant()) || Digest(source) != item.S("sha256"))
            {
                throw new ArgumentException("Source extension/hash mismatch: " + id);
            }

            foreach (var copy in item.A("copybookPaths"))
            {
                var directory = Confined(sourceRoot, copy.GetValue<string>());
                if (!Directory.Exists(directory))
                {
                    throw new ArgumentException("Missing COPY directory: " + directory);
                }

                foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(sourceRoot, file).Replace('\\', '/');
                    _ = Confined(sourceRoot, relative);
                    if (item["copybookHashes"]?[relative] is null)
                    {
                        throw new ArgumentException("Unpinned COPY candidate: " + file);
                    }
                }
            }

            foreach (var (name, expected) in item["copybookHashes"]?.AsObject() ?? [])
            {
                if (Digest(Confined(sourceRoot, name)) != expected!.GetValue<string>())
                {
                    throw new ArgumentException("COPY hash mismatch: " + name);
                }
            }

            if (item["execution"] is { } execution)
            {
                if (execution.S("expectationKind") != "independent-calculation")
                {
                    throw new ArgumentException("Only independent-calculation expectations supported");
                }

                _ = Read(Confined(Path.GetDirectoryName(lockPath)!, execution.S("expectedFile")));
            }

            cases.Add(item);
        }

        return cases.Count == 0 ? throw new ArgumentException("No selected cases") : (locked, cases.ToArray());
    }

    /// <summary>コミットとライセンスを固定して取得します。既存の保存先は変更しません。</summary>
    /// <param name="lockPath">固定マニフェスト。</param>
    /// <param name="destination">新規取得先。</param>
    /// <param name="includeGpl">GPL取得の明示選択。</param>
    public static void Fetch(string lockPath, string destination, bool includeGpl = false)
    {
        var upstream = Read(lockPath)["upstream"]!;
        if (upstream.S("license").Contains("GPL", StringComparison.OrdinalIgnoreCase) && !includeGpl)
        {
            throw new ArgumentException("GPL acquisition requires --include-gpl");
        }

        if (!Regex.IsMatch(upstream.S("commit"), @"\A[0-9a-f]{40}\z"))
        {
            throw new ArgumentException("A full pinned commit is required");
        }

        NewDirectory(destination);
        foreach (var arguments in new[] { new[] { "git", "init", destination }, new[] { "git", "-C", destination, "fetch", "--depth=1", "--", upstream.S("url"), upstream.S("commit") }, new[] { "git", "-C", destination, "checkout", "--detach", "FETCH_HEAD" } })
        {
            var result = Command(arguments);
            if (result.I("exitCode") != 0)
            {
                throw new IOException(result.S("stderr"));
            }
        }

        if (Digest(Confined(destination, upstream.S("licenseFile"))) != upstream.S("licenseSha256"))
        {
            throw new ArgumentException("License hash mismatch");
        }

        _ = LoadCases(lockPath, destination, includeGpl);
    }

    /// <summary>ZIPの識別子、パス、各チェックサムを検証した後だけ保存先を公開します。</summary>
    /// <param name="archive">提供されたZIP。</param>
    /// <param name="destination">新しい展開先。</param>
    /// <returns>アーカイブのハッシュと検証件数。</returns>
    public static JsonObject ImportArchive(string archive, string destination) => ImportArchive(archive, destination, ArchiveSha256);

    /// <summary>テスト用の独立合成ZIPも同じ検証を通します。CLIは固定識別子だけを許可します。</summary>
    /// <param name="archive">ZIP。</param>
    /// <param name="destination">新しい展開先。</param>
    /// <param name="expectedHash">呼出し元が固定した識別子。</param>
    /// <returns>検証結果。</returns>
    internal static JsonObject ImportArchive(string archive, string destination, string expectedHash)
    {
        if (Digest(archive) != expectedHash)
        {
            throw new ArgumentException("Archive SHA-256 mismatch");
        }

        if (Path.Exists(destination))
        {
            throw new ArgumentException("Archive destination already exists");
        }

        using var zip = ZipFile.OpenRead(archive);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.TrimEnd('/');
            _ = Confined(destination, name);
            if (!seen.Add(name) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
            {
                throw new ArgumentException("Duplicate or symlink ZIP entry");
            }
        }

        var stage = destination + ".stage-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(stage);
            zip.ExtractToDirectory(stage);
            var sums = Directory.GetFiles(stage, "SHA256SUMS.txt", SearchOption.AllDirectories);
            if (sums.Length != 1)
            {
                throw new ArgumentException("Expected one SHA256SUMS.txt");
            }

            var count = 0;
            foreach (var line in File.ReadLines(sums[0]).Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                var match = Regex.Match(line, @"\A([0-9a-f]{64}) [ *](.+)\z");
                if (!match.Success || Digest(Confined(Path.GetDirectoryName(sums[0])!, match.Groups[2].Value)) != match.Groups[1].Value)
                {
                    throw new ArgumentException("SHA256SUMS mismatch: " + line);
                }

                count++;
            }

            Directory.Move(stage, destination);
            return new JsonObject { ["archiveSha256"] = expectedHash, ["verifiedEntries"] = count };
        }
        finally
        {
            if (Directory.Exists(stage))
            {
                Directory.Delete(stage, true);
            }
        }
    }

    private static void NewDirectory(string path)
    {
        if (Path.Exists(path))
        {
            throw new IOException("Destination already exists: " + path);
        }

        Directory.CreateDirectory(path);
    }
}
