using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CobolCS.Frontend.Tests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: DoNotParallelize]

namespace CobolCS.Frontend.CorpusTests;

/// <summary>lockで固定した全公開入力を固定参照ハッシュと比較します。取得はCIの明示ステップで行います。</summary>
[TestClass]
public sealed class CorpusTests
{
    /// <summary>courseの両モードを比較します。</summary>
    [TestMethod]
    public void Course() => Verify("course");

    /// <summary>funの両モードを比較します。</summary>
    [TestMethod]
    public void Fun() => Verify("fun");

    /// <summary>bankの両モードを比較します。</summary>
    [TestMethod]
    public void Bank() => Verify("bank");

    /// <summary>carddemoの両モードを比較します。</summary>
    [TestMethod]
    public void Carddemo() => Verify("carddemo");

    /// <summary>db2の両モードを比較します。</summary>
    [TestMethod]
    public void Db2() => Verify("db2");

    private static void Verify(string corpus)
    {
        Baseline.VerifyInputs();
        var root = Baseline.Root;
        var sourceRoot = Path.Combine(root, ".cache/public-cobol-corpus", corpus);
        var locked = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "tests/public-corpus", corpus + ".lock.json")))!;
        var reference = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "tests/migration/corpus-ir-hashes.json")))!;
        var corpusReference = reference["corpora"]!.AsArray().Single(c => c!["corpus"]!.GetValue<string>() == corpus)!;
        var reviewed = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "tests/migration/reviewed-corpus-changes.json")))!;
        Assert.AreEqual(1, reviewed["schemaVersion"]!.GetValue<int>());
        Assert.AreEqual("DISPLAY_NO_ADVANCING", reviewed["cause"]!.GetValue<string>());
        var revisions = reviewed["cases"]!.AsArray().Where(c => c!["corpus"]!.GetValue<string>() == corpus).ToDictionary(c => (c!["id"]!.GetValue<string>(), c["scaffold"]!.GetValue<bool>()), c => c!);
        var comparedRevisions = 0;
        Assert.AreEqual(corpusReference["lockSha256"]!.GetValue<string>(), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "tests/public-corpus", corpus + ".lock.json")))));
        var compared = 0;
        foreach (var item in locked["cases"]!.AsArray())
        {
            var source = Path.Combine(sourceRoot, item!["source"]!.GetValue<string>());
            Assert.IsTrue(File.Exists(source), "Fetch pinned corpus first: " + source);
            Assert.AreEqual(item["sha256"]!.GetValue<string>(), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source))));
            foreach (var entry in item["copybookHashes"]?.AsObject() ?? new JsonObject())
            {
                Assert.AreEqual(entry.Value!.GetValue<string>(), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(sourceRoot, entry.Key)))));
            }

            var copies = item["copybookPaths"]!.AsArray().Select(p => Path.Combine(sourceRoot, p!.GetValue<string>())).ToArray();
            var configText = new JsonObject { ["sourceFormat"] = item["sourceFormat"]!.GetValue<string>() }.ToJsonString();
            foreach (var scaffold in new[] { false, true })
            {
                var actual = FrontendAnalyzer.Analyze(source, FrontendConfiguration.Parse(configText), copies, scaffold);
                Assert.AreEqual(FrontendAnalyzer.Version, actual["frontendVersion"]!.GetValue<string>());

                // The implementation version is the only declared migration difference.
                actual["frontendVersion"] = "0.2.2";
                var id = item["id"]!.GetValue<string>();
                var expected = corpusReference["cases"]!.AsArray().Single(c => c!["id"]!.GetValue<string>() == id && c["scaffold"]!.GetValue<bool>() == scaffold)!;
                revisions.TryGetValue((id, scaffold), out var revision);
                if (revision is not null)
                {
                    Assert.AreEqual(item["sha256"]!.GetValue<string>(), revision["inputSha256"]!.GetValue<string>(), "Reviewed change must bind to the original input");
                    Assert.IsNotEmpty(revision["changes"]!.AsArray());
                    Assert.IsTrue(revision["evidence"]!["structurallyLimited"]!.GetValue<bool>());
                    Assert.HasCount(0, revision["evidence"]!["unexpected"]!.AsArray());
                    comparedRevisions++;
                }

                var comparedChanges = 0;
                foreach (var (pointer, hash) in expected["hashes"]!.AsObject().OrderBy(p => p.Key.Length == 0))
                {
                    var fragment = pointer.Length == 0 ? actual : actual[pointer[1..]];
                    var expectedHash = hash!.GetValue<string>();
                    var change = revision?["changes"]!.AsArray().SingleOrDefault(c => c!["pointer"]!.GetValue<string>() == pointer);
                    if (change is not null)
                    {
                        // 旧参照は保持し、原文と独立意味検査で審査した差だけ別台帳へ固定します。
                        Assert.IsTrue(pointer is "" or "/programs" or "/diagnostics", "DISPLAY revision cannot alter source/configuration evidence");
                        Assert.AreEqual(expectedHash, change["beforeSha256"]!.GetValue<string>(), "Frozen reference must remain unchanged");
                        expectedHash = change["afterSha256"]!.GetValue<string>();
                        Assert.AreNotEqual(expectedHash, hash!.GetValue<string>());
                        comparedChanges++;
                    }

                    Assert.AreEqual(expectedHash, CanonicalJson.Hash(fragment?.ToJsonString() ?? "null"), $"{corpus}/{id}:{scaffold} {pointer}");
                }

                Assert.AreEqual(revision?["changes"]!.AsArray().Count ?? 0, comparedChanges, "Every reviewed pointer must be compared");

                compared++;
            }
        }

        Assert.AreEqual(revisions.Count, comparedRevisions, "Every reviewed case must be compared");
        Console.WriteLine($"{corpus}: {compared} complete IR hash comparisons passed, including {comparedRevisions} explicitly reviewed DISPLAY revisions");
    }
}
