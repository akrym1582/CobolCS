using System.Text;
using System.Text.Json.Nodes;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>独立してビルド・実行できる source プロジェクトを組み立てます。</summary>
internal sealed partial class SourceBackend
{
    /// <summary>全プログラムの C# と対応レポートを作ります。</summary>
    /// <param name="ir">解析資料。</param>
    /// <param name="files">出力ファイル。</param>
    /// <param name="diagnostics">生成中の対応診断の追加先。</param>
    internal static void Create(JsonNode ir, SortedDictionary<string, string> files, JsonArray diagnostics)
    {
        files["reports/execution-readiness.json"] = "{\"schemaVersion\":1,\"analysis\":\"static candidates, not execution order or reachability\",\"items\":[]}";
        var readiness = J.Parse(files["reports/execution-readiness.json"]);
        readiness["programs"] = new JsonArray(ir.A("programs").Select(p => (JsonNode?)new JsonObject { ["programId"] = p.S("id"), ["inputType"] = Identifier(p.S("id")) + "Input", ["argumentsType"] = Identifier(p.S("id")) + "Arguments", ["executorType"] = Identifier(p.S("id")) + "Executor" }).ToArray());
        files["reports/execution-readiness.json"] = Generator.Json(readiness);
        files["reports/execution-readiness.md"] = "# Execution readiness\n\nStatic candidates only: neither execution order nor confirmed reachability. Supply case inputs, register typed external responses, implement reached internal stubs separately, then set independent test expectations and review file hashes. User files and deletions are preserved: compare current contracts with your existing examples after regeneration. Consult reports/contract-changes.json and reports/source-map.json.\n\n";
        files["Api/Preparation.g.cs"] = Header() + "public static class Preparation\n{\npublic static T Required<T>(string item) => throw new InvalidOperationException(\"Unfilled preparation item: \" + item);\n}\n";
        var stubs = new JsonArray();
        var sourceMap = new JsonArray();
        foreach (var program in ir.A("programs"))
        {
            new SourceBackend(program, ir["config"]!, files, stubs, sourceMap).Generate();
        }

        foreach (var stub in stubs.OfType<JsonObject>())
        {
            diagnostics.Add(new JsonObject
            {
                ["code"] = "CS_SOURCE_STUB",
                ["severity"] = stub!.S("capability") == "unsupported" ? "error" : "review",
                ["stage"] = "source",
                ["programId"] = stub["programId"]!.DeepClone(),
                ["statementId"] = stub["statementId"]!.DeepClone(),
                ["capability"] = stub["capability"]!.DeepClone(),
                ["source"] = stub["source"]?.DeepClone(),
                ["message"] = stub["reason"]!.DeepClone(),
            });
        }

        foreach (var diagnostic in diagnostics.OfType<JsonObject>())
        {
            if (diagnostic.S("code") is not ("CS_EXTERNAL" or "CS_STATEMENT"))
            {
                continue;
            }

            var resolved = stubs.OfType<JsonObject>().Any(s => s.S("capability") == "adapter-required" && s.S("programId") == diagnostic.S("programId") && (diagnostic.S("code") == "CS_EXTERNAL" ? s["dependency"]?.S("id") == diagnostic.S("dependencyId") : s.S("statementId") == diagnostic.S("statementId")));
            if (resolved)
            {
                diagnostic["severity"] = "review";
                diagnostic["capability"] = "adapter-required";
            }
        }

        files["reports/stubs.json"] = Generator.Json(stubs);
        files["reports/source-map.json"] = Generator.Json(sourceMap);
        files["Contracts/SourceLocation.g.cs"] = Header() + """
            /// <summary>実行時に IR なしで使える原文の情報です。</summary>
            public sealed record SourceLocation(string ProgramId, string StatementId, string DependencyId, string File, int Line, string Raw, string Operation, IReadOnlyList<string> PassingModes, string? NextSentenceTarget);

            /// <summary>元の項目へ直接読み書きできる型付き参照です。</summary>
            internal sealed class CobolReference<T>(Func<T> read, Action<T> write)
            {
                internal T Value
                {
                    get => read();
                    set => write(value);
                }
            }

            /// <summary>未確定の宣言を正常な数値や文字列に見せない明示的な型です。</summary>
            public sealed record UnknownCobolValue(string OriginalName);

            /// <summary>未対応ブロックから返す制御の種類です。</summary>
            internal enum ControlKind
            {
                Continue,
                Goback,
                StopRun,
                Jump,
            }

            /// <summary>制御結果と、必要な場合の移動先を明示します。</summary>
            internal readonly record struct ControlResult(ControlKind Kind, string? Target = null)
            {
                internal static ControlResult Continue => new(ControlKind.Continue);

                internal static ControlResult Goback => new(ControlKind.Goback);

                internal static ControlResult StopRun => new(ControlKind.StopRun);

                internal static ControlResult JumpTo(string target) => new(ControlKind.Jump, target);
            }

            /// <summary>段落 dispatcher に制御を返す内部信号です。</summary>
            internal sealed class CobolControlTransfer(string target) : Exception("Unresolved control transfer: " + target)
            {
                internal string Target { get; } = target;
            }

            /// <summary>外部依存が必ず指定する制御結果です。</summary>
            public enum DependencyOutcome
            {
                /// <summary>正常制御です。</summary>
                Normal,
                /// <summary>依存失敗です。</summary>
                Exception,
                /// <summary>EOF または no-data です。</summary>
                End,
                /// <summary>キー不正です。</summary>
                InvalidKey,
            }
            """ + "\n";
        files["Api/Execution.g.cs"] = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Templates/Execution.g.cs.txt"));
        files["Program.cs"] = CreateRunner(ir.A("programs"));
        foreach (var path in files.Keys.Where(p => p.EndsWith(".cs", StringComparison.Ordinal)).ToArray())
        {
            files[path] = SourceFormatting.Indent(files[path]);
        }
    }

