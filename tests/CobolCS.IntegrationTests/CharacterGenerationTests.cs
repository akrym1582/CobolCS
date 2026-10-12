using System.Text.Json.Nodes;
using CobolCS.Cli;
using CobolCS.Frontend;
using CobolCS.Runtime;
using CobolCS.Tools;
using Xunit;

namespace CobolCS.IntegrationTests;

/// <summary>構造化・dispatcher・添字と文全体スタブを実際の生成 C# で確認します。</summary>
public sealed class CharacterGenerationTests
{
    /// <summary>既存 tally 加算、PIC 格納、operand 捕捉、overflow 分岐を検証します。</summary>
    /// <returns>単一ビルド後の各プログラム実行。</returns>
    [Fact]
    public async Task NativeCharacterProgramsMatchIndependentExpectations()
    {
        using var folder = new TestDirectory();
        var inputs = Path.Combine(folder.Path, "inputs");
        Directory.CreateDirectory(inputs);
        File.Copy(Path.Combine(Workspace.Root, "tests/fixtures/business-assistance/characters.cbl"), Path.Combine(inputs, "characters.cbl"));
        File.WriteAllText(Path.Combine(inputs, "array.cbl"), "IDENTIFICATION DIVISION. PROGRAM-ID. ARRAY-INSPECT. DATA DIVISION. WORKING-STORAGE SECTION. 01 T PIC XX VALUE 'AB' OCCURS 2 TIMES. 01 IDX PIC 9 VALUE 1. PROCEDURE DIVISION. INSPECT T(IDX) TALLYING IDX FOR CHARACTERS IF IDX = 3 DISPLAY T(1) IDX ELSE DISPLAY 'BAD' END-IF GOBACK.");
        File.WriteAllText(Path.Combine(inputs, "flow.cbl"), "IDENTIFICATION DIVISION. PROGRAM-ID. FLOW-STRING. DATA DIVISION. WORKING-STORAGE SECTION. 01 DEST PIC XX VALUE 'xy'. 01 PTR PIC 9 VALUE 1. 01 CNT PIC 9 VALUE 0. PROCEDURE DIVISION. MAIN. STRING 'AB,CD' DELIMITED BY ',' INTO DEST POINTER PTR ON OVERFLOW ADD 1 TO CNT GO TO BAD NOT ON OVERFLOW ADD 1 TO CNT GO TO GOOD END-STRING DISPLAY 'LEAK' GOBACK. BAD. DISPLAY 'BAD' CNT GOBACK. GOOD. DISPLAY 'NORMAL' CNT DEST PTR GOBACK.");
        File.WriteAllText(Path.Combine(inputs, "overflow.cbl"), "IDENTIFICATION DIVISION. PROGRAM-ID. OVERFLOW-STRING. DATA DIVISION. WORKING-STORAGE SECTION. 01 DEST PIC X(4) VALUE 'abcd'. 01 PTR PIC 9 VALUE 2. 01 CNT PIC 9 VALUE 0. PROCEDURE DIVISION. STRING 'ABCDE,FG' DELIMITED BY ',' INTO DEST POINTER PTR ON OVERFLOW ADD 1 TO CNT END-ADD NOT ON OVERFLOW ADD 2 TO CNT END-ADD END-STRING DISPLAY CNT DEST PTR GOBACK.");
        var root = Path.Combine(folder.Path, "generated");
        Generator.Generate(FrontendAnalyzer.Analyze(inputs, FrontendConfiguration.Parse("{\"testGeneration\":{\"maxAttempts\":4}}")), root, "scaffold");
        Assert.True(Workspace.Read(Path.Combine(root, "reports/stubs.json")).AsArray().Count == 0, File.ReadAllText(Path.Combine(root, "reports/stubs.json")));
        Assert.DoesNotContain(Workspace.Read(Path.Combine(root, "reports/diagnostics.json")).AsArray(), d => d!.S("code").StartsWith("CHAR_", StringComparison.Ordinal));
        _ = await ProcessRunner.Run("dotnet", ["build", "CobolCS.Generated.sln", "-m:1"], root);
        foreach (var (program, expected) in new[] { ("BUSINESS-CHARACTERS", new[] { "NORMAL", "BBAAA07aXZdef04" }), ("ARRAY-INSPECT", new[] { "AB3" }), ("FLOW-STRING", new[] { "NORMAL1AB3" }), ("OVERFLOW-STRING", new[] { "1aABC5" }) })
        {
            var result = J.Parse(await ProcessRunner.Run("dotnet", [Path.Combine(root, "bin/Debug/net10.0/CobolCS.Generated.dll"), program], root));
            Assert.Equal(expected, result.A("display").Select(v => v.GetValue<string>()));
        }

        var generated = Directory.GetFiles(Path.Combine(root, "Programs"), "*Program.g.cs").Single(p => p.Contains("BUSINESS", StringComparison.Ordinal));
        var text = File.ReadAllText(generated);
        Assert.Contains("SourceCharacters.Tally", text, StringComparison.Ordinal);
        Assert.DoesNotContain("reports/ir.json", text, StringComparison.Ordinal);
        var brokenCount = text.Replace("SourceCharacters.Tally(inspected, \"all\", pattern)", "(SourceCharacters.Tally(inspected, \"all\", pattern) * new Number(0))", StringComparison.Ordinal);
        Assert.NotEqual(text, brokenCount);
        File.WriteAllText(generated, brokenCount);
        _ = await ProcessRunner.Run("dotnet", ["build", "CobolCS.Generated.csproj", "-m:1"], root);
        var wrong = J.Parse(await ProcessRunner.Run("dotnet", [Path.Combine(root, "bin/Debug/net10.0/CobolCS.Generated.dll"), "BUSINESS-CHARACTERS"], root));
        Assert.NotEqual("BBAAA07aXZdef04", wrong.A("display").Last().GetValue<string>());
        File.WriteAllText(generated, text.Replace("SourceCharacters.Prefix(\"X,Y\", \",\")", "\"X,Y\"", StringComparison.Ordinal));
        _ = await ProcessRunner.Run("dotnet", ["build", "CobolCS.Generated.csproj", "-m:1"], root);
        wrong = J.Parse(await ProcessRunner.Run("dotnet", [Path.Combine(root, "bin/Debug/net10.0/CobolCS.Generated.dll"), "BUSINESS-CHARACTERS"], root));
        Assert.NotEqual("BBAAA07aXZdef04", wrong.A("display").Last().GetValue<string>());
    }

