using System.Text;
using CobolCS.Cli;
using CobolCS.Frontend;
using CobolCS.Runtime;
using CobolCS.Tools;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CobolCS.IntegrationTests;

/// <summary>多数の条件名を持つ生成でも候補・原文対応の順序と内容を保持します。</summary>
[TestClass]
public sealed class BackendScalingTests
{
    /// <summary>異なる親と同じ親への多数のSETを、更新元を失わず決定的に生成します。</summary>
    /// <param name="fieldCount">独立した基本項目数。</param>
    /// <param name="groupDepth">共有される親グループの深さ。</param>
    [TestMethod]
    [DataRow(1, 0)]
    [DataRow(300, 0)]
    [DataRow(30, 8)]
    public void ConditionParentsKeepEveryUpdateAndMapping(int fieldCount, int groupDepth)
    {
        using var folder = new TestDirectory();
        var source = new StringBuilder("IDENTIFICATION DIVISION. PROGRAM-ID. SCALE. DATA DIVISION. WORKING-STORAGE SECTION.\n");
        for (var level = 1; level <= groupDepth; level++)
        {
            source.AppendLine($"{level:D2} GROUP-{level:D2}.");
        }

        for (var i = 0; i < fieldCount; i++)
        {
            source.AppendLine($"{groupDepth + 1:D2} F-{i:D4} PIC 9. 88 YES-{i:D4} VALUE 1. 88 NO-{i:D4} VALUE 0.");
        }

        source.AppendLine("PROCEDURE DIVISION.");
        for (var i = 0; i < 300; i++)
        {
            source.AppendLine($"SET YES-{i % fieldCount:D4} TO TRUE");
        }

        source.AppendLine("GOBACK.");
        var input = Path.Combine(folder.Path, "scale.cbl");
        File.WriteAllText(input, source.ToString());
        var ir = FrontendAnalyzer.Analyze(input);
        var first = Path.Combine(folder.Path, "first");
        var second = Path.Combine(folder.Path, "second");
        Generator.Generate(ir, first, "scaffold");
        Generator.Generate(ir, second, "scaffold");
        var candidates = Workspace.Read(Path.Combine(first, "reports/execution-readiness.json"))["assertionCandidates"]!.AsArray();
        Assert.HasCount(fieldCount, candidates);
        Assert.AreEqual(300, candidates.Sum(c => c!["updates"]!.AsArray().Count));
        Assert.IsTrue(candidates.All(c => c!.S("status") == "generated-template"));
        var map = Workspace.Read(Path.Combine(first, "reports/source-map.json")).AsArray();
        Assert.HasCount(fieldCount + groupDepth, map.Where(m => m!.S("symbolId") != string.Empty).ToArray());
        var statementIds = map.Where(m => m!.S("statementId") != string.Empty).Select(m => m!.S("statementId")).ToArray();
        Assert.AreEqual(statementIds.Length, statementIds.Distinct(StringComparer.Ordinal).Count());
        Assert.IsGreaterThanOrEqualTo(301, statementIds.Length);
        foreach (var path in Directory.GetFiles(first, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(first, path);
            CollectionAssert.AreEqual(File.ReadAllBytes(path), File.ReadAllBytes(Path.Combine(second, relative)), relative);
        }
    }
}
