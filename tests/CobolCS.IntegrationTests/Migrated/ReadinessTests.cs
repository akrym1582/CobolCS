using CobolCS.Frontend.Parsing;
using Xunit;
using static CobolCS.Frontend.Parsing.Values;
using static CobolCS.IntegrationTests.RegressionSupport;

namespace CobolCS.IntegrationTests;

/// <summary>既存の test_execution_readiness の入力、期待値、失敗条件を保持する.NET回帰テストです。</summary>
public sealed class ReadinessTests
{
    private readonly V state = new();

    /// <summary>arguments adopt extract and mutate の回帰条件を検証します。</summary>
    [Fact]
    public void TestArgumentsAdoptExtractAndMutate()
    {
        V adopted_295 = Nil;
        V base_296 = Nil;
        V baseline_297 = Nil;
        V before_298 = Nil;
        V case_299 = Nil;
        V changed_300 = Nil;
        V count_301 = Nil;
        V data_302 = Nil;
        V deleted_303 = Nil;
        V f_304 = Nil;
        V failed_305 = Nil;
        V group_306 = Nil;
        V n_307 = Nil;
        V original_308 = Nil;
        V out_309 = Nil;
        V p_310 = Nil;
        V path_311 = Nil;
        V paths_312 = Nil;
        V pattern_313 = Nil;
        V program_314 = Nil;
        V project_315 = Nil;
        V scenario_316 = Nil;
        V service_317 = Nil;
        V source_318 = Nil;
        V syms_319 = Nil;
        V tmp_320 = Nil;
        using var folder321 = new TestDirectory();
        tmp_320 = PathValue(folder321.Path);
        base_296 = PathValue(Text(tmp_320));
        var unpack322 = M_generate(base_296, S("ARGCALC"));
        out_309 = unpack322[I(0)];
        n_307 = unpack322[I(1)];
        f_304 = unpack322[I(2)];
        syms_319 = unpack322[I(3)];
        group_306 = TestAdd(TestAdd(TestAdd(n_307, S("Group")), Next(Sequence(from item323 in Items(syms_319) let s_324 = item323 where Truth(B(TestEqual(s_324[S("name")], S("REQUEST-DATA")))) select s_324[S("id")]))), S("Snapshot"));
        project_315 = Divide(out_309, S("Tests/CobolCS.Generated.Tests.csproj"));
        _ = RunChecked(ListOf(S("dotnet"), S("build"), Divide(out_309, S("CobolCS.Generated.sln")), S("-m:1")), I(0));
        failed_305 = RunChecked(ListOf(S("dotnet"), S("test"), project_315), I(1));
        _ = CheckIn(S("Unfilled preparation item"), TestMember(failed_305, "stdout"));
        _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(ToolCli)).Concat(new V[] { S("prepare-readiness") }).Concat(new V[] { S("--out") }).Concat(new V[] { out_309 })), I(0));
        scenario_316 = Divide(out_309, AddText(S("UserCode/Scenarios/"), Format(n_307, string.Empty), S("Scenarios.cs")));
        case_299 = Divide(out_309, AddText(S("Tests/Cases/"), Format(n_307, string.Empty), S("Tests.cs")));
        baseline_297 = Divide(out_309, AddText(S("Tests/Baselines/"), Format(n_307, string.Empty), S(".json")));
        paths_312 = ListOf(case_299, baseline_297, scenario_316, Divide(out_309, AddText(S("UserCode/Scenarios/"), Format(n_307, string.Empty), S("ResponseExamples.cs"))));
        _ = RunChecked(ListOf(S("dotnet"), S("test"), project_315), I(0));
        before_298 = MakeDictionary(Sequence(from item325 in Items(paths_312) let p_326 = item325 select ListOf(TestCall(p_326, "relative_to", out_309), TestCall(p_326, "read_bytes"))));
        deleted_303 = Divide(out_309, AddText(S("UserCode/Scenarios/"), Format(n_307, string.Empty), S("ResponseExamples.cs")));
        _ = TestCall(deleted_303, "unlink");
        _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("generate") }).Concat(new V[] { Divide(Root, S("examples/WORKFLOW/ARGCALC.cbl")) }).Concat(new V[] { S("--output") }).Concat(new V[] { out_309 })), I(0));
        _ = CheckFalse(TestCall(deleted_303, "exists"));
        foreach (var item327 in Items(TestCall(before_298, "items")))
        {
            var unpack328 = item327;
            path_311 = unpack328[I(0)];
            data_302 = unpack328[I(1)];
            if (Truth(B(!TestEqual(path_311, TestCall(deleted_303, "relative_to", out_309)))))
            {
                _ = CheckEqual(data_302, TestCall(Divide(out_309, path_311), "read_bytes"));
            }
        }

        _ = TestCall(deleted_303, "write_bytes", before_298[TestCall(deleted_303, "relative_to", out_309)]);
        original_308 = TestCall(scenario_316, "read_text");
        _ = TestCall(scenario_316, "write_text", TestCall(original_308, "replace", S("\"A\", \"B\""), S("\"C\", \"D\"")));
        _ = CheckIn(S("Review hash mismatch"), TestMember(RunChecked(ListOf(S("dotnet"), S("test"), project_315), I(1)), "stdout"));
        _ = TestCall(scenario_316, "write_text", original_308);
        adopted_295 = Divide(base_296, S("standalone"));
        _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("adopt") }).Concat(new V[] { out_309 }).Concat(new V[] { S("--output") }).Concat(new V[] { adopted_295 })), I(0));
        _ = TestCall(out_309, "rename", Divide(base_296, S("hidden-generation")));
        _ = RunChecked(ListOf(S("dotnet"), S("build"), Divide(adopted_295, S("CobolCS.Generated.sln")), S("-m:1")), I(0));
        _ = RunChecked(ListOf(S("dotnet"), S("test"), Divide(adopted_295, S("Tests/CobolCS.Generated.Tests.csproj"))), I(0));
        _ = RunChecked(ListOf(S("dotnet"), S("run"), S("--project"), adopted_295, S("--"), S("ARGCALC"), S("--scenario"), S("sample")), I(0));
        program_314 = Next(TestCall(Divide(adopted_295, S("Programs")), "glob", S("*Program.g.cs")));
        source_318 = TestCall(program_314, "read_text");
        pattern_313 = S("\\(CobolValue.Numeric\\((state\\.[^)]+)\\) \\* CobolValue.Numeric\\((state\\.[^)]+)\\)\\)");
        var unpack329 = TestRegexSubn(pattern_313, S("Calculation.TestMultiply(\\1, \\2)"), source_318);
        changed_300 = unpack329[I(0)];
        count_301 = unpack329[I(1)];
        _ = CheckEqual(I(1), count_301);
        service_317 = Divide(adopted_295, S("UserCode/Calculation.cs"));
        _ = TestCall(service_317, "write_text", S("using CobolCS.Runtime;\n\nnamespace CobolCS.Generated;\n\n/// <summary>抽出した合成計算。</summary>\ninternal static class Calculation\n{\n    /// <summary>精密乗算。</summary>\n    /// <param name=\"amount\">金額。</param>\n    /// <param name=\"rate\">料率。</param>\n    /// <returns>計算値。</returns>\n    internal static Number TestMultiply(Number amount, Number rate) => amount * rate;\n}\n"));
        _ = TestCall(program_314, "write_text", changed_300);
        _ = RunChecked(ListOf(S("dotnet"), S("test"), Divide(adopted_295, S("Tests/CobolCS.Generated.Tests.csproj"))), I(0));
        _ = TestCall(service_317, "write_text", TestCall(TestCall(service_317, "read_text"), "replace", S("amount * rate"), S("amount + rate")));
        _ = RunChecked(ListOf(S("dotnet"), S("test"), Divide(adopted_295, S("Tests/CobolCS.Generated.Tests.csproj"))), I(1));
        _ = TestCall(service_317, "write_text", TestCall(TestCall(service_317, "read_text"), "replace", S("amount + rate"), S("amount * rate")));
    }

    /// <summary>group call の回帰条件を検証します。</summary>
    [Fact]
    public void TestGroupCall()
    {
        V f_330 = Nil;
        V group_331 = Nil;
        V method_332 = Nil;
        V n_333 = Nil;
        V out_334 = Nil;
        V source_335 = Nil;
        V stub_336 = Nil;
        V syms_337 = Nil;
        V tmp_338 = Nil;
        using var folder339 = new TestDirectory();
        tmp_338 = PathValue(folder339.Path);
        var unpack340 = M_generate(PathValue(Text(tmp_338)), S("GROUPCALL"));
        out_334 = unpack340[I(0)];
        n_333 = unpack340[I(1)];
        f_330 = unpack340[I(2)];
        syms_337 = unpack340[I(3)];
        group_331 = TestAdd(TestAdd(TestAdd(n_333, S("Group")), Next(Sequence(from item341 in Items(syms_337) let s_342 = item341 where Truth(B(TestEqual(s_342[S("name")], S("PAYLOAD")))) select s_342[S("id")]))), S("Snapshot"));
        method_332 = ReadJson(TestCall(Divide(out_334, S("reports/stubs.json")), "read_text"))[I(0)][S("method")];
        _ = M_tests(out_334, n_333, AddText(S("\n        var fake = new "), Format(n_333, string.Empty), S("ScriptedDependencies();\n        var payload = new "), Format(group_331, string.Empty), S("("), Format(f_330[S("AMOUNT")], string.Empty), S(": new Number(25), "), Format(f_330[S("ITEM")], string.Empty), S(": new[] { \"AB\", \"CD\" });\n        fake.Expect"), Format(method_332, string.Empty), S("(r => r.Target == \"SERVICE\" && r.Argument0."), Format(f_330[S("AMOUNT")], string.Empty), S(" == new Number(12) && r.Argument0."), Format(f_330[S("ITEM")], string.Empty), S(".SequenceEqual(new[] { \"  \", \"  \" }), new "), Format(n_333, string.Empty), Format(method_332, string.Empty), S("Response(\"00\", DependencyOutcome.Normal, Argument0: payload, Returning: new Number(0)));\n        var result = new "), Format(n_333, string.Empty), S("Executor().Run(new(), fake);\n        Assert.Equal(new Number(25), result.Snapshot."), Format(f_330[S("AMOUNT")], string.Empty), S(");\n        Assert.Equal(new[] { \"UPDATED\" }, result.Display.ToArray());\n        Assert.Equal(new Number(12), fake.Captured"), Format(method_332, string.Empty), S(".Single().Argument0."), Format(f_330[S("AMOUNT")], string.Empty), S(");\n        var trace = ("), Format(n_333, string.Empty), Format(method_332, string.Empty), S("RequestTrace)result.DependencyTrace.Single();\n        Assert.True(trace.Request.Argument0.ContentEquals(fake.Captured"), Format(method_332, string.Empty), S(".Single().Argument0));\n        Assert.NotSame(trace.Request.Argument0."), Format(f_330[S("ITEM")], string.Empty), S(", fake.Captured"), Format(method_332, string.Empty), S(".Single().Argument0."), Format(f_330[S("ITEM")], string.Empty), S(");\n        var noUpdate = new "), Format(n_333, string.Empty), S("ScriptedDependencies();\n        noUpdate.Expect"), Format(method_332, string.Empty), S("(r => r.Target == \"SERVICE\", new "), Format(n_333, string.Empty), Format(method_332, string.Empty), S("Response(\"00\", DependencyOutcome.Normal, Returning: new Number(0)));\n        Assert.Equal(new[] { \"UNCHANGED\" }, new "), Format(n_333, string.Empty), S("Executor().Run(new(), noUpdate).Display.ToArray());\n        var bad = new "), Format(n_333, string.Empty), S("ScriptedDependencies();\n        bad.Expect"), Format(method_332, string.Empty), S("(r => r.Argument0."), Format(f_330[S("AMOUNT")], string.Empty), S(" == new Number(12), new "), Format(n_333, string.Empty), Format(method_332, string.Empty), S("Response(\"00\", DependencyOutcome.Normal, Argument0: payload, Returning: new Number(99)));\n        Assert.Throws<ExecutionException>(() => new "), Format(n_333, string.Empty), S("Executor().Run(new(), bad));\n        Assert.Equal(new Number(12), bad.Captured"), Format(method_332, string.Empty), S(".Single().Argument0."), Format(f_330[S("AMOUNT")], string.Empty), S(");\n        Assert.Throws<ArgumentException>(() => new "), Format(group_331, string.Empty), S("("), Format(f_330[S("AMOUNT")], string.Empty), S(": new Number(1), "), Format(f_330[S("ITEM")], string.Empty), S(": new[] { \"TOOLONG\", \"OK\" }));\n")));
        _ = RunChecked(ListOf(S("dotnet"), S("test"), Divide(out_334, S("Tests/CobolCS.Generated.Tests.csproj"))), I(0));
        stub_336 = Next(Sequence(from item343 in Items(TestCall(Divide(out_334, S("Stubs")), "rglob", S("Dependency_*.cs"))) let p_344 = item343 select p_344));
        _ = TestCall(stub_336, "write_text", TestAdd(TestCall(stub_336, "read_text"), S("\n// user implementation under review\n")));
        source_335 = Divide(PathValue(Text(tmp_338)), S("changed.cbl"));
        _ = TestCall(source_335, "write_text", TestCall(TestCall(Divide(Root, S("examples/WORKFLOW/GROUPCALL.cbl")), "read_text"), "replace", S("PIC 9(3)"), S("PIC 9(4)")));
        _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("generate") }).Concat(new V[] { source_335 }).Concat(new V[] { S("--output") }).Concat(new V[] { out_334 })), I(2));
    }

    /// <summary>initialize and string の回帰条件を検証します。</summary>
    [Fact]
    public void TestInitializeAndString()
    {
        V f_346 = Nil;
        V n_347 = Nil;
        V out_348 = Nil;
        V tmp_349 = Nil;
        using var folder350 = new TestDirectory();
        tmp_349 = PathValue(folder350.Path);
        var unpack351 = M_generate(PathValue(Text(tmp_349)), S("TEXTCASE"));
        out_348 = unpack351[I(0)];
        n_347 = unpack351[I(1)];
        f_346 = unpack351[I(2)];
        _ = unpack351[I(3)];
        _ = CheckEqual(ListOf(), ReadJson(TestCall(Divide(out_348, S("reports/stubs.json")), "read_text")));
        _ = M_tests(out_348, n_347, AddText(S("\n        var executor = new "), Format(n_347, string.Empty), S("Executor();\n        foreach (var c in new[] { (2, \"aXYZe\", 5, \"OK\"), (3, \"abXYZ\", 6, \"OK\"), (4, \"abcXY\", 6, \"OVERFLOW\"), (0, \"abcde\", 0, \"OVERFLOW\"), (6, \"abcde\", 6, \"OVERFLOW\"), (-1, \"abcde\", -1, \"OVERFLOW\") })\n        {\n            var result = executor.Run(new() { InitialState = new() { "), Format(f_346[S("PTR")], string.Empty), S(" = new Number(c.Item1) } });\n            Assert.Equal(c.Item2, result.Snapshot."), Format(f_346[S("DEST")], string.Empty), S(");\n            Assert.Equal(new Number(c.Item3), result.Snapshot."), Format(f_346[S("PTR")], string.Empty), S(");\n            Assert.Equal(new[] { c.Item4, c.Item2 }, result.Display.ToArray());\n            Assert.Equal(new Number(0), result.Snapshot."), Format(f_346[S("N")], string.Empty), S(");\n            Assert.Equal(new[] { \"  \", \"  \" }, result.Snapshot."), Format(f_346[S("TXT")], string.Empty), S(".ToArray());\n        }\n\n        Assert.Equal((\"abcde\", new Number(1), false), SourceSupport.StringSize(\"abcde\", new Number(1), new[] { string.Empty }));\n        Assert.Equal((\"XYZde\", new Number(4), false), SourceSupport.StringSize(\"abcde\", new Number(1), new[] { \"XY\", \"Z\" }));\n")));
        _ = RunChecked(ListOf(S("dotnet"), S("test"), Divide(out_348, S("Tests/CobolCS.Generated.Tests.csproj"))), I(0));
    }

    /// <summary>scalar arguments and input only calls の回帰条件を検証します。</summary>
    [Fact]
    public void TestScalarArgumentsAndInputOnlyCalls()
    {
        V base_352 = Nil;
        V f_353 = Nil;
        V m_354 = Nil;
        V mapping_355 = Nil;
        V n_356 = Nil;
        V out_357 = Nil;
        V source_358 = Nil;
        V tmp_359 = Nil;
        using var folder360 = new TestDirectory();
        tmp_359 = PathValue(folder360.Path);
        base_352 = PathValue(Text(tmp_359));
        source_358 = Divide(base_352, S("SCALAR.cbl"));
        _ = TestCall(source_358, "write_text", S("       IDENTIFICATION DIVISION.\n       PROGRAM-ID. SCALAR.\n       DATA DIVISION.\n       LINKAGE SECTION.\n       01 ARG PIC 9(3).\n       01 ITEMS PIC X(2) OCCURS 2 TIMES.\n       PROCEDURE DIVISION USING BY VALUE ARG BY REFERENCE ITEMS.\n           ADD 1 TO ARG\n           MOVE \"OK\" TO ITEMS(2)\n           GOBACK.\n"));
        out_357 = Divide(base_352, S("out"));
        _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("generate") }).Concat(new V[] { source_358 }).Concat(new V[] { S("--output") }).Concat(new V[] { out_357 }).Concat(new V[] { S("--mode") }).Concat(new V[] { S("strict") })), I(0));
        mapping_355 = ReadJson(TestCall(Divide(out_357, S("reports/source-map.json")), "read_text"));
        n_356 = TestCall(Next(Sequence(from item361 in Items(mapping_355) let m_362 = item361 where Truth(B(Contains(m_362, S("executorType")))) select m_362[S("executorType")])), "removesuffix", S("Executor"));
        f_353 = MakeDictionary(Sequence(from item363 in Items(mapping_355) let m_364 = item363 where Truth(B(Contains(m_364, S("symbolId")))) select ListOf(m_364[S("originalName")], TestCall(m_364[S("generatedName")], "split", S("."))[Subtract(I(0), I(1))])));
        _ = M_tests(out_357, n_356, AddText(S("        var values = new[] { \"A\", \"B\" };\n        var input = new "), Format(n_356, string.Empty), S("Input { Arguments = new() { "), Format(f_353[S("ARG")], string.Empty), S(" = new Number(0), "), Format(f_353[S("ITEMS")], string.Empty), S(" = new Optional<IReadOnlyList<string>>(values) } };\n        var result = new "), Format(n_356, string.Empty), S("Executor().Run(input);\n        Assert.NotNull(result.Arguments);\n        Assert.Equal(new Number(1), result.Arguments."), Format(f_353[S("ARG")], string.Empty), S(");\n        Assert.Equal(new[] { \"A \", \"OK\" }, result.Arguments."), Format(f_353[S("ITEMS")], string.Empty), S(".ToArray());\n        Assert.Equal(\"B\", values[1]);\n        Assert.Throws<ExecutionException>(() => new "), Format(n_356, string.Empty), S("Executor().Run(new() { Arguments = new() { "), Format(f_353[S("ARG")], string.Empty), S(" = new Number(0) } }));")));
        _ = RunChecked(ListOf(S("dotnet"), S("test"), Divide(out_357, S("Tests/CobolCS.Generated.Tests.csproj"))), I(0));
    }

    /// <summary>string flow and initialize selection の回帰条件を検証します。</summary>
    [Fact]
    public void TestStringFlowAndInitializeSelection()
    {
        V base_365 = Nil;
        V body_366 = Nil;
        V check_367 = Nil;
        V decl_368 = Nil;
        V expected_369 = Nil;
        V fields_370 = Nil;
        V m_371 = Nil;
        V mapping_372 = Nil;
        V n_373 = Nil;
        V out_374 = Nil;
        V program_375 = Nil;
        V programs_376 = Nil;
        V source_377 = Nil;
        V tmp_378 = Nil;
        V values_379 = Nil;
        programs_376 = Put(Put(Put(Put(Put(Dict(), S("FLOWTEXT"), ListOf(S("01 T PIC X(3) VALUE 'xyz'. 01 P PIC 9 VALUE 1."), S("MAIN. STRING 'AB' 'CD' DELIMITED BY SIZE INTO T POINTER P ON OVERFLOW GO TO FAILED NOT ON OVERFLOW GO TO GOOD END-STRING DISPLAY 'LEAK' GOBACK. FAILED. DISPLAY T P GOBACK. GOOD. DISPLAY 'BAD' GOBACK."), ListOf(S("ABC4")))), S("EMPTYTEXT"), ListOf(S("01 T PIC X(5) VALUE 'abcde'."), S("STRING \"\" \"Z\" DELIMITED BY SIZE INTO T END-STRING DISPLAY T GOBACK."), ListOf(S("Zbcde")))), S("NOPTR"), ListOf(S("01 T PIC X(3) VALUE 'xyz'."), S("STRING \"ABCDE\" DELIMITED BY SIZE INTO T END-STRING DISPLAY T GOBACK."), ListOf(S("ABC")))), S("INITSELECT"), ListOf(S("01 G. 05 ROWS OCCURS 2 TIMES. 10 N PIC 99 VALUE 17. 88 SEVENTEEN VALUE 17. 10 T PIC X(2) VALUE 'AB'. 10 FILLER PIC X VALUE 'Z'. 01 NAT PIC N(2) VALUE 'あい'."), S("INITIALIZE ROWS(2) NAT DISPLAY N(1) N(2) T(1) T(2) NAT GOBACK."), ListOf(S("1700AB    ")))), S("UNREACHED"), ListOf(S("01 N PIC 9 VALUE 1. 01 T PIC X(2)."), S("IF N = 0 STRING \"A\" DELIMITED BY \"Z\" INTO T ON OVERFLOW DISPLAY \"LEAK\" END-STRING END-IF DISPLAY \"OK\" GOBACK."), ListOf(S("OK"))));
        using var folder380 = new TestDirectory();
        tmp_378 = PathValue(folder380.Path);
        base_365 = PathValue(Text(tmp_378));
        foreach (var item381 in Items(TestCall(programs_376, "items")))
        {
            var unpack382 = item381;
            program_375 = unpack382[I(0)];
            var unpack383 = unpack382[I(1)];
            decl_368 = unpack383[I(0)];
            body_366 = unpack383[I(1)];
            expected_369 = unpack383[I(2)];
            source_377 = Divide(base_365, TestAdd(program_375, S(".cbl")));
            _ = TestCall(source_377, "write_text", AddText(S("IDENTIFICATION DIVISION. PROGRAM-ID. "), Format(program_375, string.Empty), S(". DATA DIVISION. WORKING-STORAGE SECTION. "), Format(decl_368, string.Empty), S(" PROCEDURE DIVISION. "), Format(body_366, string.Empty)));
            out_374 = Divide(base_365, program_375);
            _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("generate") }).Concat(new V[] { source_377 }).Concat(new V[] { S("--output") }).Concat(new V[] { out_374 })), I(0));
            mapping_372 = ReadJson(TestCall(Divide(out_374, S("reports/source-map.json")), "read_text"));
            n_373 = TestCall(Next(Sequence(from item384 in Items(mapping_372) let m_385 = item384 where Truth(B(Contains(m_385, S("executorType")))) select m_385[S("executorType")])), "removesuffix", S("Executor"));
            values_379 = TestCall(S(", "), "join", Sequence(from item386 in Items(expected_369) let v_387 = item386 select WriteJson(v_387)));
            check_367 = AddText(S("        var result = new "), Format(n_373, string.Empty), S("Executor().Run(new());\n        Assert.Equal(new[] { "), Format(values_379, string.Empty), S(" }, result.Display.ToArray());"));
            if (Truth(B(TestEqual(program_375, S("INITSELECT")))))
            {
                fields_370 = MakeDictionary(Sequence(from item388 in Items(mapping_372) let m_389 = item388 where Truth(B(Contains(m_389, S("symbolId")))) select ListOf(TestCall(m_389[S("originalName")], "split", S(" OF "))[I(0)], TestCall(m_389[S("generatedName")], "split", S("."))[Subtract(I(0), I(1))])));
                check_367 = Add(check_367, AddText(S("\n        Assert.Equal(new[] { \"Z\", \"Z\" }, result.Snapshot."), Format(fields_370[S("G")], string.Empty), S("."), Format(fields_370[S("FILLER")], string.Empty), S(".ToArray());")));
            }

            _ = M_tests(out_374, n_373, check_367);
            _ = RunChecked(ListOf(S("dotnet"), S("test"), Divide(out_374, S("Tests/CobolCS.Generated.Tests.csproj"))), I(0));
        }
    }

    /// <summary>group content is input only の回帰条件を検証します。</summary>
    [Fact]
    public void TestGroupContentIsInputOnly()
    {
        V base_390 = Nil;
        V mapping_391 = Nil;
        V method_392 = Nil;
        V n_393 = Nil;
        V out_394 = Nil;
        V source_395 = Nil;
        V tmp_396 = Nil;
        using var folder397 = new TestDirectory();
        tmp_396 = PathValue(folder397.Path);
        base_390 = PathValue(Text(tmp_396));
        source_395 = Divide(base_390, S("GROUPCALL.cbl"));
        _ = TestCall(source_395, "write_text", TestCall(TestCall(Divide(Root, S("examples/WORKFLOW/GROUPCALL.cbl")), "read_text"), "replace", S("BY REFERENCE PAYLOAD"), S("BY CONTENT PAYLOAD")));
        out_394 = Divide(base_390, S("out"));
        _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("generate") }).Concat(new V[] { source_395 }).Concat(new V[] { S("--output") }).Concat(new V[] { out_394 })), I(0));
        mapping_391 = ReadJson(TestCall(Divide(out_394, S("reports/source-map.json")), "read_text"));
        n_393 = TestCall(Next(Sequence(from item398 in Items(mapping_391) let m_399 = item398 where Truth(B(Contains(m_399, S("executorType")))) select m_399[S("executorType")])), "removesuffix", S("Executor"));
        method_392 = ReadJson(TestCall(Divide(out_394, S("reports/stubs.json")), "read_text"))[I(0)][S("method")];
        _ = M_tests(out_394, n_393, AddText(S("        var fake = new "), Format(n_393, string.Empty), S("ScriptedDependencies();\n        fake.Expect"), Format(method_392, string.Empty), S("(r => r.Target == \"SERVICE\" && r.Source.PassingModes.Single() == \"content\", new "), Format(n_393, string.Empty), Format(method_392, string.Empty), S("Response(\"00\", DependencyOutcome.Normal, Returning: new Number(0)));\n        var result = new "), Format(n_393, string.Empty), S("Executor().Run(new(), fake);\n        Assert.Equal(new[] { \"UNCHANGED\" }, result.Display.ToArray());\n        Assert.Null(typeof("), Format(n_393, string.Empty), Format(method_392, string.Empty), S("Response).GetProperty(\"Argument0\"));")));
        _ = RunChecked(ListOf(S("dotnet"), S("test"), Divide(out_394, S("Tests/CobolCS.Generated.Tests.csproj"))), I(0));
    }

    /// <summary>unsupported regions and aliases の回帰条件を検証します。</summary>
    [Fact]
    public void TestUnsupportedRegionsAndAliases()
    {
        V base_400 = Nil;
        V key_401 = Nil;
        V mapping_402 = Nil;
        V n_403 = Nil;
        V original_404 = Nil;
        V out_405 = Nil;
        V source_406 = Nil;
        V stubs_407 = Nil;
        V text_408 = Nil;
        V tmp_409 = Nil;
        V variants_410 = Nil;
        using var folder411 = new TestDirectory();
        tmp_409 = PathValue(folder411.Path);
        base_400 = PathValue(Text(tmp_409));
        original_404 = TestCall(Divide(Root, S("examples/WORKFLOW/GROUPCALL.cbl")), "read_text");
        variants_410 = Put(Put(Put(Put(Put(Put(Dict(), S("same"), TestCall(original_404, "replace", S("RETURNING RESULT-CODE"), S("RETURNING AMOUNT"))), S("child"), TestCall(original_404, "replace", S("BY REFERENCE PAYLOAD"), S("BY REFERENCE PAYLOAD AMOUNT"))), S("redefines"), TestCall(TestCall(original_404, "replace", S("01 RESULT-CODE"), S("01 ALIAS-DATA REDEFINES PAYLOAD PIC X(7).\n       01 RESULT-CODE")), "replace", S("BY REFERENCE PAYLOAD"), S("BY REFERENCE ALIAS-DATA"))), S("delimiter"), TestCall(TestCall(Divide(Root, S("examples/WORKFLOW/TEXTCASE.cbl")), "read_text"), "replace", S("DELIMITED BY SIZE"), S("DELIMITED BY DEST"))), S("replacing"), TestCall(TestCall(Divide(Root, S("examples/WORKFLOW/TEXTCASE.cbl")), "read_text"), "replace", S("INITIALIZE DATA-GROUP"), S("INITIALIZE DATA-GROUP REPLACING NUMERIC DATA BY 1"))), S("content-declaration"), TestCall(TestCall(Divide(Root, S("examples/WORKFLOW/ARGCALC.cbl")), "read_text"), "replace", S("USING REQUEST-DATA"), S("USING BY CONTENT REQUEST-DATA")));
        foreach (var item412 in Items(TestCall(variants_410, "items")))
        {
            var unpack413 = item412;
            key_401 = unpack413[I(0)];
            text_408 = unpack413[I(1)];
            source_406 = Divide(base_400, TestAdd(key_401, S(".cbl")));
            _ = TestCall(source_406, "write_text", text_408);
            out_405 = Divide(base_400, key_401);
            _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("generate") }).Concat(new V[] { source_406 }).Concat(new V[] { S("--output") }).Concat(new V[] { out_405 })), I(0));
            stubs_407 = ReadJson(TestCall(Divide(out_405, S("reports/stubs.json")), "read_text"));
            _ = CheckTrue(Any(Sequence(from item414 in Items(stubs_407) let s_415 = item414 select B(TestEqual(s_415[S("capability")], S("unsupported"))))));
            mapping_402 = ReadJson(TestCall(Divide(out_405, S("reports/source-map.json")), "read_text"));
            n_403 = TestCall(Next(Sequence(from item416 in Items(mapping_402) let m_417 = item416 where Truth(B(Contains(m_417, S("executorType")))) select m_417[S("executorType")])), "removesuffix", S("Executor"));
            _ = M_tests(out_405, n_403, AddText(S("        var error = Assert.Throws<ExecutionException>(() => new "), Format(n_403, string.Empty), S("Executor().Run(new()));\n        Assert.IsAssignableFrom<NotImplementedException>(error.InnerException);")));
            _ = RunChecked(ListOf(S("dotnet"), S("test"), Divide(out_405, S("Tests/CobolCS.Generated.Tests.csproj"))), I(0));
        }
    }

    /// <summary>reports and fresh templates の回帰条件を検証します。</summary>
    [Fact]
    public void TestReportsAndFreshTemplates()
    {
        V categories_418 = Nil;
        V item_419 = Nil;
        V out_420 = Nil;
        V report_421 = Nil;
        V tmp_422 = Nil;
        using var folder423 = new TestDirectory();
        tmp_422 = PathValue(folder423.Path);
        out_420 = Divide(PathValue(Text(tmp_422)), S("all"));
        _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("generate") }).Concat(new V[] { Divide(Root, S("examples/WORKFLOW")) }).Concat(new V[] { S("--output") }).Concat(new V[] { out_420 })), I(0));
        _ = RunChecked(ListOf(S("dotnet"), S("build"), Divide(out_420, S("CobolCS.Generated.sln")), S("-m:1")), I(0));
        report_421 = ReadJson(TestCall(Divide(out_420, S("reports/execution-readiness.json")), "read_text"));
        _ = CheckEqual(I(1), report_421[S("schemaVersion")]);
        categories_418 = SetOf(from item424 in Items(report_421[S("items")]) let i_425 = item424 select i_425[S("classification")]);
        _ = CheckTrue(B(Compare(SetOf(S("accept-input"), S("linkage-input"), S("call-response"), S("file-response"), S("sql-response")), categories_418) <= 0));
        foreach (var item426 in Items(report_421[S("items")]))
        {
            item_419 = item426;
            _ = CheckTrue(TestCall(Divide(out_420, item_419[S("editFile")]), "is_file"), item_419);
            _ = CheckTrue(item_419[S("contractType")]);
        }

        _ = RunChecked(ListOf(S("dotnet"), S("test"), Divide(out_420, S("Tests/CobolCS.Generated.Tests.csproj"))), I(1));
    }

    /// <summary>
    /// 元の解析規則とIRの順序を保持します。
    /// </summary>
    /// <param name="base_">base の解析値。</param>
    /// <param name="program_">program の解析値。</param>
    /// <returns>解析結果または更新後の状態。</returns>
    private V M_generate(V base_, V program_)
    {
        V fields_284 = Nil;
        V m_285 = Nil;
        V mapping_286 = Nil;
        V name_287 = Nil;
        V output_288 = Nil;
        V symbols_289 = Nil;
        output_288 = Divide(base_, program_);
        _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("generate") }).Concat(new V[] { Divide(Root, AddText(S("examples/WORKFLOW/"), Format(program_, string.Empty), S(".cbl"))) }).Concat(new V[] { S("--output") }).Concat(new V[] { output_288 })), I(0));
        mapping_286 = ReadJson(TestCall(Divide(output_288, S("reports/source-map.json")), "read_text"));
        name_287 = TestCall(Next(Sequence(from item290 in Items(mapping_286) let m_291 = item290 where Truth(B(Contains(m_291, S("executorType")))) select m_291[S("executorType")])), "removesuffix", S("Executor"));
        fields_284 = MakeDictionary(Sequence(from item292 in Items(mapping_286) let m_293 = item292 where Truth(B(Contains(m_293, S("symbolId")))) select ListOf(TestCall(m_293[S("originalName")], "split", S(" OF "))[I(0)], TestCall(m_293[S("generatedName")], "split", S("."))[Subtract(I(0), I(1))])));
        symbols_289 = ReadJson(TestCall(Divide(output_288, S("reports/ir.json")), "read_text"))[S("programs")][I(0)][S("symbols")];
        return ListOf(output_288, name_287, fields_284, symbols_289);
    }

    /// <summary>
    /// 元の解析規則とIRの順序を保持します。
    /// </summary>
    /// <param name="output_">output の解析値。</param>
    /// <param name="name_">name の解析値。</param>
    /// <param name="body_">body の解析値。</param>
    /// <returns>解析結果または更新後の状態。</returns>
    private V M_tests(V output_, V name_, V body_)
    {
        V path_294 = Nil;
        path_294 = Divide(output_, AddText(S("Tests/Cases/"), Format(name_, string.Empty), S("Tests.cs")));
        _ = TestCall(path_294, "write_text", AddText(S("using CobolCS.Runtime;\nusing Xunit;\n\nnamespace CobolCS.Generated.Tests;\n\n/// <summary>独立合成期待値を公開境界で検証します。</summary>\npublic sealed class "), Format(name_, string.Empty), S("Tests\n{\n    /// <summary>正常・境界・異常と値の分離を確認します。</summary>\n    [Fact]\n    public void "), Format(name_, string.Empty), S("_Sample()\n    {\n"), Format(TestCall(body_, "strip", S(char.ConvertFromUtf32(Number(I(10))))), string.Empty), S("\n    }\n}\n")));
        return path_294;
    }
}
