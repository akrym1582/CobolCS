namespace CobolCS.Frontend.Tests;

/// <summary>参照プロセスの出力回収と終了を検証します。</summary>
[TestClass]
[TestCategory("PythonReference")]
public sealed class ReferenceProcessTests
{
    /// <summary>引数をシェル解釈せず、非ゼロ終了時も UTF-8 の両ストリームを回収します。</summary>
    /// <returns>非同期検査。</returns>
    [TestMethod]
    public async Task ArgumentsAndBothStreamsArePreserved()
    {
        var argument = "日本 😀 spaces $() ; \"quoted\"";
        var result = await ReferenceProcess.Run(Python(), ["-c", "import sys; sys.stdout.write(sys.argv[1]); sys.stderr.write('error 日本'); sys.exit(7)", argument], Baseline.Root, TimeSpan.FromSeconds(10));
        Assert.AreEqual(7, result.ExitCode);
        Assert.AreEqual(argument, result.Output);
        Assert.AreEqual("error 日本", result.Error);
    }

    /// <summary>パイプの容量を超える出力を同時に回収してデッドロックを防ぎます。</summary>
    /// <returns>非同期検査。</returns>
    [TestMethod]
    public async Task LargeStreamsDoNotDeadlock()
    {
        var result = await ReferenceProcess.Run(Python(), ["-c", "import sys; sys.stdout.write('a'*200000); sys.stderr.write('b'*200000)"], Baseline.Root, TimeSpan.FromSeconds(10));
        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual(new string('a', 200000), result.Output);
        Assert.AreEqual(new string('b', 200000), result.Error);
    }

    /// <summary>停止しない参照プロセスを制限時間で回収します。</summary>
    /// <returns>非同期検査。</returns>
    [TestMethod]
    public async Task TimeoutTerminatesTheReference()
    {
        await Assert.ThrowsAsync<TimeoutException>(() => ReferenceProcess.Run(Python(), ["-c", "import time; time.sleep(120)"], Baseline.Root, TimeSpan.FromMilliseconds(300)));
    }

    private static string Python() => Environment.GetEnvironmentVariable("COBOLCS_REFERENCE_PYTHON") ?? (OperatingSystem.IsWindows() ? "python" : "python3");
}
