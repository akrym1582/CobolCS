using System.Text.Json.Nodes;
using CobolCS.Cli;
using CobolCS.Frontend;
using CobolCS.Runtime;
using CobolCS.Tools;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CobolCS.IntegrationTests;

/// <summary>FD の標準ラベル指定を外部ファイル実装の宣言として保持します。</summary>
[TestClass]
public sealed class FileLabelClauseTests
{
    /// <summary>標準・省略ラベルのメタデータと原文位置を保持し、不正な句は引き続き拒否します。</summary>
    /// <param name="clause">検査する FD 句。</param>
    /// <param name="expected">合法な場合のラベル値。</param>
    [TestMethod]
    [DataRow("LABEL RECORDS ARE STANDARD", "STANDARD")]
    [DataRow("LABEL RECORD IS STANDARD", "STANDARD")]
    [DataRow("LABEL RECORDS STANDARD", "STANDARD")]
    [DataRow("LABEL RECORDS ARE OMITTED", "OMITTED")]
    [DataRow("LABEL RECORDS ARE NONSTANDARD", null)]
    [DataRow("LABEL RECORDS ARE", null)]
    [DataRow("LABEL RECORDS ARE STANDARD LABEL RECORDS ARE OMITTED", null)]
    [DataRow("LABEL ARE STANDARD", null)]
    public void LabelMetadataAndInvalidClauses(string clause, string? expected)
    {
        using var folder = new TestDirectory();
        var ir = Analyze(folder.Path, clause);
        if (expected == null)
        {
            Assert.IsTrue(ir.A("diagnostics").Any(d => d.S("severity") == "error"));
            Assert.AreEqual(0, ir.A("programs").Count());
            return;
        }

        Assert.IsFalse(ir.A("diagnostics").Any(d => d.S("severity") == "error"), ir.ToJsonString());
        var dependency = ir["programs"]![0]!["dependencies"]![0]!;
        Assert.AreEqual(expected, dependency.S("labelRecords"));
        var label = dependency.A("descriptionClauses").Single(c => c.S("kind") == "LABEL");
        Assert.AreEqual(clause, label["source"]!.S("raw"));
        Assert.IsGreaterThan(0, label["source"]!["start"]!.I("line"));
    }

    /// <summary>STANDARD の READ を生成・ビルドし、既存の型付き FILE 応答で実行します。</summary>
    /// <returns>生成したテストの完了。</returns>
    [TestMethod]
    public async Task StandardLabelReadUsesExistingFileContract()
    {
        using var folder = new TestDirectory();
        var ir = Analyze(folder.Path, "LABEL RECORDS ARE STANDARD");
        Assert.AreEqual(1, ir.A("programs").Count());
        var output = Path.Combine(folder.Path, "generated");
        Generator.Generate(ir, output, "scaffold");
        var stubs = Workspace.Read(Path.Combine(output, "reports/stubs.json")).AsArray();
        Assert.HasCount(1, stubs);
        Assert.AreEqual("adapter-required", stubs[0]!.S("capability"));
        Assert.AreEqual("STANDARD", stubs[0]!["dependency"]!.S("labelRecords"));
        var method = stubs[0]!.S("method");
        var mapping = Workspace.Read(Path.Combine(output, "reports/source-map.json")).AsArray();
        var executor = mapping.Single(m => m!["executorType"] != null)!.S("executorType");
        var name = executor[..^"Executor".Length];
        var record = ir["programs"]![0]!.A("symbols").Single(s => s.S("name") == "TEXT-VALUE").S("id");
        var testPath = Directory.GetFiles(Path.Combine(output, "Tests/Cases"), "*.cs").Single();
        var testSource = $$"""
            using CobolCS.Runtime;
            using Microsoft.VisualStudio.TestTools.UnitTesting;

            namespace CobolCS.Generated.Tests;

            /// <summary>標準ラベルの物理処理は外部実装に委ね、応答契約を検査します。</summary>
            [TestClass]
            public sealed class {{name}}Tests
            {
                /// <summary>明示した応答レコードと状態が表示されます。</summary>
                [TestMethod]
                public void ReadRecord()
                {
                    var fake = new {{name}}ScriptedDependencies();
                    fake.Expect{{method}}(request => request.Source.Operation == "read", new {{name}}{{method}}Response("00", DependencyOutcome.Normal, Record{{record}}: "OK", FileStatus: "00"));
                    var result = new {{executor}}().Run(new(), fake);
                    CollectionAssert.AreEqual(new[] { "OK00" }, result.Display.ToArray());
                    Assert.AreEqual(TerminationKind.Goback, result.Termination);
                    Assert.HasCount(1, result.DependencyTrace);
                }
            }
            """ + "\n";
        File.WriteAllText(testPath, testSource);
        await ProcessRunner.Run("dotnet", ["test", Path.Combine(output, "Tests/CobolCS.Generated.Tests.csproj"), "-m:1"], folder.Path);
    }

    private static JsonObject Analyze(string root, string clause)
    {
        var path = Path.Combine(root, "label.cbl");
        var source = $$"""
            IDENTIFICATION DIVISION. PROGRAM-ID. LABELCASE.
            ENVIRONMENT DIVISION. INPUT-OUTPUT SECTION. FILE-CONTROL.
            SELECT F ASSIGN TO 'SYNTHETIC' ORGANIZATION LINE SEQUENTIAL FILE STATUS ST.
            DATA DIVISION. FILE SECTION.
            FD F {{clause}}.
            01 REC.
            05 TEXT-VALUE PIC X(2).
            WORKING-STORAGE SECTION.
            01 ST PIC XX.
            PROCEDURE DIVISION.
            READ F END-READ
            DISPLAY TEXT-VALUE ST GOBACK.
            """;
        File.WriteAllText(path, source);
        return FrontendAnalyzer.Analyze(path);
    }
}
