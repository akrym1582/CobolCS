using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using CobolCS.Runtime;
using CobolCS.Tools;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CobolCS.IntegrationTests;

/// <summary>外部取得なしの合成入力で、取得の検証と測定の判定を検査します。</summary>
[TestClass]
public sealed class CorpusToolTests
{
    private TestDirectory directory = null!;
    private string source = string.Empty;
    private string locked = string.Empty;
    private JsonObject item = null!;

    /// <summary>独立した合成COBOLとハッシュ付きlockを用意します。</summary>
    [TestInitialize]
    public void Initialize()
    {
        directory = new TestDirectory();
        source = Path.Combine(directory.Path, "native.SQB");
        locked = Path.Combine(directory.Path, "lock.json");
        File.WriteAllText(source, "IDENTIFICATION DIVISION.\nPROGRAM-ID. NATIVE.\nDATA DIVISION.\nWORKING-STORAGE SECTION.\n01 N PIC 9 VALUE 0.\nPROCEDURE DIVISION.\nMAIN.\nPERFORM STEP-P\nEVALUATE N WHEN 1 DISPLAY \"one\" WHEN OTHER DISPLAY \"wrong\" END-EVALUATE\nGOBACK.\nSTEP-P.\nADD 1 TO N.\n");
        item = new JsonObject { ["id"] = "native-1", ["source"] = "native.SQB", ["sha256"] = Workspace.Digest(source), ["sourceFormat"] = "free", ["copybookPaths"] = new JsonArray(), ["license"] = "MIT", ["optionalGpl"] = false };
        Save(item);
    }

    /// <summary>生成物と一時入力を削除します。</summary>
    [TestCleanup]
    public void Cleanup() => directory.Dispose();

    /// <summary>改ざん入力は出力ディレクトリ作成前に拒否します。</summary>
    [TestMethod]
    public void TestTamperFailsBeforeOutputIsCreated()
    {
        File.WriteAllText(source, "changed");
        var error = Assert.ThrowsExactly<ArgumentException>(() => Evaluate());
        StringAssert.Contains(error.Message, "hash mismatch");
        Assert.IsFalse(Directory.Exists(Path.Combine(directory.Path, "out")));
    }

    /// <summary>同じケースIDを暗黙に統合しません。</summary>
    [TestMethod]
    public void TestDuplicateIdsAreNotMerged()
    {
        Save(item, item);
        var error = Assert.ThrowsExactly<ArgumentException>(() => Corpus.LoadCases(locked, directory.Path));
        StringAssert.Contains(error.Message, "duplicate case ID");
    }

    /// <summary>GPL入力は明示的に選択した場合だけ評価します。</summary>
    [TestMethod]
    public void TestGplRequiresExplicitSelection()
    {
        var gpl = item.DeepClone();
        gpl["id"] = "optional";
        gpl["license"] = "GPL-3.0-only";
        gpl["optionalGpl"] = true;
        Save(item, gpl);
        Assert.HasCount(1, Corpus.LoadCases(locked, directory.Path).Cases);
        Assert.HasCount(2, Corpus.LoadCases(locked, directory.Path, true).Cases);
    }

    /// <summary>正規でない相対パスとCOPYの不一致を拒否します。</summary>
    [TestMethod]
    public void TestPathsAndCopyHashes()
    {
        foreach (var path in new[] { "../escape", "/absolute", "a/../b", "a\\b", "./a" })
        {
            Assert.ThrowsExactly<ArgumentException>(() => Corpus.Confined(directory.Path, path));
        }

        item["copybookHashes"] = new JsonObject { ["native.SQB"] = new string('0', 64) };
        Save(item);
        var error = Assert.ThrowsExactly<ArgumentException>(() => Corpus.LoadCases(locked, directory.Path));
        StringAssert.Contains(error.Message, "COPY hash mismatch");
    }

