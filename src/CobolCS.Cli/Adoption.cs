using System.Text;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>最新のレビュー、TRX と完了証跡を検査し、独立して試験できる採用先を作ります。</summary>
public static class Adoption
{
    /// <summary>採用レビューの対象内容を返します。期待値を更新する処理ではありません。</summary>
    /// <param name="output">生成先。</param>
    /// <returns>原文、設定、コード、全ケースと観測境界のレビュー対象。</returns>
    public static JsonNode ReviewPayload(string output)
    {
        var context = J.Parse(File.ReadAllText(Path.Combine(output, "Tests/Templates/ReferenceCases/context.json")));
        var inventory = J.Parse(File.ReadAllText(Path.Combine(output, "reports/quality/reference-inventory.json")));
        return new JsonObject { ["context"] = context, ["inventory"] = inventory };
    }

    /// <summary>一意に照合された最新の成功結果だけを採用し、元 IR と CLI を実行依存にしません。</summary>
    /// <param name="output">検査済み生成先。</param>
    /// <param name="destination">新規または空の採用先。</param>
    /// <param name="trxPath">全件成功した MSTest TRX。</param>
    /// <param name="reviewPath">採用内容に結び付いた人の承認。</param>
    /// <returns>コピーしたファイルハッシュと適用範囲を持つ採用結果。</returns>
    public static JsonNode Adopt(string output, string destination, string trxPath, string reviewPath)
    {
        output = Path.GetFullPath(output);
        destination = Path.GetFullPath(destination);
        foreach (var path in new[] { output, destination, trxPath, reviewPath })
        {
            Generator.SafePath(path);
        }

        if (destination == output || destination.StartsWith(output + Path.DirectorySeparatorChar, StringComparison.Ordinal) || output.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.Ordinal) || (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any()))
        {
            throw new IOException("Adoption requires a separate new or empty destination");
        }

        var manifest = Generator.ReadManifest(output);
        Generator.Verify(output, manifest);
        if (!manifest["judgments"]!.B("generated"))
        {
            throw new InvalidOperationException("Cannot adopt a refused generation");
        }

        var payload = ReviewPayload(output);
        var context = payload["context"]!;
        var inventory = payload["inventory"]!;
        if (inventory.A("errors").Any())
        {
            throw new InvalidDataException("Reference case gate is blocked");
        }

        var review = ReferenceCase.Parse(File.ReadAllText(reviewPath));
        FixedExternalResponses.RequireKeys(review, "approved reviewer contentHash");
        var reviewHash = ReferenceCase.ReviewHash(payload);
        if (!review.B("approved") || string.IsNullOrWhiteSpace(review.S("reviewer")) || review.S("contentHash") != reviewHash)
        {
            throw new InvalidDataException("Missing/stale adoption review");
        }

