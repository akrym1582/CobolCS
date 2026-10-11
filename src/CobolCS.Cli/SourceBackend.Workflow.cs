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
        files["README.md"] = "# Generated C# solution\n\nThis is a source translation, not evidence of original COBOL equivalence or production readiness.\n\n- Run: `dotnet run --project . -- PROGRAM-ID [fixture.json]` or `--scenario sample`.\n- Restore tests: `dotnet restore CobolCS.Generated.sln`; then `dotnet test Tests/CobolCS.Generated.Tests.csproj --no-restore`. Initial tests intentionally fail until independently reviewed.\n- Working-storage InitialState is test setup, not a formal business argument. LINKAGE/PROCEDURE USING, aliases and unknown layouts remain unsupported. Groups expose typed basic members; table entries use full outer-to-inner 1-based subscripts.\n- Each Executor.Run creates new state, queues and counters. Results are copied; user adapters are not promised thread safety.\n- ExecutionOptions controls cooperative limits and cancellation. A synchronous adapter cannot be forcibly interrupted by the API. The CLI wrapper also imposes a process timeout.\n- CALL stubs throw by default. Explicit `--call-stubs noop` only affects generated unimplemented CALL templates. It cannot bypass a supplied fake or edited implementation. NOOP is exploration; NOT ON EXCEPTION and provisional RETURNING can take incorrect business branches. Limits are not successful completion.\n- Inspect typed DependencyTrace, NoOpCalls and HasNoOpCalls. Exploration is never a reviewed business baseline.\n- EOF/no-data and failed responses preserve omitted outputs. SQL negative NULL indicators require omitted host values; status outputs are explicit. All outputs are validated before storage.\n- Stubs, UserCode, Tests/Cases, Tests/Baselines, Tests/review.json and Fixtures are user-owned; regeneration preserves bytes and deletions. Review contract changes in managed reports.\n- Review baselines record kind, sourceHashes, toolVersion, input, fake script, expected comparisons and reviewReason. review.json binds program/case to file hashes. Characterization is not correctness evidence. Original COBOL capture additionally needs captureEvidence.\n- Adopt with `cobolcs adopt DIRECTORY --out NEW_DIRECTORY` after real tests pass. In the adopted solution all sources, including .g.cs, can be edited. Provenance is history, not an edit lock. Regenerate elsewhere and port a diff.\n- Keep LICENSE and THIRD-PARTY-NOTICES. Source COBOL distribution terms remain applicable; not all derived source is automatically MIT. Review permitted user code, fixtures and baselines for secrets before distribution.\n\n## Public type mapping\n\n" + string.Join("\n", programs.Select(p => $"- `{p.S("id")}`: `using Executor = CobolCS.Generated.{Identifier(p.S("id"))}Executor;`")) + "\n\nManaged source-map.json maps original names and symbol IDs to members. Editable scenarios use actual aliases.\n";
        files["Tests/Support/Review.g.cs"] = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Templates/Review.g.cs.txt"));
        var registration = new StringBuilder("namespace CobolCS.Generated;\n\n/// <summary>利用者の C# シナリオ登録。名前の重複は拒否します。</summary>\ninternal static class ScenarioRegistration\n{\n    /// <summary>実行ごとの options を各シナリオへ渡します。</summary>\n    /// <param name=\"registry\">登録先。</param>\n    internal static void Register(ScenarioRegistry registry)\n    {\n");
        var generatedRegistration = new StringBuilder(Header() + "internal static class GeneratedScenarioRegistration\n{\ninternal static void Register(ScenarioRegistry registry)\n{\n");
        var review = new JsonArray();
        foreach (var program in programs)
        {
            var id = program.S("id");
            var name = Identifier(id);
            generatedRegistration.AppendLine($"        registry.Register({Quote(id)}, \"sample\", {name}Scenarios.Sample);");
            files[$"UserCode/Scenarios/{name}Scenarios.cs"] = $"using Executor = CobolCS.Generated.{name}Executor;\nusing Input = CobolCS.Generated.{name}Input;\nusing Result = CobolCS.Generated.{name}Result;\n\nnamespace CobolCS.Generated;\n\n/// <summary>入力と応答を毎回作る編集可能なシナリオです。</summary>\npublic static class {name}Scenarios\n{{\n    /// <summary>ACCEPT と InitialState を編集してください。未実装依存は到達時に失敗します。</summary>\n    /// <param name=\"options\">この実行だけの制限。</param>\n    /// <returns>業務同値性を証明しない型付き観測結果。</returns>\n    public static Result Sample(ExecutionOptions options)\n    {{\n        var input = new Input();\n        return new Executor().Run(input, options: options);\n    }}\n}}\n";
            var caseId = name + "_Sample";
            files[$"Tests/Cases/{name}Tests.cs"] = $"using Microsoft.VisualStudio.TestTools.UnitTesting;\n\nnamespace CobolCS.Generated.Tests;\n\n/// <summary>レビュー済みの独立期待値を入力してから回帰試験にします。</summary>\n[TestClass]\npublic sealed class {name}Tests\n{{\n    /// <summary>初回は必ず失敗します。表示・状態・外部依頼の意味を確認してください。</summary>\n    [TestMethod]\n    public void {caseId}()\n    {{\n        Assert.Fail(\"Unreviewed: provide meaningful assertions and baseline provenance before adoption.\");\n    }}\n}}\n";
            files[$"Tests/Baselines/{name}.json"] = "{\"baselineKind\":\"csharp-characterization\",\"reviewReason\":\"UNREVIEWED\",\"expected\":null}\n";
            review.Add(new JsonObject { ["programId"] = id, ["caseId"] = caseId, ["baselineKind"] = "csharp-characterization", ["reviewed"] = false, ["callStubMode"] = "Throw", ["hasNoOpCalls"] = false, ["files"] = new JsonObject { [$"Tests/Cases/{name}Tests.cs"] = Generator.Hash(Encoding.UTF8.GetBytes(files[$"Tests/Cases/{name}Tests.cs"])), [$"Tests/Baselines/{name}.json"] = Generator.Hash(Encoding.UTF8.GetBytes(files[$"Tests/Baselines/{name}.json"])) } });
        }

        generatedRegistration.AppendLine("}\n}");
        files["Api/GeneratedScenarioRegistration.g.cs"] = generatedRegistration.ToString();
        registration.AppendLine("    }\n}");
        files["UserCode/ScenarioRegistration.cs"] = registration.ToString();
        files["Tests/review.json"] = Generator.Json(new JsonObject { ["schemaVersion"] = 1, ["cases"] = review });
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
                <ProjectReference Include="../CobolCS.Generated.csproj" />
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.0.1" />
                <PackageReference Include="MSTest.TestFramework" Version="4.0.2" />
                <PackageReference Include="MSTest.TestAdapter" Version="4.0.2" />
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
        var previous = string.Empty;
        foreach (var line in source.Split('\n'))
        {
            if (line.TrimStart().StartsWith("public ", StringComparison.Ordinal) && !previous.TrimStart().StartsWith("///", StringComparison.Ordinal))
            {
                result.AppendLine("/// <summary>型付き実行境界の値または操作。入力は PIC 検査を受け、結果は実行ごとに分離します。</summary>");
            }

            result.AppendLine(line);
            previous = line;
        }

        return result.ToString();
    }
}
