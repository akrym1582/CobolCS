using System.Text.Json.Nodes;
using CobolCS.Cli;
using CobolCS.Frontend;
using CobolCS.Frontend.Tests;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace CobolCS.IntegrationTests;

/// <summary>解析器の変更が生成コード・契約・利用者の編集へ及ぼす影響を検証します。</summary>
public sealed class GenerationTests
{
    /// <summary>Gets 全example、両backend、両生成モードの組み合わせ。</summary>
    public static IEnumerable<object[]> Inputs =>
        from item in Baseline.Cases
        where item["input"]!.GetValue<string>().StartsWith("examples/", StringComparison.Ordinal)
        from backend in new[] { "source", "interpreter" }
        from mode in new[] { "strict", "scaffold" }
        select new object[] { item["id"]!.GetValue<string>(), backend, mode };

    /// <summary>固定Python IRからの生成物とC# IRからの生成物を比較します。</summary>
    /// <param name="id">固定入力。</param>
    /// <param name="backend">生成backend。</param>
    /// <param name="mode">拒否・scaffoldモード。</param>
    [Theory]
    [MemberData(nameof(Inputs))]
    public void GeneratedArtifactsMatch(string id, string backend, string mode)
    {
        Baseline.VerifyInputs();
        var item = Baseline.Cases.Single(c => c["id"]!.GetValue<string>() == id);
        var expected = JsonNode.Parse(Baseline.Expected(item))!;
        var copies = item["copybookPaths"]!.AsArray().Select(p => Path.Combine(Baseline.Root, p!.GetValue<string>()));
        var actual = FrontendAnalyzer.Analyze(Path.Combine(Baseline.Root, item["input"]!.GetValue<string>()), copybookPaths: copies);
        var root = Path.Combine(Path.GetTempPath(), "cobolcs-generation-" + Guid.NewGuid().ToString("N"));
        var before = Path.Combine(root, "before");
        var after = Path.Combine(root, "after");
        try
        {
            var oldManifest = Generator.Generate(expected, before, mode, backend);
            var newManifest = Generator.Generate(actual, after, mode, backend);
            Generator.Verify(before, oldManifest);
            Generator.Verify(after, newManifest);
            var oldPaths = Directory.GetFiles(before, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(before, p)).Order().ToArray();
            var newPaths = Directory.GetFiles(after, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(after, p)).Order().ToArray();
            Assert.Equal(oldPaths, newPaths);
            foreach (var path in oldPaths)
            {
                var left = File.ReadAllText(Path.Combine(before, path));
                var right = File.ReadAllText(Path.Combine(after, path));
                if (path == "reports/ir.json")
                {
                    var reference = JsonNode.Parse(left)!;
                    Assert.Equal("0.2.2", reference["frontendVersion"]!.GetValue<string>());
                    Assert.Equal(FrontendAnalyzer.Version, JsonNode.Parse(right)!["frontendVersion"]!.GetValue<string>());
                    reference["frontendVersion"] = FrontendAnalyzer.Version;
                    Assert.Empty(JsonComparison.Compare(id + ":" + path, reference.ToJsonString(), right));
                }
                else if (path == "manifest.json")
                {
                    // Both hashes have been verified against the files above. Only IR metadata changes.
                    oldManifest["artifacts"]!["reports/ir.json"] = newManifest["artifacts"]!["reports/ir.json"]!.DeepClone();
                    var differences = JsonComparison.Compare(id + ":" + path, oldManifest.ToJsonString(), newManifest.ToJsonString());
                    Assert.True(!differences.Any(), string.Join(Environment.NewLine, differences));
                }
                else
                {
                    Assert.True(right == left, id + ":" + path);
                }
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    /// <summary>Python版で作った出力の編集領域をC#版で再生成しても保持し、管理ファイルの変更・削除は拒否します。</summary>
    [Fact]
    public void RegenerationPreservesEditsAndRejectsManagedChanges()
    {
        var item = Baseline.Cases.First(c => c["input"]!.GetValue<string>().Contains("UNSUPPORTED", StringComparison.Ordinal));
        var output = Path.Combine(Path.GetTempPath(), "cobolcs-upgrade-" + Guid.NewGuid().ToString("N"));
        try
        {
            Generator.Generate(JsonNode.Parse(Baseline.Expected(item))!, output, "scaffold");
            var stub = Directory.GetFiles(Path.Combine(output, "Stubs"), "*.cs", SearchOption.AllDirectories).First();
            var edited = File.ReadAllBytes(stub).Concat(System.Text.Encoding.UTF8.GetBytes("\n// User implementation: 日本語\n")).ToArray();
            File.WriteAllBytes(stub, edited);
            var actual = FrontendAnalyzer.Analyze(Path.Combine(Baseline.Root, item["input"]!.GetValue<string>()));
            var manifest = Generator.Generate(actual, output, "scaffold");
            Assert.Equal(edited, File.ReadAllBytes(stub));
            Generator.Verify(output, manifest);
            var managed = Directory.GetFiles(Path.Combine(output, "Programs"), "*.cs").First();
            var original = File.ReadAllBytes(managed);
            File.AppendAllText(managed, "\n// edited\n");
            Assert.ThrowsAny<IOException>(() => Generator.Generate(actual, output, "scaffold"));
            File.WriteAllBytes(managed, original);
            File.Delete(managed);
            Assert.ThrowsAny<IOException>(() => Generator.Generate(actual, output, "scaffold"));
            Assert.Equal(edited, File.ReadAllBytes(stub));
        }
        finally
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, true);
            }
        }
    }
}