    /// <summary>RENAMES の共有領域と配置不明を、通常の ASCII 項目として扱いません。</summary>
    [Fact]
    public void SharedRenameAndUnknownLayoutAreNotNative()
    {
        var item = new JsonObject { ["id"] = "f1", ["level"] = 1, ["pic"] = new JsonObject { ["category"] = "text", ["digits"] = 4, ["scale"] = 0 } };
        var symbols = new Dictionary<string, JsonNode> { ["f1"] = item, ["alias"] = new JsonObject { ["id"] = "alias", ["level"] = 66 } };
        var statement = J.Parse("{\"kind\":\"inspectOperations\",\"value\":{\"kind\":\"reference\",\"symbolId\":\"f1\",\"subscripts\":[]},\"operations\":[{\"verb\":\"replace\",\"mode\":\"characters\",\"replacement\":{\"kind\":\"literal\",\"valueType\":\"text\",\"value\":\"A\"}}]}");
        Assert.Equal("CHAR_LAYOUT", CharacterCapabilities.Reason(statement, symbols));
        symbols["other"] = new JsonObject { ["id"] = "other", ["name"] = "OTHER", ["level"] = 1 };
        symbols["child"] = new JsonObject { ["id"] = "child", ["name"] = "CHILD", ["level"] = 5, ["parentId"] = "other" };
        symbols["alias"]["parentId"] = "child";
        symbols["alias"]["source"] = new JsonObject { ["raw"] = "66 ALIAS RENAMES CHILD." };
        Assert.Equal(string.Empty, CharacterCapabilities.Reason(statement, symbols));
        var transfer = J.Parse("{\"kind\":\"stringTransfer\",\"target\":{\"kind\":\"reference\",\"symbolId\":\"f1\",\"subscripts\":[]},\"sendings\":[{\"value\":{\"kind\":\"literal\",\"valueType\":\"text\",\"value\":\"A\"}}]}");
        Assert.Equal(string.Empty, CharacterCapabilities.Reason(transfer, symbols));
        item["name"] = "SHARED";
        symbols["alias"]["source"]!["raw"] = "66 ALIAS RENAMES SHARED.";
        symbols["alias"]["parentId"] = "f1";
        Assert.Equal("CHAR_LAYOUT", CharacterCapabilities.Reason(statement, symbols));
        Assert.Equal("CHAR_LAYOUT", CharacterCapabilities.Reason(transfer, symbols));
        symbols["alias"]["source"]!["raw"] = "66 ALIAS RENAMES MISSING THRU CHILD.";
        symbols["alias"]["parentId"] = "child";
        Assert.Equal("CHAR_LAYOUT", CharacterCapabilities.Reason(statement, symbols));
        Assert.Equal("CHAR_LAYOUT", CharacterCapabilities.Reason(transfer, symbols));
        symbols.Remove("alias");
        item["layoutStatus"] = "unknown-layout";
        Assert.Equal("CHAR_LAYOUT", CharacterCapabilities.Reason(statement, symbols));
        item["layoutStatus"] = "known";
        Assert.Equal(string.Empty, CharacterCapabilities.Reason(statement, symbols));
    }

