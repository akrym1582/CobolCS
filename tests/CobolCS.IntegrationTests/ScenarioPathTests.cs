using CobolCS.Cli;
using CobolCS.Frontend;
using CobolCS.Runtime;
using CobolCS.Tools;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CobolCS.IntegrationTests;

/// <summary>編集済みスタブと明示fakeの独立した経路を検証します。</summary>
[TestClass]
public sealed class ScenarioPathTests
{
    /// <summary>新規サンプルは外部注入を省略し、別ファイルのscriptedだけがfakeを使用します。</summary>
    [TestMethod]
    public void NewScenariosSeparateStubsAndScripted()
    {
        using var folder = new TestDirectory();
        var source = Path.Combine(folder.Path, "call.cbl");
        File.WriteAllText(source, "IDENTIFICATION DIVISION. PROGRAM-ID. PATHS. DATA DIVISION. WORKING-STORAGE SECTION. 01 RESULT PIC 9(3). PROCEDURE DIVISION. CALL 'REMOTE' USING RESULT DISPLAY RESULT GOBACK.");
        var output = Path.Combine(folder.Path, "generated");
        Generator.Generate(FrontendAnalyzer.Analyze(source), output, "scaffold");
        var scenarioPath = Directory.GetFiles(Path.Combine(output, "UserCode/Scenarios"), "*Scenarios.cs").Single(p => !p.EndsWith("ScriptedScenarios.cs", StringComparison.Ordinal));
        var name = Path.GetFileName(scenarioPath)[..^"Scenarios.cs".Length];
        var scenario = File.ReadAllText(scenarioPath);
        StringAssert.Contains(scenario, ".Run(input, options: options)");
        var scripted = File.ReadAllText(Path.Combine(output, $"UserCode/Scenarios/{name}ScriptedScenarios.cs"));
        StringAssert.Contains(scripted, ".Run(input, fake, options)");
        var registration = File.ReadAllText(Path.Combine(output, "Api/GeneratedScenarioRegistration.g.cs"));
        StringAssert.Contains(registration, $"\"sample-scripted\", {name}ScriptedScenarios.Sample");
        var review = Workspace.Read(Path.Combine(output, "Tests/review.json"))["cases"]![0]!["files"]!.AsObject();
        Assert.IsTrue(review.Any(p => p.Key.StartsWith($"Stubs/{name}/", StringComparison.Ordinal)));
        Assert.IsTrue(review.ContainsKey($"UserCode/Scenarios/{name}ScriptedScenarios.cs"));
        File.Delete(Path.Combine(output, $"UserCode/Scenarios/{name}ScriptedScenarios.cs"));
        Generator.Generate(FrontendAnalyzer.Analyze(source), output, "scaffold");
        Assert.IsFalse(File.Exists(Path.Combine(output, $"UserCode/Scenarios/{name}ScriptedScenarios.cs")));
        Assert.IsFalse(File.ReadAllText(Path.Combine(output, "Api/GeneratedScenarioRegistration.g.cs")).Contains(name + "ScriptedScenarios.Sample", StringComparison.Ordinal));
    }

