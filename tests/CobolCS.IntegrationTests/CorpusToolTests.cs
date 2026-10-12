using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using CobolCS.Runtime;
using CobolCS.Tools;
using Xunit;

namespace CobolCS.IntegrationTests;

/// <summary>外部取得なしの合成入力で、取得の検証と測定の判定を検査します。</summary>
public sealed class CorpusToolTests : IDisposable
{
    private TestDirectory directory = null!;
    private string source = string.Empty;
    private string locked = string.Empty;
    private JsonObject item = null!;

    /// <summary>Initializes a new instance of the <see cref="CorpusToolTests"/> class. 独立した合成COBOLとlockを用意します。</summary>
    public CorpusToolTests()
    {
        directory = new TestDirectory();
        source = Path.Combine(directory.Path, "native.SQB");
        locked = Path.Combine(directory.Path, "lock.json");
        File.WriteAllText(source, "IDENTIFICATION DIVISION.\nPROGRAM-ID. NATIVE.\nDATA DIVISION.\nWORKING-STORAGE SECTION.\n01 N PIC 9 VALUE 0.\nPROCEDURE DIVISION.\nMAIN.\nPERFORM STEP-P\nEVALUATE N WHEN 1 DISPLAY \"one\" WHEN OTHER DISPLAY \"wrong\" END-EVALUATE\nGOBACK.\nSTEP-P.\nADD 1 TO N.\n");
        item = new JsonObject { ["id"] = "native-1", ["source"] = "native.SQB", ["sha256"] = Workspace.Digest(source), ["sourceFormat"] = "free", ["copybookPaths"] = new JsonArray(), ["license"] = "MIT", ["optionalGpl"] = false };
        Save(item);
    }

    /// <summary>生成物と一時入力を削除します。</summary>
    public void Dispose() => directory.Dispose();

    /// <summary>改ざん入力は出力ディレクトリ作成前に拒否します。</summary>
    [Fact]
    public void TestTamperFailsBeforeOutputIsCreated()
    {
        File.WriteAllText(source, "changed");
        var error = Assert.Throws<ArgumentException>(() => Evaluate());
        Assert.Contains("hash mismatch", error.Message);
        Assert.False(Directory.Exists(Path.Combine(directory.Path, "out")));
    }

    /// <summary>同じケースIDを暗黙に統合しません。</summary>
    [Fact]
    public void TestDuplicateIdsAreNotMerged()
    {
        Save(item, item);
        var error = Assert.Throws<ArgumentException>(() => Corpus.LoadCases(locked, directory.Path));
        Assert.Contains("duplicate case ID", error.Message);
    }

    /// <summary>GPL入力は明示的に選択した場合だけ評価します。</summary>
    [Fact]
    public void TestGplRequiresExplicitSelection()
    {
        var gpl = item.DeepClone();
        gpl["id"] = "optional";
        gpl["license"] = "GPL-3.0-only";
        gpl["optionalGpl"] = true;
        Save(item, gpl);
        Assert.Single(Corpus.LoadCases(locked, directory.Path).Cases);
        Assert.Equal(2, Corpus.LoadCases(locked, directory.Path, true).Cases.Count());
    }

    /// <summary>正規でない相対パスとCOPYの不一致を拒否します。</summary>
    [Fact]
    public void TestPathsAndCopyHashes()
    {
        foreach (var path in new[] { "../escape", "/absolute", "a/../b", "a\\b", "./a" })
        {
            Assert.Throws<ArgumentException>(() => Corpus.Confined(directory.Path, path));
        }

        item["copybookHashes"] = new JsonObject { ["native.SQB"] = new string('0', 64) };
        Save(item);
        var error = Assert.Throws<ArgumentException>(() => Corpus.LoadCases(locked, directory.Path));
        Assert.Contains("COPY hash mismatch", error.Message);
    }

    /// <summary>リンクを経由するルート外への脱出を拒否します。</summary>
    [Fact]
    public void TestSymlinkEscape()
    {
        Directory.CreateSymbolicLink(Path.Combine(directory.Path, "link"), Path.GetDirectoryName(directory.Path)!);
        var error = Assert.Throws<ArgumentException>(() => Corpus.Confined(directory.Path, "link/outside"));
        Assert.Contains("escapes", error.Message);
    }

    /// <summary>終了期限を超えたプロセスを成功として扱いません。</summary>
    [Fact]
    public void TestTimeoutIsNotSuccess()
    {
        var result = Workspace.Command(["dotnet", typeof(ProcessProbe.Marker).Assembly.Location, "delay"], 0.05);
        Assert.True(result.B("timedOut"));
        Assert.NotEqual(0, result.I("exitCode"));
    }

