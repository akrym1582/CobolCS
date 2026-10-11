using System.Text;
using System.Text.Json.Nodes;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>公開境界で実行ごとの状態と入力・結果を分離します。</summary>
internal sealed partial class SourceBackend
{
    /// <summary>型付き入力、結果、fixture 変換を生成します。</summary>
    private void Api()
    {
        var leaves = symbols.Values.Where(s => s.I("level") != 88 && s.S("name") != "FILLER" && (s["pic"] != null || s.B("stub"))).ToArray();
        foreach (var symbol in leaves)
        {
            var mapped = sourceMap.OfType<JsonObject>().SingleOrDefault(m => m.S("programId") == program.S("id") && m.S("symbolId") == symbol.S("id"));
            if (mapped == null)
            {
                mapped = new JsonObject { ["programId"] = program.S("id"), ["symbolId"] = symbol.S("id"), ["originalName"] = symbol.S("qualifiedName"), ["generatedName"] = name + "Snapshot." + Member(symbol) };
                sourceMap.Add(mapped);
            }

            mapped["publicSetupMember"] = Known(symbol) ? name + "StateSetup." + Member(symbol) : null;
            mapped["publicSnapshotMember"] = name + "Snapshot." + Member(symbol);
            mapped["pic"] = symbol["pic"]?.DeepClone();
        }

        var api = new StringBuilder(Header());
        api.AppendLine($"public sealed class {name}StateSetup\n{{");
        foreach (var symbol in leaves.Where(Known))
        {
            var type = TypeName(symbol);
            var member = Member(symbol);
            api.AppendLine(Dimensions(symbol).Length == 0 ? $"public Optional<{type}> {member} {{ get; init; }}" : $"public IReadOnlyList<TableEntry<{type}>> {member} {{ get; init; }} = Array.Empty<TableEntry<{type}>>();");
        }

        foreach (var group in PublicGroups())
        {
            api.AppendLine($"public {name}Group{group.S("id")}Setup? {Member(group)} {{ get; init; }}");
        }

        api.AppendLine($"}}\npublic sealed class {name}Input\n{{\npublic IReadOnlyList<string> AcceptInputs {{ get; init; }} = Array.Empty<string>();\npublic {name}StateSetup InitialState {{ get; init; }} = new();\n}}");
        api.AppendLine($"public sealed class {name}Snapshot\n{{");
        foreach (var symbol in leaves)
        {
            var type = TypeName(symbol);
            api.AppendLine($"public {(!Known(symbol) || Dimensions(symbol).Length == 0 ? type : $"IReadOnlyList<{type}>")} {Member(symbol)} {{ get; }}");
        }

        foreach (var group in PublicGroups())
        {
            api.AppendLine($"public {name}Group{group.S("id")}Snapshot {Member(group)} {{ get; }}");
        }

        api.AppendLine($"internal {name}Snapshot({name}State state)\n{{");
        foreach (var symbol in leaves)
        {
            api.AppendLine($"{Member(symbol)} = {(!Known(symbol) ? $"new UnknownCobolValue({Quote(symbol.S("qualifiedName"))})" : Dimensions(symbol).Length == 0 ? Access(symbol, []) : $"Array.AsReadOnly({Elements(symbol, a => a)}.ToArray())")};");
        }

        foreach (var group in PublicGroups())
        {
            api.AppendLine($"{Member(group)} = new(state);");
        }

        api.AppendLine($"}}\n}}\npublic sealed record {name}Result({name}Snapshot Snapshot, IReadOnlyList<string> Display, IReadOnlyList<DependencyTrace> DependencyTrace, TerminationKind Termination, CallStubMode CallStubMode)\n{{\npublic bool HasNoOpCalls => DependencyTrace.Any(t => t.Implementation == \"generated-call-no-op\");\npublic IReadOnlyList<DependencyTrace> NoOpCalls => Array.AsReadOnly(DependencyTrace.Where(t => t.Implementation == \"generated-call-no-op\").ToArray());\npublic IReadOnlyList<string> Diagnostics => Array.AsReadOnly(new[] {{ \"WORKING-STORAGE setup is for testing; LINKAGE/PROCEDURE USING and original COBOL equivalence are not established.\" }});\n}}");
        api.AppendLine($"public interface I{name}Executor\n{{\npublic {name}Result Run({name}Input input, I{name}ExternalDependencies? external = null, ExecutionOptions? options = null, CancellationToken cancellationToken = default);\n}}\npublic sealed class {name}Executor : I{name}Executor\n{{\npublic {name}Result Run({name}Input input, I{name}ExternalDependencies? external = null, ExecutionOptions? options = null, CancellationToken cancellationToken = default)\n{{\nArgumentNullException.ThrowIfNull(input);\noptions ??= new ExecutionOptions();\noptions.Validate();\ncancellationToken.ThrowIfCancellationRequested();\nreturn new {name}Program(new {name}Router(external, options), options, cancellationToken).Run(input);\n}}\n}}");
        api.Append(GroupDtos());
        files[$"Api/{name}Api.g.cs"] = api.ToString();
        sourceMap.Add(new JsonObject { ["programId"] = program.S("id"), ["executorType"] = name + "Executor", ["inputType"] = name + "Input", ["snapshotType"] = name + "Snapshot" });

        var adapter = new StringBuilder(Header());
        adapter.AppendLine($"internal static class {name}FixtureAdapter\n{{\ninternal static {name}Input Parse(JsonNode? fixture)\n{{\nvar state = new {name}StateSetup();\nvar inputs = new List<string>();");
        foreach (var symbol in leaves.Where(Known))
        {
            adapter.AppendLine(Dimensions(symbol).Length == 0 ? $"Optional<{TypeName(symbol)}> {Member(symbol)} = default;" : $"var {Member(symbol)} = new List<TableEntry<{TypeName(symbol)}>>();");
        }

        adapter.AppendLine("if (fixture != null)\n{\nforeach (var property in fixture.AsObject())\n{\nif (property.Key is not (\"fields\" or \"tableFields\" or \"inputs\") && !(property.Key == \"calls\" && property.Value is JsonArray { Count: 0 }) && !(property.Key == \"files\" && property.Value is JsonObject { Count: 0 })) throw new NotSupportedException(\"Unsupported fixture property: \" + property.Key);\n}\nif (fixture[\"inputs\"] is JsonArray accept) inputs.AddRange(accept.Select(v => v!.GetValue<string>()));\nelse if (fixture[\"inputs\"] != null) throw new ArgumentException(\"Invalid fixture inputs\");\nvoid Assign(string field, int[] indices, string value)\n{\nswitch (field)\n{");
        foreach (var symbol in leaves.Where(Known))
        {
            if (symbols.Values.Count(s => s.S("qualifiedName").Equals(symbol.S("qualifiedName"), StringComparison.OrdinalIgnoreCase)) > 1)
            {
                continue;
            }

            adapter.AppendLine($"case {Quote(symbol.S("qualifiedName"))}:");
            if (symbol.S("name") != symbol.S("qualifiedName") && symbols.Values.Count(s => s.S("name") == symbol.S("name")) == 1)
            {
                adapter.AppendLine($"case {Quote(symbol.S("name"))}:");
            }

            var member = Member(symbol);
            var value = TypeName(symbol) == "Number" ? "CobolValue.Numeric(value)" : "value";
            adapter.AppendLine($"if (indices.Length != {Dimensions(symbol).Length}) throw new ArgumentException(\"Invalid fixture subscript\");");
            adapter.AppendLine(Dimensions(symbol).Length == 0 ? $"if ({member}.HasValue) throw new ArgumentException(\"Duplicate fixture field\");\n{member} = {value};\nbreak;" : $"{member}.Add(new(indices, {value}));\nbreak;");
        }

        adapter.AppendLine("default: throw new ArgumentException(\"Unknown or ambiguous fixture field \" + field);\n}\n}\nif (fixture[\"fields\"] is JsonObject fields) foreach (var (field, value) in fields) Assign(field, [], value!.GetValue<string>());\nelse if (fixture[\"fields\"] != null) throw new ArgumentException(\"Invalid fixture fields\");\nif (fixture[\"tableFields\"] is JsonArray entries) foreach (var entry in entries) Assign(entry![\"name\"]!.GetValue<string>(), entry[\"subscripts\"]!.AsArray().Select(v => v!.GetValue<int>()).ToArray(), entry[\"value\"]!.GetValue<string>());\nelse if (fixture[\"tableFields\"] != null) throw new ArgumentException(\"Invalid fixture tableFields\");\n}\nreturn new() { AcceptInputs = inputs, InitialState = new() {");
        foreach (var symbol in leaves.Where(Known))
        {
            adapter.AppendLine($"{Member(symbol)} = {Member(symbol)},");
        }

        adapter.AppendLine($"}} }};\n}}\ninternal static JsonObject Serialize({name}Result result)\n{{\nvar fields = new JsonObject();");
        foreach (var symbol in leaves.Where(Known))
        {
            var key = symbol.S("qualifiedName");
            if (symbols.Values.Count(s => s.S("qualifiedName").Equals(key, StringComparison.OrdinalIgnoreCase)) > 1)
            {
                key += "#" + symbol.S("id");
            }

            string Format(string value) => TypeName(symbol) == "Number" ? value + $".Decimal({symbol["pic"]!.I("scale")})" : value;
            var access = "result.Snapshot." + Member(symbol);
            adapter.AppendLine($"fields[{Quote(key)}] = {(Dimensions(symbol).Length == 0 ? Format(access) : "new JsonArray(" + access + ".Select(v => (JsonNode?)JsonValue.Create(" + Format("v") + ")).ToArray())")};");
        }

        adapter.AppendLine("var json = new JsonObject { [\"display\"] = new JsonArray(result.Display.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray()), [\"fields\"] = fields, [\"signal\"] = result.Termination switch { TerminationKind.Goback => \"goback\", TerminationKind.StopRun => \"stopRun\", TerminationKind.ExitProgram => \"exitProgram\", _ => \"fallthrough\" } };\nif (result.DependencyTrace.Count != 0) json[\"dependencies\"] = new JsonArray(result.DependencyTrace.Select(t => (JsonNode?)new JsonObject { [\"dependencyId\"] = t.Source.DependencyId, [\"statementId\"] = t.Source.StatementId, [\"operation\"] = t.Source.Operation, [\"status\"] = t.StatusCode, [\"outcome\"] = t.Outcome.ToString(), [\"implementation\"] = t.Implementation }).ToArray());\nif (result.CallStubMode != CallStubMode.Throw || result.HasNoOpCalls) json[\"metadata\"] = new JsonObject { [\"callStubMode\"] = result.CallStubMode.ToString(), [\"hasNoOpCalls\"] = result.HasNoOpCalls, [\"noOpCalls\"] = System.Text.Json.JsonSerializer.SerializeToNode(result.NoOpCalls, RunnerSerialization.Options), [\"warning\"] = \"Exploration only; normal return and provisional RETURNING may take incorrect business branches.\" };\nreturn json;\n}\n}");
        files[$"Api/{name}FixtureAdapter.g.cs"] = adapter.ToString();
    }

