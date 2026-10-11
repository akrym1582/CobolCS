namespace CobolCS.Frontend.Tests;

/// <summary>移行中だけ使用する固定 Python 実装の差分・決定性検査です。</summary>
[TestClass]
[TestCategory("PythonReference")]
public sealed class PythonReferenceTests
{
    /// <summary>Gets 比較入力の ID。</summary>
    public static IEnumerable<object[]> Inputs => Baseline.CaseIds();

    /// <summary>実行前に参照実装と入力のハッシュを検証します。</summary>
    /// <param name="context">テスト実行コンテキスト。</param>
    [ClassInitialize]
    public static void Initialize(TestContext context)
    {
        _ = context;
        Baseline.VerifyReference();
        Baseline.VerifyInputs();
    }

    /// <summary>固定期待値と二回の解析を比較し、終了コード・両出力も検査します。</summary>
    /// <param name="id">固定入力 ID。</param>
    /// <returns>非同期検査。</returns>
    [TestMethod]
    [DynamicData(nameof(Inputs))]
    public async Task FrozenReferenceIsReproducible(string id)
    {
        var item = Baseline.Cases.Single(c => c["id"]!.GetValue<string>() == id);
        var arguments = new List<string> { "python/frontend/main.py", item["input"]!.GetValue<string>() };
        if (item["config"] is not null)
        {
            arguments.AddRange(["--config", item["config"]!.GetValue<string>()]);
        }

        foreach (var path in item["copybookPaths"]!.AsArray())
        {
            arguments.AddRange(["--copybook-path", path!.GetValue<string>()]);
        }

        var python = Environment.GetEnvironmentVariable("COBOLCS_REFERENCE_PYTHON") ?? (OperatingSystem.IsWindows() ? "python" : "python3");
        var first = await ReferenceProcess.Run(python, arguments, Baseline.Root, TimeSpan.FromSeconds(30));
        var second = await ReferenceProcess.Run(python, arguments, Baseline.Root, TimeSpan.FromSeconds(30));
        Assert.AreEqual(first, second, "Non-deterministic reference: " + id);
        Assert.AreEqual(item["exitCode"]!.GetValue<int>(), first.ExitCode, first.Error);

        // Process line endings alone differ on Windows; source and IR fields remain untouched.
        Assert.AreEqual(item["stderr"]!.GetValue<string>(), first.Error.Replace("\r\n", "\n", StringComparison.Ordinal));
        var expected = Baseline.Expected(item);
        if (expected.Length == 0)
        {
            Assert.AreEqual(string.Empty, first.Output);
        }
        else
        {
            Assert.IsEmpty(JsonComparison.Compare(id, expected, first.Output));
        }
    }
}
