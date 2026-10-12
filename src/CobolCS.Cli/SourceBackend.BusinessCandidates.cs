using System.Text;
using System.Text.Json.Nodes;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>既存の型・更新候補から、業務境界を利用者が選ぶための材料を生成します。</summary>
internal sealed partial class SourceBackend
{
    private readonly JsonArray businessOperations = new();

    /// <summary>既存 DTO の構造だけを公開し、業務名を推測しません。</summary>
    /// <param name="symbol">固定宣言。</param>
    /// <returns>直接配線できない場合は null。</returns>
    private JsonNode? BusinessWire(JsonNode symbol)
    {
        if (symbol["pic"] == null)
        {
            if (!FixedLayout(symbol))
            {
                return null;
            }

            var children = new JsonArray();
            foreach (var leaf in LayoutLeaves(symbol))
            {
                var child = BusinessWire(leaf);
                if (child == null)
                {
                    return null;
                }

                child["key"] = "field_" + leaf.S("id");
                children.Add(child);
            }

            return new JsonObject { ["kind"] = "object", ["csharpType"] = ArgumentType(symbol), ["member"] = Member(symbol), ["fields"] = children };
        }

        if (!Known(symbol) || symbol["pic"]!.S("raw").Contains('P'))
        {
            return null;
        }

        var scalar = new JsonObject { ["kind"] = TypeName(symbol) == "Number" ? "number" : "string", ["csharpType"] = TypeName(symbol), ["pic"] = symbol["pic"]!.S("raw"), ["scale"] = symbol["pic"]!.I("scale"), ["member"] = Member(symbol) };
        try
        {
            BusinessComparison.ValidateType(new JsonObject { ["type"] = scalar.S("kind"), ["pic"] = scalar.S("pic") });
        }
        catch (ArgumentException)
        {
            return null;
        }

        return Dimensions(symbol).Length == 0 ? scalar : new JsonObject { ["kind"] = "array", ["csharpType"] = $"IReadOnlyList<{TypeName(symbol)}>", ["count"] = Dimensions(symbol).Aggregate(1, (a, b) => a * b), ["member"] = Member(symbol), ["items"] = scalar };
    }

    /// <summary>実際の外部 record と同じ field 型を保存します。</summary>
    /// <param name="type">生成済みの型。</param>
    /// <param name="label">record の field。</param>
    /// <param name="reference">元 operand。</param>
    /// <returns>型付き投影 metadata。</returns>
    private JsonNode? BusinessExternalWire(string type, string label, JsonNode? reference)
    {
        JsonNode? wire;
        if (reference != null && symbols.TryGetValue(reference.S("symbolId"), out var symbol))
        {
            wire = BusinessWire(symbol);
            if (wire?.S("kind") == "array" && reference.A("subscripts").Any())
            {
                wire = wire["items"]!.DeepClone();
            }
        }
        else
        {
            wire = type is "string" or "Number" or "bool" ? new JsonObject { ["kind"] = type == "Number" ? "number" : type == "bool" ? "boolean" : "string", ["csharpType"] = type, ["scale"] = 0 } : null;
        }

        if (type == "Number" && reference?.S("kind") != "reference")
        {
            if (reference?.S("kind") != "literal" || reference.S("valueType") != "numeric")
            {
                wire = null;
            }
            else if (wire != null)
            {
                wire["scale"] = reference.S("value").Split('.').Skip(1).FirstOrDefault()?.Length ?? 0;
            }
        }

        if (wire != null)
        {
            wire["member"] = label;
            wire["key"] = label;
            wire["symbolId"] = reference?.S("symbolId");
        }

        return wire;
    }

