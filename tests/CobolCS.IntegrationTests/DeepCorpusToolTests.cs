using System.Text.Json.Nodes;
using CobolCS.Runtime;
using CobolCS.Tools;
using Xunit;

namespace CobolCS.IntegrationTests;

/// <summary>深掘り評価の欠落、改ざん、工程分離と再開条件を合成入力で検証します。</summary>
public sealed class DeepCorpusToolTests
{
    /// <summary>欠落入力は在庫へ残し、重複bytesの別pathは別IDへ固定します。</summary>
    [Fact]
    public void TestInventoryRetainsMissingAndDuplicateBytes()
    {
        using var directory = new TestDirectory();
        var root = directory.Path;
        Directory.CreateDirectory(Path.Combine(root, "compact-selection"));
        File.WriteAllText(Path.Combine(root, "one.cbl"), "IDENTIFICATION DIVISION.\nPROGRAM-ID. ONE.\nPROCEDURE DIVISION.\nGOBACK.\n");
        File.Copy(Path.Combine(root, "one.cbl"), Path.Combine(root, "two.cbl"));
        var hash = Workspace.Digest(Path.Combine(root, "one.cbl"));
        Workspace.Write(Path.Combine(root, "compact-selection/catalog.json"), new JsonArray(new[] { "one.cbl", "two.cbl", "missing" }.Select(p => (JsonNode?)new JsonObject { ["path"] = p, ["sha256"] = hash }).ToArray()));
        var locked = DeepCorpus.Import(root, Path.Combine(root, "lock.json"));
        Assert.Equal(3, locked.I("expectedCases"));
        Assert.Equal(1, locked.I("distinctInputHashes"));
        Assert.Equal(3, locked.A("cases").Select(c => c.S("id")).Distinct().Count());
        Assert.False(locked.A("cases").Last().B("inputAvailableAtImport"));
        Assert.Equal("free", locked.A("cases").First().S("sourceFormat"));
    }

    /// <summary>入力hash改ざんは台帳作成前に拒否します。</summary>
    [Fact]
    public void TestImportRejectsTamperedBytes()
    {
        using var directory = new TestDirectory();
        Directory.CreateDirectory(Path.Combine(directory.Path, "compact-selection"));
        File.WriteAllText(Path.Combine(directory.Path, "one.cbl"), "changed");
        Workspace.Write(Path.Combine(directory.Path, "compact-selection/catalog.json"), new JsonArray(new JsonObject { ["path"] = "one.cbl", ["sha256"] = new string('0', 64) }));
        Assert.Throws<ArgumentException>(() => DeepCorpus.Import(directory.Path, Path.Combine(directory.Path, "lock.json")));
        Assert.False(File.Exists(Path.Combine(directory.Path, "lock.json")));
    }

    /// <summary>解析checkpointは生成成功を表さず、設定変更や台帳変更で再利用しません。</summary>
    [Fact]
    public void TestStageCheckpointIdentityAndMissingInput()
    {
        using var directory = new TestDirectory();
        Directory.CreateDirectory(Path.Combine(directory.Path, "compact-selection"));
        Workspace.Write(Path.Combine(directory.Path, "compact-selection/catalog.json"), new JsonArray(new JsonObject { ["path"] = "missing.cbl", ["sha256"] = new string('0', 64) }));
        var lockPath = Path.Combine(directory.Path, "lock.json");
        var locked = DeepCorpus.Import(directory.Path, lockPath);
        var output = Path.Combine(directory.Path, "output");
        DeepCorpus.Evaluate(lockPath, directory.Path, output, "analyze", Workspace.Cli[0], Workspace.Root);
        var checkpoint = Path.Combine(output, locked.A("cases").Single().S("id"), "analyze.json");
        var row = Workspace.Read(checkpoint);
        Assert.Equal("input-missing", row.S("status"));
        Assert.False(row.B("inputVerified"));
        Assert.False(row.B("generated"));
        DeepCorpus.Evaluate(lockPath, directory.Path, output, "analyze", Workspace.Cli[0], Workspace.Root);
        Assert.Throws<ArgumentException>(() => DeepCorpus.Evaluate(lockPath, directory.Path, output, "analyze", Workspace.Cli[0], Workspace.Root, timeout: 121));
        var summary = DeepCorpus.Summarize(lockPath, output);
        Assert.Equal(1, summary["counts"]!.I("inventory"));
        Assert.Equal("not-performed", summary.A("cases").Single()["build"]!.S("status"));
    }
}