    /// <summary>リンクを経由するルート外への脱出を拒否します。</summary>
    [TestMethod]
    public void TestSymlinkEscape()
    {
        Directory.CreateSymbolicLink(Path.Combine(directory.Path, "link"), Path.GetDirectoryName(directory.Path)!);
        var error = Assert.ThrowsExactly<ArgumentException>(() => Corpus.Confined(directory.Path, "link/outside"));
        StringAssert.Contains(error.Message, "escapes");
    }

    /// <summary>終了期限を超えたプロセスを成功として扱いません。</summary>
    [TestMethod]
    public void TestTimeoutIsNotSuccess()
    {
        var result = Workspace.Command(["dotnet", typeof(ProcessProbe.Marker).Assembly.Location, "delay"], 0.05);
        Assert.IsTrue(result.B("timedOut"));
        Assert.AreNotEqual(0, result.I("exitCode"));
    }

    /// <summary>アダプタ未設定の失敗、実行、再生成保持の証拠が全部必要です。</summary>
    [TestMethod]
    public void TestVerifyRequiresAdapterFailureAndPreservationEvidence()
    {
        var row = new JsonObject { ["id"] = "adapter", ["strictGenerated"] = false, ["scaffoldGenerated"] = true, ["built"] = true, ["matchedIndependentExpectation"] = true, ["executedWithoutReports"] = true, ["unsetAdapterFailed"] = true, ["stubFixtureExecuted"] = true, ["editedStubsPreserved"] = true };
        var summary = new JsonObject { ["backend"] = "source", ["expectedCases"] = 1, ["minimumGenerated"] = 1, ["expectedExecutionIds"] = new JsonArray("adapter"), ["counts"] = new JsonObject(), ["cases"] = new JsonArray(row) };
        Assert.IsTrue(Corpus.Verify(summary));
        foreach (var field in new[] { "unsetAdapterFailed", "stubFixtureExecuted", "editedStubsPreserved" })
        {
            row[field] = false;
            Assert.IsFalse(Corpus.Verify(summary), field);
            row[field] = true;
        }
    }

    /// <summary>ZIPの固定識別子を検査し、改行を含むバイトを保持します。</summary>
    [TestMethod]
    public void TestArchivePreservesBytesAndChecksIdentity()
    {
        var (archive, content) = MakeZip();
        Assert.ThrowsExactly<ArgumentException>(() => Corpus.ImportArchive(archive, Path.Combine(directory.Path, "bad")));
        var result = Corpus.ImportArchive(archive, Path.Combine(directory.Path, "imported"), Workspace.Digest(archive));
        Assert.AreEqual(1, result.I("verifiedEntries"));
        CollectionAssert.AreEqual(content, File.ReadAllBytes(Path.Combine(directory.Path, "imported/source.cbl")));
    }

    /// <summary>ZIP脱出とチェックサム不一致は保存先を公開しません。</summary>
    [TestMethod]
    public void TestArchiveTraversalAndBadChecksumAreAtomic()
    {
        foreach (var (name, checksum) in new[] { ("../escape", (string?)null), ("source.cbl", new string('0', 64)) })
        {
            var (archive, _) = MakeZip(name, checksum);
            var destination = Path.Combine(directory.Path, "bad");
            Assert.ThrowsExactly<ArgumentException>(() => Corpus.ImportArchive(archive, destination, Workspace.Digest(archive)));
            Assert.IsFalse(Directory.Exists(destination));
        }
    }