    /// <summary>静的候補をプログラム別に追加し、空の選択用 mapping を用意します。</summary>
    private void BusinessCandidates()
    {
        var statements = Validation.Walk(program["paragraphs"]).Where(s => s.S("id").StartsWith('s') && s["source"] != null).DistinctBy(s => s.S("id")).ToArray();
        var candidates = new JsonArray();
        void Add(JsonNode symbol, string role, string source, string? statementId, string? expression, string reason)
        {
            candidates.Add(new JsonObject
            {
                ["programId"] = program.S("id"),
                ["symbolId"] = symbol.S("id"),
                ["statementId"] = statementId,
                ["originalName"] = symbol.S("qualifiedName"),
                ["source"] = source,
                ["role"] = role,
                ["location"] = symbol["source"]?.DeepClone(),
                ["pic"] = symbol["pic"]?.DeepClone(),
                ["dimensions"] = new JsonArray(Dimensions(symbol).Select(d => (JsonNode?)JsonValue.Create(d)).ToArray()),
                ["wire"] = role == "test-state-injection" && symbol.S("name") == "FILLER" ? null : BusinessWire(symbol),
                ["expression"] = expression,
                ["generatedFile"] = $"Api/{name}Api.g.cs",
                ["reason"] = reason,
                ["copyHistory"] = symbol["source"]?["copyStack"]?.DeepClone(),
                ["generatedType"] = BusinessWire(symbol)?.S("csharpType"),
                ["limitations"] = "Static candidate; business significance and independent expectations require explicit selection. Group field keys are provisional; table order is full declared order.",
            });
        }

        foreach (var argument in program.A("using"))
        {
            if (symbols.TryGetValue(argument.S("symbolId"), out var s))
            {
                Add(s, "formal-input", "arguments", null, Member(s), "PROCEDURE USING fixed argument");
                candidates[^1]!["passingMode"] = argument.S("mode");
                candidates[^1]!["required"] = true;
                candidates[^1]!["missingSelectionReason"] = "missing-formal-argument";
                if (!SupportedUsing())
                {
                    candidates[^1]!["wire"] = null;
                }

                Add(s, "result", "arguments", null, "result.Arguments!." + Member(s), "Updated formal argument snapshot");
            }
        }

        foreach (var statement in statements.Where(s => s.S("kind") == "acceptInput"))
        {
            if (symbols.TryGetValue(statement["target"]!.S("symbolId"), out var s))
            {
                Add(s, "formal-input", "accept", statement.S("id"), null, "ACCEPT target; source order is not necessarily consumption order");
                if (statement.S("inputSource") != "CONSOLE")
                {
                    candidates[^1]!["wire"] = null;
                }
            }
        }

        foreach (var symbol in symbols.Values.Where(s => s.I("level") != 88 && s.S("scope") != "LINKAGE" && s["pic"] != null))
        {
            Add(symbol, "test-state-injection", "initialState", null, Member(symbol), "WORKING-STORAGE injection is test setup, not formal input");
        }

        foreach (var candidate in AssertionCandidates())
        {
            if (symbols.TryGetValue(candidate!.S("symbolId"), out var s))
            {
                Add(s, "result", candidate!.S("assertionKind") == "arguments" ? "argumentsLeaf" : "snapshot", null, candidate!.S("assertionMember"), "Explicit update candidate; FILLER/index/temporary roles require review, names do not establish business significance");
                candidates[^1]!["updates"] = candidate!["updates"]!.DeepClone();
            }
        }

        candidates.Add(new JsonObject { ["programId"] = program.S("id"), ["symbolId"] = string.Empty, ["statementId"] = string.Empty, ["source"] = "display", ["role"] = "result", ["wire"] = new JsonObject { ["kind"] = "array", ["csharpType"] = "IReadOnlyList<string>", ["items"] = new JsonObject { ["kind"] = "string", ["csharpType"] = "string" } }, ["expression"] = "result.Display", ["reason"] = "Ordered DISPLAY capture, not a terminal screen" });
        var blockedExternal = stubs.Where(s => s!.S("programId") == program.S("id") && s!.S("executionRole") is "external-unsupported" or "unknown").ToArray();
        foreach (var stub in blockedExternal)
        {
            businessOperations.Add(new JsonObject { ["statementId"] = stub!.S("statementId"), ["supported"] = false, ["kind"] = stub!["statement"]!.S("kind"), ["source"] = stub["source"]?.DeepClone(), ["limitations"] = stub.S("reason") });
        }

        var row = new JsonObject
        {
            ["blockedExternal"] = blockedExternal.Length,
            ["programHash"] = Generator.Hash(Encoding.UTF8.GetBytes(Generator.Json(program))),
            ["programId"] = program.S("id"),
            ["programKey"] = name,
            ["executorType"] = name + "Executor",
            ["inputType"] = name + "Input",
            ["argumentsType"] = name + "Arguments",
            ["setupType"] = name + "StateSetup",
            ["fakeType"] = name + "ScriptedDependencies",
            ["source"] = (program["source"] ?? program.A("paragraphs").FirstOrDefault()?["source"])?.DeepClone(),
            ["configHash"] = Generator.Hash(Encoding.UTF8.GetBytes(Generator.Json(config))),
            ["candidates"] = candidates,
            ["operations"] = businessOperations.DeepClone(),
            ["acceptStatements"] = new JsonArray(statements.Where(s => s.S("kind") == "acceptInput").Select(s => (JsonNode?)new JsonObject { ["statementId"] = s.S("id"), ["symbolId"] = s["target"]!.S("symbolId"), ["source"] = s["source"]?.DeepClone() }).ToArray()),
            ["acceptModes"] = new JsonArray("fixed-queue", "case-sequence"),
            ["fixedAcceptOrder"] = !statements.Any(s => s.S("kind") is "if" or "evaluate" or "loop" or "perform" or "goto" or "gotoDepending"),
        };
        row["candidateHash"] = Generator.Hash(Encoding.UTF8.GetBytes(Generator.Json(row)));
        var report = files.TryGetValue("reports/business-test-candidates.json", out var prior) ? J.Parse(prior) : new JsonObject { ["schemaVersion"] = 1, ["programs"] = new JsonArray() };
        report["programs"]!.AsArray().Add(row);
        files["reports/business-test-candidates.json"] = Generator.Json(report);
        files["reports/business-test-candidates.md"] = "# Business test candidates\n\nStatic input/update/external metadata, not execution order or business approval. Select explicitly with scaffold-business. Unknown wire layouts cannot be connected automatically. Expectations remain unfilled.\n\n" + string.Join("\n", report.A("programs").Select(p => $"- `{p.S("programId")}`: {p.A("candidates").Count()} candidates, {p.A("operations").Count()} typed operations; mapping `Tests/Templates/Business/{p.S("programKey")}/mapping.json.txt`.")) + "\n";
        files[$"Tests/Templates/Business/{name}/mapping.json.txt"] = Generator.Json(new JsonObject { ["schemaVersion"] = 1, ["programId"] = program.S("id"), ["candidateHash"] = row.S("candidateHash"), ["contractId"] = "UNFILLED-v1", ["version"] = 1, ["selectionReason"] = "UNFILLED: explicitly justify the boundary", ["inputs"] = new JsonArray(), ["results"] = new JsonArray(), ["operations"] = new JsonArray(), ["termination"] = new JsonObject() });
    }
}
