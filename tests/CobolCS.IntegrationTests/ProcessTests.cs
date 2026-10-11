using System.Text.Json;
using CobolCS.Runtime;
using CobolCS.Tools;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CobolCS.IntegrationTests;

/// <summary>シェルを使わない.NETプロセスの出力回収と終了を検証します。</summary>
[TestClass]
public sealed class ProcessTests
{
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

    /// <summary>制限時間で子プロセスを終了し、成功とは区別します。</summary>
    [TestMethod]
    public void TimeoutTerminatesProcess()
    {
        var result = Workspace.Command(["dotnet", typeof(ProcessProbe.Marker).Assembly.Location, "delay"], 0.3);
        Assert.IsTrue(result.B("timedOut"));
        Assert.AreNotEqual(0, result.I("exitCode"));
    }
}