    /// <summary>PERFORMとEVALUATEの独立期待値を、生成数とは別に計測します。</summary>
    [TestMethod]
    public void TestNativePerformEvaluateAndMeasurementSeparation()
    {
        Workspace.Write(Path.Combine(directory.Path, "expected.json"), JsonNode.Parse("{\"display\":[\"one\"],\"fields\":{\"N\":\"1\"},\"signal\":\"goback\"}")!);
        item["execution"] = new JsonObject { ["program"] = "NATIVE", ["fixture"] = new JsonObject(), ["expectedFile"] = "expected.json", ["expectationKind"] = "independent-calculation" };
        Save(item);
        var counts = Evaluate()["counts"]!;
        foreach (var key in new[] { "strictGenerated", "built", "fixtureExecuted", "matchedIndependentExpectation" })
        {
            Assert.AreEqual(1, counts.I(key), key);
        }

        Assert.AreEqual(0, counts.I("stubFixtureExecuted"));
        Assert.AreEqual(0, counts.I("matchedOriginalCobol"));
    }

    /// <summary>外部CALLのスタブ生成を実行成功とは数えません。</summary>
    [TestMethod]
    public void TestExternalScaffoldIsNotCountedAsExecution()
    {
        File.WriteAllText(source, "IDENTIFICATION DIVISION. PROGRAM-ID. EXTERNAL.\nPROCEDURE DIVISION.\nCALL \"VENDOR\".\nGOBACK.\n");
        item["sha256"] = Workspace.Digest(source);
        Save(item);
        var row = Evaluate()["cases"]![0]!;
        Assert.IsTrue(row.B("strictGenerated"));
        Assert.IsFalse(row.B("scaffoldGenerated"));
        Assert.IsTrue(row.B("built"));
        Assert.IsTrue(row.B("uneditedExecutionFailed"));
        Assert.IsFalse(row.B("fixtureExecuted"));
        Assert.IsFalse(row.B("matchedOriginalCobol"));
        var dependencies = File.ReadAllText(Path.Combine(directory.Path, "out/native-1/strict/reports/dependencies.json"));
        StringAssert.Contains(dependencies, "VENDOR");
        StringAssert.Contains(dependencies, "copyStack");
    }

    /// <summary>欠落LINKAGEのscaffold回復と解析エラーを分けて記録します。</summary>
    [TestMethod]
    public void TestMissingLinkageRecoveryIsSeparateFromAnalysis()
    {
        File.WriteAllText(source, "IDENTIFICATION DIVISION. PROGRAM-ID. UNKNOWNARG.\nDATA DIVISION. LINKAGE SECTION. COPY \"arg.cpy\".\nPROCEDURE DIVISION USING ARG. GOBACK.\n");
        item["sha256"] = Workspace.Digest(source);
        Save(item);
        var row = Evaluate()["cases"]![0]!;
        Assert.IsFalse(row.B("analyzed"));
        Assert.IsTrue(row.B("analysisHasErrors"));
        Assert.IsTrue(row.B("scaffoldAnalyzed"));
        Assert.IsTrue(row.B("scaffoldAnalysisHasErrors"));
        Assert.IsTrue(row.B("scaffoldGenerated"));
        Assert.IsTrue(row.B("built"));
        Assert.IsFalse(row.B("strictGenerated"));
        Assert.IsFalse(row.B("fixtureExecuted"));
    }

    private void Save(params JsonNode[] cases) => Workspace.Write(locked, new JsonObject { ["schemaVersion"] = 1, ["scope"] = "synthetic", ["cases"] = new JsonArray(cases.Select(c => c.DeepClone()).ToArray()) });

    private JsonObject Evaluate() => Corpus.Evaluate(locked, directory.Path, Path.Combine(directory.Path, "out"), build: true);

    private (string Archive, byte[] Content) MakeZip(string name = "source.cbl", string? checksum = null)
    {
        var path = Path.Combine(directory.Path, "input.zip");
        var content = Encoding.UTF8.GetBytes("synthetic source\r\n");
        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        using (var output = zip.CreateEntry(name).Open())
        {
            output.Write(content);
        }

        using (var writer = new StreamWriter(zip.CreateEntry("SHA256SUMS.txt").Open()))
        {
            writer.Write((checksum ?? Workspace.Hash(content)) + "  " + name + "\n");
        }

        return (path, content);
    }
}
