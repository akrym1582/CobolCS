using CobolCS.Frontend.Parsing;
using Xunit;
using static CobolCS.Frontend.Parsing.Values;
using static CobolCS.IntegrationTests.RegressionSupport;

namespace CobolCS.IntegrationTests;

/// <summary>既存の test_workflow_external の入力、期待値、失敗条件を保持する.NET回帰テストです。</summary>
public sealed class ExternalWorkflowTests
{
    private readonly V state = new();

    /// <summary>file and sql の回帰条件を検証します。</summary>
    [Fact]
    public void TestFileAndSql()
    {
        V base_599 = Nil;
        V body_600 = Nil;
        V contract_601 = Nil;
        V fields_602 = Nil;
        V m_603 = Nil;
        V mapping_604 = Nil;
        V method_605 = Nil;
        V name_606 = Nil;
        V output_607 = Nil;
        V program_608 = Nil;
        V read_609 = Nil;
        V record_610 = Nil;
        V stubs_611 = Nil;
        V temp_612 = Nil;
        V tests_613 = Nil;
        V write_614 = Nil;
        using var folder615 = new TestDirectory();
        temp_612 = PathValue(folder615.Path);
        base_599 = PathValue(Text(temp_612));
        foreach (var item616 in Items(ListOf(S("FILECASE"), S("SQLCASE"))))
        {
            program_608 = item616;
            output_607 = Divide(base_599, program_608);
            _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("generate") }).Concat(new V[] { Divide(Root, AddText(S("examples/WORKFLOW/"), Format(program_608, string.Empty), S(".cbl"))) }).Concat(new V[] { S("--output") }).Concat(new V[] { output_607 })), I(0));
            mapping_604 = ReadJson(TestCall(Divide(output_607, S("reports/source-map.json")), "read_text"));
            name_606 = TestCall(Next(Sequence(from item617 in Items(mapping_604) let m_618 = item617 where Truth(B(Contains(m_618, S("executorType")))) select m_618[S("executorType")])), "removesuffix", S("Executor"));
            fields_602 = MakeDictionary(Sequence(from item619 in Items(mapping_604) let m_620 = item619 where Truth(B(Contains(m_620, S("symbolId")))) select ListOf(TestCall(m_620[S("originalName")], "split", S(" OF "))[I(0)], TestCall(m_620[S("generatedName")], "split", S("."))[Subtract(I(0), I(1))])));
            stubs_611 = ReadJson(TestCall(Divide(output_607, S("reports/stubs.json")), "read_text"));
            if (Truth(B(TestEqual(program_608, S("FILECASE")))))
            {
                read_609 = Next(Sequence(from item621 in Items(stubs_611) let s_622 = item621 where Truth(B(TestEqual(s_622[S("statement")][S("operation")], S("read")))) select s_622[S("method")]));
                write_614 = Next(Sequence(from item623 in Items(stubs_611) let s_624 = item623 where Truth(B(TestEqual(s_624[S("statement")][S("operation")], S("write")))) select s_624[S("method")]));
                contract_601 = TestCall(Divide(output_607, AddText(S("Contracts/"), Format(name_606, string.Empty), Format(read_609, string.Empty), S("Request.g.cs"))), "read_text");
                record_610 = TestCall(RegexSearch(S("Optional<string> (Record\\w+)"), contract_601), "group", I(1));
                body_600 = AddText(S("\n        var executor = new "), Format(name_606, string.Empty), S("Executor();\n        var fake = new "), Format(name_606, string.Empty), S("ScriptedDependencies();\n        fake.Expect"), Format(read_609, string.Empty), S("(_ => true, new "), Format(name_606, string.Empty), Format(read_609, string.Empty), S("Response(\"00\", DependencyOutcome.Normal, \"ABC\", \"00\"));\n        fake.Expect"), Format(write_614, string.Empty), S("(r => r."), Format(record_610, string.Empty), S(" == \"ABC\", new "), Format(name_606, string.Empty), Format(write_614, string.Empty), S("Response(\"00\", DependencyOutcome.Normal, \"00\"));\n        var result = executor.Run(new(), fake);\n        Assert.Equal(\"ABC\", result.Snapshot."), Format(fields_602[S("TEXT-VALUE")], string.Empty), S(");\n        Assert.Equal(\"00\", result.Snapshot."), Format(fields_602[S("ST")], string.Empty), S(");\n        Assert.Equal(\"ABC\", fake.Captured"), Format(write_614, string.Empty), S(".Single()."), Format(record_610, string.Empty), S(");\n        var eof = new "), Format(name_606, string.Empty), S("ScriptedDependencies();\n        eof.Expect"), Format(read_609, string.Empty), S("(_ => true, new "), Format(name_606, string.Empty), Format(read_609, string.Empty), S("Response(\"10\", DependencyOutcome.End, FileStatus: \"10\"));\n        var ended = executor.Run(new() { InitialState = new() { "), Format(fields_602[S("TEXT-VALUE")], string.Empty), S(" = \"OLD\" } }, eof);\n        Assert.Equal(\"OLD\", ended.Snapshot."), Format(fields_602[S("TEXT-VALUE")], string.Empty), S(");\n        Assert.Equal(new[] { \"EOF\", \"10\" }, ended.Display.ToArray());\n        var missing = new "), Format(name_606, string.Empty), S("ScriptedDependencies();\n        missing.Expect"), Format(read_609, string.Empty), S("(_ => true, new "), Format(name_606, string.Empty), Format(read_609, string.Empty), S("Response(\"00\", DependencyOutcome.Normal, FileStatus: \"00\"));\n        Assert.Throws<ExecutionException>(() => executor.Run(new(), missing));\n        var wrong = new "), Format(name_606, string.Empty), S("ScriptedDependencies();\n        wrong.Expect"), Format(read_609, string.Empty), S("(_ => true, new "), Format(name_606, string.Empty), Format(read_609, string.Empty), S("Response(\"00\", DependencyOutcome.End, FileStatus: \"00\"));\n        Assert.Throws<ExecutionException>(() => executor.Run(new(), wrong));\n        var order = new "), Format(name_606, string.Empty), S("ScriptedDependencies();\n        order.Expect"), Format(write_614, string.Empty), S("(_ => true, new "), Format(name_606, string.Empty), Format(write_614, string.Empty), S("Response(\"00\", DependencyOutcome.Normal, \"00\"));\n        Assert.Throws<ExecutionException>(() => executor.Run(new(), order));\n        Assert.Throws<ExecutionException>(() => executor.Run(new(), options: new ExecutionOptions { CallStubMode = CallStubMode.NoOp }));\n"));
            }
            else
            {
                method_605 = stubs_611[I(0)][S("method")];
                body_600 = AddText(S("\n        var executor = new "), Format(name_606, string.Empty), S("Executor();\n        var fake = new "), Format(name_606, string.Empty), S("ScriptedDependencies();\n        fake.Expect"), Format(method_605, string.Empty), S("(r => r.Host1 == new Number(1), new "), Format(name_606, string.Empty), Format(method_605, string.Empty), S("Response(\"0\", DependencyOutcome.Normal, new Number(42), new Number(0), new Number(0), \"00000\"));\n        var result = executor.Run(new(), fake);\n        Assert.Equal(new Number(42), result.Snapshot."), Format(fields_602[S("N")], string.Empty), S(");\n        Assert.Equal(new Number(0), result.Snapshot."), Format(fields_602[S("SQLCODE")], string.Empty), S(");\n        var noData = new "), Format(name_606, string.Empty), S("ScriptedDependencies();\n        noData.Expect"), Format(method_605, string.Empty), S("(_ => true, new "), Format(name_606, string.Empty), Format(method_605, string.Empty), S("Response(\"100\", DependencyOutcome.End, SqlCode: new Number(100), SqlState: \"02000\"));\n        var ended = executor.Run(new(), noData);\n        Assert.Equal(new Number(17), ended.Snapshot."), Format(fields_602[S("N")], string.Empty), S(");\n        Assert.Equal(new Number(100), ended.Snapshot."), Format(fields_602[S("SQLCODE")], string.Empty), S(");\n        var failed = new "), Format(name_606, string.Empty), S("ScriptedDependencies();\n        failed.Expect"), Format(method_605, string.Empty), S("(_ => true, new "), Format(name_606, string.Empty), Format(method_605, string.Empty), S("Response(\"-1\", DependencyOutcome.Exception, SqlCode: new Number(-1), SqlState: \"HY000\"));\n        var errorResult = executor.Run(new(), failed);\n        Assert.Equal(new Number(17), errorResult.Snapshot."), Format(fields_602[S("N")], string.Empty), S(");\n        Assert.Equal(new Number(-1), errorResult.Snapshot."), Format(fields_602[S("SQLCODE")], string.Empty), S(");\n        var nullValue = new "), Format(name_606, string.Empty), S("ScriptedDependencies();\n        nullValue.Expect"), Format(method_605, string.Empty), S("(_ => true, new "), Format(name_606, string.Empty), Format(method_605, string.Empty), S("Response(\"0\", DependencyOutcome.Normal, Indicator0: new Number(-1), SqlCode: new Number(0), SqlState: \"00000\"));\n        Assert.Equal(new Number(17), executor.Run(new(), nullValue).Snapshot."), Format(fields_602[S("N")], string.Empty), S(");\n        var wrong = new "), Format(name_606, string.Empty), S("ScriptedDependencies();\n        wrong.Expect"), Format(method_605, string.Empty), S("(_ => true, new "), Format(name_606, string.Empty), Format(method_605, string.Empty), S("Response(\"0\", DependencyOutcome.End, SqlCode: new Number(100), SqlState: \"02000\"));\n        Assert.Throws<ExecutionException>(() => executor.Run(new(), wrong));\n        Assert.Throws<ExecutionException>(() => executor.Run(new(), options: new ExecutionOptions { CallStubMode = CallStubMode.NoOp }));\n"));
            }

            tests_613 = AddText(S("using CobolCS.Runtime;\nusing Xunit;\n\nnamespace CobolCS.Generated.Tests;\n\n/// <summary>合成応答の状態・値・順序を独立期待値で固定します。</summary>\npublic sealed class "), Format(name_606, string.Empty), S("Tests\n{\n    /// <summary>実サーバーを使わず公開契約の成否を確認します。</summary>\n    [Fact]\n    public void StatusAndValues()\n    {"), Format(body_600, string.Empty), S("    }\n}\n"));
            _ = TestCall(Divide(output_607, AddText(S("Tests/Cases/"), Format(name_606, string.Empty), S("Tests.cs"))), "write_text", tests_613);
            _ = RunChecked(ListOf(S("dotnet"), S("test"), Divide(output_607, S("Tests/CobolCS.Generated.Tests.csproj"))), I(0));
        }
    }

    /// <summary>typed setup and snapshot の回帰条件を検証します。</summary>
    [Fact]
    public void TestTypedSetupAndSnapshot()
    {
        V base_625 = Nil;
        V body_626 = Nil;
        V fields_627 = Nil;
        V m_628 = Nil;
        V mapping_629 = Nil;
        V name_630 = Nil;
        V output_631 = Nil;
        V source_632 = Nil;
        V temp_633 = Nil;
        using var folder634 = new TestDirectory();
        temp_633 = PathValue(folder634.Path);
        base_625 = PathValue(Text(temp_633));
        source_632 = Divide(base_625, S("layout.cbl"));
        _ = TestCall(source_632, "write_text", S("IDENTIFICATION DIVISION.\nPROGRAM-ID. LAYOUT.\nDATA DIVISION.\nWORKING-STORAGE SECTION.\n01 ROOT-GROUP.\n 05 ROW OCCURS 2.\n  10 CELL PIC S9(5)V99 OCCURS 3.\n01 ZERO-TEST PIC 9 VALUE 7.\n01 TEXT-TEST PIC X(4) JUSTIFIED RIGHT VALUE 'AB'.\n01 NATIONAL-TEST PIC N(3).\n01 BIG-TEST PIC 9(1000).\nPROCEDURE DIVISION.\nGOBACK.\n"));
        output_631 = Divide(base_625, S("generated"));
        _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("generate") }).Concat(new V[] { source_632 }).Concat(new V[] { S("--output") }).Concat(new V[] { output_631 })), I(0));
        mapping_629 = ReadJson(TestCall(Divide(output_631, S("reports/source-map.json")), "read_text"));
        name_630 = TestCall(Next(Sequence(from item635 in Items(mapping_629) let m_636 = item635 where Truth(B(Contains(m_636, S("executorType")))) select m_636[S("executorType")])), "removesuffix", S("Executor"));
        fields_627 = MakeDictionary(Sequence(from item637 in Items(mapping_629) let m_638 = item637 where Truth(B(Contains(m_638, S("symbolId")))) select ListOf(TestCall(m_638[S("originalName")], "split", S(" OF "))[I(0)], TestCall(m_638[S("generatedName")], "split", S("."))[Subtract(I(0), I(1))])));
        body_626 = AddText(S("using CobolCS.Runtime;\nusing Xunit;\n\nnamespace CobolCS.Generated.Tests;\n\n/// <summary>精密値・グループ・多次元表と結果の分離を確認します。</summary>\npublic sealed class "), Format(name_630, string.Empty), S("Tests\n{\n    /// <summary>全入力は fixture と同じ PIC 境界を通ります。</summary>\n    [Fact]\n    public void TypedStorage()\n    {\n        var executor = new "), Format(name_630, string.Empty), S("Executor();\n        var defaults = executor.Run(new());\n        Assert.Equal(new Number(7), defaults.Snapshot."), Format(fields_627[S("ZERO-TEST")], string.Empty), S(");\n        var huge = CobolValue.Numeric(new string('9', 1000));\n        var entries = new[] { new TableEntry<Number>(new[] { 2, 3 }, CobolValue.Numeric(\"-12.34\")) };\n        var result = executor.Run(new()\n        {\n            InitialState = new()\n            {\n            "), Format(fields_627[S("ROOT-GROUP")], string.Empty), S(" = new() { "), Format(fields_627[S("CELL")], string.Empty), S(" = entries },\n            "), Format(fields_627[S("ZERO-TEST")], string.Empty), S(" = new Number(0),\n            "), Format(fields_627[S("TEXT-TEST")], string.Empty), S(" = string.Empty,\n            "), Format(fields_627[S("NATIONAL-TEST")], string.Empty), S(" = \"日本\",\n            "), Format(fields_627[S("BIG-TEST")], string.Empty), S(" = huge,\n            },\n        });\n        Assert.Equal(new Number(0), result.Snapshot."), Format(fields_627[S("ZERO-TEST")], string.Empty), S(");\n        Assert.Equal(\"    \", result.Snapshot."), Format(fields_627[S("TEXT-TEST")], string.Empty), S(");\n        Assert.Equal(\"日本 \", result.Snapshot."), Format(fields_627[S("NATIONAL-TEST")], string.Empty), S(");\n        Assert.Equal(huge, result.Snapshot."), Format(fields_627[S("BIG-TEST")], string.Empty), S(");\n        Assert.Equal(CobolValue.Numeric(\"-12.34\"), result.Snapshot."), Format(fields_627[S("ROOT-GROUP")], string.Empty), S("."), Format(fields_627[S("CELL")], string.Empty), S("[5]);\n        entries[0] = new(new[] { 1, 1 }, new Number(99));\n        Assert.Equal(CobolValue.Numeric(\"-12.34\"), result.Snapshot."), Format(fields_627[S("CELL")], string.Empty), S("[5]);\n        Assert.Throws<NotSupportedException>(() => ((IList<Number>)result.Snapshot."), Format(fields_627[S("CELL")], string.Empty), S(")[5] = new Number(99));\n        Assert.Throws<ExecutionException>(() => executor.Run(new() { InitialState = new() { "), Format(fields_627[S("ZERO-TEST")], string.Empty), S(" = default(Number) } }));\n        Assert.Throws<ExecutionException>(() => executor.Run(new() { InitialState = new() { "), Format(fields_627[S("ZERO-TEST")], string.Empty), S(" = new Number(10) } }));\n        foreach (var indices in new[] { new[] { 0, 1 }, new[] { 3, 1 }, new[] { 1 } })\n        {\n            Assert.Throws<ExecutionException>(() => executor.Run(new() { InitialState = new() { "), Format(fields_627[S("CELL")], string.Empty), S(" = new[] { new TableEntry<Number>(indices, new Number(1)) } } }));\n        }\n\n        var duplicate = new TableEntry<Number>(new[] { 1, 1 }, new Number(1));\n        Assert.Throws<ExecutionException>(() => executor.Run(new() { InitialState = new() { "), Format(fields_627[S("CELL")], string.Empty), S(" = new[] { duplicate, duplicate } } }));\n    }\n}\n"));
        _ = TestCall(Divide(output_631, AddText(S("Tests/Cases/"), Format(name_630, string.Empty), S("Tests.cs"))), "write_text", body_626);
        _ = RunChecked(ListOf(S("dotnet"), S("test"), Divide(output_631, S("Tests/CobolCS.Generated.Tests.csproj"))), I(0));
    }
}
