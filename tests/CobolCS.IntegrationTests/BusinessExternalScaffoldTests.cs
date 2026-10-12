using System.Text.Json.Nodes;
using CobolCS.Cli;
using CobolCS.Frontend;
using CobolCS.Runtime;
using CobolCS.Tools;
using Xunit;

namespace CobolCS.IntegrationTests;

/// <summary>雛形の FILE EOF と SQL no-data/指示変数を実際の型付き fake で確認します。</summary>
public sealed class BusinessExternalScaffoldTests
{
    /// <summary>省略更新と明示状態を保持し、余剰・不足・順序・payload の違いを失敗にします。</summary>
    /// <returns>生成先 xUnit の成功と負のケース。</returns>
    [Fact]
    public async Task FileAndSqlUseStrictTypedBusinessFakes()
    {
        using var folder = new TestDirectory();
        var source = Path.Combine(folder.Path, "source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "file.cbl"), "IDENTIFICATION DIVISION. PROGRAM-ID. FILE-BOUNDARY. ENVIRONMENT DIVISION. INPUT-OUTPUT SECTION. FILE-CONTROL. SELECT F ASSIGN TO 'SYNTHETIC' ORGANIZATION LINE SEQUENTIAL FILE STATUS ST. DATA DIVISION. FILE SECTION. FD F. 01 REC PIC X(3). WORKING-STORAGE SECTION. 01 ST PIC XX. PROCEDURE DIVISION. READ F AT END DISPLAY 'EOF' NOT AT END DISPLAY 'ROW' END-READ DISPLAY ST GOBACK.");
        File.Copy(Path.Combine(Workspace.Root, "examples/WORKFLOW/SQLCASE.cbl"), Path.Combine(source, "sql.cbl"));
        var root = Path.Combine(folder.Path, "generated");
        Generator.Generate(FrontendAnalyzer.Analyze(source, FrontendConfiguration.Parse("{\"testGeneration\":{\"maxAttempts\":4}}")), root, "scaffold");
        var programs = Workspace.Read(Path.Combine(root, "reports/business-test-candidates.json")).A("programs").ToArray();
        foreach (var program in programs)
        {
            var sql = program.S("programId") == "SQLCASE";
            var operation = program.A("operations").Single();
            Assert.True(operation.B("supported"));
            var requestMap = new JsonObject(operation.A("requestFields").Select(f => KeyValuePair.Create<string, JsonNode?>(f.S("member"), JsonValue.Create(f.S("member")))));
            var responseMap = new JsonObject(operation.A("responseFields").Select(f => KeyValuePair.Create<string, JsonNode?>(f.S("member"), JsonValue.Create(f.S("member"))))) { ["status"] = "StatusCode", ["outcome"] = "Outcome" };
            var mapping = new JsonObject { ["schemaVersion"] = 1, ["programId"] = program.S("programId"), ["candidateHash"] = program.S("candidateHash"), ["contractId"] = sql ? "sql-boundary-v1" : "file-boundary-v1", ["version"] = 1, ["selectionReason"] = "Explicit synthetic operation with provisional field keys; EOF/no-data preserves receiving values", ["inputs"] = new JsonArray(), ["results"] = new JsonArray(new JsonObject { ["key"] = "display", ["source"] = "display" }), ["operations"] = new JsonArray(new JsonObject { ["key"] = sql ? "db.lookup" : "file.read", ["statementId"] = operation.S("statementId"), ["request"] = requestMap, ["response"] = responseMap }), ["termination"] = new JsonObject { ["Goback"] = "returned" } };
            var mappingPath = Path.Combine(folder.Path, sql ? "sql.json" : "file.json");
            Workspace.Write(mappingPath, mapping);
            Assert.Equal(0, BusinessScaffolding.Run(root, program.S("programId"), mappingPath));
            var report = Directory.GetFiles(Path.Combine(root, "reports/business-scaffolds"), "*.json").Where(p => !p.EndsWith("-review-files.json", StringComparison.Ordinal)).Select(Workspace.Read).Single(r => r.S("contractId") == mapping.S("contractId"));
            var files = report.A("files").Select(f => f.GetValue<string>()).ToArray();
            var registration = Path.Combine(root, "UserCode/Business/BusinessRegistration.cs");
            var example = File.ReadAllText(Path.Combine(root, files.Single(p => p.EndsWith("registration.cs.txt", StringComparison.Ordinal)))).Trim();
            File.WriteAllText(registration, File.ReadAllText(registration).Replace("// adapters.Add(\"payment-v1\", () => new PaymentAdapter());", example + "\n\n        // adapters.Add(\"payment-v1\", () => new PaymentAdapter());", StringComparison.Ordinal));
            var item = Workspace.Read(Path.Combine(root, files.Single(p => p.EndsWith("case.json.txt", StringComparison.Ordinal))));
            item["caseId"] = program.S("programId");
            item["reviewReason"] = "Independent fixed EOF/no-data outcome; receiving record/N remains unchanged";
            item["provenance"] = new JsonObject { ["specification"] = "Synthetic fixed record, SQL output and null-indicator contract; no actual connection" };
            var request = new JsonObject();
            foreach (var field in operation.A("requestFields"))
            {
                request[field.S("member")] = sql ? field.S("member") switch { "Host0" => "17", "Indicator0" => "0", "Host1" => "1", _ => throw new InvalidOperationException("Unexpected SQL fixture field") } : "   ";
            }

            var response = new JsonObject { ["status"] = sql ? "100" : "10", ["outcome"] = "End" };
            foreach (var field in operation.A("responseFields"))
            {
                response[field.S("member")] = field.S("member") switch { "SqlCode" => "100", "SqlState" => "02000", "FileStatus" => "10", "Indicator0" => "-1", _ => null };
            }

            var key = sql ? "db.lookup" : "file.read";
            item["responses"] = new JsonArray(new JsonObject { ["operation"] = key, ["request"] = request, ["response"] = response });
            item["expected"] = new JsonObject { ["result"] = new JsonObject { ["display"] = sql ? new JsonArray("17") : new JsonArray("EOF", "10") }, ["effects"] = new JsonArray(new JsonObject { ["operation"] = key, ["request"] = request.DeepClone() }), ["termination"] = "returned" };
            Directory.CreateDirectory(Path.Combine(root, "Tests/BusinessCases"));
            Workspace.Write(Path.Combine(root, "Tests/BusinessCases", sql ? "sql.json" : "file.json"), item);
        }

        foreach (var stub in Workspace.Read(Path.Combine(root, "reports/stubs.json")).AsArray())
        {
            var path = Path.Combine(root, stub!.S("file"));
            File.WriteAllText(path, File.ReadAllText(path).Replace("throw new NotImplementedException", "throw new InvalidOperationException", StringComparison.Ordinal));
        }

        _ = await ProcessRunner.Run("dotnet", ["test", "Tests/CobolCS.Generated.Tests.csproj", "--filter", "Category=Business"], root);
        var casePath = Path.Combine(root, "Tests/BusinessCases/file.json");
        var original = Workspace.Read(casePath);
        foreach (var mutate in new Action<JsonNode>[]
        {
            c => c["responses"]!.AsArray().Clear(),
            c => c["responses"]!.AsArray().Add(c["responses"]![0]!.DeepClone()),
            c => c["responses"]![0]!["operation"] = "db.lookup",
            c =>
            {
                var payload = c["responses"]![0]!["request"]!.AsObject();
                payload[payload.First().Key] = "BAD";
            },
        })
        {
            var wrong = original.DeepClone();
            mutate(wrong);
            Workspace.Write(casePath, wrong);
            await Assert.ThrowsAsync<InvalidOperationException>(() => ProcessRunner.Run("dotnet", ["test", "Tests/CobolCS.Generated.Tests.csproj", "--no-build", "--filter", "Category=Business"], root));
        }
    }
}
