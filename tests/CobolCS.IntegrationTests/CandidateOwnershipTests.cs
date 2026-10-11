using System.Text.Json.Nodes;
using CobolCS.Cli;
using CobolCS.Frontend;
using CobolCS.Runtime;
using CobolCS.Tools;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CobolCS.IntegrationTests;

/// <summary>静的候補の追加と利用者の検査・レビュー所有権を区別します。</summary>
[TestClass]
public sealed class CandidateOwnershipTests
{
    /// <summary>保持・編集・削除された検査を自動承認せず、未知候補も残します。</summary>
    /// <param name="editTest">利用者テストへ識別可能な編集を追加するか。</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RegenerationPreservesTestsAndReviewEvidence(bool editTest)
    {
        using var folder = new TestDirectory();
        var input = Path.Combine(folder.Path, "ownership.cbl");
        File.WriteAllText(input, "IDENTIFICATION DIVISION. PROGRAM-ID. OWNERSHIP. DATA DIVISION. WORKING-STORAGE SECTION. 01 NET PIC 9(5). 01 MYSTERY USAGE POINTER. PROCEDURE DIVISION. MOVE 9700 TO NET MOVE 1 TO MYSTERY GOBACK.");
        var ir = FrontendAnalyzer.Analyze(input);
        var output = Path.Combine(folder.Path, "generated");
        Generator.Generate(ir, output, "scaffold");
        AssertCandidateStatuses(output, "generated-template");
        var testPath = Directory.GetFiles(Path.Combine(output, "Tests/Cases"), "*.cs").Single();
        if (editTest)
        {
            File.AppendAllText(testPath, "\n// 利用者の検査メモを再生成で失わない。\n");
        }

        var testBytes = File.ReadAllBytes(testPath);
        var baselinePath = Directory.GetFiles(Path.Combine(output, "Tests/Baselines"), "*.json").Single();
        var reviewPath = Path.Combine(output, "Tests/review.json");
        var baseline = Workspace.Read(baselinePath);
        baseline["reviewReason"] = "UNREVIEWED: expectations require independent review; preserve this note.";
        Workspace.Write(baselinePath, baseline);
        var review = Workspace.Read(reviewPath);
        review["cases"]![0]!["reviewReason"] = "No automatic approval or hash refresh.";
        Workspace.Write(reviewPath, review);
        var evidence = new[] { baselinePath, reviewPath }.ToDictionary(path => path, File.ReadAllBytes);
        var evidenceHashes = evidence.ToDictionary(pair => pair.Key, pair => Workspace.Hash(pair.Value));
        var boundHashes = review["cases"]![0]!["files"]!.DeepClone();

        void VerifyEvidence()
        {
            foreach (var (path, bytes) in evidence)
            {
                CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
                Assert.AreEqual(evidenceHashes[path], Workspace.Digest(path));
            }

            var current = Workspace.Read(reviewPath)["cases"]![0]!;
            Assert.IsFalse(current.B("reviewed"));
            Assert.IsTrue(JsonNode.DeepEquals(boundHashes, current["files"]));
        }

        Generator.Generate(ir, output, "scaffold");
        CollectionAssert.AreEqual(testBytes, File.ReadAllBytes(testPath));
        AssertCandidateStatuses(output, "manual-existing-tests");
        VerifyEvidence();
        File.Delete(testPath);
        Generator.Generate(ir, output, "scaffold");
        Assert.IsFalse(File.Exists(testPath));
        AssertCandidateStatuses(output, "manual-existing-tests");
        VerifyEvidence();
        File.Delete(reviewPath);
        File.Delete(baselinePath);
        Generator.Generate(ir, output, "scaffold");
        Assert.IsFalse(File.Exists(testPath));
        Assert.IsFalse(File.Exists(reviewPath));
        Assert.IsFalse(File.Exists(baselinePath));
        AssertCandidateStatuses(output, "manual-existing-tests");
    }

    private static void AssertCandidateStatuses(string output, string knownStatus)
    {
        var candidates = Workspace.Read(Path.Combine(output, "reports/execution-readiness.json"))["assertionCandidates"]!.AsArray();
        Assert.AreEqual(knownStatus, candidates.Single(candidate => candidate!.S("name") == "NET")!.S("status"));
        Assert.AreEqual("manual-unknown", candidates.Single(candidate => candidate!.S("name") == "MYSTERY")!.S("status"));
        var report = File.ReadAllText(Path.Combine(output, "reports/execution-readiness.md"));
        StringAssert.Contains(report, "`NET`: " + knownStatus + ";");
        StringAssert.Contains(report, "`MYSTERY`: manual-unknown;");
        if (knownStatus == "manual-existing-tests")
        {
            Assert.IsFalse(report.Contains("`NET`: generated-template;", StringComparison.Ordinal));
            StringAssert.Contains(report, "No test coverage or reviewed expectation is inferred.");
        }
    }
}
