using System.Text.Json;
using System.Text.Json.Nodes;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>frontend の消費台帳から IR と生成命令への対応を検査します。</summary>
internal static class TranslationQuality
{
    /// <summary>原文を基準にした状態と対応欠落の作業項目を生成ファイルへ追加します。</summary>
    /// <param name="ir">解析結果。</param>
    /// <param name="diagnostics">frontend と backend の診断。</param>
    /// <param name="files">生成物。生成範囲はこの内容から取得します。</param>
    /// <param name="generated">実行プロジェクトを生成したかどうか。</param>
    internal static void Add(JsonNode ir, JsonArray diagnostics, IDictionary<string, string> files, bool generated)
    {
        var items = new JsonArray();
        var work = new JsonArray();
        var sources = ir.A("sources").ToDictionary(s => s.S("path"));
        var unparsed = 0;
        var index = 0;
        JsonObject Item(string? program, string id, string type, JsonNode? source, string status, string reason)
        {
            var diagnosticIds = diagnostics.Where(d => d != null && Related(source, d["source"])).Select(d => (JsonNode?)JsonValue.Create(d!.S("code"))).ToArray();
            var entry = new JsonObject
            {
                ["schemaVersion"] = "1.0",
                ["programId"] = program,
                [type == "statement" ? "statementId" : "regionId"] = id,
                ["regionType"] = type,
                ["source"] = source?.DeepClone(),
                ["copyExpansionPath"] = source?["copyStack"]?.DeepClone() ?? new JsonArray(),
                ["sourceHash"] = source != null && sources.TryGetValue(source.S("file"), out var original) ? original.S("sha256") : null,
                ["status"] = status,
                ["reasonCode"] = reason,
                ["diagnosticIds"] = new JsonArray(diagnosticIds),
                ["irReferences"] = new JsonArray(),
                ["generatedRanges"] = new JsonArray(),
            };
            items.Add(entry);
            if (status != "translated")
            {
                work.Add(new JsonObject { ["programId"] = program, ["statementId"] = type == "statement" ? id : null, ["regionId"] = type != "statement" ? id : null, ["ruleId"] = reason, ["diagnosticIds"] = entry["diagnosticIds"]!.DeepClone(), ["status"] = "open", ["source"] = source?.DeepClone() });
            }

            return entry;
        }

        foreach (var program in ir.A("programs"))
        {
            var programId = program.S("id");
            var statements = Validation.Walk(program["paragraphs"]).Where(n => n.S("id").StartsWith('s') && n["kind"] != null).ToArray();
            var ids = statements.Select(s => s.S("id")).ToHashSet(StringComparer.Ordinal);
            foreach (var duplicate in statements.GroupBy(s => s.S("id")).Where(g => g.Count() > 1))
            {
                Item(programId, duplicate.Key, "unparsed-region", duplicate.First()["source"], "untranslated", "duplicate-statement-id");
                unparsed++;
            }

            var file = $"Programs/Program{index:D4}.cs";
            var lines = files.TryGetValue(file, out var content) ? content.Split('\n') : [];
            foreach (var statement in statements)
            {
                var kind = statement.S("kind");
                var omitted = kind is "continue" or "sentenceBoundary" or "sequence";
                var children = Validation.Walk(statement).Where(n => n["kind"] != null).ToArray();
                var unknown = children.Any(c => c.S("kind") == "unknown");
                var external = children.Any(c => c.S("kind") is "external");
                var diagnosed = diagnostics.Any(d => d != null && d.S("severity") == "error" && (d["source"] == null || Related(statement["source"], d["source"])));
                var status = unknown ? "untranslated" : external || children.Any(c => c.S("kind") is "controlStub" or "controlTarget") ? "stubbed" : diagnosed || !generated ? "untranslated" : diagnostics.Any(d => d != null && d.S("severity") == "review") ? "needs-verification" : "translated";
                var reason = unknown ? "unparsed-region" : external ? "external-adapter-unimplemented" : status == "stubbed" ? "internal-unimplemented" : omitted ? "grammar-" + kind : status == "untranslated" ? "generation-blocked" : "emitted-instruction";
                var entry = Item(programId, statement.S("id"), kind == "sentenceBoundary" ? "non-executable" : "statement", statement["source"], status, reason);
                entry["irReferences"]!.AsArray().Add(programId + "/" + statement.S("id"));
                if (unknown)
                {
                    unparsed++;
                }

                for (var line = 0; line < lines.Length; line++)
                {
                    if (lines[line].TrimEnd().EndsWith(", " + JsonSerializer.Serialize(statement.S("id")) + "),", StringComparison.Ordinal))
                    {
                        entry["generatedRanges"]!.AsArray().Add(new JsonObject { ["file"] = file, ["member"] = $"Program{index:D4}.Run", ["startLine"] = line + 1, ["endLine"] = line + 1 });
                    }
                }

                if (status == "translated" && !omitted && entry.A("generatedRanges").Count() == 0)
                {
                    entry["status"] = "untranslated";
                    entry["reasonCode"] = "missing-generated-mapping";
                    work.Add(new JsonObject { ["programId"] = programId, ["statementId"] = statement.S("id"), ["ruleId"] = "missing-generated-mapping", ["status"] = "open" });
                }
            }

            foreach (var token in program.A("tokens"))
            {
                var owner = token.S("ownerKind");
                if (owner == "declaration" && token.S("ownerId") == "$structure")
                {
                    var unresolved = diagnostics.Any(d => d != null && d.S("severity") == "error" && Related(token["source"], d["source"]));
                    Item(programId, "r" + items.Count, "non-executable", token["source"], unresolved ? "needs-verification" : "translated", unresolved ? "diagnosed-structure" : "grammar-structure-token");
                }

                if (owner is "statement" or "stubbed" && !ids.Contains(token.S("ownerId")))
                {
                    Item(programId, "r" + items.Count, "unparsed-region", token["source"], "untranslated", "missing-ir-owner");
                    unparsed++;
                }
                else if (owner == "unknown" && !ids.Contains(token.S("ownerId")))
                {
                    Item(programId, "r" + items.Count, "unparsed-region", token["source"], "untranslated", "unconsumed-token");
                    unparsed++;
                }
            }

            foreach (var symbol in program.A("symbols"))
            {
                var unresolved = symbol.B("stub") || diagnostics.Any(d => d != null && d.S("severity") == "error" && Related(symbol["source"], d["source"]));
                var entry = Item(programId, symbol.S("id"), "data-definition", symbol["source"], unresolved ? "needs-verification" : "translated", unresolved ? "unresolved-data-layout" : "declared-logical-storage");
                entry["symbolId"] = symbol.S("id");
                entry["irReferences"]!.AsArray().Add(programId + "/" + symbol.S("id"));
            }

            index++;
        }

        foreach (var token in ir.A("rejectedTokens"))
        {
            Item(null, "r" + items.Count, "unparsed-region", token["source"], "untranslated", "parse-rejected");
            unparsed++;
        }

        foreach (var expansion in ir.A("copyExpansions"))
        {
            var entry = Item(null, expansion.S("expansionId"), "copy-expansion", expansion["source"], expansion.S("status") == "expanded" ? "translated" : "needs-verification", "copy-" + expansion.S("status"));
            entry["expansion"] = expansion.DeepClone();
        }

        foreach (var excluded in ir.A("excluded"))
        {
            Item(null, "r" + items.Count, "non-executable", excluded["source"], "translated", excluded.S("reason"));
        }

        foreach (var diagnostic in diagnostics.Where(d => d != null && (d.S("code").StartsWith("COPY_", StringComparison.Ordinal) || d.S("code") is "INPUT_DECODE" or "INPUT_READ" or "EMPTY_SOURCE")))
        {
            Item(null, "r" + items.Count, "unparsed-region", diagnostic!["source"], "untranslated", diagnostic.S("code"));
            unparsed++;
        }

        foreach (var item in items)
        {
            var source = item!["source"];
            if (source != null && sources.TryGetValue(source.S("file"), out var original))
            {
                var start = source["start"]?.I("byteOffset") ?? -1;
                var end = source["end"]?.I("byteOffset") ?? -1;
                if (start < 0 || end < start || end > original.S("bytesHex").Length / 2)
                {
                    item["status"] = "untranslated";
                    item["reasonCode"] = "source-range-out-of-bounds";
                    unparsed++;
                    work.Add(new JsonObject { ["regionId"] = item["regionId"]?.DeepClone(), ["statementId"] = item["statementId"]?.DeepClone(), ["ruleId"] = "source-range-out-of-bounds", ["status"] = "open" });
                }
            }
        }

        var counts = new JsonObject();
        foreach (var state in new[] { "translated", "needs-verification", "stubbed", "untranslated" })
        {
            counts[state] = items.Count(i => i!.S("regionType") == "statement" && i!.S("status") == state);
        }

        var summary = new JsonObject
        {
            ["schemaVersion"] = "1.0",
            ["phase"] = "generation",
            ["statementCounts"] = counts,
            ["unparsedRegions"] = unparsed,
            ["mappingFailures"] = items.Count(i => i!.S("reasonCode") is "missing-generated-mapping" or "missing-ir-owner" or "duplicate-statement-id" or "source-range-out-of-bounds"),
            ["statementDenominatorComplete"] = unparsed == 0,
            ["missingCopies"] = diagnostics.Count(d => d!.S("code") is "COPY_MISSING" or "COPY_MISSING_STUB"),
            ["unresolvedDataDefinitions"] = items.Count(i => i!.S("regionType") == "data-definition" && i!.S("status") != "translated"),
            ["independentVerification"] = "not-configured",
            ["execution"] = "not-run",
            ["branchCoverage"] = "not-measured",
            ["physicalRecordCompatibility"] = "not-verified",
            ["externalImplementation"] = "not-verified",
            ["equivalence"] = "not-established",
            ["limitations"] = new JsonArray("Adoption requires explicit reviewed configuration and latest complete TRX evidence", "Real external services are unverified; only ordered fixed responses are replayed", "Adapter byte comparison does not implement PIC physical record decoding"),
        };
        files["reports/quality/translation-ledger.json"] = Generator.Json(new JsonObject { ["schemaVersion"] = "1.0", ["sources"] = new JsonArray(sources.Values.Select(s => (JsonNode?)new JsonObject { ["path"] = s.S("path"), ["sha256"] = s.S("sha256"), ["encoding"] = s.S("encoding") }).ToArray()), ["configHash"] = ir.S("configHash"), ["config"] = ir["config"]!.DeepClone(), ["items"] = items });
        files["reports/quality/summary.json"] = Generator.Json(summary);
        files["reports/quality/work-items.json"] = Generator.Json(work);
    }

    private static bool Related(JsonNode? left, JsonNode? right)
    {
        if (left == null || right == null || left.S("file") != right.S("file") || !JsonNode.DeepEquals(left["copyStack"], right["copyStack"]))
        {
            return false;
        }

        return left["start"]!.I("byteOffset") <= right["end"]!.I("byteOffset") && right["start"]!.I("byteOffset") <= left["end"]!.I("byteOffset");
    }
}
