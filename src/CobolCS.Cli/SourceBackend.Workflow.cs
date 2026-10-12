using System.Text;
using System.Text.Json.Nodes;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>利用者が編集するシナリオと別 assembly のテストを生成します。</summary>
internal sealed partial class SourceBackend
{
    /// <summary>採用可能なプログラムだけにサンプルを登録します。</summary>
    /// <param name="programs">strict 判定後のプログラム。</param>
    /// <param name="files">生成物。</param>
    internal static void Workflow(JsonNode[] programs, SortedDictionary<string, string> files)
    {
        BusinessWorkflow(files);
        files["README.md"] = "# Generated C# solution\n\nThis is a source translation, not evidence of original COBOL equivalence or production readiness.\n\n- Run: `dotnet run --project . -- PROGRAM-ID [fixture.json]` or `--scenario sample`.\n- New sample uses editable Stubs; sample-scripted injects its own ScriptedDependencies. Supply inputs in each separate scenario file. To test fake responses, change the test call to ScriptedScenarios.Sample and include its inputs and ResponseExamples in review.json files. Missing, mismatched, out-of-order or unused fake responses fail without falling back to Stubs.\n- Restore tests: `dotnet restore CobolCS.Generated.sln`; then `dotnet test Tests/CobolCS.Generated.Tests.csproj --no-restore`. Ordinary generate explores branches with synthetic responses and emits fixed, hash-bound C# characterization tests (reviewed=false) in Tests/Cases/BranchGenerated/<runId>. They run before review; adopt still requires review. Unfilled manual samples are reference templates in Tests/Templates/*.cs.txt. Reports and exclusions are in reports/test-generation/<runId>. See reports/branches.json; no original COBOL correctness or unreachability claim.\n- Working-storage InitialState is test setup, not a formal business argument. Known fixed LINKAGE uses Input.Arguments and Result.Arguments; unsupported declaration modes, aliases and unknown layouts retain entry stubs. See reports/execution-readiness.md. Groups expose typed basic members; table entries use full outer-to-inner 1-based subscripts.\n- Each Executor.Run creates new state, queues and counters. Results are copied; user adapters are not promised thread safety.\n- ExecutionOptions controls cooperative limits and cancellation. A synchronous adapter cannot be forcibly interrupted by the API. The CLI wrapper also imposes a process timeout.\n- CALL stubs throw by default. Explicit `--call-stubs noop` only affects generated unimplemented CALL templates. It cannot bypass a supplied fake or edited implementation. NOOP is exploration; NOT ON EXCEPTION and provisional RETURNING can take incorrect business branches. Limits are not successful completion.\n- Inspect typed DependencyTrace, NoOpCalls and HasNoOpCalls. Exploration is never a reviewed business baseline.\n- EOF/no-data and failed responses preserve omitted outputs. SQL negative NULL indicators require omitted host values; status outputs are explicit. All outputs are validated before storage.\n- Stubs, UserCode, Tests/Cases, Tests/Baselines, Tests/Templates, Tests/review.json and Fixtures are user-owned; regeneration preserves bytes and deletions. Review contract changes in managed reports.\n- Review baselines record kind, sourceHashes, toolVersion, input, fake script, expected comparisons and reviewReason. review.json binds program/case to file hashes. Characterization is not correctness evidence. Original COBOL capture additionally needs captureEvidence.\n- Adopt with `cobolcs adopt DIRECTORY --output NEW_DIRECTORY` after real tests pass. In the adopted solution all sources, including .g.cs, can be edited. Provenance is history, not an edit lock. Regenerate elsewhere and port a diff.\n- Keep LICENSE and THIRD-PARTY-NOTICES. Source COBOL distribution terms remain applicable; not all derived source is automatically MIT. Review permitted user code, fixtures and baselines for secrets before distribution.\n\n## Branch exploration\n\nDefault seed=1; maximum 200 process executions per program including two fixed replays; overall timeout=120000ms; trace limit=10000 events. Existing maxSteps/timeoutMs apply. Set testGeneration in the existing --config JSON (maxAttempts, seed, timeoutMs, maxTraceEvents, candidates). Manual candidates have programId and typed input (AcceptInputs, Arguments, InitialState), optional responseMode/responseModes/responseValues. Member names and symbol IDs are in reports/branch-plans.json. Numeric values are strings or exact numerator/denominator strings. Unknown members, incomplete layouts and duplicate subscripts fail. State injection is labeled. CALL/FILE/SQL use synthetic contract responses, never edited external Stubs or real connections. Missing SDK, blocked internal code, invalid inputs, timeout and truncated traces leave an incomplete report and ordinary source output. Identical completed runs preserve edits and deletions. Review provenance and file hashes before changing reviewed; add a concrete reviewReason and update explicitly edited file hashes. Current C# observations are not independent expectations.\n\n## Internal completion and business tests\n\nRun `cobolcs prepare DIRECTORY` for reports/completion-worklist.md without executing code. Explicit `cobolcs explore DIRECTORY [--program ID]` uses edited internal Stubs and all completion helpers while keeping external CALL/FILE/SQL on synthetic/fixed fakes. Unknown or unsupported external blocks remain blocked. Put helpers in UserCode/Completion; no external connections in internal completion. Worker isolation is not a security sandbox. Run/explore evidence is local in reports/executions and becomes stale after code/input changes. Manual work-items done is separate from measured reach/completion.\n\nBusiness tests compare chosen stable names in Tests/BusinessCases and Tests/BusinessContracts through UserCode/Business adapters. See Tests/Templates/Business/README.md. Templates are not successful cases. Business names and independent expectations are selected explicitly. Prepare retains required IDs; adopt checks current code/data/mapping hashes and actual per-case execution. After adopt implementation hashes are provenance, while changed business contracts/expectations require explicit review. Internal snapshot characterization remains a separate regression.\n\n## Public type mapping\n\n" + string.Join("\n", programs.Select(p => $"- `{p.S("id")}`: `using Executor = CobolCS.Generated.{Identifier(p.S("id"))}Executor;`")) + "\n\nManaged source-map.json maps original names and symbol IDs to members. Editable scenarios use actual aliases.\n";
        files["UserCode/Completion/work-items.json"] = "{\"schemaVersion\":1,\"items\":[]}\n";
        files["UserCode/Completion/README.md"] = "# Internal completion\n\nPut helper C# sources here. Helpers and Stubs are user-owned and deletions are preserved. Implement only internal logic; do not connect to external systems. Explicit explore executes this code in a worker; process isolation is not a security sandbox. Manual notes in work-items.json use itemId, status (open/in-progress/done), notes and assignee. Manual done does not prove runtime completion.\n";
        files["Tests/Support/Review.g.cs"] = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Templates/Review.g.cs.txt"));
        var registration = new StringBuilder("namespace CobolCS.Generated;\n\n/// <summary>利用者の C# シナリオ登録。名前の重複は拒否します。</summary>\ninternal static class ScenarioRegistration\n{\n    /// <summary>実行ごとの options を各シナリオへ渡します。</summary>\n    /// <param name=\"registry\">登録先。</param>\n    internal static void Register(ScenarioRegistry registry)\n    {\n");
        var generatedRegistration = new StringBuilder(Header() + "internal static class GeneratedScenarioRegistration\n{\ninternal static void Register(ScenarioRegistry registry)\n{\n");
        var review = new JsonArray();
        foreach (var program in programs)
        {
            var id = program.S("id");
            var name = Identifier(id);
            generatedRegistration.AppendLine($"        registry.Register({Quote(id)}, \"sample\", {name}Scenarios.Sample);");
            generatedRegistration.AppendLine($"        registry.Register({Quote(id)}, \"sample-scripted\", {name}ScriptedScenarios.Sample);");
            var caseId = name + "_Sample";
            files[$"Tests/Baselines/{name}.json"] = "{\"baselineKind\":\"csharp-characterization\",\"reviewReason\":\"UNREVIEWED\",\"expected\":null}\n";
            review.Add(new JsonObject { ["programId"] = id, ["caseId"] = caseId, ["baselineKind"] = "csharp-characterization", ["reviewed"] = false, ["callStubMode"] = "Throw", ["hasNoOpCalls"] = false, ["files"] = new JsonObject { [$"UserCode/Scenarios/{name}Scenarios.cs"] = Generator.Hash(Encoding.UTF8.GetBytes(files[$"UserCode/Scenarios/{name}Scenarios.cs"])), [$"UserCode/Scenarios/{name}ResponseExamples.cs"] = Generator.Hash(Encoding.UTF8.GetBytes(files[$"UserCode/Scenarios/{name}ResponseExamples.cs"])), [$"Tests/Cases/{name}Tests.cs"] = Generator.Hash(Encoding.UTF8.GetBytes(files[$"Tests/Cases/{name}Tests.cs"])), [$"Tests/Baselines/{name}.json"] = Generator.Hash(Encoding.UTF8.GetBytes(files[$"Tests/Baselines/{name}.json"])) } });
            var reviewFiles = review[^1]!["files"]!.AsObject();
            foreach (var (path, content) in files.Where(p => p.Key.StartsWith($"Stubs/{name}/", StringComparison.Ordinal) || p.Key == $"UserCode/Scenarios/{name}ScriptedScenarios.cs"))
            {
                reviewFiles[path] = Generator.Hash(Encoding.UTF8.GetBytes(content));
            }
        }

        generatedRegistration.AppendLine("}\n}");
        files["Api/GeneratedScenarioRegistration.g.cs"] = generatedRegistration.ToString();
        registration.AppendLine("    }\n}");
        files["UserCode/ScenarioRegistration.cs"] = registration.ToString();
        files["Tests/review.json"] = Generator.Json(new JsonObject { ["schemaVersion"] = 1, ["cases"] = review });
        files["Tests/xunit.runner.json"] = "{\"methodDisplay\":\"method\",\"parallelizeTestCollections\":false}\n";
        files["Tests/CobolCS.Generated.Tests.csproj"] = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                <GenerateDocumentationFile>true</GenerateDocumentationFile>
                <NoWarn>$(NoWarn);SA1101;SA1200;SA1309;SA1629;SA1633</NoWarn>
                <IsTestProject>true</IsTestProject>
                <IsPackable>false</IsPackable>
              </PropertyGroup>
              <ItemGroup>
                <None Update="xunit.runner.json" CopyToOutputDirectory="PreserveNewest" />
                <ProjectReference Include="../CobolCS.Generated.csproj" />
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.0.1" />
                <PackageReference Include="xunit" Version="2.9.3" />
                <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" PrivateAssets="all" />
                <PackageReference Include="StyleCop.Analyzers" Version="1.2.0-beta.556" PrivateAssets="all" />
              </ItemGroup>
            </Project>
            """ + "\n";
        files["CobolCS.Generated.sln"] = """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "CobolCS.Generated", "CobolCS.Generated.csproj", "{F54B2C56-436E-47E5-8CF9-000000000001}"
            EndProject
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "CobolCS.Generated.Tests", "Tests/CobolCS.Generated.Tests.csproj", "{F54B2C56-436E-47E5-8CF9-000000000002}"
            EndProject
            Global
                GlobalSection(SolutionConfigurationPlatforms) = preSolution
                    Debug|Any CPU = Debug|Any CPU
                EndGlobalSection
                GlobalSection(ProjectConfigurationPlatforms) = postSolution
                    {F54B2C56-436E-47E5-8CF9-000000000001}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
                    {F54B2C56-436E-47E5-8CF9-000000000001}.Debug|Any CPU.Build.0 = Debug|Any CPU
                    {F54B2C56-436E-47E5-8CF9-000000000002}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
                    {F54B2C56-436E-47E5-8CF9-000000000002}.Debug|Any CPU.Build.0 = Debug|Any CPU
                EndGlobalSection
            EndGlobal
            """ + "\n";
    }

    /// <summary>公開生成メンバーに日本語の境界契約を添えます。</summary>
    /// <param name="source">生成コードだけを対象にします。</param>
    /// <returns>XML 文書を含むソース。</returns>
    internal static string DocumentPublic(string source)
    {
        var result = new StringBuilder();
        var previousWasDocumentation = false;
        foreach (var range in source.AsSpan().Split('\n'))
        {
            var line = source.AsSpan()[range];
            if (line.TrimStart().StartsWith("public ", StringComparison.Ordinal) && !previousWasDocumentation)
            {
                result.AppendLine("/// <summary>型付き実行境界の値または操作。入力は PIC 検査を受け、結果は実行ごとに分離します。</summary>");
            }

            result.Append(line).AppendLine();
            previousWasDocumentation = line.TrimStart().StartsWith("///", StringComparison.Ordinal) || (previousWasDocumentation && line.TrimStart().StartsWith("[", StringComparison.Ordinal));
        }

        return result.ToString();
    }
}
