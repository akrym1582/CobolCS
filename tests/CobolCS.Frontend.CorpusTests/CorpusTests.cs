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
                foreach (var (pointer, hash) in expected["hashes"]!.AsObject().OrderBy(p => p.Key.Length == 0))
                {
                    var fragment = pointer.Length == 0 ? actual : actual[pointer[1..]];
                    Assert.AreEqual(hash!.GetValue<string>(), CanonicalJson.Hash(fragment?.ToJsonString() ?? "null"), $"{corpus}/{id}:{scaffold} {pointer}");
                }

                compared++;
            }
        }

        Console.WriteLine($"{corpus}: {compared} complete frozen IR hash comparisons passed");
    }
}
