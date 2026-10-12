using System.Text.Json;
using CobolCS.Cli;
using CobolCS.Runtime;
using CobolCS.Tools;
using Xunit;

namespace CobolCS.IntegrationTests;

/// <summary>シェルを使わない.NETプロセスの出力回収と終了を検証します。</summary>
public sealed class ProcessTests
{
    /// <summary>並列restore後の再利用ノードが出力パイプを保持せず、親の設定に依存せず完了します。</summary>
    /// <param name="useTools">Tools側のプロセス境界を検査する場合はtrue。</param>
    /// <returns>両プロジェクトのrestoreと出力回収の完了を検査するタスク。</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
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
                Assert.False(result.B("timedOut"), result.ToJsonString());
                Assert.True(result.I("exitCode") == 0, result.ToJsonString());
            }
            else
            {
                await ProcessRunner.Run("dotnet", ["restore", solution, "-m:2"], folder.Path, 30);
            }

            Assert.True(File.Exists(Path.Combine(folder.Path, "First/obj/project.assets.json")));
            Assert.True(File.Exists(Path.Combine(folder.Path, "Second/obj/project.assets.json")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("MSBUILDDISABLENODEREUSE", previous);
        }
    }

    /// <summary>引数のメタ文字とUTF-8をそのまま保持し、失敗時も両出力を返します。</summary>
    [Fact]
    public void ArgumentsAndBothStreamsArePreserved()
    {
        var argument = "日本 😀 spaces $() ; \"quoted\"";
        var result = Workspace.Command(["dotnet", typeof(ProcessProbe.Marker).Assembly.Location, argument]);
        Assert.Equal(7, result.I("exitCode"));
        Assert.Equal(new[] { argument }, JsonSerializer.Deserialize<string[]>(result.S("stdout"))!);
        Assert.Equal("probe-error" + Environment.NewLine, result.S("stderr"));
    }

    /// <summary>両方のパイプが容量を超えてもデッドロックしません。</summary>
    [Fact]
    public void LargeStreamsDoNotDeadlock()
    {
        var result = Workspace.Command(["dotnet", typeof(ProcessProbe.Marker).Assembly.Location, "large"], 10);
        Assert.False(result.B("timedOut"));
        Assert.Equal(0, result.I("exitCode"));
        Assert.Equal(new string('a', 200000), result.S("stdout"));
        Assert.Equal(new string('b', 200000), result.S("stderr"));
    }

    /// <summary>探索の出力量制限は両パイプを回収し、成功出力として扱いません。</summary>
    /// <returns>上限超過プロセスの終了。</returns>
    [Fact]
    public async Task BoundedExplorationOutputIsRejected()
    {
        var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => ProcessRunner.Run("dotnet", [typeof(ProcessProbe.Marker).Assembly.Location, "large"], timeoutSeconds: 10, maxOutputCharacters: 1000));
        Assert.Contains("output limit", error.Message);
    }

    /// <summary>強制 timeout 後も回収された出力を取得でき、完走と混同しません。</summary>
    /// <returns>timeout と出力回収。</returns>
    [Fact]
    public async Task ExplorationTimeoutCollectsStreams()
    {
        var error = await Assert.ThrowsAnyAsync<TimeoutException>(() => ProcessRunner.Run("dotnet", [typeof(ProcessProbe.Marker).Assembly.Location, "delay"], timeoutSeconds: 1));
        Assert.NotNull(error.Data["StandardOutput"]);
        Assert.NotNull(error.Data["StandardError"]);
    }

    /// <summary>制限時間で子プロセスを終了し、成功とは区別します。</summary>
    [Fact]
    public void TimeoutTerminatesProcess()
    {
        var result = Workspace.Command(["dotnet", typeof(ProcessProbe.Marker).Assembly.Location, "delay"], 0.3);
        Assert.True(result.B("timedOut"));
        Assert.NotEqual(0, result.I("exitCode"));
    }
}
