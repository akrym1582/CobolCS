using System.Text.Json.Nodes;
using CobolCS.Cli;
using CobolCS.Frontend;
using CobolCS.Runtime;
using CobolCS.Tools;
using Xunit;

namespace CobolCS.IntegrationTests;

/// <summary>FD の標準ラベル指定を外部ファイル実装の宣言として保持します。</summary>
public sealed class FileLabelClauseTests
{
    /// <summary>標準・省略ラベルのメタデータと原文位置を保持し、不正な句は引き続き拒否します。</summary>
    /// <param name="clause">検査する FD 句。</param>
    /// <param name="expected">合法な場合のラベル値。</param>
    [Theory]
    [InlineData("LABEL RECORDS ARE STANDARD", "STANDARD")]
    [InlineData("LABEL RECORD IS STANDARD", "STANDARD")]
    [InlineData("LABEL RECORDS STANDARD", "STANDARD")]
    [InlineData("LABEL RECORDS ARE OMITTED", "OMITTED")]
    [InlineData("LABEL RECORDS ARE NONSTANDARD", null)]
    [InlineData("LABEL RECORDS ARE", null)]
    [InlineData("LABEL RECORDS ARE STANDARD LABEL RECORDS ARE OMITTED", null)]
    [InlineData("LABEL ARE STANDARD", null)]
    public void LabelMetadataAndInvalidClauses(string clause, string? expected)
    {
        using var folder = new TestDirectory();
        var ir = Analyze(folder.Path, clause);
        if (expected == null)
        {
            Assert.Contains(ir.A("diagnostics"), d => d.S("severity") == "error");
            Assert.Empty(ir.A("programs"));
            return;
        }

        Assert.False(ir.A("diagnostics").Any(d => d.S("severity") == "error"), ir.ToJsonString());
        var dependency = ir["programs"]![0]!["dependencies"]![0]!;
        Assert.Equal(expected, dependency.S("labelRecords"));
        var label = dependency.A("descriptionClauses").Single(c => c.S("kind") == "LABEL");
        Assert.Equal(clause, label["source"]!.S("raw"));
        Assert.True(label["source"]!["start"]!.I("line") > 0);
    }

    /// <summary>STANDARD の READ を生成・ビルドし、既存の型付き FILE 応答で実行します。</summary>
    /// <returns>生成したテストの完了。</returns>
    [Fact]
    public async Task StandardLabelReadUsesExistingFileContract()
    {
        using var folder = new TestDirectory();
        var ir = Analyze(folder.Path, "LABEL RECORDS ARE STANDARD");
        Assert.Single(ir.A("programs"));
        var output = Path.Combine(folder.Path, "generated");
        Generator.Generate(ir, output, "scaffold");
        ManualTemplateSupport.Prepare(output);
        var stubs = Workspace.Read(Path.Combine(output, "reports/stubs.json")).AsArray();
        Assert.Single(stubs);
        Assert.Equal("adapter-required", stubs[0]!.S("capability"));
        Assert.Equal("STANDARD", stubs[0]!["dependency"]!.S("labelRecords"));
        var method = stubs[0]!.S("method");
        var mapping = Workspace.Read(Path.Combine(output, "reports/source-map.json")).AsArray();
        var executor = mapping.Single(m => m!["executorType"] != null)!.S("executorType");
        var name = executor[..^"Executor".Length];
        var record = ir["programs"]![0]!.A("symbols").Single(s => s.S("name") == "TEXT-VALUE").S("id");
        var testPath = Directory.GetFiles(Path.Combine(output, "Tests/Cases"), "*.cs").Single();
        var testSource = $$"""
            using CobolCS.Runtime;
            using Xunit;

            namespace CobolCS.Generated.Tests;

            /// <summary>標準ラベルの物理処理は外部実装に委ね、応答契約を検査します。</summary>
                        public sealed class {{name}}Tests
            {
                /// <summary>明示した応答レコードと状態が表示されます。</summary>
                [Fact]
                public void ReadRecord()
                {
                    var fake = new {{name}}ScriptedDependencies();
                    fake.Expect{{method}}(request => request.Source.Operation == "read", new {{name}}{{method}}Response("00", DependencyOutcome.Normal, Record{{record}}: "OK", FileStatus: "00"));
                    var result = new {{executor}}().Run(new(), fake);
                    Assert.Equal(new[] { "OK00" }, result.Display.ToArray());
                    Assert.Equal(TerminationKind.Goback, result.Termination);
                    Assert.Single(result.DependencyTrace);
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
