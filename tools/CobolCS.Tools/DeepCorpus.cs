using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CobolCS.Frontend;
using CobolCS.Runtime;
using static CobolCS.Tools.Workspace;

namespace CobolCS.Tools;

/// <summary>深掘りコーパスの入力台帳と再開可能な工程別測定を管理します。</summary>
public static partial class DeepCorpus
{
    /// <summary>catalog の全項目をバイト検証し、原文を含まない固定台帳を保存します。</summary>
    /// <param name="root">取得済み入力。</param>
    /// <param name="output">新規台帳。</param>
    /// <returns>固定台帳。</returns>
    public static JsonObject Import(string root, string output)
    {
        var catalogPath = Corpus.Confined(root, "compact-selection/catalog.json");
        var cases = new JsonArray();
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in Read(catalogPath).AsArray().Select(n => n!))
        {
            var path = entry.S("path");
            if (!paths.Add(path))
            {
                throw new ArgumentException("Duplicate catalog path");
            }

            var available = File.Exists(Corpus.Confined(root, path));
            var bytes = available ? File.ReadAllBytes(Corpus.Confined(root, path)) : [];
            if (available && Hash(bytes) != entry.S("sha256"))
            {
                throw new ArgumentException("Catalog input hash mismatch: " + path);
            }

            var text = string.Empty;
            var encoding = "utf-8";
            try
            {
                text = new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                encoding = "unknown-non-utf8";
            }

            var directive = Regex.Match(text, @">>\s*SOURCE\s+FORMAT\s+(?:IS\s+)?(FREE|FIXED)", RegexOptions.IgnoreCase);
            var fixedEvidence = text.Split('\n').Count(line => Regex.IsMatch(line, @"^[ 0-9]{6}[ */D-]"));
            var freeEvidence = text.Split('\n').Count(line => Regex.IsMatch(line, @"^(?:IDENTIFICATION|ID|PROGRAM-ID|DATA|PROCEDURE|WORKING-STORAGE)\b", RegexOptions.IgnoreCase));
            var format = directive.Success ? directive.Groups[1].Value.ToLowerInvariant() : freeEvidence > 0 ? "free" : fixedEvidence > 0 ? "fixed" : "free";
            var evidence = directive.Success ? "source-format-directive" : freeEvidence > 0 ? "column-one-division-header-heuristic" : fixedEvidence > 0 ? "sequence-indicator-columns-heuristic" : "unknown-default-free";
            var config = new JsonObject { ["sourceFormat"] = format, ["sourceEncoding"] = "utf-8" };
            var programs = Regex.Matches(text, @"\bPROGRAM-ID\s*\.\s*([\w-]+)", RegexOptions.IgnoreCase).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToArray();
            cases.Add(new JsonObject
            {
                ["id"] = "deep-" + Hash(Encoding.UTF8.GetBytes(path))[..16],
                ["source"] = path,
                ["sha256"] = entry.S("sha256"),
                ["inputAvailableAtImport"] = available,
                ["origin"] = entry.S("repository"),
                ["commit"] = entry.S("commit"),
                ["category"] = entry.S("category"),
                ["inputKind"] = Path.GetExtension(path).Equals(".tcbl", StringComparison.OrdinalIgnoreCase) ? "typecobol-extension" : programs.Length == 0 ? "copybook-or-fragment" : "original-program",
                ["inputKindEvidence"] = "lexical-program-id-only-unreviewed",
                ["encoding"] = encoding,
                ["encodingEvidence"] = "strict-utf8-byte-validation",
                ["encodingHint"] = entry.S("encoding_hint"),
                ["sourceFormat"] = format,
                ["sourceFormatEvidence"] = evidence,
                ["dialect"] = "unknown",
                ["programIds"] = Strings(programs),
                ["copyNames"] = Strings(entry.S("copy_names").Split(';', StringSplitOptions.RemoveEmptyEntries)),
                ["copybookPaths"] = new JsonArray(),
                ["copybookHashes"] = new JsonObject(),
                ["copyResolution"] = "unresolved-no-reviewed-search-order",
                ["duplicateOf"] = entry.S("duplicate_of"),
                ["licenseDisposition"] = "external-only-unreviewed",
                ["licenseNote"] = entry.S("license_note"),
                ["expectation"] = "unknown",
                ["oracle"] = null,
                ["config"] = config,
                ["configSha256"] = FrontendConfiguration.Parse(config.ToJsonString()).Hash,
            });
        }

        var result = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["scope"] = "deep-selected",
            ["catalogSha256"] = Digest(catalogPath),
            ["filesLockSha256"] = Digest(Path.Combine(Root, "tasks/deep-corpus/files.tsv.gz")),
            ["caseIdRule"] = "deep- plus first 16 hex SHA256 of UTF8 catalog relative path",
            ["expectedCases"] = cases.Count,
            ["distinctInputHashes"] = cases.Select(c => c!.S("sha256")).Distinct(StringComparer.Ordinal).Count(),
            ["cases"] = cases,
        };
        if (File.Exists(output))
        {
            throw new ArgumentException("Lock destination already exists");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        Write(output, result);
        return result;
    }

    /// <summary>製品と評価器のソースを別々に固定するための識別子を返します。</summary>
    /// <param name="directory">ハッシュ化するディレクトリ。</param>
    /// <returns>相対名とバイト列の識別子。</returns>
    public static string SourceHash(string directory)
    {
        using var bytes = new MemoryStream();
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Where(p => Path.GetExtension(p) is ".cs" or ".csproj").Where(p => !Path.GetRelativePath(directory, p).Split(Path.DirectorySeparatorChar).Any(s => s is "bin" or "obj")).Order(StringComparer.Ordinal))
        {
            bytes.Write(Encoding.UTF8.GetBytes(Path.GetRelativePath(directory, file).Replace('\\', '/') + "\0"));
            bytes.Write(File.ReadAllBytes(file));
            bytes.WriteByte(0);
        }

        return Hash(bytes.ToArray());
    }

    private static JsonArray Strings(IEnumerable<string> values) => new(values.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());

    private static void AtomicWrite(string path, JsonNode value)
    {
        var temporary = path + ".tmp";
        Write(temporary, value);
        File.Move(temporary, path, true);
    }
}
