using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CobolCS.Frontend.Tests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: DoNotParallelize]

namespace CobolCS.Frontend.CorpusTests;

/// <summary>lockで固定した全公開入力を両解析器で比較します。取得はCIの明示ステップで行います。</summary>
[TestClass]
public sealed class CorpusTests
{
    /// <summary>courseの両モードを比較します。</summary>
    /// <returns>非同期の差分検査。</returns>
    [TestMethod]
    public Task Course() => Verify("course");

    /// <summary>funの両モードを比較します。</summary>
    /// <returns>非同期の差分検査。</returns>
    [TestMethod]
    public Task Fun() => Verify("fun");

    /// <summary>bankの両モードを比較します。</summary>
    /// <returns>非同期の差分検査。</returns>
    [TestMethod]
    public Task Bank() => Verify("bank");

    /// <summary>carddemoの両モードを比較します。</summary>
    /// <returns>非同期の差分検査。</returns>
    [TestMethod]
    public Task Carddemo() => Verify("carddemo");

    /// <summary>db2の両モードを比較します。</summary>
    /// <returns>非同期の差分検査。</returns>
    [TestMethod]
    public Task Db2() => Verify("db2");

    private static async Task Verify(string corpus)
    {
        Baseline.VerifyReference();
        Baseline.VerifyInputs();
        var root = Baseline.Root;
        var sourceRoot = Path.Combine(root, ".cache/public-cobol-corpus", corpus);
        var locked = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "tests/public-corpus", corpus + ".lock.json")))!;
        var configPath = Path.Combine(Path.GetTempPath(), "cobolcs-corpus-" + Guid.NewGuid().ToString("N") + ".json");
        var compared = 0;
        try
        {
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
                File.WriteAllText(configPath, configText);
                foreach (var scaffold in new[] { false, true })
                {
                    var args = new List<string> { "python/frontend/main.py", source, "--config", configPath };
                    foreach (var copy in copies)
                    {
                        args.AddRange(["--copybook-path", copy]);
                    }

                    if (scaffold)
                    {
                        args.Add("--allow-missing-copy");
                    }

                    var reference = await ReferenceProcess.Run(Environment.GetEnvironmentVariable("COBOLCS_REFERENCE_PYTHON") ?? "python3", args, root, TimeSpan.FromSeconds(60));
                    Assert.AreEqual(0, reference.ExitCode, reference.Error);
                    var expected = JsonNode.Parse(reference.Output)!.AsObject();
                    var actual = FrontendAnalyzer.Analyze(source, FrontendConfiguration.Parse(configText), copies, scaffold);
                    Assert.AreEqual("0.2.2", expected["frontendVersion"]!.GetValue<string>());
                    Assert.AreEqual(FrontendAnalyzer.Version, actual["frontendVersion"]!.GetValue<string>());
                    expected["frontendVersion"] = FrontendAnalyzer.Version;
                    var id = item["id"]!.GetValue<string>() + (scaffold ? ":scaffold" : ":strict");
                    var diff = JsonComparison.Compare(id, expected.ToJsonString(), actual.ToJsonString());
                    Assert.IsEmpty(diff, string.Join("\n", diff.Take(10)));
                    compared++;
                }
            }
        }
        finally
        {
            File.Delete(configPath);
        }

        Console.WriteLine($"{corpus}: {compared} complete IR comparisons passed");
    }
}