    /// <summary>アダプタ未設定の失敗、実行、再生成保持の証拠が全部必要です。</summary>
    [Fact]
    public void TestVerifyRequiresAdapterFailureAndPreservationEvidence()
    {
        var row = new JsonObject { ["id"] = "adapter", ["strictGenerated"] = false, ["scaffoldGenerated"] = true, ["built"] = true, ["matchedIndependentExpectation"] = true, ["executedWithoutReports"] = true, ["unsetAdapterFailed"] = true, ["stubFixtureExecuted"] = true, ["editedStubsPreserved"] = true };
        var summary = new JsonObject { ["backend"] = "source", ["expectedCases"] = 1, ["minimumGenerated"] = 1, ["expectedExecutionIds"] = new JsonArray("adapter"), ["counts"] = new JsonObject(), ["cases"] = new JsonArray(row) };
        Assert.True(Corpus.Verify(summary));
        foreach (var field in new[] { "unsetAdapterFailed", "stubFixtureExecuted", "editedStubsPreserved" })
        {
            row[field] = false;
            Assert.False(Corpus.Verify(summary), field);
            row[field] = true;
        }
    }

    /// <summary>ZIPの固定識別子を検査し、改行を含むバイトを保持します。</summary>
    [Fact]
    public void TestArchivePreservesBytesAndChecksIdentity()
    {
        var (archive, content) = MakeZip();
        Assert.Throws<ArgumentException>(() => Corpus.ImportArchive(archive, Path.Combine(directory.Path, "bad")));
        var result = Corpus.ImportArchive(archive, Path.Combine(directory.Path, "imported"), Workspace.Digest(archive));
        Assert.Equal(1, result.I("verifiedEntries"));
        Assert.Equal(content, File.ReadAllBytes(Path.Combine(directory.Path, "imported/source.cbl")));
    }

    /// <summary>ZIP脱出とチェックサム不一致は保存先を公開しません。</summary>
    [Fact]
    public void TestArchiveTraversalAndBadChecksumAreAtomic()
    {
        foreach (var (name, checksum) in new[] { ("../escape", (string?)null), ("source.cbl", new string('0', 64)) })
        {
            var (archive, _) = MakeZip(name, checksum);
            var destination = Path.Combine(directory.Path, "bad");
            Assert.Throws<ArgumentException>(() => Corpus.ImportArchive(archive, destination, Workspace.Digest(archive)));
            Assert.False(Directory.Exists(destination));
        }
    }

    /// <summary>PERFORMとEVALUATEの独立期待値を、生成数とは別に計測します。</summary>
    [Fact]
    public void TestNativePerformEvaluateAndMeasurementSeparation()
    {
        Workspace.Write(Path.Combine(directory.Path, "expected.json"), JsonNode.Parse("{\"display\":[\"one\"],\"fields\":{\"N\":\"1\"},\"signal\":\"goback\"}")!);
        item["execution"] = new JsonObject { ["program"] = "NATIVE", ["fixture"] = new JsonObject(), ["expectedFile"] = "expected.json", ["expectationKind"] = "independent-calculation" };
        Save(item);
        var counts = Evaluate()["counts"]!;
        foreach (var key in new[] { "strictGenerated", "built", "fixtureExecuted", "matchedIndependentExpectation" })
        {
            Assert.True(counts.I(key) == 1, key);
        }

        Assert.Equal(0, counts.I("stubFixtureExecuted"));
        Assert.Equal(0, counts.I("matchedOriginalCobol"));
    }

    /// <summary>外部CALLのスタブ生成を実行成功とは数えません。</summary>
    [Fact]
    public void TestExternalScaffoldIsNotCountedAsExecution()
    {
        File.WriteAllText(source, "IDENTIFICATION DIVISION. PROGRAM-ID. EXTERNAL.\nPROCEDURE DIVISION.\nCALL \"VENDOR\".\nGOBACK.\n");
        item["sha256"] = Workspace.Digest(source);
        Save(item);
        var row = Evaluate()["cases"]![0]!;
        Assert.True(row.B("strictGenerated"));
        Assert.False(row.B("scaffoldGenerated"));
        Assert.True(row.B("built"));
        Assert.True(row.B("uneditedExecutionFailed"));
        Assert.False(row.B("fixtureExecuted"));
        Assert.False(row.B("matchedOriginalCobol"));
        var dependencies = File.ReadAllText(Path.Combine(directory.Path, "out/native-1/strict/reports/dependencies.json"));
        Assert.Contains("VENDOR", dependencies);
        Assert.Contains("copyStack", dependencies);
    }

    /// <summary>欠落LINKAGEのscaffold回復と解析エラーを分けて記録します。</summary>
    [Fact]
    public void TestMissingLinkageRecoveryIsSeparateFromAnalysis()
    {
        File.WriteAllText(source, "IDENTIFICATION DIVISION. PROGRAM-ID. UNKNOWNARG.\nDATA DIVISION. LINKAGE SECTION. COPY \"arg.cpy\".\nPROCEDURE DIVISION USING ARG. GOBACK.\n");
        item["sha256"] = Workspace.Digest(source);
        Save(item);
        var row = Evaluate()["cases"]![0]!;
        Assert.False(row.B("analyzed"));
        Assert.True(row.B("analysisHasErrors"));
        Assert.True(row.B("scaffoldAnalyzed"));
        Assert.True(row.B("scaffoldAnalysisHasErrors"));
        Assert.True(row.B("scaffoldGenerated"));
        Assert.True(row.B("built"));
        Assert.False(row.B("strictGenerated"));
        Assert.False(row.B("fixtureExecuted"));
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
