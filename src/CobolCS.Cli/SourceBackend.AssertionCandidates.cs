using System.Text;
using System.Text.Json.Nodes;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>IR の更新先から期待値記入用の静的候補を作ります。</summary>
internal sealed partial class SourceBackend
{
    /// <summary>今回の実行経路を推測せず、更新可能な基本項目と不確定な領域を列挙します。</summary>
    /// <returns>symbolId 順の候補。更新元は全て保持します。</returns>
    private JsonArray AssertionCandidates()
    {
        var candidates = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        void Add(JsonNode? reference, JsonNode statement, string reason)
        {
            var id = reference?.S("symbolId") ?? string.Empty;
            symbols.TryGetValue(id, out var root);
            if (root?.I("level") == 88)
            {
                symbols.TryGetValue(root.S("parentId"), out root);
            }

            var allLeaves = root == null ? Array.Empty<JsonNode>() : LayoutLeaves(root).ToArray();
            var leaves = allLeaves.Where(s => s.S("name") != "FILLER").ToArray();
            if (allLeaves.Length > 0 && leaves.Length == 0)
            {
                return;
            }

            foreach (var leaf in leaves.Length == 0 ? new JsonNode?[] { root } : leaves)
            {
                var symbolId = leaf?.S("id") ?? (id.Length == 0 ? null : id);
                var key = symbolId ?? "unknown:" + statement.S("id");
                if (!candidates.TryGetValue(key, out var candidate))
                {
                    var argument = leaf == null ? null : program.A("using").Where(a => symbols.ContainsKey(a.S("symbolId"))).Select(a => symbols[a.S("symbolId")]).FirstOrDefault(a => FixedLayout(a) && Ancestors(leaf).Any(p => p.S("id") == a.S("id")));
                    var known = leaf != null && Known(leaf);
                    var member = !known ? null : argument == null ? "result.Snapshot." + Member(leaf!) : "result.Arguments." + Member(argument) + (argument.S("id") == symbolId ? string.Empty : "." + Member(leaf!));
                    candidate = new JsonObject
                    {
                        ["programId"] = program.S("id"),
                        ["symbolId"] = symbolId,
                        ["name"] = leaf?.S("qualifiedName"),
                        ["pic"] = leaf?["pic"]?.DeepClone(),
                        ["type"] = known ? TypeName(leaf!) : null,
                        ["dimensions"] = leaf == null ? null : new JsonArray(Dimensions(leaf).Select(d => (JsonNode?)JsonValue.Create(d)).ToArray()),
                        ["updates"] = new JsonArray(),
                        ["assertionMember"] = member,
                        ["assertionKind"] = argument == null ? "snapshot" : "arguments",
                        ["testFile"] = $"Tests/Cases/{name}Tests.cs",
                        ["testReference"] = name + "Tests." + name + "_Sample",
                        ["status"] = known ? "generated-template" : "manual-unknown",
                    };
                    candidates.Add(key, candidate);
                }

                var updates = candidate["updates"]!.AsArray();
                if (!updates.Any(u => u!.S("statementId") == statement.S("id") && u!.S("reason") == reason))
                {
                    updates.Add(new JsonObject { ["statementId"] = statement.S("id"), ["source"] = statement["source"] == null ? null : new JsonObject { ["file"] = statement["source"]!["file"]?.DeepClone(), ["start"] = statement["source"]!["start"]?.DeepClone(), ["end"] = statement["source"]!["end"]?.DeepClone() }, ["reason"] = reason });
                }
            }
        }

        foreach (var statement in Validation.Walk(program["paragraphs"]).Where(s => s.S("id") != string.Empty && s.S("kind") != string.Empty).DistinctBy(s => s.S("id")))
        {
            var kind = statement.S("kind");
            if (kind is "move" or "initialize" or "setIndex" or "arithmetic")
            {
                foreach (var target in statement.A("targets"))
                {
                    Add(kind == "arithmetic" ? target["reference"] : target, statement, kind);
                }

                if (statement["remainder"] is JsonNode remainder)
                {
                    Add(remainder, statement, "arithmetic remainder");
                }
            }
            else if (kind is "acceptInput" or "stringTransfer")
            {
                Add(statement["target"], statement, kind);
                if (statement["pointer"] is JsonNode pointer)
                {
                    Add(pointer, statement, "STRING POINTER");
                }
            }
            else if (kind == "set88")
            {
                Add(statement, statement, "SET condition parent");
            }
            else if (kind == "loop")
            {
                foreach (var varying in statement.A("varyingAfter").Concat(statement["varying"] is JsonNode first ? new[] { first } : []))
                {
                    Add(varying["reference"], statement, "PERFORM VARYING");
                }
            }
            else if (kind == "external")
            {
                var dependency = program.A("dependencies").FirstOrDefault(d => d.S("id") == statement.S("dependencyId"));
                if (dependency == null)
                {
                    Add(null, statement, "Unresolved external update region");
                    continue;
                }

                if (dependency.S("kind") == "call")
                {
                    var modes = dependency.A("modes").ToArray();
                    var inputs = dependency.A("inputs").ToArray();
                    for (var i = 0; i < inputs.Length; i++)
                    {
                        if (i >= modes.Length || modes[i].GetValue<string>() == "reference")
                        {
                            Add(inputs[i], statement, "CALL BY REFERENCE");
                        }
                    }

                    if (dependency["call"]?["returning"] is JsonNode returning)
                    {
                        Add(returning, statement, "CALL RETURNING");
                    }
                }
                else
                {
                    foreach (var output in dependency.A("outputs"))
                    {
                        Add(output, statement, dependency.S("kind") + " output");
                    }

                    if (dependency.S("kind") == "file" && statement.S("operation").StartsWith("read", StringComparison.Ordinal))
                    {
                        Add(new JsonObject { ["symbolId"] = dependency.S("recordId") }, statement, "FILE READ record");
                        if (statement["transfer"] is JsonNode transfer)
                        {
                            Add(transfer, statement, "FILE transfer");
                        }
                    }

                    foreach (var binding in dependency["sql"]?.A("bindings") ?? [])
                    {
                        if (binding.S("direction") == "output")
                        {
                            Add(binding["host"], statement, "SQL output host");
                            if (binding["indicator"] is JsonNode indicator)
                            {
                                Add(indicator, statement, "SQL output indicator");
                            }
                        }
                    }

                    if (dependency["sql"]?["sqlstate"] is JsonNode sqlstate)
                    {
                        Add(sqlstate, statement, "SQLSTATE");
                    }
                }

                if (dependency["status"] is JsonNode status)
                {
                    Add(status, statement, dependency.S("kind") + " status");
                }
            }
            else if (kind is not ("display" or "if" or "evaluate" or "sequence" or "halt" or "continue" or "sentenceBoundary" or "exitPerform" or "perform" or "goto" or "gotoDepending" or "exitParagraph" or "exitSection" or "nextSentence"))
            {
                foreach (var target in statement.A("targets"))
                {
                    Add(target["reference"] ?? target, statement, "Explicit unsupported update target");
                }

                foreach (var varying in statement["perform"]?.A("varying") ?? [])
                {
                    Add(varying["reference"], statement, "Unsupported PERFORM VARYING target");
                }

                if (statement["target"] is JsonNode targetReference)
                {
                    Add(targetReference, statement, "Explicit unsupported update target");
                }

                Add(null, statement, "Unsupported statement: read/write region requires manual inspection");
            }
        }

        return new JsonArray(candidates.Values.Select(c => (JsonNode?)c).ToArray());
    }

    /// <summary>既知の更新項目だけに期待値記入用の比較を作ります。</summary>
    /// <param name="test">生成テスト。</param>
    /// <param name="candidates">静的候補。</param>
    private void AppendCandidateAssertions(StringBuilder test, JsonArray candidates)
    {
        foreach (var candidate in candidates.Where(c => c!.S("status") == "generated-template" && c!.S("assertionKind") == "snapshot"))
        {
            var symbol = symbols[candidate!.S("symbolId")];
            var actual = candidate!.S("assertionMember");
            var expected = $"Preparation.Required<{TypeName(symbol)}{(Dimensions(symbol).Length == 0 ? string.Empty : "[]")}>({Quote("expected " + symbol.S("qualifiedName"))})";
            test.AppendLine(Dimensions(symbol).Length == 0 ? $"Assert.AreEqual({expected}, {actual});" : $"CollectionAssert.AreEqual({expected}, {actual}.ToArray());\n\n// For a selected element, replace the array comparison with Assert.AreEqual(expectedElement, {actual}[zeroBasedIndex]);");
        }
    }
}
