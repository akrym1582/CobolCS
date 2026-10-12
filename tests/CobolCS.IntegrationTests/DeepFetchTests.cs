using System.IO.Compression;
using System.Text;
using CobolCS.Runtime;
using CobolCS.Tools;
using Xunit;

namespace CobolCS.IntegrationTests;

/// <summary>固定版取得の部分状態と、展開前のZIP拒否条件をネットワークなしで検証します。</summary>
public sealed class DeepFetchTests
{
    /// <summary>未取得archiveを成功扱いせず、取得済みのハッシュ検証済み入力だけを別領域へ保存します。</summary>
    [Fact]
    public void CachedOnlyKeepsMissingInputsExplicit()
    {
        using var directory = new TestDirectory();
        var script = Prepare(directory.Path, null);
        File.AppendAllText(Path.Combine(directory.Path, "tasks/deep-corpus/origins.tsv"), "1\thttps://invalid.example/missing\t0\t-\tmissing.zip\n");
        var result = Workspace.Command(["bash", script, "--cached-only"]);
        Assert.True(result.I("exitCode") == 0, result.ToJsonString());
        var root = Path.Combine(directory.Path, ".cache/public-cobol-corpus/deep-available");
        Assert.False(File.Exists(Path.Combine(root, "READY.json")));
        Assert.Equal(1, Workspace.Read(Path.Combine(root, "INCOMPLETE.json")).I("missingArchives"));
        Assert.Equal("source bytes\n", File.ReadAllText(Path.Combine(root, "cobolcs-deep-samples/sample.cbl")));
        Assert.False(Directory.Exists(Path.Combine(directory.Path, ".cache/public-cobol-corpus/deep-selected")));
    }

    /// <summary>選定外でも危険な名前・重複・リンクを含むZIPは出力を確定しません。</summary>
    /// <param name="entry">追加する不正entryの名前。</param>
    /// <param name="symlink">Unix symlink属性を付けるか。</param>
    /// <param name="rejection">リンク以外の拒否で確認する診断。</param>
    [Theory]
    [InlineData("../escape", false, "Unsafe archive path")]
    [InlineData("/absolute", false, "Unsafe archive path")]
    [InlineData("sample.cbl", false, "Duplicate archive entry")]
    [InlineData("linked", true, "")]
    public void UnsafeArchiveIsRejected(string entry, bool symlink, string rejection)
    {
        using var directory = new TestDirectory();
        var script = Prepare(directory.Path, entry, symlink);
        var result = Workspace.Command(["bash", script, "--cached-only"]);
        var root = Path.Combine(directory.Path, ".cache/public-cobol-corpus/deep-available");
        if (symlink)
        {
            Assert.True(result.I("exitCode") == 0, result.ToJsonString());
            Assert.Equal(1, Workspace.Read(Path.Combine(root, "INCOMPLETE.json")).I("rejectedArchives"));
        }
        else
        {
            Assert.NotEqual(0, result.I("exitCode"));
            Assert.Contains(rejection, result.S("stderr"));
            Assert.False(File.Exists(Path.Combine(root, "INCOMPLETE.json")));
        }

        Assert.False(File.Exists(Path.Combine(root, "READY.json")));
        Assert.False(Directory.Exists(Path.Combine(root, "cobolcs-deep-samples")));
    }

    private static string Prepare(string root, string? extra, bool symlink = false)
    {
        var task = Path.Combine(root, "tasks/deep-corpus");
        Directory.CreateDirectory(task);
        var script = Path.Combine(task, "fetch.sh");
        File.Copy(Path.Combine(Workspace.Root, "tasks/deep-corpus/fetch.sh"), script);
        const string source = "source bytes\n";
        var hash = Workspace.Hash(Encoding.UTF8.GetBytes(source));
        using (var file = File.Create(Path.Combine(task, "files.tsv.gz")))
        using (var gzip = new GZipStream(file, CompressionMode.Compress))
        using (var writer = new StreamWriter(gzip))
        {
            writer.Write($"0\tsample.cbl\tcobolcs-deep-samples/sample.cbl\t{hash}\n");
        }

        File.WriteAllText(Path.Combine(task, "origins.tsv"), "0\thttps://invalid.example/sample\t0\t-\tsample.zip\n");
        var downloads = Path.Combine(root, ".cache/public-cobol-corpus/.downloads");
        Directory.CreateDirectory(downloads);
        using (var zip = ZipFile.Open(Path.Combine(downloads, "sample.zip"), ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("sample.cbl").Open()))
            {
                writer.Write(source);
            }

            if (extra is not null)
            {
                var additional = zip.CreateEntry(extra);
                if (symlink)
                {
                    additional.ExternalAttributes = unchecked((int)0xA1FF0000);
                }
            }
        }

        var metadata = Path.Combine(root, "metadata/compact-selection");
        Directory.CreateDirectory(metadata);
        File.WriteAllText(Path.Combine(metadata, "summary.json"), "{}");
        var packed = Workspace.Command(["tar", "-czf", Path.Combine(task, "metadata.tar.gz"), "-C", Path.GetDirectoryName(metadata)!, "compact-selection"]);
        Assert.Equal(0, packed.I("exitCode"));
        return script;
    }
}
