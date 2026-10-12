using System.Text.Json.Nodes;
using CobolCS.Cli;
using CobolCS.Runtime;
using CobolCS.Tools;
using Xunit;

namespace CobolCS.IntegrationTests;

/// <summary>探索workerの状態分離・上限拒否・異常終了からの復旧を検査します。</summary>
public sealed class ProbeSessionTests
{
    /// <summary>起動を共有し、32試行ごとの回収と子プロセスのノード再利用無効化を確認します。</summary>
    /// <returns>40試行のJSON往復。</returns>
    [Fact]
    public async Task RequestsReuseWorkerAndRecycleAfterThirtyTwo()
    {
        var session = new ProcessRunner.ProbeSession(typeof(ProcessProbe.Marker).Assembly.Location, Workspace.Root);
        try
        {
            var pids = new List<int>();
            for (var i = 0; i < 40; i++)
            {
                var result = await session.Trial("normal", new JsonObject(), 5000);
                Assert.Equal("1", result.S("reuse"));
                pids.Add(result.I("pid"));
            }

            Assert.Equal(2, session.Starts);
            Assert.Single(pids.Take(32).Distinct());
            Assert.Single(pids.Skip(32).Distinct());
            Assert.NotEqual(pids[0], pids[32]);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    /// <summary>timeout、EOF、両出力上限、UTF-8バイト上限を成功にせず、次試行を新規workerで実行します。</summary>
    /// <param name="mode">異常の種類。</param>
    /// <returns>異常回収と次の正常応答。</returns>
    [Theory]
    [InlineData("delay")]
    [InlineData("exit")]
    [InlineData("large")]
    [InlineData("stderr")]
    [InlineData("unicode")]
    [InlineData("malformed")]
    public async Task FailureKillsWorkerAndNextRequestRecovers(string mode)
    {
        var session = new ProcessRunner.ProbeSession(typeof(ProcessProbe.Marker).Assembly.Location, Workspace.Root);
        try
        {
            var previous = await session.Trial("normal", new JsonObject(), 5000);
            if (mode == "delay")
            {
                await Assert.ThrowsAnyAsync<TimeoutException>(() => session.Trial(mode, new JsonObject(), 300));
            }
            else
            {
                await Assert.ThrowsAnyAsync<InvalidOperationException>(() => session.Trial(mode, new JsonObject(), 5000));
            }

            var next = await session.Trial("normal", new JsonObject(), 5000);
            Assert.NotEqual(previous.I("pid"), next.I("pid"));
            Assert.Equal(2, session.Starts);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    /// <summary>timeoutで孫プロセスと継承パイプも回収して、次のworkerを起動します。</summary>
    /// <returns>PIDの記録、強制終了と正常復旧。</returns>
    [Fact]
    public async Task TimeoutKillsDescendantAndReleasesInheritedPipes()
    {
        using var folder = new TestDirectory();
        var pidPath = Path.Combine(folder.Path, "child.pid");
        var session = new ProcessRunner.ProbeSession(typeof(ProcessProbe.Marker).Assembly.Location, folder.Path);
        try
        {
            await session.Trial("normal", new JsonObject(), 5000);
            await Assert.ThrowsAnyAsync<TimeoutException>(() => session.Trial("descendant", new JsonObject { ["pidPath"] = pidPath }, 1000));
            Assert.True(File.Exists(pidPath));
            var pid = int.Parse(File.ReadAllText(pidPath), System.Globalization.CultureInfo.InvariantCulture);
            try
            {
                using var child = System.Diagnostics.Process.GetProcessById(pid);
                if (!child.HasExited)
                {
                    var status = "/proc/" + pid.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/stat";
                    if (File.Exists(status))
                    {
                        // コンテナーのinitが回収待ちのPIDも、実行とパイプ保持は終了しています。
                        Assert.StartsWith("Z ", File.ReadAllText(status).Split(')')[1].TrimStart());
                    }
                }
            }
            catch (ArgumentException)
            {
                // OSが既にPIDを回収した場合も、子は残っていません。
            }

            Assert.True((await session.Trial("normal", new JsonObject(), 5000)).I("pid") > 0);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    /// <summary>実際のworkerひな形を使い、static状態も毎試行で初期値へ戻ることを確認します。</summary>
    /// <param name="mode">共有可能なコード、変更可能なstatic値、readonlyな変更可能オブジェクト。</param>
    /// <returns>生成ひな形のビルドと連続試行。</returns>
    [Theory]
    [InlineData("mutable")]
    [InlineData("readonly")]
    [InlineData("stateless")]
    public async Task GeneratedWorkerReloadsStaticStateForEveryTrial(string mode)
    {
        using var folder = new TestDirectory();
        File.WriteAllText(Path.Combine(folder.Path, "worker.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>");
        var template = File.ReadAllText(Path.Combine(Workspace.Root, "src/CobolCS.Cli/Templates/BranchProbeWorker.cs.txt"));
        var fields = mode == "mutable" ? "private static int count;" : mode == "readonly" ? "private static readonly List<int> values = [];" : string.Empty;
        var mutate = mode == "readonly" ? "values.Add(1);" : string.Empty;
        var count = mode == "mutable" ? "++count" : mode == "readonly" ? "values.Count" : "1";
        File.WriteAllText(Path.Combine(folder.Path, "Program.cs"), template + "\npublic static class ProbeEntry { " + fields + " public static string Run(string id, string candidate) { " + mutate + " return new JsonObject { [\"count\"] = " + count + ", [\"context\"] = AssemblyLoadContext.GetLoadContext(typeof(ProbeEntry).Assembly)!.Name }.ToJsonString(); } }\nnamespace CobolCS.Generated { public sealed class Marker { } }\n");
        await ProcessRunner.Run("dotnet", ["build", "worker.csproj", "-m:1"], folder.Path);
        var session = new ProcessRunner.ProbeSession(Path.Combine(folder.Path, "bin/Debug/net10.0/worker.dll"), folder.Path);
        try
        {
            for (var i = 0; i < 24; i++)
            {
                var result = await session.Trial("static", new JsonObject(), 5000);
                Assert.Equal(1, result.I("count"));
                Assert.Equal(mode == "stateless" ? "Default" : "branch-trial", result.S("context"));
                Assert.Equal(mode == "stateless" ? "verified-static-state" : "fresh-assembly", result.S("workerIsolation"));
            }

            Assert.Equal(1, session.Starts);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }
}
