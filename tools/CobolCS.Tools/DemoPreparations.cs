using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using CobolCS.Runtime;
using static CobolCS.Tools.Workspace;

namespace CobolCS.Tools;

/// <summary>独立した合成期待値を、新規のユーザーテンプレートだけへ設定します。</summary>
public static class DemoPreparations
{
    /// <summary>PAYMENTまたはARGCALCのレビュー済み合成例を設定します。</summary>
    /// <param name="output">生成済みプロジェクト。</param>
    /// <param name="readiness">ARGCALCの正式入力例を選択します。</param>
    public static void Prepare(string output, bool readiness = false)
    {
        var root = Path.GetFullPath(output);
        var manifest = Read(Path.Combine(root, "manifest.json"));
        var program = readiness ? "ARGCALC" : "PAYMENT";
        if (manifest.I("manifestSchemaVersion") != 2 || manifest.S("backend") != "source" || !manifest.A("programs").Select(x => x.GetValue<string>()).SequenceEqual(new[] { program }))
        {
            throw new ArgumentException("Expected a schema 2 source " + program + " project");
        }

        var source = Path.Combine(Root, readiness ? "examples/WORKFLOW/ARGCALC.cbl" : "examples/WORKFLOW/PAYMENT.cbl");
        if (!readiness && !manifest["inputHashes"]!.AsObject().Select(p => p.Value!.GetValue<string>()).SequenceEqual(new[] { Digest(source) }))
        {
            throw new ArgumentException("Only the repository's synthetic PAYMENT source is accepted");
        }

        foreach (var (path, digest) in manifest["artifacts"]!.AsObject())
        {
            if (Digest(Safe(root, path)) != digest!.GetValue<string>())
            {
                throw new ArgumentException("Managed source changed: " + path);
            }
        }

        var mapping = Read(Path.Combine(root, "reports/source-map.json")).AsArray().Select(x => x!).ToArray();
        var name = mapping.First(x => x["executorType"] is not null).S("executorType")[..^"Executor".Length];
        var fields = mapping.Where(x => x["symbolId"] is not null).ToDictionary(x => readiness ? x.S("originalName").Split(" OF ")[0] : x.S("originalName"), x => x.S("generatedName").Split('.').Last());
        var bindings = new Dictionary<string, string> { ["name"] = name, ["n"] = name };
        foreach (var (key, value) in fields)
        {
            bindings["fields['" + key + "']"] = value;
            bindings["f['" + key + "']"] = value;
        }

        var files = new Dictionary<string, string>();
        var caseId = name + (readiness ? "_Sample" : "_HappyPath");
        bindings["case"] = caseId;
        var baseline = new JsonObject
        {
            ["baselineKind"] = "independent-synthetic",
            ["reviewReason"] = readiness ? "Hand multiplication with truncation to two fractional digits; expected constants reside in hash-bound test code." : "Hand calculation: 100.00 minus 3.50 equals 96.50; PIC 9(3)V99 displays 09650. No original COBOL capture.",
            ["sourceHashes"] = manifest["inputHashes"]!.DeepClone(),
            ["toolVersion"] = manifest["toolVersion"]!.DeepClone(),
        };
        var casePath = $"Tests/Cases/{name}Tests.cs";
        if (readiness)
        {
            var symbols = Read(Path.Combine(root, "reports/ir.json"))["programs"]![0]!.A("symbols");
            bindings["group"] = name + "Group" + symbols.First(s => s.S("name") == "REQUEST-DATA").S("id") + "Snapshot";
            files[$"UserCode/Scenarios/{name}Scenarios.cs"] = Template("ReadinessScenario", bindings);
            bindings["body.strip(chr(10))"] = Template("ReadinessBody", bindings).Trim('\n');
            files[casePath] = Template("ReadinessTests", bindings);
            baseline["expected"] = new JsonObject { ["codeFile"] = casePath, ["caseId"] = caseId };
        }
        else
        {
            if (!fields.Keys.ToHashSet().SetEquals(new[] { "GROSS", "FEE", "NET", "RETURN-CODE" }))
            {
                throw new ArgumentException("Unexpected PAYMENT layout");
            }

            var stubs = Read(Path.Combine(root, "reports/stubs.json")).AsArray();
            if (stubs.Count != 1 || stubs[0]!["dependency"]!.S("kind") != "call")
            {
                throw new ArgumentException("Expected one typed CALL");
            }

            bindings["method"] = stubs[0]!.S("method");
            files[$"UserCode/Scenarios/{name}Scenarios.cs"] = Template("PaymentScenario", bindings);
            files["UserCode/ScenarioRegistration.cs"] = Template("PaymentRegistration", bindings);
            files[casePath] = Template("PaymentTests", bindings);
            baseline["input"] = "100.00";
            baseline["fake"] = new JsonObject { ["fee"] = "3.50", ["returning"] = "0" };
            baseline["expected"] = new JsonObject { ["net"] = "96.50", ["display"] = "09650", ["termination"] = "Goback", ["calls"] = 1 };
        }

        files[$"Tests/Baselines/{name}.json"] = baseline.ToJsonString() + "\n";
        var hashes = new JsonObject();
        foreach (var (path, content) in files)
        {
            hashes[path] = Hash(Encoding.UTF8.GetBytes(content));
        }

        if (readiness)
        {
            var responses = $"UserCode/Scenarios/{name}ResponseExamples.cs";
            hashes[responses] = Digest(Safe(root, responses));
        }

        files["Tests/review.json"] = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["cases"] = new JsonArray(new JsonObject { ["programId"] = program, ["caseId"] = caseId, ["baselineKind"] = "independent-synthetic", ["reviewed"] = true, ["callStubMode"] = "Throw", ["hasNoOpCalls"] = false, ["files"] = hashes }),
        }.ToJsonString() + "\n";
        foreach (var (path, content) in files)
        {
            var target = Safe(root, path);
            if (!File.Exists(target))
            {
                throw new ArgumentException("Deleted user template must not be recreated: " + path);
            }

            var previous = File.ReadAllBytes(target);
            if (!previous.SequenceEqual(Encoding.UTF8.GetBytes(content)) && Hash(previous) != manifest["userTemplateHashes"]?[path]?.GetValue<string>())
            {
                throw new ArgumentException("Refusing to overwrite user edits: " + path);
            }
        }

        foreach (var (path, content) in files)
        {
            File.WriteAllText(Path.Combine(root, path), content);
        }

        Console.WriteLine("Prepared independent-synthetic " + program + "; no original COBOL equivalence claim.");
    }

    private static string Template(string name, Dictionary<string, string> bindings)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CobolCS.Tools.Templates." + name + ".txt") ?? throw new InvalidOperationException(name);
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();
        foreach (var (key, value) in bindings)
        {
            text = text.Replace("@@" + key + "@@", value, StringComparison.Ordinal);
        }

        return text.Contains("@@", StringComparison.Ordinal) ? throw new InvalidOperationException("Unbound template: " + name) : text;
    }

    private static string Safe(string root, string path)
    {
        var target = Corpus.Confined(root, path);
        for (var item = new FileInfo(target) as FileSystemInfo; item is not null; item = item is FileInfo file ? file.Directory : ((DirectoryInfo)item).Parent)
        {
            if (item.LinkTarget is not null)
            {
                throw new ArgumentException("Unsafe user path: " + path);
            }
        }

        return target;
    }
}
