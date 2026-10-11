namespace CobolCS.IntegrationTests;

/// <summary>回帰ケースごとに独立した一時ディレクトリを管理します。</summary>
internal sealed class TestDirectory : IDisposable
{
    /// <summary>Initializes a new instance of the <see cref="TestDirectory"/> class.</summary>
    internal TestDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cobolcs-native-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>Gets 回帰検証に使用する 一時保存先。</summary>
    internal string Path { get; }

    /// <summary>回帰条件を.NETのファイル、プロセス、値の操作で検証します。</summary>
    public void Dispose() => Directory.Delete(Path, true);
}
