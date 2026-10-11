using System.IO.Compression;
using System.Text;
using CobolCS.Runtime;
using CobolCS.Tools;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CobolCS.IntegrationTests;

/// <summary>固定版取得の部分状態と、展開前のZIP拒否条件をネットワークなしで検証します。</summary>
[TestClass]
public sealed class DeepFetchTests
{
    /// <summary>未取得archiveを成功扱いせず、取得済みのハッシュ検証済み入力だけを別領域へ保存します。</summary>
    [TestMethod]
    public void CachedOnlyKeepsMissingInputsExplicit()
    {
        using var directory = new TestDirectory();
        var script = Prepare(directory.Path, null);
        File.AppendAllText(Path.Combine(directory.Path, "tasks/deep-corpus/origins.tsv"), "1\thttps://invalid.example/missing\t0\t-\tmissing.zip\n");
        var result = Workspace.Command(["bash", script, "--cached-only"]);
        Assert.AreEqual(0, result.I("exitCode"), result.ToJsonString());
        var root = Path.Combine(directory.Path, ".cache/public-cobol-corpus/deep-available");
        Assert.IsFalse(File.Exists(Path.Combine(root, "READY.json")));
        Assert.AreEqual(1, Workspace.Read(Path.Combine(root, "INCOMPLETE.json")).I("missingArchives"));
        Assert.AreEqual("source bytes\n", File.ReadAllText(Path.Combine(root, "cobolcs-deep-samples/sample.cbl")));
        Assert.IsFalse(Directory.Exists(Path.Combine(directory.Path, ".cache/public-cobol-corpus/deep-selected")));
    }

    /// <summary>選定外でも危険な名前・重複・リンクを含むZIPは出力を確定しません。</summary>
    /// <param name="entry">追加する不正entryの名前。</param>
    /// <param name="symlink">Unix symlink属性を付けるか。</param>
    /// <param name="rejection">リンク以外の拒否で確認する診断。</param>
    [TestMethod]
    [DataRow("../escape", false, "Unsafe archive path")]
    [DataRow("/absolute", false, "Unsafe archive path")]
    [DataRow("sample.cbl", false, "Duplicate archive entry")]
    [DataRow("linked", true, "")]
    public void UnsafeArchiveIsRejected(string entry, bool symlink, string rejection)
    {
        using var directory = new TestDirectory();
        var script = Prepare(directory.Path, entry, symlink);
        var result = Workspace.Command(["bash", script, "--cached-only"]);
        var root = Path.Combine(directory.Path, ".cache/public-cobol-corpus/deep-available");
        if (symlink)
        {
            Assert.AreEqual(0, result.I("exitCode"), result.ToJsonString());
            Assert.AreEqual(1, Workspace.Read(Path.Combine(root, "INCOMPLETE.json")).I("rejectedArchives"));
        }
        else
        {
            Assert.AreNotEqual(0, result.I("exitCode"));
            StringAssert.Contains(result.S("stderr"), rejection, result.ToJsonString());
            Assert.IsFalse(File.Exists(Path.Combine(root, "INCOMPLETE.json")));
        }

        Assert.IsFalse(File.Exists(Path.Combine(root, "READY.json")));
        Assert.IsFalse(Directory.Exists(Path.Combine(root, "cobolcs-deep-samples")));
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
        Assert.AreEqual(0, packed.I("exitCode"));
        return script;
    }
}