    /// <summary>実 CBL の保持宣言から独立領域を判定し、検査と両生成経路を揃えます。</summary>
    [Fact]
    public void RenamesScopeUsesReferencedRegionInStructuredAndDispatcherSources()
    {
        using var folder = new TestDirectory();
        foreach (var flow in new[] { false, true })
        {
            foreach (var (declaration, native) in new[]
            {
                ("01 OTHER. 05 PART PIC XX. 66 ALIAS RENAMES PART.", true),
                ("66 ALIAS RENAMES T.", false),
                ("01 OTHER. 05 PART PIC XX. 66 ALIAS RENAMES MISSING THRU PART.", false),
                (string.Empty, true),
            })
            {
                var input = Path.Combine(folder.Path, "input.cbl");
                File.WriteAllText(input, "IDENTIFICATION DIVISION. PROGRAM-ID. RENAME-CHAR. DATA DIVISION. WORKING-STORAGE SECTION. 01 T PIC XX VALUE 'AB'. " + declaration + " PROCEDURE DIVISION. " + (flow ? "MAIN. " : string.Empty) + "INSPECT T REPLACING CHARACTERS BY 'Z' STRING 'X' DELIMITED BY 'Q' INTO T END-STRING GOBACK.");
                var root = Path.Combine(folder.Path, Guid.NewGuid().ToString("N"));
                Generator.Generate(FrontendAnalyzer.Analyze(input, FrontendConfiguration.Parse("{\"testGeneration\":{\"maxAttempts\":1}}")), root, "scaffold");
                var stubs = Workspace.Read(Path.Combine(root, "reports/stubs.json")).AsArray().Where(s => s!["statement"]!.S("kind") is "inspectOperations" or "stringTransfer").ToArray();
                Assert.Equal(native ? 0 : 2, stubs.Length);
                var text = File.ReadAllText(Directory.GetFiles(Path.Combine(root, "Programs")).Single());
                Assert.Equal(native, text.Contains("SourceCharacters.Replace", StringComparison.Ordinal));
                Assert.Equal(native, text.Contains("SourceCharacters.Prefix", StringComparison.Ordinal));
                var diagnostics = Workspace.Read(Path.Combine(root, "reports/diagnostics.json")).AsArray().Where(d => d!.S("code").StartsWith("CHAR_", StringComparison.Ordinal)).ToArray();
                Assert.Equal(native ? 0 : 2, diagnostics.Length);
            }
        }
    }

    /// <summary>対応外の全句と元 IR を保持し、部分 native 実行をしません。</summary>
    [Fact]
    public void UnsupportedCharacterClausesStayWholeStatements()
    {
        using var folder = new TestDirectory();
        var inputs = Path.Combine(folder.Path, "inputs");
        Directory.CreateDirectory(inputs);
        var cases = new[]
        {
            "INSPECT T TALLYING N FOR ALL 'A' BEFORE 'Z'",
            "INSPECT T TALLYING N FOR ALL 'A' LEADING 'B'",
            "INSPECT T REPLACING ALL 'AA' BY 'B'",
            "INSPECT T CONVERTING 'AB' TO 'CD'",
            "INSPECT T(1:2) REPLACING ALL 'A' BY 'B'",
            "INSPECT NAT REPLACING ALL 'A' BY 'B'",
            "INSPECT T REPLACING ALL PAT BY T",
            "STRING T DELIMITED BY SIZE INTO T END-STRING",
            "STRING 'A' DELIMITED BY T INTO T END-STRING",
            "STRING NAT DELIMITED BY SIZE INTO T END-STRING",
            "STRING 'A' DELIMITED BY '' INTO T END-STRING",
            "STRING 'A' DELIMITED BY ',' INTO T(1:2) END-STRING",
        };
        for (var i = 0; i < cases.Length; i++)
        {
            File.WriteAllText(Path.Combine(inputs, i + ".cbl"), $"IDENTIFICATION DIVISION. PROGRAM-ID. UNSUPPORTED-{i}. DATA DIVISION. WORKING-STORAGE SECTION. 01 T PIC X(4). 01 PAT PIC XX. 01 N PIC 99. 01 NAT PIC N(4). PROCEDURE DIVISION. {cases[i]} DISPLAY 'AFTER' GO TO FINISH. FINISH. GOBACK.");
        }

        var root = Path.Combine(folder.Path, "generated");
        var ir = FrontendAnalyzer.Analyze(inputs, FrontendConfiguration.Parse("{\"testGeneration\":{\"maxAttempts\":1}}"));
        Generator.Generate(ir, root, "scaffold");
        var stubs = Workspace.Read(Path.Combine(root, "reports/stubs.json")).AsArray();
        Assert.Equal(cases.Length, stubs.Count);
        Assert.All(stubs, s => Assert.StartsWith("CHAR_", s!.S("reasonCode"), StringComparison.Ordinal));
        Assert.All(stubs, s => Assert.Contains(s!["statement"]!.S("kind"), new[] { "inspectOperations", "stringTransfer" }));
        foreach (var file in Directory.GetFiles(Path.Combine(root, "Programs")))
        {
            Assert.DoesNotContain("SourceCharacters.", File.ReadAllText(file), StringComparison.Ordinal);
        }

        Assert.All(stubs, s => Assert.NotNull(s!["statement"]!["source"]));
        var metrics = Workspace.Read(Path.Combine(root, "reports/internal-stub-summary.json"));
        Assert.Equal(cases.Length, metrics.A("groups").Sum(g => g.I("statements")));
        Assert.Equal(0, metrics.A("groups").Sum(g => g.I("firstStopPrograms")));
        Assert.Equal(cases.Length, metrics.A("groups").Sum(g => g.I("unexecutedStatements")));
    }
}