    /// <summary>新規状態へ PIC 検査付きで試験入力を反映します。</summary>
    /// <returns>JSON を必要としない入力反映メソッド。</returns>
    private string ApplyInput()
    {
        var code = new StringBuilder($"private void ApplyInput({name}Input value)\n{{\nArgumentNullException.ThrowIfNull(value.InitialState);\nArgumentNullException.ThrowIfNull(value.AcceptInputs);\nforeach (var item in value.AcceptInputs.ToArray()) input.Enqueue(item ?? throw new ArgumentException(\"Null ACCEPT input\"));\nvar setup = value.InitialState;\n");
        foreach (var symbol in symbols.Values.Where(s => s.I("level") != 88 && s.S("name") != "FILLER" && Known(s)))
        {
            var dims = Dimensions(symbol);
            var member = "setup." + Member(symbol);
            var root = Ancestors(symbol).First();
            if (PublicGroups().Any(g => g.S("id") == root.S("id")))
            {
                var selected = "selected_" + symbol.S("id");
                var groupMember = "setup." + Member(root) + "." + Member(symbol);
                code.AppendLine($"var {selected} = {member};\nif (setup.{Member(root)} != null)\n{{");
                if (dims.Length == 0)
                {
                    code.AppendLine($"if ({selected}.HasValue && {groupMember}.HasValue) throw new ArgumentException(\"Duplicate scalar setup through group\");\nif ({groupMember}.HasValue) {selected} = {groupMember};");
                }
                else
                {
                    code.AppendLine($"{selected} = {selected}.Concat({groupMember}).ToArray();");
                }

                code.AppendLine("}");
                member = selected;
            }

            if (dims.Length == 0)
            {
                code.AppendLine($"if ({member}.HasValue) {Access(symbol, [])} = {ConvertValue(symbol, member + ".Value", sizeError: true)};");
            }
            else
            {
                code.AppendLine($"{{\nvar assigned = new HashSet<string>();\nforeach (var entry in {member}.ToArray())\n{{\nif (entry == null || entry.Subscripts.Count != {dims.Length} || !assigned.Add(string.Join(\",\", entry.Subscripts))) throw new ArgumentException(\"Duplicate or invalid table subscript\");");
                code.AppendLine($"{Access(symbol, dims.Select((d, i) => $"SourceSupport.Index(new Number(entry.Subscripts[{i}]), {d})").ToArray())} = {ConvertValue(symbol, "entry.Value", sizeError: true)};\n}}\n}}");
            }
        }

        return code.AppendLine("}").ToString();
    }
}
