using CobolCS.Runtime;
using CobolCS.Tools;
using Xunit;

namespace CobolCS.IntegrationTests;

/// <summary>補助ツールが不完全な引数を成功扱いしないことを検証します。</summary>
public sealed class ToolArgumentTests
{
    /// <summary>未知オプション、値の欠落、余分な位置引数は処理開始前に拒否します。</summary>
    /// <param name="arguments">拒否されるコマンド行。</param>
    [Theory]
    [InlineData("fetch", "--include-gp1")]
    [InlineData("prepare-workflow", "--out")]
    [InlineData("verify", "one", "extra")]
    [InlineData("evaluate", "--lock", "missing", "--root", ".", "--out", "unused", "--shard-count", "0")]
    [InlineData("evaluate", "--lock", "missing", "--root", ".", "--out", "unused", "--shard-index", "2", "--shard-count", "2")]
    [InlineData("evaluate", "--lock", "missing", "--root", ".", "--out", "unused", "--shard-count", "invalid")]
    public void InvalidArgumentsFail(params string[] arguments)
    {
        var result = Workspace.Command(new[] { "dotnet", typeof(Corpus).Assembly.Location }.Concat(arguments));
        Assert.Equal(2, result.I("exitCode"));
        Assert.NotEqual(string.Empty, result.S("stderr"));
        Assert.Equal(string.Empty, result.S("stdout"));
    }
}
