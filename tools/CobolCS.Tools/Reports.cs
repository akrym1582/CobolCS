using System.Text.Json.Nodes;
using CobolCS.Runtime;
using static CobolCS.Tools.Workspace;

namespace CobolCS.Tools;

/// <summary>原文を含まない測定結果と、固定171入力の回復証跡を検証します。</summary>
public static class Reports
{
    private static readonly string[] CountKeys = ["selected", "analyzed", "strictGenerated", "scaffoldGenerated", "built", "fixtureExecuted", "stubFixtureExecuted", "matchedIndependentExpectation", "matchedOriginalCobol"];

    /// <summary>旧測定と新測定から日本語の集計表を作ります。</summary>
    /// <param name="input">測定ルート。</param>
    /// <param name="beforeSuffix">旧測定の接尾辞。</param>
    /// <param name="afterSuffix">新測定の接尾辞。</param>
    /// <param name="output">JSONの保存先。</param>
    /// <param name="markdown">集計文書の保存先。</param>
    public static void Write(string input, string beforeSuffix, string afterSuffix, string output, string markdown)
    {
        Directory.CreateDirectory(output);
        var lines = new List<string> { "# 公開コーパス実測", string.Empty, "通常 C# source backend の固定入力による測定。元 COBOL の実測比較は未実施。", string.Empty, "PR #3 基準: `5234e2b84b4e93983c4d7abb0764a44a5df91953`。Current の正確なコンパイラ入力は各 summary の `compilerSourceSha256`。", string.Empty };
        var failures = new List<string>();
        var kinds = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (stage, suffix) in new[] { ("PR #3", beforeSuffix), ("Current", afterSuffix) })
        {
            lines.AddRange(["## " + stage, string.Empty, "| set | 対象 | 解析 | strict生成 | scaffold生成 | build | fixture実行 | C#スタブ実行 | 独立期待値一致 | 元COBOL一致 |", "|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|"]);
            var totals = CountKeys.ToDictionary(k => k, _ => 0);
            foreach (var name in Corpus.Names)
            {
                var directory = Path.Combine(input, name + suffix);
                var summary = Read(Path.Combine(directory, "summary.json"));
                var counts = summary["counts"] ?? throw new ArgumentException("Incomplete measurement: " + directory);
                if (summary.A("cases").Count() != counts.I("selected"))
                {
                    throw new ArgumentException("Incomplete measurement: " + directory);
                }

                lines.Add("| " + name + " | " + string.Join(" | ", CountKeys.Select(k => counts.I(k))) + " |");
                foreach (var key in CountKeys)
                {
                    totals[key] += counts.I(key);
                }

                if (stage == "Current")
                {
                    foreach (var row in summary.A("cases"))
                    {
                        var diagnostic = Path.Combine(directory, row.S("id"), "analysis/reports/diagnostics.json");
                        if (File.Exists(diagnostic))
                        {
                            row["diagnosticEvidence"] = new JsonArray(Read(diagnostic).AsArray().Select(d => d!).Where(d => d.S("code") is "PARSE_ERROR" or "INPUT_DECODE" or "COPY_MISSING" or "CS_NO_PROGRAMS").Select(d => (JsonNode)new JsonObject { ["code"] = d["code"]!.DeepClone(), ["severity"] = d["severity"]!.DeepClone(), ["message"] = d["message"]!.DeepClone(), ["file"] = d["source"]?["file"]?.DeepClone(), ["line"] = d["source"]?["start"]?["line"]?.DeepClone() }).ToArray());
                        }

                        if (!row.B("built"))
                        {
                            var reason = string.Join(", ", row.A("buildDiagnosticCodes").Select(x => x.GetValue<string>()));
                            if (reason.Length == 0)
                            {
                                reason = string.Join(", ", row["diagnosticCounts"]?.AsObject().Select(p => p.Key) ?? []);
                            }

                            var primary = row.A("diagnosticEvidence").FirstOrDefault(d => d.S("code") == "PARSE_ERROR");
                            if (primary is not null)
                            {
                                reason = $"{primary.S("code")}: {primary.S("message")} ({primary.S("file")}:{primary.I("line")})";
                            }

                            failures.Add($"| {Escape(name)} | {Escape(row.S("source"))} | {Escape(reason.Length == 0 ? "CLI failure; inspect phase logs" : reason)} |");
                        }

                        foreach (var (kind, count) in row["stubKinds"]?.AsObject() ?? [])
                        {
                            kinds[kind] = kinds.GetValueOrDefault(kind) + count!.GetValue<int>();
                        }
                    }
                }

                Workspace.Write(Path.Combine(output, name + (stage == "Current" ? string.Empty : "-pr3") + ".json"), summary);
            }

            lines.Add("| total | " + string.Join(" | ", CountKeys.Select(k => totals[k])) + " |");
            lines.Add(string.Empty);
        }