        using var xml = XmlReader.Create(trxPath, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8388608 });
        var trx = XDocument.Load(xml);
        var results = trx.Descendants().Where(e => e.Name.LocalName == "UnitTestResult").ToArray();
        if (results.Length == 0 || results.Any(r => (string?)r.Attribute("outcome") != "Passed" || ((string?)r.Attribute("testName"))?.Contains("NoOp", StringComparison.OrdinalIgnoreCase) == true))
        {
            throw new InvalidDataException("TRX contains zero, failed, skipped or incomplete tests");
        }

        var resultSummary = trx.Descendants().SingleOrDefault(e => e.Name.LocalName == "ResultSummary");
        var counters = resultSummary?.Elements().SingleOrDefault(e => e.Name.LocalName == "Counters");
        if (resultSummary == null || (string?)resultSummary.Attribute("outcome") is not ("Completed" or "Passed") || counters == null || (int?)counters.Attribute("total") != results.Length || (int?)counters.Attribute("passed") != results.Length || (int?)counters.Attribute("executed") != results.Length || counters.Attributes().Any(a => a.Name.LocalName is "failed" or "error" or "timeout" or "aborted" or "inconclusive" or "notRunnable" or "notExecuted" or "disconnected" or "passedButRunAborted" && a.Value != "0"))
        {
            throw new InvalidDataException("TRX run summary is failed/incomplete or has inconsistent counts");
        }

        var definitions = trx.Descendants().Where(e => e.Name.LocalName == "UnitTest").ToArray();
        var expectedTests = inventory.A("cases").Select(c => c.S("testMethod")).Concat(inventory.A("characterization").Select(c => c.S("testMethod"))).Append("ReferenceCaseGate").ToArray();
        if (expectedTests.Length <= 1 || expectedTests.Distinct(StringComparer.Ordinal).Count() != expectedTests.Length)
        {
            throw new InvalidDataException("Zero behavioral cases or ambiguous test mappings");
        }

        string? executionId = null;
        foreach (var method in expectedTests)
        {
            if (method.Contains("NoOp", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("NoOp cannot be adopted");
            }

            var definition = definitions.Where(d => d.Elements().Any(m => m.Name.LocalName == "TestMethod" && (string?)m.Attribute("name") == method && ((string?)m.Attribute("className"))?.Split(',')[0] == "CobolCS.ReferenceTests.ReferenceGenerated")).ToArray();
            if (definition.Length != 1)
            {
                throw new InvalidDataException("Missing/ambiguous TRX method: " + method);
            }

            var matching = results.Where(r => (string?)r.Attribute("testId") == (string?)definition[0].Attribute("id")).ToArray();
            if (matching.Length != 1 || (string?)matching[0].Attribute("testName") != method)
            {
                throw new InvalidDataException("Missing/duplicate TRX result: " + method);
            }

            var markers = matching[0].Descendants().Where(e => e.Name.LocalName == "StdOut").SelectMany(e => e.Value.Split('\n')).Where(l => l.Trim().StartsWith("COBOLCS_QUALITY_EXECUTION=", StringComparison.Ordinal)).Select(l => l.Trim()["COBOLCS_QUALITY_EXECUTION=".Length..]).ToArray();
            if (markers.Length != 1 || !Guid.TryParseExact(markers[0], "N", out _) || (executionId != null && markers[0] != executionId))
            {
                throw new InvalidDataException("TRX execution provenance missing/ambiguous");
            }

            executionId = markers[0];
        }

        var starts = Directory.GetFiles(Path.Combine(output, "TestResults/quality"), "started.json", SearchOption.AllDirectories).Select(path => (Path: path, Started: J.Parse(File.ReadAllText(path)))).OrderByDescending(p => DateTimeOffset.Parse(p.Started.S("startedAt"), System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        if (starts.Length == 0 || Path.GetFileName(Path.GetDirectoryName(starts[0].Path)) != executionId)
        {
            throw new InvalidDataException("A newer verification is incomplete or does not match TRX");
        }

        var summaries = Directory.GetFiles(Path.Combine(output, "TestResults/quality"), "summary.json", SearchOption.AllDirectories).Select(path => (Path: path, Result: J.Parse(File.ReadAllText(path)))).Where(p => p.Result.B("completed")).OrderByDescending(p => DateTimeOffset.Parse(p.Result.S("completedAt"), System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        if (summaries.Length == 0 || summaries[0].Result.S("executionId") != executionId)
        {
            throw new InvalidDataException("TRX is not the latest completed verification");
        }

        var proof = summaries[0].Result;
        if (proof.S("status") is not ("matched" or "not-configured") || !JsonNode.DeepEquals(proof["context"], context) || !JsonNode.DeepEquals(proof["caseInventory"], inventory) || !JsonNode.DeepEquals(proof["executedCodeArtifacts"], context["codeArtifacts"]) || proof.I("unexecuted") != 0 || proof.I("infrastructureFailures") != 0 || proof.S("compiledSourceFingerprint") != context.S("compiledSourceFingerprint"))
        {
            throw new InvalidDataException("Failed/stale/incomplete quality evidence");
        }

        foreach (var item in inventory.A("cases"))
        {
            var path = Path.Combine(output, "Tests/ReferenceCases", item.S("path"));
            if (!File.Exists(path) || Generator.Hash(File.ReadAllBytes(path)) != item.S("hash"))
            {
                throw new InvalidDataException("Missing/stale reference case: " + item.S("caseId"));
            }

            ReferenceCase.Validate(ReferenceCase.Parse(File.ReadAllText(path)), context, Path.Combine(output, "Tests/ReferenceCases"));
            var records = proof.A("cases").Where(c => c.S("caseId") == item.S("caseId")).ToArray();
            if (records.Length != 1 || records[0].S("status") != "matched" || records[0].S("caseHash") != item.S("hash"))
            {
                throw new InvalidDataException("Missing/failed case proof: " + item.S("caseId"));
            }
        }

        foreach (var (name, hash) in context["contractFiles"]!.AsObject())
        {
            var path = Path.Combine(output, "Tests/Contracts", name);
            if (!File.Exists(path) || Generator.Hash(File.ReadAllBytes(path)) != hash!.GetValue<string>())
            {
                throw new InvalidDataException("Stale business contract");
            }
        }

        var copy = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (name, _) in manifest["artifacts"]!.AsObject())
        {
            if (!name.StartsWith("reports/", StringComparison.Ordinal) && !name.StartsWith("Tests/Templates/", StringComparison.Ordinal))
            {
                copy[name] = File.ReadAllBytes(Path.Combine(output, name));
            }
        }

        foreach (var directory in new[] { "Tests/ReferenceCases", "Tests/Contracts", "Tests/Reviews" })
        {
            var root = Path.Combine(output, directory);
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var path in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                Generator.SafePath(path);
                copy[Path.GetRelativePath(output, path).Replace('\\', '/')] = File.ReadAllBytes(path);
            }
        }

        copy["Tests/Reviews/adoption.json"] = Encoding.UTF8.GetBytes(Generator.Json(review));
        var adopted = new JsonObject { ["schemaVersion"] = "1.0", ["reviewHash"] = reviewHash, ["reviewer"] = review.S("reviewer"), ["executionId"] = executionId, ["trxHash"] = Generator.Hash(File.ReadAllBytes(trxPath)), ["independentVerification"] = inventory.A("cases").Any(c => c.S("origin") != "csharp-characterization") ? "tested-registered-cases-only" : "not-configured", ["equivalence"] = "not-established", ["context"] = context.DeepClone(), ["proof"] = proof.DeepClone(), ["artifacts"] = new JsonObject(copy.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)JsonValue.Create(Generator.Hash(p.Value))))) };
        copy["adoption.json"] = Encoding.UTF8.GetBytes(Generator.Json(adopted));
        var stage = destination + ".stage-" + Guid.NewGuid().ToString("N");
        try
        {
            foreach (var (name, content) in copy)
            {
                var path = Path.Combine(stage, name);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, content);
            }

            Generator.Verify(output, manifest);
            foreach (var (name, content) in copy.Where(p => p.Key != "adoption.json" && p.Key != "Tests/Reviews/adoption.json"))
            {
                if (!File.ReadAllBytes(Path.Combine(output, name)).SequenceEqual(content))
                {
                    throw new IOException("Concurrent edit during adoption: " + name);
                }
            }

            if (Directory.Exists(destination))
            {
                Directory.Delete(destination);
            }

            Directory.Move(stage, destination);
        }
        finally
        {
            if (Directory.Exists(stage))
            {
                Directory.Delete(stage, recursive: true);
            }
        }

        return adopted;
    }
}
