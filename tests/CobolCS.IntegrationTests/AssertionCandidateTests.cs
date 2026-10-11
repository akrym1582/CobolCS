using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CobolCS.Cli;
using CobolCS.Frontend;
using CobolCS.Runtime;
using CobolCS.Tools;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CobolCS.IntegrationTests;

/// <summary>表示されない更新結果も独立期待値で固定できることを検証します。</summary>
[TestClass]
public sealed class AssertionCandidateTests
{
    /// <summary>静的候補は複数受信先、条件親、表、余り、両分岐と文字ポインターを保持します。</summary>
    [TestMethod]
    public void StaticUpdatesIncludeAllReceivers()
    {
        using var folder = new TestDirectory();
        var output = Generate(folder.Path, "01 A PIC 99. 01 B PIC 99. 01 R PIC 99. 01 IDX PIC 9. 01 FLAG PIC X. 88 YES VALUE 'Y'. 01 TAB PIC 99 OCCURS 3. 01 DEST PIC X(5). 01 PTR PIC 99. 01 UNUSED PIC 9.", "MOVE 1 TO A B DIVIDE 7 BY 2 GIVING A REMAINDER R IF A = 1 MOVE 2 TO B ELSE MOVE 3 TO R END-IF PERFORM VARYING IDX FROM 1 BY 1 UNTIL IDX > 3 MOVE A TO TAB(IDX) END-PERFORM SET YES TO TRUE STRING 'A' DELIMITED BY SIZE INTO DEST POINTER PTR END-STRING GOBACK.");
        var candidates = Workspace.Read(Path.Combine(output, "reports/execution-readiness.json"))["assertionCandidates"]!.AsArray();
        CollectionAssert.AreEquivalent(new[] { "A", "B", "R", "IDX", "FLAG", "TAB", "DEST", "PTR" }, candidates.Select(c => c!.S("name")).ToArray());
        Assert.AreEqual(candidates.Count, candidates.Select(c => c!.S("symbolId")).Distinct().Count());
        Assert.IsTrue(candidates.All(c => c!["updates"]!.AsArray().Count > 0));
        var test = File.ReadAllText(Directory.GetFiles(Path.Combine(output, "Tests/Cases")).Single());
        StringAssert.Contains(test, "Preparation.Required<Number[]>(\"expected TAB\")");
        Assert.IsFalse(test.Contains("expected UNUSED", StringComparison.Ordinal));
    }

    /// <summary>更新のない宣言を埋め合わせに選ばず、未知の更新領域は手動確認として残します。</summary>
    [TestMethod]
    public void EmptyAndUnknownUpdatesRemainHonest()
    {
        using var folder = new TestDirectory();
        var output = Generate(folder.Path, "01 UNUSED PIC 9.", "DISPLAY 'OK' GOBACK.");
        Assert.AreEqual(0, Workspace.Read(Path.Combine(output, "reports/execution-readiness.json"))["assertionCandidates"]!.AsArray().Count);
        using var unknown = new TestDirectory();
        var unknownOutput = Generate(unknown.Path, "01 MYSTERY USAGE POINTER.", "MOVE 1 TO MYSTERY GOBACK.");
        var candidates = Workspace.Read(Path.Combine(unknownOutput, "reports/execution-readiness.json"))["assertionCandidates"]!.AsArray();
        Assert.IsTrue(candidates.Any(c => c!.S("status") == "manual-unknown"));
    }

    /// <summary>外部更新、固定グループ、初期化と引数比較の対応を確認します。</summary>
    [TestMethod]
    public void DependencyAndArgumentCandidatesUseActualOutputs()
    {
        using var folder = new TestDirectory();
        var output = Path.Combine(folder.Path, "workflow");
        Generator.Generate(FrontendAnalyzer.Analyze(Path.Combine(Workspace.Root, "examples/WORKFLOW")), output, "scaffold");
        ManualTemplateSupport.Prepare(output);
        var candidates = Workspace.Read(Path.Combine(output, "reports/execution-readiness.json"))["assertionCandidates"]!.AsArray();
        string[] Names(string id) => candidates.Where(c => c!.S("programId") == id).Select(c => c!.S("name").Split('.')[^1]).ToArray();
        CollectionAssert.AreEquivalent(new[] { "N", "IND", "SQLCODE", "SQLSTATE" }, Names("SQLCASE"));
        Assert.IsTrue(Names("FILECASE").Any(n => n.Contains("TEXT-VALUE", StringComparison.Ordinal)));
        CollectionAssert.Contains(Names("FILECASE"), "ST");
        Assert.HasCount(3, Names("GROUPCALL"));
        var argument = candidates.Single(c => c!.S("programId") == "ARGCALC")!;
        Assert.AreEqual("arguments", argument.S("assertionKind"));
        StringAssert.StartsWith(argument.S("assertionMember"), "result.Arguments.");
        var argumentTest = File.ReadAllText(Directory.GetFiles(Path.Combine(output, "Tests/Cases"), "*ARGCALC*Tests.cs").Single());
        Assert.IsFalse(argumentTest.Contains("result.Snapshot.", StringComparison.Ordinal));
        StringAssert.Contains(argumentTest, "ContentEquals");
        using var inputOnly = new TestDirectory();
        var inputOutput = Generate(inputOnly.Path, "01 INPUT-VALUE PIC 99. 01 OUTPUT-VALUE PIC 99.", "CALL 'SVC' USING BY CONTENT INPUT-VALUE BY REFERENCE OUTPUT-VALUE END-CALL GOBACK.");
        var callCandidates = Workspace.Read(Path.Combine(inputOutput, "reports/execution-readiness.json"))["assertionCandidates"]!.AsArray();
        Assert.AreEqual(1, callCandidates.Count);
        Assert.AreEqual("OUTPUT-VALUE", callCandidates[0]!.S("name"));
        using var initialized = new TestDirectory();
        var initializedOutput = Generate(initialized.Path, "01 GRP. 05 NUM PIC 99. 88 ZERO-NUM VALUE 0. 05 TXT PIC X OCCURS 2. 05 FILLER PIC X.", "INITIALIZE GRP GOBACK.");
        Assert.AreEqual(2, Workspace.Read(Path.Combine(initializedOutput, "reports/execution-readiness.json"))["assertionCandidates"]!.AsArray().Count);
    }