        lines.AddRange(["## 残る生成・ビルド未到達", string.Empty, "診断件数は失敗ケース数ではない。解析の error があっても原文付き scaffold を生成できる場合がある。", string.Empty, "| set | 原文パス | 診断分類 |", "|---|---|---|"]);
        lines.AddRange(failures);
        lines.AddRange([string.Empty, "## 生成スタブの種類", string.Empty, "同じケースに複数のスタブがある。これらの数は実装・実行成功数ではない。", string.Empty, "| kind | 件数 |", "|---|---:|"]);
        lines.AddRange(kinds.Select(p => $"| {Escape(p.Key)} | {p.Value} |"));
        lines.AddRange([string.Empty, "再現コマンド、ライセンス、実行の根拠、制限は [public-corpus.md](public-corpus.md) を参照。", string.Empty]);
        File.WriteAllText(markdown, string.Join('\n', lines));
    }

    /// <summary>歴史的な18件の回復を、同じlock、入力、実行証跡で検証します。</summary>
    /// <param name="beforeRoot">回復前の測定ルート。</param>
    /// <param name="afterRoot">回復後の測定ルート。</param>
    /// <returns>原文なしの回復一覧。</returns>
    public static JsonObject VerifyRecovery(string beforeRoot, string afterRoot)
    {
        var sizes = new Dictionary<string, int> { ["course"] = 30, ["fun"] = 2, ["carddemo"] = 44, ["bank"] = 43, ["db2"] = 52 };
        var recovered = new JsonArray();
        var hashes = new Dictionary<string, HashSet<string>> { ["before"] = [], ["after"] = [] };
        var counts = new JsonObject();
        foreach (var (name, size) in sizes)
        {
            var before = Read(Path.Combine(beforeRoot, name, "summary.json"));
            var after = Read(Path.Combine(afterRoot, name, "summary.json"));
            foreach (var (phase, summary) in new[] { ("before", before), ("after", after) })
            {
                Require(summary.A("cases").Count() == size && summary.A("cases").Select(r => r.S("id")).Distinct().Count() == size, name + " case count/duplicate ID");
                Require(summary.S("backend") == "source" && !summary.B("includeGpl") && summary.S("originalCobolComparison") == "not-performed", name + " measurement scope");
                hashes[phase].Add(summary.S("compilerSourceSha256"));
            }

            Require(before.S("lockSha256") == after.S("lockSha256"), name + " lock changed");
            var old = before.A("cases").ToDictionary(r => r.S("id"));
            Require(old.Keys.ToHashSet().SetEquals(after.A("cases").Select(r => r.S("id"))), name + " case set changed");
            foreach (var row in after.A("cases"))
            {
                var previous = old[row.S("id")];
                Require(row.S("source") == previous.S("source") && row.S("sha256") == previous.S("sha256") && !row.B("matchedOriginalCobol"), name + " input changed");
                Require((row.B("strictGenerated") || row.B("scaffoldGenerated")) && row.B("built"), name + "/" + row.S("id") + " did not build");
                if (!previous.B("built"))
                {
                    recovered.Add(new JsonObject { ["set"] = name, ["id"] = row.S("id"), ["source"] = row.S("source"), ["sha256"] = row.S("sha256") });
                }

                if (after.A("expectedExecutionIds").Any(id => id.GetValue<string>() == row.S("id")))
                {
                    Require(row.B("fixtureExecuted") && row.B("matchedIndependentExpectation") && row.B("builtWithoutReports") && row.B("executedWithoutReports"), name + " missing fixture evidence");
                    if (row["unsetAdapterFailed"] is not null)
                    {
                        Require(row.B("unsetAdapterFailed") && row.B("stubFixtureExecuted") && row.B("editedStubsPreserved"), name + " missing adapter evidence");
                    }
                }
            }

            counts[name] = new JsonObject { ["before"] = before["counts"]!.DeepClone(), ["after"] = after["counts"]!.DeepClone() };
        }

        Require(hashes.Values.All(v => v.Count == 1), "Mixed compiler versions within measurement");
        Require(recovered.Count == 18, "Expected the original eighteen failures to recover");
        Require(counts.Sum(p => p.Value!["after"]!.I("fixtureExecuted")) == 3 && counts.Sum(p => p.Value!["after"]!.I("stubFixtureExecuted")) == 1, "Execution evidence count");
        return new JsonObject { ["selected"] = 171, ["generatedAndBuilt"] = 171, ["recovered"] = recovered, ["regressions"] = new JsonArray(), ["compilerSourceSha256"] = new JsonObject { ["before"] = hashes["before"].Single(), ["after"] = hashes["after"].Single() }, ["counts"] = counts, ["originalCobolComparison"] = "not-performed" };
    }

    private static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new ArgumentException(message);
        }
    }
}
