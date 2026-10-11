using System.Text.Json;
using CobolCS.Cli;
using CobolCS.Runtime;
using CobolCS.Tools;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CobolCS.IntegrationTests;

/// <summary>シェルを使わない.NETプロセスの出力回収と終了を検証します。</summary>
[TestClass]
public sealed class ProcessTests
{
    /// <summary>並列restore後の再利用ノードが出力パイプを保持せず、親の設定に依存せず完了します。</summary>
    /// <param name="useTools">Tools側のプロセス境界を検査する場合はtrue。</param>
    /// <returns>両プロジェクトのrestoreと出力回収の完了を検査するタスク。</returns>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ParallelRestoreClosesOutputStreams(bool useTools)
    {
        using var folder = new TestDirectory();
        foreach (var name in new[] { "First", "Second" })
        {
            var directory = Path.Combine(folder.Path, name);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, name + ".csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        }

        File.WriteAllText(Path.Combine(folder.Path, "NuGet.Config"), "<configuration><packageSources><clear /></packageSources></configuration>");
        var solution = Path.Combine(folder.Path, "Restore.slnx");
        File.WriteAllText(solution, "<Solution><Project Path=\"First/First.csproj\" /><Project Path=\"Second/Second.csproj\" /></Solution>");
        var previous = Environment.GetEnvironmentVariable("MSBUILDDISABLENODEREUSE");
        try
        {
            Environment.SetEnvironmentVariable("MSBUILDDISABLENODEREUSE", "0");
            if (useTools)
            {
                var result = Workspace.Command(["dotnet", "restore", solution, "-m:2"], 30);
                Assert.IsFalse(result.B("timedOut"), result.ToJsonString());
                Assert.AreEqual(0, result.I("exitCode"), result.ToJsonString());
            }
            else
            {
                await ProcessRunner.Run("dotnet", ["restore", solution, "-m:2"], folder.Path, 30);
            }

            Assert.IsTrue(File.Exists(Path.Combine(folder.Path, "First/obj/project.assets.json")));
            Assert.IsTrue(File.Exists(Path.Combine(folder.Path, "Second/obj/project.assets.json")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("MSBUILDDISABLENODEREUSE", previous);
        }
    }

    /// <summary>引数のメタ文字とUTF-8をそのまま保持し、失敗時も両出力を返します。</summary>
    [TestMethod]
    public void ArgumentsAndBothStreamsArePreserved()
    {
        var argument = "日本 😀 spaces $() ; \"quoted\"";
        var result = Workspace.Command(["dotnet", typeof(ProcessProbe.Marker).Assembly.Location, argument]);
        Assert.AreEqual(7, result.I("exitCode"));
        CollectionAssert.AreEqual(new[] { argument }, JsonSerializer.Deserialize<string[]>(result.S("stdout"))!);
        Assert.AreEqual("probe-error" + Environment.NewLine, result.S("stderr"));
    }

    /// <summary>両方のパイプが容量を超えてもデッドロックしません。</summary>
    [TestMethod]
    public void LargeStreamsDoNotDeadlock()
    {
        var result = Workspace.Command(["dotnet", typeof(ProcessProbe.Marker).Assembly.Location, "large"], 10);
        Assert.IsFalse(result.B("timedOut"));
        Assert.AreEqual(0, result.I("exitCode"));
        Assert.AreEqual(new string('a', 200000), result.S("stdout"));
        Assert.AreEqual(new string('b', 200000), result.S("stderr"));
    }

    /// <summary>探索の出力量制限は両パイプを回収し、成功出力として扱いません。</summary>
    /// <returns>上限超過プロセスの終了。</returns>
    [TestMethod]
    public async Task BoundedExplorationOutputIsRejected()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ProcessRunner.Run("dotnet", [typeof(ProcessProbe.Marker).Assembly.Location, "large"], timeoutSeconds: 10, maxOutputCharacters: 1000));
        StringAssert.Contains(error.Message, "output limit");
    }

    /// <summary>強制 timeout 後も回収された出力を取得でき、完走と混同しません。</summary>
    /// <returns>timeout と出力回収。</returns>
    [TestMethod]
    public async Task ExplorationTimeoutCollectsStreams()
    {
        var error = await Assert.ThrowsAsync<TimeoutException>(() => ProcessRunner.Run("dotnet", [typeof(ProcessProbe.Marker).Assembly.Location, "delay"], timeoutSeconds: 1));
        Assert.IsNotNull(error.Data["StandardOutput"]);
        Assert.IsNotNull(error.Data["StandardError"]);
    }

    /// <summary>制限時間で子プロセスを終了し、成功とは区別します。</summary>
    [TestMethod]
    public void TimeoutTerminatesProcess()
    {
        var result = Workspace.Command(["dotnet", typeof(ProcessProbe.Marker).Assembly.Location, "delay"], 0.3);
        Assert.IsTrue(result.B("timedOut"));
        Assert.AreNotEqual(0, result.I("exitCode"));
    }
}