    /// <summary>実生成物でスタブ編集だけのsample、fake応答、不足と不一致と余剰応答を実行します。</summary>
    /// <returns>生成プロジェクトの実行とテストが完了するタスク。</returns>
    [TestMethod]
    public async Task EditedStubAndScriptedExecuteIndependently()
    {
        using var folder = new TestDirectory();
        File.WriteAllText(Path.Combine(folder.Path, "NuGet.Config"), "<configuration><packageSources><clear /></packageSources></configuration>");
        var source = Path.Combine(folder.Path, "call.cbl");
        File.WriteAllText(source, "IDENTIFICATION DIVISION. PROGRAM-ID. PATHS. DATA DIVISION. WORKING-STORAGE SECTION. 01 RESULT PIC 9(3). PROCEDURE DIVISION. CALL 'REMOTE' USING RESULT DISPLAY RESULT GOBACK.");
        var output = Path.Combine(folder.Path, "generated");
        Generator.Generate(FrontendAnalyzer.Analyze(source), output, "scaffold");
        var scenarioPath = Directory.GetFiles(Path.Combine(output, "UserCode/Scenarios"), "*Scenarios.cs").Single(p => !p.EndsWith("ScriptedScenarios.cs", StringComparison.Ordinal));
        var name = Path.GetFileName(scenarioPath)[..^"Scenarios.cs".Length];
        var stub = Workspace.Read(Path.Combine(output, "reports/stubs.json")).AsArray().Single()!;
        var method = "Dependency_" + stub.S("statementId");
        var response = name + method + "Response";
        var stubPath = Path.Combine(output, stub.S("file"));
        var text = File.ReadAllText(stubPath);
        var start = text.IndexOf("        if (Options.CallStubMode", StringComparison.Ordinal);
        var end = text.LastIndexOf("    }", StringComparison.Ordinal);
        File.WriteAllText(stubPath, text[..start] + $"        return new {response}(\"00\", DependencyOutcome.Normal, new Number(111));\n" + text[end..]);
        var cli = await ProcessRunner.Run("dotnet", [.. Workspace.Cli, "run", output, "--scenario", "sample"], folder.Path);
        StringAssert.Contains(cli, "111");
        var direct = await ProcessRunner.Run("dotnet", ["run", "--project", output, "--no-build", "--", "PATHS", "--scenario", "sample"], folder.Path);
        StringAssert.Contains(direct, "111");
        var missing = Workspace.Command(["dotnet", "run", "--project", output, "--no-build", "--", "PATHS", "--scenario", "sample-scripted"]);
        Assert.AreNotEqual(0, missing.I("exitCode"));
        StringAssert.Contains(missing.S("stderr"), "Missing or out-of-order response");
        var scriptedPath = Path.Combine(output, $"UserCode/Scenarios/{name}ScriptedScenarios.cs");
        File.WriteAllText(scriptedPath, File.ReadAllText(scriptedPath).Replace($"var fake = new {name}ScriptedDependencies();", $"var fake = new {name}ScriptedDependencies();\n        fake.Expect{method}(_ => true, new {response}(\"00\", DependencyOutcome.Normal, new Number(222)));", StringComparison.Ordinal));
        var scripted = await ProcessRunner.Run("dotnet", [.. Workspace.Cli, "run", output, "--scenario", "sample-scripted"], folder.Path);
        StringAssert.Contains(scripted, "222");
        var tests = Directory.GetFiles(Path.Combine(output, "Tests/Cases"), "*.cs").Single();
        var testSource = $$"""
            using CobolCS.Runtime;
            using Microsoft.VisualStudio.TestTools.UnitTesting;

            namespace CobolCS.Generated.Tests;

            /// <summary>独立した固定値で二つの経路を検証します。</summary>
            [TestClass]
            public sealed class {{name}}Tests
            {
                /// <summary>スタブとfakeを別値に固定し、fakeの失敗を迂回しません。</summary>
                [TestMethod]
                public void PathsAndStrictFailures()
                {
                    CollectionAssert.AreEqual(new[] { "111" }, {{name}}Scenarios.Sample(new()).Display.ToArray());
                    CollectionAssert.AreEqual(new[] { "222" }, {{name}}ScriptedScenarios.Sample(new()).Display.ToArray());
                    var fake = new {{name}}ScriptedDependencies();
                    StringAssert.Contains(Assert.Throws<ExecutionException>(() => new {{name}}Executor().Run(new(), fake)).Message, "Missing or out-of-order response");
                    fake.Expect{{method}}(_ => false, new {{response}}("00", DependencyOutcome.Normal, new Number(333)));
                    StringAssert.Contains(Assert.Throws<ExecutionException>(() => new {{name}}Executor().Run(new(), fake)).Message, "Request mismatch");
                    fake = new();
                    fake.Expect{{method}}(_ => true, new {{response}}("00", DependencyOutcome.Normal, new Number(333)));
                    fake.Expect{{method}}(_ => true, new {{response}}("00", DependencyOutcome.Normal, new Number(444)));
                    StringAssert.Contains(Assert.Throws<ExecutionException>(() => new {{name}}Executor().Run(new(), fake)).Message, "Unused dependency responses");
                }
            }
            """ + "\n";
        File.WriteAllText(tests, testSource);
        await ProcessRunner.Run("dotnet", ["test", Path.Combine(output, "Tests/CobolCS.Generated.Tests.csproj"), "-m:1"], folder.Path);
        var userRegistration = Path.Combine(output, "UserCode/ScenarioRegistration.cs");
        File.WriteAllText(userRegistration, File.ReadAllText(userRegistration).Replace("    {\n", $"    {{\n        registry.Register(\"PATHS\", \"sample\", {name}Scenarios.Sample);\n", StringComparison.Ordinal));
        var duplicate = Workspace.Command(["dotnet", .. Workspace.Cli, "run", output, "--scenario", "sample"]);
        Assert.AreNotEqual(0, duplicate.I("exitCode"));
        StringAssert.Contains(duplicate.S("stderr"), "Duplicate scenario");
    }

