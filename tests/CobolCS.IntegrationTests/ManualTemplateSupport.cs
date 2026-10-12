using System.Text;
using System.Text.Json.Nodes;
using CobolCS.Cli;
using CobolCS.Runtime;

namespace CobolCS.IntegrationTests;

/// <summary>従来の手動テスト回帰では、参考テンプレートを利用者操作として明示的に有効化します。</summary>
internal static class ManualTemplateSupport
{
    /// <summary>自動ケースとは別の手動ワークフローを、一時試験先だけで準備します。</summary>
    /// <param name="output">合成試験専用の出力先。</param>
    internal static void Prepare(string output)
    {
        var manifest = Generator.ReadManifest(output);
        var review = new JsonObject { ["schemaVersion"] = 1, ["cases"] = new JsonArray() };
        foreach (var directory in new[] { "Tests/Cases/BranchGenerated", "Tests/Baselines/BranchGenerated" })
        {
            if (Directory.Exists(Path.Combine(output, directory)))
            {
                Directory.Delete(Path.Combine(output, directory), true);
            }
        }

        var plans = J.Parse(File.ReadAllText(Path.Combine(output, "reports/branch-plans.json")));
        foreach (var plan in plans.AsArray().OfType<JsonObject>())
        {
            var name = plan.S("name");
            var codePath = $"Tests/Cases/{name}Tests.cs";
            var baselinePath = $"Tests/Baselines/{name}.json";
            Directory.CreateDirectory(Path.Combine(output, "Tests/Cases"));
            Directory.CreateDirectory(Path.Combine(output, "Tests/Baselines"));
            File.Copy(Path.Combine(output, $"Tests/Templates/{name}Tests.cs.txt"), Path.Combine(output, codePath), true);
            File.Copy(Path.Combine(output, $"Tests/Templates/{name}.json"), Path.Combine(output, baselinePath), true);
            var paths = new[] { codePath, baselinePath, $"UserCode/Scenarios/{name}Scenarios.cs", $"UserCode/Scenarios/{name}ScriptedScenarios.cs", $"UserCode/Scenarios/{name}ResponseExamples.cs" }.Concat(Directory.Exists(Path.Combine(output, "Stubs", name)) ? Directory.GetFiles(Path.Combine(output, "Stubs", name)).Select(p => Path.GetRelativePath(output, p)) : []);
            var hashes = new JsonObject(paths.Select(p => KeyValuePair.Create<string, JsonNode?>(p, JsonValue.Create(Generator.Hash(File.ReadAllBytes(Path.Combine(output, p)))))));
            review["cases"]!.AsArray().Add(new JsonObject { ["programId"] = plan.S("programId"), ["caseId"] = name + "_Sample", ["baselineKind"] = "csharp-characterization", ["reviewed"] = false, ["callStubMode"] = "Throw", ["hasNoOpCalls"] = false, ["files"] = hashes });
            foreach (var path in new[] { codePath, baselinePath })
            {
                manifest["userTemplateHashes"]![path] = hashes[path]!.DeepClone();
                manifest["userBindings"]![path] = null;
            }
        }

        var readinessPath = Path.Combine(output, "reports/execution-readiness.json");
        var readiness = J.Parse(File.ReadAllText(readinessPath));
        var markdownPath = Path.Combine(output, "reports/execution-readiness.md");
        var markdown = File.ReadAllText(markdownPath);
        foreach (var candidate in readiness.A("assertionCandidates").Where(c => c.S("status") != "manual-unknown"))
        {
            var path = candidate.S("testFile");
            var manualPath = "Tests/Cases/" + Path.GetFileName(path).Replace(".txt", string.Empty, StringComparison.Ordinal);
            candidate["testFile"] = manualPath;
            markdown = markdown.Replace(path, manualPath, StringComparison.Ordinal);
        }

        File.WriteAllText(readinessPath, Generator.Json(readiness));
        File.WriteAllText(markdownPath, markdown);
        manifest["artifacts"]!["reports/execution-readiness.json"] = Generator.Hash(File.ReadAllBytes(readinessPath));
        manifest["artifacts"]!["reports/execution-readiness.md"] = Generator.Hash(File.ReadAllBytes(markdownPath));
        var text = Generator.Json(review);
        File.WriteAllText(Path.Combine(output, "Tests/review.json"), text);
        manifest["userTemplateHashes"]!["Tests/review.json"] = Generator.Hash(Encoding.UTF8.GetBytes(text));
        manifest["editableFiles"] = new JsonArray(Directory.GetFiles(output, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(output, p).Replace('\\', '/')).Where(p => p.StartsWith("Stubs/", StringComparison.Ordinal) || p.StartsWith("UserCode/", StringComparison.Ordinal) || p.StartsWith("Tests/Cases/", StringComparison.Ordinal) || p.StartsWith("Tests/Baselines/", StringComparison.Ordinal) || p.StartsWith("Tests/Templates/", StringComparison.Ordinal) || p == "Tests/review.json").Order(StringComparer.Ordinal).Select(p => (JsonNode?)JsonValue.Create(p)).ToArray());
        Generator.SaveManifest(output, manifest);
    }
}
