using System.Text.Json.Nodes;
using System.Xml.Linq;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>検証済みの生成物を、再生成管理しない独立ソリューションへ採用します。</summary>
internal static class Adoption
{
    /// <summary>レビュー内容と実テストの対応を確認してから、新しい採用先を確定します。</summary>
    /// <param name="source">新形式の生成先。</param>
    /// <param name="destination">空または未作成の採用先。</param>
    /// <returns>レビュー・テスト拒否は 1、採用完了は 0。</returns>
    internal static async Task<int> Run(string source, string destination)
    {
        source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        Generator.SafePath(source);
        Generator.SafePath(destination);
        if (source.Equals(destination, StringComparison.OrdinalIgnoreCase) || source.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Source and destination must not contain each other");
        }

        if (File.Exists(destination) || (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any()))
        {
            throw new IOException("Adoption destination must be new or empty");
        }

        var manifest = Generator.ReadManifest(source);
        Generator.Verify(source, manifest);
        if (manifest.I("manifestSchemaVersion") != 2 || manifest.S("backend") != "source" || !manifest["judgments"]!.B("generated"))
        {
            throw new IOException("Adoption requires a schema 2 source project");
        }

        var before = Generator.Inventory(source);
        var allowed = before.Where(p => p.Key != "manifest.json" && !p.Key.StartsWith("reports/", StringComparison.Ordinal)).ToArray();
        foreach (var (path, _) in before)
        {
            if (path.Split('/').Any(p => p.Equals(".git", StringComparison.OrdinalIgnoreCase) || p.StartsWith(".env", StringComparison.OrdinalIgnoreCase) || p.Equals("NuGet.Config", StringComparison.OrdinalIgnoreCase)) || Path.GetExtension(path) is ".key" or ".pfx" or ".pem")
            {
                throw new IOException("Sensitive configuration is not an adoption artifact: " + path);
            }

            if (path != "manifest.json" && manifest["artifacts"]?[path] == null && !Generator.IsUserFile(path))
            {
                throw new IOException("Unmanaged file prevents adoption: " + path);
            }
        }

        JsonNode review;
        try
        {
            review = ValidateReview(source, manifest);
        }
        catch (InvalidOperationException error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var stage = destination + ".stage-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(stage);
            foreach (var (path, _) in allowed)
            {
                var target = Path.Combine(stage, path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(Path.Combine(source, path), target);
            }

            var stageBefore = Generator.Inventory(stage);
            await ProcessRunner.Run("dotnet", ["restore", "CobolCS.Generated.sln"], stage, 300);
            await ProcessRunner.Run("dotnet", ["build", "CobolCS.Generated.sln", "--no-restore", "-m:1"], stage, 300);
            try
            {
                await ProcessRunner.Run("dotnet", ["test", "Tests/CobolCS.Generated.Tests.csproj", "--no-build", "--no-restore", "--logger", "trx;LogFileName=adoption.trx", "--results-directory", "TestResults"], stage, 300);
            }
            catch (InvalidOperationException error)
            {
                Console.Error.WriteLine(error.Message);
                return 1;
            }

            var trxPath = Path.Combine(stage, "TestResults/adoption.trx");
            if (!File.Exists(trxPath))
            {
                Console.Error.WriteLine("Adoption requires a TRX report with actual tests");
                return 1;
            }

            System.Xml.Linq.XElement[] results;
            try
            {
                results = ValidateResults(trxPath, review.A("cases").Select(c => c.S("caseId")).ToArray());
            }
            catch (InvalidOperationException error)
            {
                Console.Error.WriteLine(error.Message);
                return 1;
            }

            if (!before.SequenceEqual(Generator.Inventory(source)) || !stageBefore.SequenceEqual(Generator.Inventory(stage)))
            {
                throw new IOException("Source or staged inputs changed during adoption");
            }

            foreach (var path in Directory.EnumerateDirectories(stage, "*", SearchOption.AllDirectories).OrderByDescending(p => p.Length))
            {
                if (Path.GetFileName(path) is "bin" or "obj" or "TestResults")
                {
                    Directory.Delete(path, recursive: true);
                }
            }

            var provenance = new JsonObject
            {
                ["toolVersion"] = manifest.S("toolVersion"),
                ["inputHashes"] = manifest["inputHashes"]!.DeepClone(),
                ["programs"] = manifest["programs"]!.DeepClone(),
                ["adoptedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
                ["files"] = new JsonObject(allowed.Select(p => KeyValuePair.Create<string, JsonNode?>(p.Key, JsonValue.Create(p.Value)))),
                ["review"] = review.DeepClone(),
                ["passedTests"] = results.Length,
                ["testResults"] = new JsonArray(results.Select(r => (JsonNode?)new JsonObject { ["caseId"] = (string?)r.Attribute("testName"), ["outcome"] = (string?)r.Attribute("outcome") }).ToArray()),
                ["limitations"] = "Synthetic or C# characterization only unless independently evidenced. No original COBOL equivalence or production migration claim. Review source distribution terms and user fixtures for secrets before redistribution.",
            };
            Directory.CreateDirectory(Path.Combine(stage, "provenance"));
            File.WriteAllText(Path.Combine(stage, "provenance/cobolcs-origin.json"), Generator.Json(provenance));
            if (Directory.Exists(destination))
            {
                Directory.Delete(destination);
            }

            Directory.Move(stage, destination);
            Console.WriteLine("Adopted user-owned solution: " + destination);
            return 0;
        }
        finally
        {
            if (Directory.Exists(stage))
            {
                Directory.Delete(stage, recursive: true);
            }
        }
    }

    /// <summary>実際の TRX の識別子とメソッド名を照合し、同名・skip・ゼロ件を拒否します。</summary>
    /// <param name="path">VSTest が出力した TRX。</param>
    /// <param name="required">全必須ケースのメソッド名。</param>
    /// <returns>一意に対応した成功結果。</returns>
    internal static XElement[] ValidateResults(string path, string[] required)
    {
        var trx = XDocument.Load(path);
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        var results = trx.Descendants(ns + "UnitTestResult").ToArray();
        var definitions = trx.Descendants(ns + "UnitTest").ToArray();
        if (results.Length == 0 || results.Any(r => (string?)r.Attribute("outcome") != "Passed") || results.GroupBy(r => (string?)r.Attribute("testName"), StringComparer.Ordinal).Any(g => g.Count() != 1) || definitions.GroupBy(d => (string?)d.Attribute("name"), StringComparer.Ordinal).Any(g => g.Count() != 1) || required.Distinct(StringComparer.Ordinal).Count() != required.Length)
        {
            throw new InvalidOperationException("Adoption requires unique passed, non-skipped tests; duplicate TRX names are refused");
        }

        foreach (var name in required)
        {
            var result = results.SingleOrDefault(r => (string?)r.Attribute("testName") == name) ?? throw new InvalidOperationException("Missing required test: " + name);
            var testId = (string?)result.Attribute("testId");
            var definition = definitions.SingleOrDefault(d => (string?)d.Attribute("id") == testId);
            var method = definition?.Element(ns + "TestMethod");
            var execution = definition?.Element(ns + "Execution");
            if (string.IsNullOrEmpty(testId) || string.IsNullOrWhiteSpace((string?)result.Attribute("executionId")) || (string?)definition?.Attribute("name") != name || (string?)method?.Attribute("name") != name || string.IsNullOrWhiteSpace((string?)method?.Attribute("className")) || (string?)execution?.Attribute("id") != (string?)result.Attribute("executionId"))
            {
                throw new InvalidOperationException("Ambiguous TRX definition for " + name);
            }
        }

        return results;
    }

    /// <summary>各プログラムの非 no-op ケースと期待値のレビューを要求します。</summary>
    /// <param name="source">レビュー対象のプロジェクト。</param>
    /// <param name="manifest">採用するプログラム一覧。</param>
    /// <returns>内容ハッシュが現在と一致したレビュー一覧。</returns>
    private static JsonNode ValidateReview(string source, JsonNode manifest)
    {
        var review = J.Parse(File.ReadAllText(Path.Combine(source, "Tests/review.json")));
        if (review.I("schemaVersion") != 1 || !review.A("cases").Any())
        {
            throw new InvalidOperationException("Missing baseline review cases");
        }

        var ids = new HashSet<string>();
        foreach (var entry in review.A("cases"))
        {
            if (!ids.Add(entry.S("caseId")) || !entry.B("reviewed") || entry.B("hasNoOpCalls") || entry.S("callStubMode") != "Throw" || entry.S("baselineKind") is not ("independent-synthetic" or "csharp-characterization" or "original-cobol-captured"))
            {
                throw new InvalidOperationException("Unreviewed, duplicate, or exploratory baseline case: " + entry.S("caseId"));
            }

            if (entry["files"] is not JsonObject hashes || !hashes.Any(p => p.Key.StartsWith("Tests/Cases/", StringComparison.Ordinal)) || !hashes.Any(p => p.Key.StartsWith("Tests/Baselines/", StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("Review must bind case and baseline files");
            }

            if (!manifest.A("programs").Any(p => p.GetValue<string>() == entry.S("programId")))
            {
                throw new InvalidOperationException("Review refers to an unknown or rejected program");
            }

            foreach (var (path, hash) in hashes)
            {
                if (!Generator.IsUserFile(path) || Path.IsPathRooted(path) || path.Contains('\\') || path.Split('/').Any(p => p is ".." or "." or ""))
                {
                    throw new IOException("Unsafe review file path");
                }

                if (!File.Exists(Path.Combine(source, path)) || Generator.Hash(File.ReadAllBytes(Path.Combine(source, path))) != hash!.GetValue<string>())
                {
                    throw new InvalidOperationException("Review hash mismatch: " + path);
                }

                if (path.StartsWith("Tests/Baselines/", StringComparison.Ordinal))
                {
                    var baseline = J.Parse(File.ReadAllText(Path.Combine(source, path)));
                    if (baseline.S("baselineKind") != entry.S("baselineKind") || (string.IsNullOrWhiteSpace(baseline.S("reviewReason")) || baseline.S("reviewReason").StartsWith("UNREVIEWED", StringComparison.Ordinal)) || baseline["expected"] is not JsonObject { Count: > 0 } || !JsonNode.DeepEquals(baseline["sourceHashes"], manifest["inputHashes"]) || baseline.S("toolVersion") != manifest.S("toolVersion"))
                    {
                        throw new InvalidOperationException("Baseline needs explicit expectations, source hashes, version and review reason: " + path);
                    }

                    if (entry.S("baselineKind") == "original-cobol-captured" && baseline["captureEvidence"] is not JsonObject { Count: > 0 })
                    {
                        throw new InvalidOperationException("Original COBOL baseline requires capture evidence");
                    }
                }
            }
        }

        foreach (var program in manifest.A("programs"))
        {
            if (!review.A("cases").Any(c => c.S("programId") == program.GetValue<string>()))
            {
                throw new InvalidOperationException("No reviewed non-no-op business case for " + program.GetValue<string>());
            }
        }

        return review;
    }
}
