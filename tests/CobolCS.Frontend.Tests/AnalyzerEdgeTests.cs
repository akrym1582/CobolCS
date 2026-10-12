using System.Text.Json.Nodes;

namespace CobolCS.Frontend.Tests;

/// <summary>Unicode、COPY異常系、ディレクトリ列挙を撤去前に固定した完全IRと比較します。</summary>
public sealed class AnalyzerEdgeTests
{
    /// <summary>Gets 前処理の追加境界入力。</summary>
    public static IEnumerable<object[]> Inputs
    {
        get
        {
            foreach (var name in new[] { "日本", "𐐨", "ß", "ﬃ", "ı" })
            {
                var upper = name switch { "ß" => "SS", "ﬃ" => "FFI", "ı" => "I", _ => name.ToUpperInvariant() };
                yield return new object[] { "identifier-" + name, "IDENTIFICATION DIVISION.\nPROGRAM-ID. TEST.\nDATA DIVISION.\nWORKING-STORAGE SECTION.\n01 " + name + " PIC 9 VALUE 1.\nPROCEDURE DIVISION.\nDISPLAY " + upper + ".\nSTOP RUN.\n", "{}", string.Empty };
            }

            yield return new object[] { "cycle", "IDENTIFICATION DIVISION. PROGRAM-ID. TEST. DATA DIVISION. WORKING-STORAGE SECTION. COPY BOOK. PROCEDURE DIVISION. STOP RUN.", "{}", "COPY BOOK." };
            yield return new object[] { "replacement", "IDENTIFICATION DIVISION. PROGRAM-ID. TEST. DATA DIVISION. WORKING-STORAGE SECTION. COPY BOOK REPLACING LEADING ==PRE-== BY ==POST-==. PROCEDURE DIVISION. DISPLAY POST-N STOP RUN.", "{}", "01 PRE-N PIC 9 VALUE 3." };
            yield return new object[] { "replacement-literal", "IDENTIFICATION DIVISION. PROGRAM-ID. TEST. DATA DIVISION. WORKING-STORAGE SECTION. COPY BOOK REPLACING =='ab'== BY =='CD'==. PROCEDURE DIVISION. DISPLAY N STOP RUN.", "{}", "01 N PIC X(2) VALUE 'ab'." };
            yield return new object[] { "stub-control", "IDENTIFICATION DIVISION. PROGRAM-ID. TEST. PROCEDURE DIVISION. PERFORM A THRU B. STOP RUN. A. DISPLAY 'A'. B. DISPLAY 'B'.", "{\"controlFlowMode\":\"stub-control\"}", string.Empty };
            yield return new object[] { "decimal-comma", "IDENTIFICATION DIVISION. PROGRAM-ID. TEST. ENVIRONMENT DIVISION. CONFIGURATION SECTION. SPECIAL-NAMES. DECIMAL-POINT IS COMMA. DATA DIVISION. WORKING-STORAGE SECTION. 01 N PIC 9V99. PROCEDURE DIVISION. MOVE 1,25 TO N DISPLAY N STOP RUN.", "{}", string.Empty };
            yield return new object[] { "bom", "\ufeffIDENTIFICATION DIVISION. PROGRAM-ID. TEST. PROCEDURE DIVISION. DISPLAY 'x' STOP RUN.", "{}", string.Empty };
            yield return new object[] { "crlf-literal", "IDENTIFICATION DIVISION.\r\nPROGRAM-ID. TEST.\r\nPROCEDURE DIVISION.\r\nDISPLAY '日😀&\r\n&'本' STOP RUN.\r\n", "{}", string.Empty };
        }
    }

    /// <summary>ディレクトリのリンクを再帰せず、ファイルのリンクは入力として解析します。</summary>
    [Fact]
    public void DirectoryEnumerationMatchesReference()
    {
        var root = Path.Combine(Path.GetTempPath(), "cobolcs-directory-" + Guid.NewGuid().ToString("N"));
        var input = Directory.CreateDirectory(Path.Combine(root, "input")).FullName;
        var external = Directory.CreateDirectory(Path.Combine(root, "external")).FullName;
        try
        {
            File.WriteAllText(Path.Combine(input, "A.CBL"), "IDENTIFICATION DIVISION. PROGRAM-ID. A. PROCEDURE DIVISION. STOP RUN.");
            File.WriteAllText(Path.Combine(external, "B.sqb"), "IDENTIFICATION DIVISION. PROGRAM-ID. B. PROCEDURE DIVISION. STOP RUN.");
            Directory.CreateSymbolicLink(Path.Combine(input, "linked-directory"), external);
            File.CreateSymbolicLink(Path.Combine(input, "linked-file.cob"), Path.Combine(external, "B.sqb"));
            var expected = Frozen("directory-links", root);
            var actual = FrontendAnalyzer.Analyze(input);
            Assert.Equal(2, expected["programs"]!.AsArray().Count);
            Assert.Equal("0.2.2", expected["frontendVersion"]!.GetValue<string>());
            Assert.Equal(FrontendAnalyzer.Version, actual["frontendVersion"]!.GetValue<string>());
            expected["frontendVersion"] = FrontendAnalyzer.Version;
            var differences = JsonComparison.Compare("directory-links", expected.ToJsonString(), actual.ToJsonString());
            Assert.True(!differences.Any(), string.Join("\n", differences.Take(10)));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>異常系も含め、同じ入力・探索順・動作モードで全IRを比較します。</summary>
    /// <param name="id">境界の名前。</param>
    /// <param name="source">入力COBOL。</param>
    /// <param name="config">設定JSON。</param>
    /// <param name="copy">BOOKの内容。</param>
    [Theory]
    [MemberData(nameof(Inputs))]
    public void CompleteIrMatchesFrozenReference(string id, string source, string config, string copy)
    {
        var directory = Path.Combine(Path.GetTempPath(), "cobolcs-edge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var input = Path.Combine(directory, "sample.cbl");
            var configuration = Path.Combine(directory, "config.json");
            File.WriteAllText(input, source);
            File.WriteAllText(configuration, config);
            if (copy.Length > 0)
            {
                File.WriteAllText(Path.Combine(directory, "BOOK.cpy"), copy);
            }

            var expected = Frozen(id, directory);
            var actual = FrontendAnalyzer.Analyze(input, FrontendConfiguration.Parse(config));
            Assert.Equal("0.2.2", expected["frontendVersion"]!.GetValue<string>());
            Assert.Equal(FrontendAnalyzer.Version, actual["frontendVersion"]!.GetValue<string>());
            expected["frontendVersion"] = FrontendAnalyzer.Version;
            var diff = JsonComparison.Compare(id, expected.ToJsonString(), actual.ToJsonString());
            Assert.True(!diff.Any(), string.Join("\n", diff.Take(10)));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static JsonNode Frozen(string id, string root)
    {
        var path = Path.Combine(Baseline.Root, "tests/migration/edge-ir", id + ".json");
        var hashes = JsonNode.Parse(File.ReadAllText(Path.Combine(Baseline.Root, "tests/migration/edge-ir/manifest.json")))!;
        Assert.Equal(hashes["files"]![id + ".json"]!.GetValue<string>(), Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))));
        return JsonNode.Parse(File.ReadAllText(path).Replace("@@ROOT@@", root, StringComparison.Ordinal))!;
    }
}