    /// <summary>生成可能なプログラムだけを起動対象へ登録します。</summary>
    /// <param name="programs">生成したプログラム。</param>
    /// <returns>独立した CLI の C#。</returns>
    internal static string CreateRunner(IEnumerable<JsonNode> programs)
    {
        var runner = new StringBuilder("// <auto-generated/>\n#nullable enable\nusing System.Text.Json;\nusing System.Text.Json.Nodes;\nusing CobolCS.Generated;\n\ntry\n{\n    if (args.Length < 1) throw new ArgumentException(\"Usage: <PROGRAM-ID> [fixture.json | --scenario name] [--call-stubs throw|noop]\");\n    string? fixturePath = null;\n    string? scenario = null;\n    var mode = CallStubMode.Throw;\n    var seen = new HashSet<string>();\n    for (var i = 1; i < args.Length; i++)\n    {\n        if (args[i].StartsWith(\"--\"))\n        {\n            var key = args[i];\n            if (!seen.Add(key) || ++i >= args.Length) throw new ArgumentException(\"Duplicate or missing option\");\n            if (key == \"--scenario\") scenario = args[i];\n            else if (key == \"--call-stubs\") mode = args[i] switch { \"throw\" => CallStubMode.Throw, \"noop\" => CallStubMode.NoOp, _ => throw new ArgumentException(\"Invalid CALL stub mode\") };\n            else throw new ArgumentException(\"Unknown option\");\n        }\n        else if (fixturePath == null) fixturePath = args[i];\n        else throw new ArgumentException(\"Duplicate fixture\");\n    }\n    if (fixturePath != null && scenario != null) throw new ArgumentException(\"Fixture and scenario are mutually exclusive\");\n    var options = new ExecutionOptions { CallStubMode = mode };\n    if (mode == CallStubMode.NoOp) Console.Error.WriteLine(\"WARNING: CALL no-op exploration can take incorrect business branches; completion is not business success.\");\n    var registry = new ScenarioRegistry();\n    GeneratedScenarioRegistration.Register(registry);\n    ScenarioRegistration.Register(registry);\n    if (scenario != null)\n    {\n        var scenarioResult = registry.Run(args[0], scenario, options);\n        var scenarioJson = scenarioResult switch\n        {\n" + string.Join("\n", programs.Select(p => $"            {Identifier(p.S("id"))}Result typed => {Identifier(p.S("id"))}FixtureAdapter.Serialize(typed),")) + "\n            _ => JsonSerializer.SerializeToNode(scenarioResult, RunnerSerialization.Options),\n        };\n        Console.WriteLine(scenarioJson!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));\n        return 0;\n    }\n    var fixture = fixturePath == null ? null : JsonNode.Parse(File.ReadAllText(fixturePath));\n    var result = args[0] switch\n    {\n");
        foreach (var program in programs)
        {
            runner.AppendLine($"        {Quote(program.S("id"))} => {Identifier(program.S("id"))}FixtureAdapter.Serialize(new {Identifier(program.S("id"))}Executor().Run({Identifier(program.S("id"))}FixtureAdapter.Parse(fixture), options: options)),");
        }

        runner.AppendLine("        _ => throw new ArgumentException(\"Unknown PROGRAM-ID \" + args[0]),\n    };\n    Console.WriteLine(result.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));\n    return 0;\n}\ncatch (Exception error)\n{\n    Console.Error.WriteLine(error.Message);\n    if (error.Data[\"DependencyTrace\"] is IReadOnlyList<DependencyTrace> trace) Console.Error.WriteLine(JsonSerializer.Serialize(trace, RunnerSerialization.Options));\n    return 2;\n}");
        return SourceFormatting.Indent(runner.ToString());
    }
}
