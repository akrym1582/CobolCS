using CobolCS.Runtime;
using CobolCS.Tools;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CobolCS.IntegrationTests;

/// <summary>補助ツールが不完全な引数を成功扱いしないことを検証します。</summary>
[TestClass]
public sealed class ToolArgumentTests
{
    /// <summary>未知オプション、値の欠落、余分な位置引数は処理開始前に拒否します。</summary>
    /// <param name="arguments">拒否されるコマンド行。</param>
    [TestMethod]
    [DataRow(new[] { "fetch", "--include-gp1" })]
    [DataRow(new[] { "prepare-workflow", "--out" })]
    [DataRow(new[] { "verify", "one", "extra" })]
    public void InvalidArgumentsFail(string[] arguments)
    {
        var result = Workspace.Command(new[] { "dotnet", typeof(Corpus).Assembly.Location }.Concat(arguments));
        Assert.AreEqual(2, result.I("exitCode"));
        Assert.AreNotEqual(string.Empty, result.S("stderr"));
        Assert.AreEqual(string.Empty, result.S("stdout"));
    }
}
