using CobolCS.Frontend.Parsing;
using Xunit;
using static CobolCS.Frontend.Parsing.Values;
using static CobolCS.IntegrationTests.RegressionSupport;

namespace CobolCS.IntegrationTests;

/// <summary>既存の test_workflow_noop の入力、期待値、失敗条件を保持する.NET回帰テストです。</summary>
public sealed class ExplorationTests
{
    private readonly V state = new();

    /// <summary>partial trace and default throw の回帰条件を検証します。</summary>
    [Fact]
    public void TestPartialTraceAndDefaultThrow()
    {
        V base_639 = Nil;
        V body_640 = Nil;
        V config_641 = Nil;
        V end_642 = Nil;
        V failed_643 = Nil;
        V implemented_failure_644 = Nil;
        V ordinary_645 = Nil;
        V output_646 = Nil;
        V program_647 = Nil;
        V source_648 = Nil;
        V start_649 = Nil;
        V stub_650 = Nil;
        V temp_651 = Nil;
        V text_652 = Nil;
        using var folder653 = new TestDirectory();
        temp_651 = PathValue(folder653.Path);
        base_639 = PathValue(Text(temp_651));
        foreach (var item654 in Items(ListOf(ListOf(S("LOOP"), S("PERFORM UNTIL N > 0 CALL \"WAIT\" USING N END-CALL END-PERFORM GOBACK.")), ListOf(S("ERROR"), S("CALL \"WAIT\" USING N END-CALL COMPUTE N = 1 / 0 GOBACK.")))))
        {
            var unpack655 = item654;
            program_647 = unpack655[I(0)];
            body_640 = unpack655[I(1)];
            source_648 = Divide(base_639, TestAdd(program_647, S(".cbl")));
            _ = TestCall(source_648, "write_text", TestAdd(TestAdd(TestAdd(S("IDENTIFICATION DIVISION. PROGRAM-ID. "), program_647), S(".\nDATA DIVISION. WORKING-STORAGE SECTION.\n01 N PIC 99 VALUE ZERO.\nPROCEDURE DIVISION.\n")), body_640));
            output_646 = Divide(base_639, program_647);
            config_641 = Divide(base_639, S("config.json"));
            _ = TestCall(config_641, "write_text", S("{\"maxSteps\":20}"));
            _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("generate") }).Concat(new V[] { source_648 }).Concat(new V[] { S("--config") }).Concat(new V[] { config_641 }).Concat(new V[] { S("--output") }).Concat(new V[] { output_646 })), I(0));
            failed_643 = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("run") }).Concat(new V[] { output_646 }).Concat(new V[] { S("--program") }).Concat(new V[] { program_647 }).Concat(new V[] { S("--call-stubs") }).Concat(new V[] { S("noop") })), I(2));
            _ = CheckIn(S("generated-call-no-op"), TestMember(failed_643, "stderr"));
            _ = CheckIn(S("WAIT"), TestMember(failed_643, "stderr"));
            _ = CheckIn(Truth(B(TestEqual(program_647, S("LOOP")))) ? S("Execution limit") : S("DivideByZeroException"), TestMember(failed_643, "stderr"));
            ordinary_645 = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("run") }).Concat(new V[] { output_646 }).Concat(new V[] { S("--program") }).Concat(new V[] { program_647 })), I(2));
            _ = CheckIn(S("NotImplementedException"), TestMember(ordinary_645, "stderr"));
            _ = CheckNotIn(S("generated-call-no-op"), TestMember(ordinary_645, "stderr"));
            stub_650 = Next(TestCall(Divide(output_646, S("Stubs")), "rglob", S("Dependency_*.cs")));
            text_652 = TestCall(stub_650, "read_text");
            start_649 = TestCall(text_652, "index", S("        if (Options.CallStubMode"));
            end_642 = TestCall(text_652, "index", S("        throw new NotImplementedException"), start_649);
            text_652 = TestAdd(text_652[Slice(Nil, start_649, Nil)], text_652[Slice(end_642, Nil, Nil)]);
            text_652 = TestCall(text_652, "replace", S("throw new NotImplementedException"), S("throw new InvalidOperationException"));
            _ = TestCall(stub_650, "write_text", text_652);
            implemented_failure_644 = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("run") }).Concat(new V[] { output_646 }).Concat(new V[] { S("--program") }).Concat(new V[] { program_647 }).Concat(new V[] { S("--call-stubs") }).Concat(new V[] { S("noop") })), I(2));
            _ = CheckIn(S("InvalidOperationException"), TestMember(implemented_failure_644, "stderr"));
            _ = CheckNotIn(S("generated-call-no-op"), TestMember(implemented_failure_644, "stderr"));
        }
    }

    /// <summary>cancel after noop retains trace の回帰条件を検証します。</summary>
    [Fact]
    public void TestCancelAfterNoopRetainsTrace()
    {
        V base_656 = Nil;
        V helper_657 = Nil;
        V insertion_658 = Nil;
        V mapping_659 = Nil;
        V name_660 = Nil;
        V output_661 = Nil;
        V scenario_662 = Nil;
        V source_663 = Nil;
        V temp_664 = Nil;
        V tests_665 = Nil;
        V text_666 = Nil;
        using var folder667 = new TestDirectory();
        temp_664 = PathValue(folder667.Path);
        base_656 = PathValue(Text(temp_664));
        source_663 = Divide(base_656, S("cancel.cbl"));
        _ = TestCall(source_663, "write_text", S("IDENTIFICATION DIVISION. PROGRAM-ID. CANCELCASE. DATA DIVISION. WORKING-STORAGE SECTION. 01 N PIC 99. PROCEDURE DIVISION. CALL \"WAIT\" USING N END-CALL GOBACK."));
        output_661 = Divide(base_656, S("generated"));
        _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("generate") }).Concat(new V[] { source_663 }).Concat(new V[] { S("--output") }).Concat(new V[] { output_661 })), I(0));
        mapping_659 = ReadJson(TestCall(Divide(output_661, S("reports/source-map.json")), "read_text"));
        name_660 = TestCall(Next(Sequence(from item668 in Items(mapping_659) let m_669 = item668 where Truth(B(Contains(m_669, S("executorType")))) select m_669[S("executorType")])), "removesuffix", S("Executor"));
        helper_657 = Next(TestCall(Divide(output_661, S("Stubs")), "rglob", S("NoOp_*.cs")));
        _ = TestCall(helper_657, "write_text", TestCall(TestCall(helper_657, "read_text"), "replace", S("        return new("), S("        CancelProbe.Callback?.Invoke();\n        return new(")));
        _ = TestCall(Divide(output_661, S("UserCode/CancelProbe.cs")), "write_text", S("namespace CobolCS.Generated;\n\n/// <summary>テストで no-op helper の戻り直前を確定的に観測します。</summary>\ninternal static class CancelProbe\n{\n    /// <summary>Gets or sets 戻り直前にキャンセルを通知するテスト操作。</summary>\n    internal static Action? Callback { get; set; }\n}\n"));
        scenario_662 = Divide(output_661, AddText(S("UserCode/Scenarios/"), Format(name_660, string.Empty), S("Scenarios.cs")));
        text_666 = TestCall(scenario_662, "read_text");
        insertion_658 = AddText(S("\n    /// <summary>no-op 完了直後の協調キャンセルを発生させます。</summary>\n    /// <returns>呼出し履歴を保持したキャンセル例外。</returns>\n    public static OperationCanceledException CancelAfterCall()\n    {\n        using var source = new CancellationTokenSource();\n        CancelProbe.Callback = source.Cancel;\n        try\n        {\n            new "), Format(name_660, string.Empty), S("Executor().Run(new(), options: new ExecutionOptions { CallStubMode = CallStubMode.NoOp }, cancellationToken: source.Token);\n            throw new InvalidOperationException(\"Expected cancellation\");\n        }\n        catch (OperationCanceledException error)\n        {\n            return error;\n        }\n        finally\n        {\n            CancelProbe.Callback = null;\n        }\n    }\n"));
        text_666 = TestAdd(TestAdd(text_666[Slice(Nil, TestCall(text_666, "rfind", S("}")), Nil)], insertion_658), S("}\n"));
        _ = TestCall(scenario_662, "write_text", text_666);
        tests_665 = AddText(S("using Xunit;\n\nnamespace CobolCS.Generated.Tests;\n\n/// <summary>探索の途中記録をキャンセル診断に保持します。</summary>\npublic sealed class "), Format(name_660, string.Empty), S("Tests\n{\n    /// <summary>待機や短い sleep に頼らず応答直後をキャンセルします。</summary>\n    [Fact]\n    public void CancelAfterCall()\n    {\n        var error = "), Format(name_660, string.Empty), S("Scenarios.CancelAfterCall();\n        var trace = (IReadOnlyList<DependencyTrace>)error.Data[\"DependencyTrace\"]!;\n        Assert.Equal(\"generated-call-no-op\", trace.Single().Implementation);\n        Assert.Equal(\"WAIT\", trace.Single().Target);\n        Assert.NotNull(error.Data[\"SourceLocation\"]);\n    }\n}\n"));
        _ = TestCall(Divide(output_661, AddText(S("Tests/Cases/"), Format(name_660, string.Empty), S("Tests.cs"))), "write_text", tests_665);
        _ = RunChecked(ListOf(S("dotnet"), S("test"), Divide(output_661, S("Tests/CobolCS.Generated.Tests.csproj"))), I(0));
    }
}
