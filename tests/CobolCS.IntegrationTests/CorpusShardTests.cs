using System.Text.Json.Nodes;
using CobolCS.Runtime;
using CobolCS.Tools;
using Xunit;

namespace CobolCS.IntegrationTests;

/// <summary>固定コーパスの分割が検証範囲と全体の下限を弱めないことを確認します。</summary>
public sealed class CorpusShardTests
{
    /// <summary>通常評価と2分割を実際に生成・ビルドし、同じ全ケースをCLIで統合します。</summary>
    [Fact]
    public void ShardsBuildEveryCaseAndMergeThroughCli()
    {
        using var folder = new TestDirectory();
        var source = Path.Combine(folder.Path, "synthetic.cbl");
        File.WriteAllText(source, "IDENTIFICATION DIVISION. PROGRAM-ID. SYNTHETIC. PROCEDURE DIVISION. DISPLAY 'OK' GOBACK.");
        var locked = SyntheticLock();
        foreach (var item in locked.A("cases"))
        {
            item["source"] = "synthetic.cbl";
            item["sha256"] = Workspace.Digest(source);
            item["sourceFormat"] = "free";
            item["copybookPaths"] = new JsonArray();
            item.AsObject().Remove("execution");
        }

        var lockPath = Path.Combine(folder.Path, "lock.json");
        Workspace.Write(lockPath, locked);
        var full = Corpus.Evaluate(lockPath, folder.Path, Path.Combine(folder.Path, "full"), build: true);
        var input = Path.Combine(folder.Path, "parts");
        var parts = Enumerable.Range(0, 2).Select(i => Corpus.Evaluate(lockPath, folder.Path, Path.Combine(input, "part-" + i), build: true, shardIndex: i, shardCount: 2)).ToArray();
        Assert.Equal(2, parts[0].A("cases").Count());
        Assert.Single(parts[1].A("cases"));
        Assert.False(Corpus.Verify(parts[0]));
        var merged = Corpus.MergeEvaluations(lockPath, parts);
        Assert.True(Corpus.Verify(merged));
        Assert.True(JsonNode.DeepEquals(full["counts"], merged["counts"]));
        Assert.Equal(full.A("cases").Select(c => c.S("id")).ToArray(), merged.A("cases").Select(c => c.S("id")).ToArray());
        Assert.True(merged.A("cases").All(c => c.B("built") && c["phases"]!["build"]!.I("exitCode") == 0));
        var output = Path.Combine(folder.Path, "merged/summary.json");
        var result = Workspace.Command(["dotnet", typeof(Corpus).Assembly.Location, "merge-evaluations", "--lock", lockPath, "--input", input, "--out", output]);
        Assert.True(result.I("exitCode") == 0, result.ToJsonString());
        Assert.True(Corpus.Verify(Workspace.Read(output)));
    }

    /// <summary>欠落・重複・別条件・分割への誤配・入力hash変更を統合時に拒否します。</summary>
    /// <param name="mutation">改変する証跡。</param>
    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("unfinished")]
    [InlineData("compiler")]
    [InlineData("floor")]
    [InlineData("wrong-shard")]
    [InlineData("source-hash")]
    public void InvalidShardEvidenceIsRejected(string mutation)
    {
        using var folder = new TestDirectory();
        var path = Path.Combine(folder.Path, "lock.json");
        var locked = SyntheticLock();
        Workspace.Write(path, locked);
        var parts = SyntheticParts(path, locked);
        switch (mutation)
        {
            case "missing":
                parts = [parts[0]];
                break;
            case "duplicate":
                parts = [parts[0], parts[0]];
                break;
            case "unfinished":
                parts[0]["cases"]!.AsArray().RemoveAt(0);
                break;
            case "compiler":
                parts[1]["compilerSourceSha256"] = "different compiler";
                break;
            case "floor":
                parts[1]["minimumGenerated"] = 0;
                break;
            case "wrong-shard":
                parts[0]["cases"]![0] = parts[1]["cases"]![0]!.DeepClone();
                break;
            case "source-hash":
                parts[0]["cases"]![0]!["sha256"] = "different source";
                break;
        }

        Assert.ThrowsAny<ArgumentException>(() => Corpus.MergeEvaluations(path, parts));
    }

    /// <summary>全分割が揃っても、ビルド・独立期待値・元の下限を満たさなければ成功にしません。</summary>
    /// <param name="mutation">失敗させる工程。</param>
    [Theory]
    [InlineData("build")]
    [InlineData("expectation")]
    [InlineData("floor")]
    public void MergedFailuresStillFailVerification(string mutation)
    {
        using var folder = new TestDirectory();
        var path = Path.Combine(folder.Path, "lock.json");
        var locked = SyntheticLock();
        Workspace.Write(path, locked);
        var parts = SyntheticParts(path, locked);
        if (mutation == "build")
        {
            parts[0]["cases"]![0]!["built"] = false;
        }
        else if (mutation == "expectation")
        {
            parts[1]["cases"]![0]!["matchedIndependentExpectation"] = false;
        }
        else
        {
            foreach (var part in parts)
            {
                foreach (var row in part.A("cases"))
                {
                    row["strictGenerated"] = false;
                }
            }
        }

        Assert.False(Corpus.Verify(Corpus.MergeEvaluations(path, parts)));
    }

    private static JsonObject SyntheticLock() => new()
    {
        ["schemaVersion"] = 1,
        ["scope"] = "synthetic shard verification",
        ["minimumGenerated"] = 3,
        ["cases"] = new JsonArray(Enumerable.Range(0, 3).Select(i => (JsonNode?)new JsonObject { ["id"] = "case-" + i, ["source"] = "source-" + i, ["sha256"] = new string('a', 64), ["license"] = "MIT", ["execution"] = i == 1 ? new JsonObject() : null }).ToArray()),
    };

    private static JsonNode[] SyntheticParts(string path, JsonNode locked) => Enumerable.Range(0, 2).Select(index => (JsonNode)new JsonObject
    {
        ["schemaVersion"] = 1,
        ["scope"] = locked["scope"]!.DeepClone(),
        ["lockSha256"] = Workspace.Digest(path),
        ["expectedCases"] = 3,
        ["minimumGenerated"] = 3,
        ["expectedCaseIds"] = new JsonArray("case-0", "case-1", "case-2"),
        ["expectedExecutionIds"] = new JsonArray("case-1"),
        ["shardIndex"] = index,
        ["shardCount"] = 2,
        ["compilerCommit"] = "synthetic",
        ["compilerSourceSha256"] = new string('b', 64),
        ["dotnetVersion"] = "10.0.401",
        ["backend"] = "source",
        ["counts"] = new JsonObject { ["selected"] = index == 0 ? 2 : 1 },
        ["cases"] = new JsonArray(locked.A("cases").Where((_, i) => i % 2 == index).Select(c => (JsonNode?)new JsonObject { ["id"] = c.S("id"), ["source"] = c.S("source"), ["sha256"] = c.S("sha256"), ["strictGenerated"] = true, ["built"] = true, ["matchedIndependentExpectation"] = true, ["executedWithoutReports"] = true }).ToArray()),
    }).ToArray();
}