    /// <summary>旧出力の意味、同名ファイル、削除済みテンプレートとレビューを保持します。</summary>
    /// <returns>再生成と独立した生成プロジェクトのビルドが完了するタスク。</returns>
    [TestMethod]
    public async Task LegacyAndDeletedScenariosRemainUserOwned()
    {
        using var folder = new TestDirectory();
        File.WriteAllText(Path.Combine(folder.Path, "NuGet.Config"), "<configuration><packageSources><clear /></packageSources></configuration>");
        var source = Path.Combine(folder.Path, "legacy.cbl");
        File.WriteAllText(source, "IDENTIFICATION DIVISION. PROGRAM-ID. LEGACY. PROCEDURE DIVISION. GOBACK.");
        var ir = FrontendAnalyzer.Analyze(source);
        var output = Path.Combine(folder.Path, "generated");
        Generator.Generate(ir, output, "scaffold");
        var scenarioPath = Directory.GetFiles(Path.Combine(output, "UserCode/Scenarios"), "*Scenarios.cs").Single(p => !p.EndsWith("ScriptedScenarios.cs", StringComparison.Ordinal));
        var name = Path.GetFileName(scenarioPath)[..^"Scenarios.cs".Length];
        var scriptedRelative = $"UserCode/Scenarios/{name}ScriptedScenarios.cs";
        var scriptedPath = Path.Combine(output, scriptedRelative);
        File.WriteAllText(scenarioPath, File.ReadAllText(scenarioPath).Replace(".Run(input, options: options)", $".Run(input, new {name}ScriptedDependencies(), options)", StringComparison.Ordinal) + "\n// Keep original fake behavior and user bytes.\n");
        File.WriteAllText(scriptedPath, "// Same-name user file with no template class.\n");
        var review = File.ReadAllBytes(Path.Combine(output, "Tests/review.json"));
        var scenario = File.ReadAllBytes(scenarioPath);
        var manifestPath = Path.Combine(output, "manifest.json");
        var manifest = Workspace.Read(manifestPath).AsObject();
        manifest.Remove("scenarioRegistrations");
        manifest["userTemplateHashes"]!.AsObject().Remove(scriptedRelative);
        Workspace.Write(manifestPath, manifest);
        Generator.Generate(ir, output, "scaffold");
        CollectionAssert.AreEqual(scenario, File.ReadAllBytes(scenarioPath));
        CollectionAssert.AreEqual(review, File.ReadAllBytes(Path.Combine(output, "Tests/review.json")));
        await ProcessRunner.Run("dotnet", ["build", Path.Combine(output, "CobolCS.Generated.csproj"), "-m:1"], folder.Path);
        Assert.AreEqual("// Same-name user file with no template class.\n", File.ReadAllText(scriptedPath));
        var registration = File.ReadAllText(Path.Combine(output, "Api/GeneratedScenarioRegistration.g.cs"));
        StringAssert.Contains(registration, name + "Scenarios.Sample");
        Assert.IsFalse(registration.Contains(name + "ScriptedScenarios.Sample", StringComparison.Ordinal));
        StringAssert.Contains(File.ReadAllText(Path.Combine(output, "reports/execution-readiness.md")), "Omit the Executor.Run external argument");
        File.Delete(scenarioPath);
        File.Delete(scriptedPath);
        File.Delete(Path.Combine(output, $"Tests/Cases/{name}Tests.cs"));
        Generator.Generate(ir, output, "scaffold");
        Assert.IsFalse(File.Exists(scenarioPath));
        Assert.IsFalse(File.Exists(scriptedPath));
        Assert.IsFalse(File.ReadAllText(Path.Combine(output, "Api/GeneratedScenarioRegistration.g.cs")).Contains("Scenarios.Sample", StringComparison.Ordinal));
        CollectionAssert.AreEqual(review, File.ReadAllBytes(Path.Combine(output, "Tests/review.json")));
        await ProcessRunner.Run("dotnet", ["build", Path.Combine(output, "CobolCS.Generated.csproj"), "-m:1"], folder.Path);
    }
}
