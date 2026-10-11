using CobolCS.Frontend.Parsing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CobolCS.Frontend.Parsing.Values;
using static CobolCS.IntegrationTests.RegressionSupport;

namespace CobolCS.IntegrationTests;

/// <summary>既存の test_workflow の入力、期待値、失敗条件を保持する.NET回帰テストです。</summary>
[TestClass]
public sealed class WorkflowTests
{
    private readonly V state = new();

    /// <summary>public api and adoption の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestPublicApiAndAdoption()
    {
        V adopted_562 = Nil;
        V base_563 = Nil;
        V content_564 = Nil;
        V discovered_565 = Nil;
        V fixture_566 = Nil;
        V harness_567 = Nil;
        V judgments_568 = Nil;
        V legacy_569 = Nil;
        V mapping_570 = Nil;
        V match_571 = Nil;
        V method_572 = Nil;
        V modified_573 = Nil;
        V name_574 = Nil;
        V net_575 = Nil;
        V original_576 = Nil;
        V output_577 = Nil;
        V p_578 = Nil;
        V path_579 = Nil;
        V program_580 = Nil;
        V scenario_581 = Nil;
        V service_582 = Nil;
        V source_583 = Nil;
        V temp_584 = Nil;
        V tests_585 = Nil;
        V text_586 = Nil;
        V wrapped_587 = Nil;
        using var folder588 = new TestDirectory();
        temp_584 = PathValue(folder588.Path);
        base_563 = PathValue(Text(temp_584));
        output_577 = Divide(base_563, S("payment"));
        source_583 = Divide(Root, S("examples/WORKFLOW/PAYMENT.cbl"));
        _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("generate") }).Concat(new V[] { source_583 }).Concat(new V[] { S("--output") }).Concat(new V[] { output_577 })), I(0));
        tests_585 = Divide(output_577, S("Tests/CobolCS.Generated.Tests.csproj"));
        _ = RunChecked(ListOf(S("dotnet"), S("test"), tests_585), I(1));
        _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("adopt") }).Concat(new V[] { output_577 }).Concat(new V[] { S("--output") }).Concat(new V[] { Divide(base_563, S("unreviewed")) })), I(1));
        _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(ToolCli)).Concat(new V[] { S("prepare-workflow") }).Concat(new V[] { S("--out") }).Concat(new V[] { output_577 })), I(0));
        mapping_570 = ReadJson(TestCall(Divide(output_577, S("reports/source-map.json")), "read_text"));
        name_574 = TestCall(Next(Sequence(from item589 in Items(mapping_570) let m_590 = item589 where Truth(B(Contains(m_590, S("executorType")))) select m_590[S("executorType")])), "removesuffix", S("Executor"));
        net_575 = Next(Sequence(from item591 in Items(mapping_570) let m_592 = item591 where Truth(B(TestEqual(TestCall(m_592, "get", S("originalName")), S("NET")))) select TestCall(m_592[S("generatedName")], "split", S("."))[Subtract(I(0), I(1))]));
        method_572 = ReadJson(TestCall(Divide(output_577, S("reports/stubs.json")), "read_text"))[I(0)][S("method")];
        harness_567 = TestCall(TestCall(TestCall(S("using CobolCS.Runtime;\nusing Microsoft.VisualStudio.TestTools.UnitTesting;\nusing Executor = CobolCS.Generated.NAMEExecutor;\nusing Fake = CobolCS.Generated.NAMEScriptedDependencies;\nusing Input = CobolCS.Generated.NAMEInput;\nusing Response = CobolCS.Generated.NAMEMETHODResponse;\n\nnamespace CobolCS.Generated.Tests;\n\n/// <summary>実行の分離と明示的な失敗を公開境界から検証します。</summary>\n[TestClass]\npublic sealed class ExecutionBoundaryTests\n{\n    /// <summary>成功・失敗・キャンセル後も同じ facade を再利用できます。</summary>\n    [TestMethod]\n    public void RepeatedRuns()\n    {\n        var executor = new Executor();\n        var input = new Input { AcceptInputs = new[] { \"100.00\" } };\n        var fake = new Fake();\n        fake.ExpectMETHOD(r => r.Argument0 == new Number(100), new Response(\"00\", DependencyOutcome.Normal, CobolValue.Numeric(\"3.50\"), new Number(0)));\n        var first = executor.Run(input, fake);\n        Assert.ThrowsExactly<ExecutionException>(() => executor.Run(input));\n        using var canceled = new CancellationTokenSource();\n        canceled.Cancel();\n        Assert.ThrowsExactly<OperationCanceledException>(() => executor.Run(input, cancellationToken: canceled.Token));\n        var nextFake = new Fake();\n        nextFake.ExpectMETHOD(r => r.Argument1 == new Number(0), new Response(\"00\", DependencyOutcome.Normal, new Number(4), new Number(0)));\n        var second = executor.Run(input, nextFake);\n        Assert.AreEqual(CobolValue.Numeric(\"96.50\"), first.Snapshot.NET);\n        Assert.AreEqual(new Number(96), second.Snapshot.NET);\n        CollectionAssert.AreEqual(new[] { \"09650\" }, first.Display.ToArray());\n        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<string>)first.Display)[0] = \"MUTATED\");\n        Assert.ThrowsExactly<StepLimitException>(() => executor.Run(input, options: new ExecutionOptions { MaxSteps = 1 }));\n        Assert.ThrowsExactly<ArgumentException>(() => executor.Run(input, options: new ExecutionOptions { MaxSteps = 0 }));\n    }\n\n    /// <summary>探索が fake の失敗を迂回せず、既定値へ漏れないことを確認します。</summary>\n    [TestMethod]\n    public void ExplicitExploration()\n    {\n        var executor = new Executor();\n        var input = new Input { AcceptInputs = new[] { \"100.00\" } };\n        var options = new ExecutionOptions { CallStubMode = CallStubMode.NoOp };\n        var result = executor.Run(input, options: options);\n        Assert.IsTrue(result.HasNoOpCalls);\n        Assert.AreEqual(\"NOOP\", result.NoOpCalls.Single().StatusCode);\n        Assert.AreEqual(new Number(100), result.Snapshot.NET);\n        Assert.ThrowsExactly<ExecutionException>(() => executor.Run(input));\n        Assert.ThrowsExactly<ExecutionException>(() => executor.Run(input, new Fake(), options));\n        var wrong = new Fake();\n        wrong.ExpectMETHOD(_ => false, new Response(\"00\", DependencyOutcome.Normal));\n        Assert.ThrowsExactly<ExecutionException>(() => executor.Run(input, wrong, options));\n        var extra = new Fake();\n        extra.ExpectMETHOD(_ => true, new Response(\"00\", DependencyOutcome.Normal, Returning: new Number(0)));\n        extra.ExpectMETHOD(_ => true, new Response(\"00\", DependencyOutcome.Normal));\n        Assert.ThrowsExactly<ExecutionException>(() => executor.Run(input, extra));\n    }\n\n    /// <summary>外部応答直後のキャンセルと不正な数値を拒否します。</summary>\n    [TestMethod]\n    public void ResponseValidation()\n    {\n        var executor = new Executor();\n        var input = new Input { AcceptInputs = new[] { \"100.00\" } };\n        using var canceled = new CancellationTokenSource();\n        var fake = new Fake();\n        fake.ExpectMETHOD(\n            _ =>\n            {\n                canceled.Cancel();\n                return true;\n            },\n            new Response(\"00\", DependencyOutcome.Normal));\n        Assert.ThrowsExactly<OperationCanceledException>(() => executor.Run(input, fake, cancellationToken: canceled.Token));\n        var invalid = new Fake();\n        invalid.ExpectMETHOD(_ => true, new Response(\"00\", DependencyOutcome.Normal, default(Number), new Number(0)));\n        Assert.ThrowsExactly<ExecutionException>(() => executor.Run(input, invalid));\n    }\n}\n"), "replace", S("NAME"), name_574), "replace", S("METHOD"), method_572), "replace", S("Snapshot.NET"), TestAdd(S("Snapshot."), net_575));
        _ = TestCall(Divide(output_577, S("Tests/Cases/ExecutionBoundaryTests.cs")), "write_text", harness_567);
        _ = RunChecked(ListOf(S("dotnet"), S("test"), tests_585, S("--logger"), S("trx")), I(0));
        discovered_565 = TestMember(RunChecked(ListOf(S("dotnet"), S("test"), tests_585, S("--no-restore"), S("--list-tests")), I(0)), "stdout");
        _ = CheckIn(S("RepeatedRuns"), discovered_565);
        fixture_566 = Divide(base_563, S("input.json"));
        _ = TestCall(fixture_566, "write_text", S("{\"inputs\":[\"100.00\"]}"));
        legacy_569 = ReadJson(TestMember(RunChecked(ListOf(S("dotnet"), S("run"), S("--project"), output_577, S("--no-build"), S("--"), S("PAYMENT"), fixture_566, S("--call-stubs"), S("noop")), I(0)), "stdout"));
        scenario_581 = ReadJson(TestMember(RunChecked(ListOf(S("dotnet"), S("run"), S("--project"), output_577, S("--no-build"), S("--"), S("PAYMENT"), S("--scenario"), S("noop-explore"), S("--call-stubs"), S("noop")), I(0)), "stdout"));
        _ = CheckEqual(legacy_569, scenario_581);
        wrapped_587 = ReadJson(TestMember(RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("run") }).Concat(new V[] { output_577 }).Concat(new V[] { S("--program") }).Concat(new V[] { S("PAYMENT") }).Concat(new V[] { S("--scenario") }).Concat(new V[] { S("noop-explore") }).Concat(new V[] { S("--call-stubs") }).Concat(new V[] { S("noop") })), I(0)), "stdout"));
        _ = CheckEqual(wrapped_587, legacy_569);
        judgments_568 = ReadJson(TestCall(Divide(output_577, S("manifest.json")), "read_text"))[S("judgments")];
        _ = CheckEqual(judgments_568[S("executionKind")], S("call-no-op-exploration"));
        _ = CheckTrue(judgments_568[S("hasNoOpCalls")]);
        _ = CheckEqual(legacy_569[S("display")], ListOf(S("10000")));
        _ = CheckEqual(legacy_569[S("metadata")][S("noOpCalls")][I(0)][S("Target")], S("FEE-SERVICE"));
        _ = CheckEqual(Length(legacy_569[S("metadata")][S("noOpCalls")][I(0)][S("Arguments")]), I(2));
        _ = CheckEqual(legacy_569[S("metadata")][S("noOpCalls")][I(0)][S("Arguments")][I(0)][S("Numeric")], Put(Put(Dict(), S("numerator"), S("100")), S("denominator"), S("1")));
        original_576 = MakeDictionary(Sequence(from item593 in Items(TestCall(output_577, "rglob", S("*"))) let p_594 = item593 where Truth(And(TestCall(p_594, "is_file"), () => Or(B(Contains(TestMember(p_594, "parts"), S("UserCode"))), () => Or(B(Contains(TestMember(p_594, "parts"), S("Cases"))), () => Or(B(Contains(TestMember(p_594, "parts"), S("Baselines"))), () => B(TestEqual(TestMember(p_594, "name"), S("review.json")))))))) select ListOf(TestStringValue(TestCall(p_594, "relative_to", output_577)), TestCall(p_594, "read_bytes"))));
        _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("generate") }).Concat(new V[] { source_583 }).Concat(new V[] { S("--output") }).Concat(new V[] { output_577 })), I(0));
        foreach (var item595 in Items(TestCall(original_576, "items")))
        {
            var unpack596 = item595;
            path_579 = unpack596[I(0)];
            content_564 = unpack596[I(1)];
            _ = CheckEqual(content_564, TestCall(Divide(output_577, path_579), "read_bytes"));
        }

        adopted_562 = Divide(base_563, S("adopted"));
        _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("adopt") }).Concat(new V[] { output_577 }).Concat(new V[] { S("--output") }).Concat(new V[] { adopted_562 })), I(0));
        _ = CheckFalse(TestCall(Divide(adopted_562, S("manifest.json")), "exists"));
        _ = CheckFalse(TestCall(Divide(adopted_562, S("reports")), "exists"));
        _ = CheckTrue(TestCall(Divide(adopted_562, S("provenance/cobolcs-origin.json")), "exists"));
        _ = CheckFalse(Any(Sequence(from item597 in Items(TestCall(adopted_562, "rglob", S("*"))) let p_598 = item597 select B(Contains(SetOf(S("bin"), S("obj"), S("TestResults")), TestMember(p_598, "name"))))));
        _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("generate") }).Concat(new V[] { source_583 }).Concat(new V[] { S("--output") }).Concat(new V[] { adopted_562 })), I(2));
        _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("adopt") }).Concat(new V[] { output_577 }).Concat(new V[] { S("--output") }).Concat(new V[] { Divide(output_577, S("nested")) })), I(2));
        _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("adopt") }).Concat(new V[] { output_577 }).Concat(new V[] { S("--output") }).Concat(new V[] { adopted_562 })), I(2));
        program_580 = Next(TestCall(Divide(adopted_562, S("Programs")), "glob", S("*Program.g.cs")));
        text_586 = TestCall(program_580, "read_text");
        match_571 = RegexSearch(S("CobolValue.Numeric\\(\\(CobolValue.Numeric\\((state\\.\\w+)\\) - CobolValue.Numeric\\((state\\.\\w+)\\)\\)\\)"), text_586);
        _ = CheckIsNotNone(match_571);
        modified_573 = TestAdd(TestAdd(TestAdd(TestAdd(TestAdd(TestAdd(text_586[Slice(Nil, TestCall(match_571, "start"), Nil)], S("PaymentService.Net(")), TestCall(match_571, "group", I(1))), S(", ")), TestCall(match_571, "group", I(2))), S(")")), text_586[Slice(TestCall(match_571, "end"), Nil, Nil)]);
        _ = TestCall(program_580, "write_text", modified_573);
        service_582 = Divide(adopted_562, S("UserCode/PaymentService.cs"));
        _ = TestCall(service_582, "write_text", S("using CobolCS.Runtime;\n\nnamespace CobolCS.Generated;\n\n/// <summary>採用後に抽出した通常の手書き計算です。</summary>\ninternal static class PaymentService\n{\n    /// <summary>手数料を差し引きます。</summary>\n    /// <param name=\"gross\">受入金額。</param>\n    /// <param name=\"fee\">明示された手数料。</param>\n    /// <returns>差引額。格納側で PIC 検査します。</returns>\n    internal static Number Net(Number gross, Number fee) => gross - fee;\n}\n"));
        _ = RemoveTree(output_577);
        _ = RunChecked(ListOf(S("dotnet"), S("test"), Divide(adopted_562, S("Tests/CobolCS.Generated.Tests.csproj"))), I(0));
        _ = TestCall(service_582, "write_text", TestCall(TestCall(service_582, "read_text"), "replace", S("gross - fee"), S("gross + fee")));
        _ = RunChecked(ListOf(S("dotnet"), S("test"), Divide(adopted_562, S("Tests/CobolCS.Generated.Tests.csproj")), S("--no-restore")), I(1));
    }
}