    /// <summary>NET の独立期待値9700が採用後の減算破壊を結果不一致として検出します。</summary>
    /// <returns>生成テストの実行。</returns>
    [TestMethod]
    public async Task NetExpectationDetectsAdoptedCalculationMutation()
    {
        using var folder = new TestDirectory();
        var output = Generate(folder.Path, "01 AMOUNT PIC 9(5) VALUE 10000. 01 FEE PIC 9(5) VALUE 300. 01 NET PIC 9(5) VALUE 0.", "COMPUTE NET = AMOUNT - FEE GOBACK.");
        var candidates = Workspace.Read(Path.Combine(output, "reports/execution-readiness.json"))["assertionCandidates"]!.AsArray();
        Assert.AreEqual(1, candidates.Count);
        Assert.AreEqual("NET", candidates[0]!.S("name"));
        var path = Directory.GetFiles(Path.Combine(output, "Tests/Cases")).Single();
        var source = File.ReadAllText(path);
        StringAssert.Contains(source, "Preparation.Required<Number>(\"expected NET\")");
        source = source.Replace("Preparation.Required<string[]>(\"expected Display\")", "Array.Empty<string>()", StringComparison.Ordinal)
            .Replace("Preparation.Required<TerminationKind>(\"expected Termination\")", "TerminationKind.Goback", StringComparison.Ordinal)
            .Replace("Preparation.Required<int>(\"expected dependency count\")", "0", StringComparison.Ordinal)
            .Replace("Preparation.Required<Number>(\"expected NET\")", "new Number(9700)", StringComparison.Ordinal);
        File.WriteAllText(path, source);
        var reviewPath = Path.Combine(output, "Tests/review.json");
        var review = Workspace.Read(reviewPath);
        var entry = review["cases"]![0]!;
        entry["reviewed"] = true;
        entry["baselineKind"] = "independent-synthetic";
        var manifest = Workspace.Read(Path.Combine(output, "manifest.json"));
        var baselinePath = Directory.GetFiles(Path.Combine(output, "Tests/Baselines")).Single();
        var baseline = new JsonObject
        {
            ["baselineKind"] = "independent-synthetic",
            ["reviewReason"] = "Independent arithmetic: 10000 minus 300 equals 9700; no observed output used.",
            ["sourceHashes"] = manifest["inputHashes"]!.DeepClone(),
            ["toolVersion"] = manifest["toolVersion"]!.DeepClone(),
            ["expected"] = new JsonObject { ["codeFile"] = Path.GetRelativePath(output, path), ["caseId"] = entry["caseId"]!.DeepClone() },
        };
        File.WriteAllText(baselinePath, baseline.ToJsonString());
        foreach (var file in entry["files"]!.AsObject().Select(p => p.Key).ToArray())
        {
            entry["files"]![file] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(output, file))));
        }

        File.WriteAllText(reviewPath, review.ToJsonString());
        await ProcessRunner.Run("dotnet", ["test", Path.Combine(output, "Tests/CobolCS.Generated.Tests.csproj"), "-m:1"], folder.Path);
        var adopted = Path.Combine(folder.Path, "adopted");
        await ProcessRunner.Run("dotnet", [.. Workspace.Cli, "adopt", output, "--output", adopted], folder.Path);
        var program = Directory.GetFiles(Path.Combine(adopted, "Programs"), "*Program.g.cs").Single();
        var original = File.ReadAllText(program);
        var mutated = original.Replace(" - CobolValue.Numeric(", " + CobolValue.Numeric(", StringComparison.Ordinal);
        Assert.AreNotEqual(original, mutated);
        File.WriteAllText(program, mutated);
        var result = Workspace.Command(["dotnet", "test", Path.Combine(adopted, "Tests/CobolCS.Generated.Tests.csproj"), "-m:1"]);
        Assert.AreEqual(1, result.I("exitCode"), result.ToJsonString());
        StringAssert.Contains(result.S("stdout"), "Assert.AreEqual failed");
        StringAssert.Contains(result.S("stdout"), "9700");
        StringAssert.Contains(result.S("stdout"), "10300");
        Assert.IsFalse(result.S("stdout").Contains("Review hash mismatch", StringComparison.Ordinal));
    }

    private static string Generate(string root, string declarations, string body)
    {
        var input = Path.Combine(root, "candidate.cbl");
        File.WriteAllText(input, $"IDENTIFICATION DIVISION. PROGRAM-ID. CANDIDATE. DATA DIVISION. WORKING-STORAGE SECTION. {declarations} PROCEDURE DIVISION. {body}");
        var output = Path.Combine(root, "generated");
        Generator.Generate(FrontendAnalyzer.Analyze(input), output, "scaffold");
        ManualTemplateSupport.Prepare(output);
        return output;
    }
}
