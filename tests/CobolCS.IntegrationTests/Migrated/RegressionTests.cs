using CobolCS.Frontend.Parsing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CobolCS.Frontend.Parsing.Values;
using static CobolCS.IntegrationTests.RegressionSupport;

namespace CobolCS.IntegrationTests;

/// <summary>既存の regression の入力、期待値、失敗条件を保持する.NET回帰テストです。</summary>
[TestClass]
public sealed class RegressionTests
{
    private readonly V state = new();
    private TestDirectory? directory;

    /// <summary>ケースごとの生成先を用意します。</summary>
    [TestInitialize]
    public void Initialize()
    {
        directory = new TestDirectory();
        state.Fields["base"] = PathValue(directory.Path);
        state.Fields["output"] = PathValue(Path.Combine(directory.Path, "out"));
    }

    /// <summary>生成物を削除します。</summary>
    [TestCleanup]
    public void Cleanup() => directory?.Dispose();

    /// <summary>remaining18 documentary native execution の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestRemaining18DocumentaryNativeExecution()
    {
        V ir_1 = Nil;
        V result_2 = Nil;
        V source_3 = Nil;
        source_3 = M_source(S("DISPLAY 'KEPT' GOBACK."));
        _ = TestCall(source_3, "write_text", TestCall(TestCall(source_3, "read_text"), "replace", S("DATA DIVISION."), S("AUTHOR. James O'Grady.\n  PROCEDURE and DATA are documentary words.\nDATA DIVISION.")));
        _ = RunCli(ListOf(S("generate"), source_3, S("--output"), TestMember(state, "output"), S("--mode"), S("strict")), I(0));
        result_2 = RunCli(ListOf(S("run"), TestMember(state, "output"), S("--program"), S("TEST")), I(0));
        _ = CheckEqual(ReadJson(TestMember(result_2, "stdout"))[S("display")], ListOf(S("KEPT")));
        ir_1 = ReadJson(TestCall(Divide(TestMember(state, "output"), S("reports/ir.json")), "read_text"));
        _ = CheckIn(S("O'Grady"), ir_1[S("programs")][I(0)][S("documentation")][I(0)][S("source")][S("raw")]);
    }

    /// <summary>remaining18 lifecycle fails at named stub の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestRemaining18LifecycleFailsAtNamedStub()
    {
        V attribute_4 = Nil;
        V result_5 = Nil;
        V source_6 = Nil;
        V stub_7 = Nil;
        V stubs_8 = Nil;
        foreach (var item9 in Items(ListOf(S("IS INITIAL"), S("RECURSIVE"))))
        {
            attribute_4 = item9;
            SetMember(state, "output", Divide(TestMember(state, "base"), TestCall(attribute_4, "replace", S(" "), S("-"))));
            source_6 = M_source(S("DISPLAY \"KEPT\" GOBACK."), program_: TestAdd(S("\"TEST\" "), attribute_4));
            _ = RunCli(ListOf(S("generate"), source_6, S("--output"), TestMember(state, "output"), S("--mode"), S("strict")), I(1));
            _ = RunCli(ListOf(S("generate"), source_6, S("--output"), TestMember(state, "output")), I(0));
            stubs_8 = ReadJson(TestCall(Divide(TestMember(state, "output"), S("reports/stubs.json")), "read_text"));
            stub_7 = Next(Sequence(from item10 in Items(stubs_8) let s_11 = item10 where Truth(B(Contains(TestCall(s_11[S("statement")], "get", S("reason"), S(string.Empty)), S("lifecycle")))) select s_11));
            result_5 = RunCli(ListOf(S("run"), TestMember(state, "output"), S("--program"), S("TEST")), I(2));
            _ = CheckIn(S("NotImplementedException"), TestMember(result_5, "stderr"));
            _ = CheckIn(stub_7[S("statementId")], TestMember(result_5, "stderr"));
            _ = CheckIn(S("source.cbl:2"), TestMember(result_5, "stderr"));
            _ = CheckIn(S("KEPT"), TestCall(S(string.Empty), "join", Sequence(from item12 in Items(TestCall(Divide(TestMember(state, "output"), S("Programs")), "glob", S("*.cs"))) let p_13 = item12 select TestCall(p_13, "read_text"))));
        }
    }

    /// <summary>remaining18 condition selection and unset stub の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestRemaining18ConditionSelectionAndUnsetStub()
    {
        V config_14 = Nil;
        V hashes_15 = Nil;
        V ir_16 = Nil;
        V output_17 = Nil;
        V result_18 = Nil;
        V source_19 = Nil;
        V stub_20 = Nil;
        V value_21 = Nil;
        source_19 = M_source(S("$IF P64 SET\nDISPLAY \"64\"\n$ELSE\nDISPLAY \"32\"\n$END\nDISPLAY \"AFTER\" GOBACK."), S("$IF P64 SET\n77 PTR PIC S9(18) COMP-5.\n$ELSE\n77 PTR PIC S9(9) COMP-5.\n$END"));
        hashes_15 = SetCopy();
        foreach (var item22 in Items(ListOf(B(true), B(false), Nil)))
        {
            value_21 = item22;
            output_17 = Divide(TestMember(state, "base"), TestStringValue(value_21));
            config_14 = Divide(TestMember(state, "base"), S("config.json"));
            _ = TestCall(config_14, "write_text", WriteJson(Put(Dict(), S("p64"), value_21)));
            _ = RunCli(ListOf(S("generate"), source_19, S("--config"), config_14, S("--output"), output_17), I(0));
            ir_16 = ReadJson(TestCall(Divide(output_17, S("reports/ir.json")), "read_text"));
            _ = TestCall(hashes_15, "add", ir_16[S("configHash")]);
            _ = CheckEqual(Length(ir_16[S("programs")][I(0)][S("conditionalDirectives")]), I(2));
            if (Truth(B(ReferenceEquals(value_21, Nil))))
            {
                result_18 = RunCli(ListOf(S("run"), output_17, S("--program"), S("TEST")), I(2));
                stub_20 = Next(Sequence(from item23 in Items(ReadJson(TestCall(Divide(output_17, S("reports/stubs.json")), "read_text"))) let s_24 = item23 where Truth(TestCall(s_24[S("statement")], "get", S("conditional"))) select s_24));
                _ = CheckIn(S("NotImplementedException"), TestMember(result_18, "stderr"));
                _ = CheckIn(stub_20[S("statementId")], TestMember(result_18, "stderr"));
                _ = CheckIn(S("source.cbl:11"), TestMember(result_18, "stderr"));
                _ = RunCli(ListOf(S("generate"), source_19, S("--config"), config_14, S("--output"), Divide(TestMember(state, "base"), S("strict-unset")), S("--mode"), S("strict")), I(1));
            }
            else
            {
                result_18 = RunCli(ListOf(S("run"), output_17, S("--program"), S("TEST")), I(0));
                _ = CheckEqual(ReadJson(TestMember(result_18, "stdout"))[S("display")], ListOf(Truth(value_21) ? S("64") : S("32"), S("AFTER")));
            }
        }

        _ = CheckEqual(Length(hashes_15), I(3));
        _ = TestCall(config_14, "write_text", S("{\"p64\":\"true\"}"));
        _ = CheckIn(S("p64 must be"), TestMember(RunCli(ListOf(S("analyze"), source_19, S("--config"), config_14, S("--output"), Divide(TestMember(state, "base"), S("invalid"))), I(2)), "stderr"));
    }

    /// <summary>remaining18 missing linkage unknown build の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestRemaining18MissingLinkageUnknownBuild()
    {
        V ir_25 = Nil;
        V linkage_26 = Nil;
        V result_27 = Nil;
        V source_28 = Nil;
        V symbol_29 = Nil;
        source_28 = M_source(S("DISPLAY KNOWN GOBACK."));
        _ = TestCall(source_28, "write_text", S("IDENTIFICATION DIVISION. PROGRAM-ID. TEST.\nDATA DIVISION. LINKAGE SECTION.\nCOPY \"missing.cpy\" REPLACING ==OLD== BY ==NEW==.\n01 KNOWN PIC X(3).\nPROCEDURE DIVISION USING MISSING KNOWN. DISPLAY KNOWN GOBACK."));
        _ = RunCli(ListOf(S("generate"), source_28, S("--output"), TestMember(state, "output")), I(0));
        _ = RunCli(ListOf(S("build"), TestMember(state, "output")), I(0));
        ir_25 = ReadJson(TestCall(Divide(TestMember(state, "output"), S("reports/ir.json")), "read_text"));
        symbol_29 = Next(Sequence(from item30 in Items(ir_25[S("programs")][I(0)][S("symbols")]) let s_31 = item30 where Truth(B(TestEqual(s_31[S("name")], S("MISSING")))) select s_31));
        _ = CheckEqual(symbol_29[S("scope")], S("LINKAGE"));
        _ = CheckIn(S("REPLACING"), symbol_29[S("missingCopyOrigins")][I(0)][S("source")][S("raw")]);
        result_27 = RunCli(ListOf(S("run"), TestMember(state, "output"), S("--program"), S("TEST")), I(2));
        _ = CheckIn(S("NotImplementedException"), TestMember(result_27, "stderr"));
        _ = CheckIn(S("TEST linkage source.cbl:5"), TestMember(result_27, "stderr"));
        _ = CheckIn(S("UnknownCobolValue"), TestCall(S(string.Empty), "join", Sequence(from item32 in Items(TestCall(Divide(TestMember(state, "output"), S("Models")), "glob", S("*.cs"))) let p_33 = item32 select TestCall(p_33, "read_text"))));
        linkage_26 = Next(Sequence(from item34 in Items(ReadJson(TestCall(Divide(TestMember(state, "output"), S("reports/stubs.json")), "read_text"))) let s_35 = item34 where Truth(B(TestEqual(s_35[S("statementId")], S("linkage")))) select s_35));
        _ = CheckIn(S("CobolReference<UnknownCobolValue>"), linkage_26[S("signature")]);
        _ = CheckIn(S("CobolReference<string>"), linkage_26[S("signature")]);
        _ = CheckNotIn(S("State state"), linkage_26[S("signature")]);
    }

    /// <summary>numeric class review の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestNumericClassReview()
    {
        V result_42 = Nil;
        result_42 = M_run_source(S("IF N IS NUMERIC DISPLAY \"OK\" ELSE DISPLAY \"NG\" END-IF GOBACK."), S("01 N PIC S9V99 VALUE -1.25."));
        _ = CheckEqual(result_42[S("display")], ListOf(S("OK")));
    }

    /// <summary>function move review の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestFunctionMoveReview()
    {
        V source_43 = Nil;
        source_43 = M_source(S("MOVE FUNCTION ABS(-1) TO T GOBACK."), S("01 T PIC X(3)."));
        _ = RunCli(ListOf(S("generate"), source_43, S("--output"), TestMember(state, "output"), S("--mode"), S("strict")), I(1));
        _ = CheckIn(S("CS_MOVE"), TestCall(Divide(TestMember(state, "output"), S("reports/diagnostics.json")), "read_text"));
    }

    /// <summary>numeric class matrix both backends の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestNumericClassMatrixBothBackends()
    {
        V backend_44 = Nil;
        V body_45 = Nil;
        V declarations_46 = Nil;
        V expected_47 = Nil;
        V i_48 = Nil;
        V output_49 = Nil;
        V result_50 = Nil;
        V source_51 = Nil;
        V value_52 = Nil;
        declarations_46 = ListOf();
        body_45 = ListOf();
        expected_47 = ListOf();
        foreach (var item53 in Items(Enumerate(ListOf(S("-1"), S("1.25"), S("-1.25"), S("0"), S("25")))))
        {
            var unpack54 = item53;
            i_48 = unpack54[I(0)];
            value_52 = unpack54[I(1)];
            _ = TestCall(declarations_46, "append", AddText(S("01 N"), Format(i_48, string.Empty), S(" PIC S9(3)V99 VALUE "), Format(value_52, string.Empty), S(".")));
            _ = TestCall(body_45, "append", AddText(S("IF N"), Format(i_48, string.Empty), S(" IS NUMERIC DISPLAY \"Y\" ELSE DISPLAY \"N\" END-IF IF N"), Format(i_48, string.Empty), S(" IS NOT NUMERIC DISPLAY \"N\" ELSE DISPLAY \"Y\" END-IF")));
            _ = TestCall(expected_47, "extend", ListOf(S("Y"), S("Y")));
        }

        foreach (var item55 in Items(Enumerate(ListOf(S("123"), S("-12"), S("1.2"), S(" 12"), S("1A2")))))
        {
            var unpack56 = item55;
            i_48 = unpack56[I(0)];
            value_52 = unpack56[I(1)];
            _ = TestCall(declarations_46, "append", AddText(S("01 T"), Format(i_48, string.Empty), S(" PIC X(3) VALUE \""), Format(value_52, string.Empty), S("\".")));
            _ = TestCall(body_45, "append", AddText(S("IF T"), Format(i_48, string.Empty), S(" IS NUMERIC DISPLAY \"Y\" ELSE DISPLAY \"N\" END-IF IF T"), Format(i_48, string.Empty), S(" IS NOT NUMERIC DISPLAY \"Y\" ELSE DISPLAY \"N\" END-IF")));
            _ = TestCall(expected_47, "extend", Truth(B(TestEqual(i_48, I(0)))) ? ListOf(S("Y"), S("N")) : ListOf(S("N"), S("Y")));
        }

        source_51 = M_source(TestAdd(TestCall(S(" "), "join", body_45), S(" GOBACK.")), TestCall(S("\n"), "join", declarations_46));
        foreach (var item57 in Items(ListOf(S("source"), S("interpreter"))))
        {
            backend_44 = item57;
            output_49 = Divide(TestMember(state, "base"), backend_44);
            _ = RunCli(ListOf(S("generate"), source_51, S("--output"), output_49, S("--backend"), backend_44), I(0));
            result_50 = RunCli(ListOf(S("run"), output_49, S("--program"), S("TEST")), I(0));
            _ = CheckEqual(ReadJson(TestMember(result_50, "stdout"))[S("display")], expected_47);
        }
    }

    /// <summary>function type validation matrix の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestFunctionTypeValidationMatrix()
    {
        V backend_58 = Nil;
        V diagnostic_59 = Nil;
        V expression_60 = Nil;
        V result_61 = Nil;
        V source_62 = Nil;
        foreach (var item63 in Items(ListOf(S("source"), S("interpreter"))))
        {
            backend_58 = item63;
            foreach (var item64 in Items(ListOf(ListOf(S("FUNCTION ABS(-1)"), S("CS_MOVE")), ListOf(S("FUNCTION LENGTH(\"abc\")"), S("CS_MOVE")), ListOf(S("FUNCTION NUMVAL(\"12\")"), S("CS_MOVE")), ListOf(S("FUNCTION INTEGER(-1.2)"), S("CS_MOVE")), ListOf(S("FUNCTION ABS(FUNCTION INTEGER(-1.2))"), S("CS_MOVE")), ListOf(S("1"), S("CS_MOVE")), ListOf(S("FUNCTION MYSTERY(1)"), S("CS_FUNCTION")), ListOf(S("FUNCTION ABS(1, 2)"), S("CS_FUNCTION")))))
            {
                var unpack65 = item64;
                expression_60 = unpack65[I(0)];
                diagnostic_59 = unpack65[I(1)];
                source_62 = M_source(AddText(S("MOVE "), Format(expression_60, string.Empty), S(" TO T GOBACK.")), S("01 T PIC X(3)."));
                _ = RunCli(ListOf(S("generate"), source_62, S("--output"), TestMember(state, "output"), S("--backend"), backend_58, S("--mode"), S("strict")), I(1));
                _ = CheckIn(diagnostic_59, TestCall(Divide(TestMember(state, "output"), S("reports/diagnostics.json")), "read_text"));
            }
        }

        result_61 = M_run_source(S("MOVE FUNCTION ABS(-1) TO N MOVE FUNCTION LENGTH(\"abc\") TO N MOVE FUNCTION NUMVAL(\"12\") TO N MOVE FUNCTION INTEGER(-1.2) TO N MOVE FUNCTION TRIM(\" A \") TO T GOBACK."), S("01 N PIC S9(3).\n01 T PIC X(3)."));
        _ = CheckEqual(result_61[S("fields")], Put(Put(Dict(), S("N"), S("-2")), S("T"), S("A  ")));
    }

    /// <summary>source has no runtime ir and is portable の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestSourceHasNoRuntimeIrAndIsPortable()
    {
        V build_66 = Nil;
        V code_67 = Nil;
        V forbidden_68 = Nil;
        V native_69 = Nil;
        V portable_70 = Nil;
        V result_71 = Nil;
        V source_72 = Nil;
        source_72 = M_source(S("MOVE 2 TO N IF N > 0 ADD 1 TO N END-IF PERFORM 2 TIMES SUBTRACT 1 FROM N END-PERFORM DISPLAY N GOBACK."));
        _ = RunCli(ListOf(S("generate"), source_72, S("--output"), TestMember(state, "output")), I(0));
        code_67 = TestCall(S("\n"), "join", Sequence(from item73 in Items(TestCall(Divide(TestMember(state, "output"), S("Programs")), "glob", S("*.cs"))) let p_74 = item73 select TestCall(p_74, "read_text")));
        foreach (var item75 in Items(ListOf(S("Instruction[]"), S("Machine.Run"), S("new Machine"), S("J.Parse"), S("JsonNode e"), S("Eval("))))
        {
            forbidden_68 = item75;
            _ = CheckNotIn(forbidden_68, code_67);
        }

        foreach (var item76 in Items(ListOf(S("if ("), S("for ("), S(" + "), S(" = "))))
        {
            native_69 = item76;
            _ = CheckIn(native_69, code_67);
        }

        _ = CheckTrue(ListCopy(TestCall(Divide(TestMember(state, "output"), S("Models")), "glob", S("*State.g.cs"))));
        portable_70 = Divide(TestMember(state, "base"), S("portable"));
        _ = CopyTree(TestMember(state, "output"), portable_70);
        _ = RemoveTree(Divide(portable_70, S("reports")));
        _ = TestCall(Divide(portable_70, S("manifest.json")), "unlink");
        build_66 = RunProcess(ListOf(S("dotnet"), S("build"), TestStringValue(Divide(portable_70, S("CobolCS.Generated.csproj"))), S("--nologo")), I(150));
        _ = CheckEqual(TestMember(build_66, "returncode"), I(0), TestAdd(TestMember(build_66, "stdout"), TestMember(build_66, "stderr")));
        result_71 = RunProcess(ListOf(S("dotnet"), TestStringValue(Divide(portable_70, S("bin/Debug/net10.0/CobolCS.Generated.dll"))), S("TEST")), I(20));
        _ = CheckEqual(TestMember(result_71, "returncode"), I(0), TestMember(result_71, "stderr"));
        _ = CheckEqual(ReadJson(TestMember(result_71, "stdout"))[S("fields")][S("N")], S("1"));
    }

    /// <summary>call stub replacement and regeneration の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestCallStubReplacementAndRegeneration()
    {
        V edited_82 = Nil;
        V failure_83 = Nil;
        V path_84 = Nil;
        V refusal_85 = Nil;
        V response_type_86 = Nil;
        V result_87 = Nil;
        V source_88 = Nil;
        V stub_89 = Nil;
        source_88 = M_source(S("CALL \"SERVICE\" USING BY REFERENCE N BY CONTENT T RETURNING R END-CALL ADD 1 TO N DISPLAY N R T GOBACK."), S("01 N PIC 9 VALUE 1.\n01 R PIC 9.\n01 T PIC X(3) VALUE \"ABC\"."));
        _ = RunCli(ListOf(S("generate"), source_88, S("--output"), TestMember(state, "output"), S("--mode"), S("strict")), I(0));
        _ = RunCli(ListOf(S("build"), TestMember(state, "output")), I(0));
        failure_83 = RunCli(ListOf(S("run"), TestMember(state, "output"), S("--program"), S("TEST")), I(2));
        _ = CheckIn(S("s000000"), TestMember(failure_83, "stderr"));
        _ = CheckIn(S("source.cbl:"), TestMember(failure_83, "stderr"));
        stub_89 = ReadJson(TestCall(Divide(TestMember(state, "output"), S("reports/stubs.json")), "read_text"))[I(0)];
        response_type_86 = TestCall(stub_89[S("signature")], "split")[I(0)];
        path_84 = M_edit_stub(stub_89, AddText(S("return new "), Format(response_type_86, string.Empty), S("(\"00\", DependencyOutcome.Normal, request.Argument0.Abs() + new Number(1), new Number(4));")));
        _ = TestCall(path_84, "write_bytes", TestAdd(new V(Convert.FromHexString("efbbbf")), TestCall(path_84, "read_bytes")));
        edited_82 = TestCall(path_84, "read_bytes");
        _ = RunCli(ListOf(S("generate"), source_88, S("--output"), TestMember(state, "output"), S("--mode"), S("strict")), I(0));
        _ = CheckEqual(TestCall(path_84, "read_bytes"), edited_82);
        result_87 = RunCli(ListOf(S("run"), TestMember(state, "output"), S("--program"), S("TEST")), I(0));
        _ = CheckEqual(ReadJson(TestMember(result_87, "stdout"))[S("display")], ListOf(S("34ABC")));
        _ = TestCall(source_88, "write_text", TestCall(TestCall(source_88, "read_text"), "replace", S("BY REFERENCE N"), S("BY REFERENCE T")));
        refusal_85 = RunCli(ListOf(S("generate"), source_88, S("--output"), TestMember(state, "output")), I(2));
        _ = CheckIn(S("Edited stub binding changed"), TestMember(refusal_85, "stderr"));
        _ = CheckEqual(TestCall(path_84, "read_bytes"), edited_82);
        _ = RunCli(ListOf(S("build"), TestMember(state, "output")), I(0));
    }

    /// <summary>nested source temporaries の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestNestedSourceTemporaries()
    {
        V result_90 = Nil;
        result_90 = M_run_source(S("EVALUATE N WHEN 0 EVALUATE N WHEN 0 DISPLAY \"nested\" END-EVALUATE END-EVALUATE ADD 10 TO N ON SIZE ERROR ADD 1 TO N NOT ON SIZE ERROR DISPLAY \"recovered\" END-ADD END-ADD GOBACK."), S("01 N PIC 9."));
        _ = CheckEqual(result_90[S("display")], ListOf(S("nested"), S("recovered")));
        _ = CheckEqual(result_90[S("fields")][S("N")], S("1"));
    }

    /// <summary>nested call response and reference temporaries の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestNestedCallResponseAndReferenceTemporaries()
    {
        V outcome_91 = Nil;
        V response_92 = Nil;
        V result_93 = Nil;
        V source_94 = Nil;
        V stub_95 = Nil;
        V target_96 = Nil;
        source_94 = M_source(S("CALL \"A\" USING N(1) ON EXCEPTION CALL \"B\" USING N(1) END-CALL END-CALL GOBACK."), S("01 N PIC 9 OCCURS 2 TIMES."));
        _ = RunCli(ListOf(S("generate"), source_94, S("--output"), TestMember(state, "output")), I(0));
        _ = RunCli(ListOf(S("build"), TestMember(state, "output")), I(0));
        foreach (var item97 in Items(ReadJson(TestCall(Divide(TestMember(state, "output"), S("reports/stubs.json")), "read_text"))))
        {
            stub_95 = item97;
            response_92 = TestCall(stub_95[S("signature")], "split")[I(0)];
            target_96 = stub_95[S("dependency")][S("name")];
            outcome_91 = Truth(B(TestEqual(target_96, S("A")))) ? S("Exception") : S("Normal");
            _ = M_edit_stub(stub_95, AddText(S("return new "), Format(response_92, string.Empty), S("(\"00\", DependencyOutcome."), Format(outcome_91, string.Empty), S(", request.Argument0 + new Number(1));")));
        }

        result_93 = ReadJson(TestMember(RunCli(ListOf(S("run"), TestMember(state, "output"), S("--program"), S("TEST")), I(0)), "stdout"));
        _ = CheckEqual(result_93[S("fields")][S("N")], ListOf(S("2"), S("0")));
        _ = CheckEqual(Length(result_93[S("dependencies")]), I(2));
    }

    /// <summary>not on size error only の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestNotOnSizeErrorOnly()
    {
        V calculation_98 = Nil;
        V expected_99 = Nil;
        V prefix_100 = Nil;
        V result_101 = Nil;
        foreach (var item102 in Items(ListOf(S(string.Empty), S("MAIN. "))))
        {
            prefix_100 = item102;
            foreach (var item103 in Items(ListOf(ListOf(S("10"), ListOf()), ListOf(S("1 / 0"), ListOf()), ListOf(S("9"), ListOf(S("fits"))))))
            {
                var unpack104 = item103;
                calculation_98 = unpack104[I(0)];
                expected_99 = unpack104[I(1)];
                result_101 = M_run_source(TestAdd(prefix_100, AddText(S("COMPUTE N = "), Format(calculation_98, string.Empty), S(" NOT ON SIZE ERROR DISPLAY \"fits\" END-COMPUTE GOBACK."))), S("01 N PIC 9."));
                _ = CheckEqual(result_101[S("display")], expected_99);
            }
        }
    }

    /// <summary>edited stub reordering is refused atomically の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestEditedStubReorderingIsRefusedAtomically()
    {
        V before_105 = Nil;
        V p_106 = Nil;
        V response_107 = Nil;
        V result_108 = Nil;
        V source_109 = Nil;
        V stub_110 = Nil;
        source_109 = M_source(S("CALL \"A\" USING N END-CALL CALL \"B\" USING N END-CALL GOBACK."));
        _ = RunCli(ListOf(S("generate"), source_109, S("--output"), TestMember(state, "output")), I(0));
        foreach (var item111 in Items(ReadJson(TestCall(Divide(TestMember(state, "output"), S("reports/stubs.json")), "read_text"))))
        {
            stub_110 = item111;
            response_107 = TestCall(stub_110[S("signature")], "split")[I(0)];
            _ = M_edit_stub(stub_110, AddText(S("return new "), Format(response_107, string.Empty), S("(\"00\", DependencyOutcome.Normal, request.Argument0 + new Number(1));")));
        }

        before_105 = MakeDictionary(Sequence(from item112 in Items(TestCall(TestMember(state, "output"), "rglob", S("*"))) let p_113 = item112 where Truth(TestCall(p_113, "is_file")) select ListOf(TestCall(p_113, "relative_to", TestMember(state, "output")), TestCall(p_113, "read_bytes"))));
        _ = TestCall(source_109, "write_text", TestCall(TestCall(TestCall(TestCall(source_109, "read_text"), "replace", S("\"A\""), S("\"TMP\"")), "replace", S("\"B\""), S("\"A\"")), "replace", S("\"TMP\""), S("\"B\"")));
        result_108 = RunCli(ListOf(S("generate"), source_109, S("--output"), TestMember(state, "output")), I(2));
        _ = CheckIn(S("Edited stub binding changed"), TestMember(result_108, "stderr"));
        _ = CheckEqual(before_105, MakeDictionary(Sequence(from item114 in Items(TestCall(TestMember(state, "output"), "rglob", S("*"))) let p_115 = item114 where Truth(TestCall(p_115, "is_file")) select ListOf(TestCall(p_115, "relative_to", TestMember(state, "output")), TestCall(p_115, "read_bytes")))));
    }

    /// <summary>public headers numbered paragraphs and filler の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestPublicHeadersNumberedParagraphsAndFiller()
    {
        V ir_116 = Nil;
        V result_117 = Nil;
        V source_118 = Nil;
        source_118 = M_source(S("100-MAIN. PERFORM 200-WORK GOBACK. 200-WORK. DISPLAY N."), S("01 G.\n 05 FILLER PIC X VALUE \"A\".\n 05 FILLER PIC X VALUE \"B\".\n 05 N PIC 9 VALUE 3."));
        _ = TestCall(source_118, "write_text", TestCall(TestCall(source_118, "read_text"), "replace", S("PROGRAM-ID. TEST."), S("PROGRAM-ID. TEST\nAUTHOR. Synthetic author.\nDATE-WRITTEN. Test only.")));
        _ = RunCli(ListOf(S("generate"), source_118, S("--output"), TestMember(state, "output"), S("--mode"), S("strict")), I(0));
        result_117 = ReadJson(TestMember(RunCli(ListOf(S("run"), TestMember(state, "output"), S("--program"), S("TEST")), I(0)), "stdout"));
        _ = CheckEqual(result_117[S("display")], ListOf(S("3")));
        ir_116 = ReadJson(TestCall(Divide(TestMember(state, "output"), S("reports/ir.json")), "read_text"));
        _ = CheckIn(S("Synthetic author"), TestStringValue(ir_116[S("programs")][I(0)][S("documentation")]));
        _ = CheckNotIn(S("FILLER"), TestStringValue(result_117[S("fields")]));
    }

    /// <summary>public extensions are enumerated の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestPublicExtensionsAreEnumerated()
    {
        V index_119 = Nil;
        V sources_120 = Nil;
        V suffix_121 = Nil;
        sources_120 = Divide(TestMember(state, "base"), S("sources"));
        _ = TestCall(sources_120, "mkdir");
        foreach (var item122 in Items(Enumerate(ListOf(S(".COBOL"), S(".SQB"), S(".CBL"), S(".COB")))))
        {
            var unpack123 = item122;
            index_119 = unpack123[I(0)];
            suffix_121 = unpack123[I(1)];
            _ = TestCall(Divide(sources_120, TestAdd(TestStringValue(index_119), suffix_121)), "write_text", AddText(S("IDENTIFICATION DIVISION. PROGRAM-ID. P"), Format(index_119, string.Empty), S(". PROCEDURE DIVISION. GOBACK.")));
        }

        _ = TestCall(Divide(sources_120, S("not-an-entry.cpy")), "write_text", S("01 X PIC 9."));
        _ = RunCli(ListOf(S("generate"), sources_120, S("--output"), TestMember(state, "output")), I(0));
        _ = CheckEqual(Length(ReadJson(TestCall(Divide(TestMember(state, "output"), S("manifest.json")), "read_text"))[S("programs")]), I(4));
    }

    /// <summary>no parsed program still produces diagnostics の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestNoParsedProgramStillProducesDiagnostics()
    {
        V command_124 = Nil;
        V diagnostics_125 = Nil;
        V options_126 = Nil;
        V source_127 = Nil;
        source_127 = Divide(TestMember(state, "base"), S("invalid.cbl"));
        _ = TestCall(source_127, "write_text", S("IDENTIFICATION DIVISION. PROGRAM-ID. BROKEN. DATA DIVISION. WORKING-STORAGE SECTION."));
        foreach (var item128 in Items(ListOf(ListOf(S("analyze"), ListOf()), ListOf(S("generate"), ListOf(S("--mode"), S("scaffold"))), ListOf(S("analyze"), ListOf()))))
        {
            var unpack129 = item128;
            command_124 = unpack129[I(0)];
            options_126 = unpack129[I(1)];
            _ = RunCli(ListOf(Enumerable.Empty<V>().Concat(new V[] { command_124 }).Concat(new V[] { source_127 }).Concat(new V[] { S("--output") }).Concat(new V[] { TestMember(state, "output") }).Concat(Items(options_126))), I(0));
            diagnostics_125 = ReadJson(TestCall(Divide(TestMember(state, "output"), S("reports/diagnostics.json")), "read_text"));
            _ = CheckTrue(Any(Sequence(from item130 in Items(diagnostics_125) let d_131 = item130 select B(TestEqual(d_131[S("code")], S("PARSE_ERROR"))))));
            _ = CheckFalse(ReadJson(TestCall(Divide(TestMember(state, "output"), S("manifest.json")), "read_text"))[S("judgments")][S("generated")]);
        }
    }

    /// <summary>add giving without to の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestAddGivingWithoutTo()
    {
        V result_132 = Nil;
        result_132 = M_run_source(S("ADD A B 3 GIVING A N DISPLAY A N GOBACK."), S("01 A PIC 9 VALUE 1.\n01 B PIC 9 VALUE 2.\n01 N PIC 9 VALUE 9."));
        _ = CheckEqual(result_132[S("display")], ListOf(S("66")));
    }

    /// <summary>ambiguous qualified fixture names do not break build の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestAmbiguousQualifiedFixtureNamesDoNotBreakBuild()
    {
        V fixture_133 = Nil;
        V result_134 = Nil;
        result_134 = M_run_source(S("DISPLAY \"done\" GOBACK."), S("01 N PIC 9 VALUE 1.\n01 N PIC 9 VALUE 2."));
        _ = CheckEqual(ListCopy(TestCall(result_134[S("fields")], "values")), ListOf(S("1"), S("2")));
        _ = CheckTrue(All(Sequence(from item135 in Items(result_134[S("fields")]) let name_136 = item135 select TestCall(name_136, "startswith", S("N#f")))));
        fixture_133 = Divide(TestMember(state, "base"), S("ambiguous.json"));
        _ = TestCall(fixture_133, "write_text", S("{\"fields\":{\"N\":\"3\"}}"));
        _ = RunCli(ListOf(S("run"), TestMember(state, "output"), S("--program"), S("TEST"), S("--fixture"), fixture_133), I(2));
    }

    /// <summary>unresolved flow and header stubs have unique control names の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestUnresolvedFlowAndHeaderStubsHaveUniqueControlNames()
    {
        V failure_137 = Nil;
        V source_138 = Nil;
        source_138 = M_source(S("GO TO MISSING GOBACK."));
        _ = TestCall(source_138, "write_text", TestCall(TestCall(source_138, "read_text"), "replace", S("DATA DIVISION."), S("UNRECOGNIZED. CONTENT.\nDATA DIVISION.")));
        _ = RunCli(ListOf(S("generate"), source_138, S("--output"), TestMember(state, "output")), I(0));
        _ = RunCli(ListOf(S("build"), TestMember(state, "output")), I(0));
        failure_137 = RunCli(ListOf(S("run"), TestMember(state, "output"), S("--program"), S("TEST")), I(2));
        _ = CheckIn(S("source.cbl:"), TestMember(failure_137, "stderr"));
    }

    /// <summary>fixed record metadata is retained の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestFixedRecordMetadataIsRetained()
    {
        V dependency_139 = Nil;
        V source_140 = Nil;
        source_140 = Divide(TestMember(state, "base"), S("file.cbl"));
        _ = TestCall(source_140, "write_text", S("IDENTIFICATION DIVISION. PROGRAM-ID. TEST. ENVIRONMENT DIVISION. INPUT-OUTPUT SECTION. FILE-CONTROL. SELECT INPUT-FILE ASSIGN TO HOSTDD. DATA DIVISION. FILE SECTION. FD INPUT-FILE RECORDING MODE F. 01 REC PIC X(8). PROCEDURE DIVISION. GOBACK."));
        _ = RunCli(ListOf(S("generate"), source_140, S("--output"), TestMember(state, "output")), I(0));
        _ = RunCli(ListOf(S("build"), TestMember(state, "output")), I(0));
        dependency_139 = ReadJson(TestCall(Divide(TestMember(state, "output"), S("reports/ir.json")), "read_text"))[S("programs")][I(0)][S("dependencies")][I(0)];
        _ = CheckEqual(dependency_139[S("recordingMode")], S("F"));
        _ = CheckIn(S("RECORDING MODE F"), dependency_139[S("descriptionSource")][S("raw")]);
        _ = TestCall(source_140, "write_text", TestCall(TestCall(source_140, "read_text"), "replace", S("RECORDING MODE F."), S("RECORD CONTAINS 8 CHARACTERS RECORDING MODE F.")));
        _ = RunCli(ListOf(S("generate"), source_140, S("--output"), TestMember(state, "output")), I(0));
        _ = RunCli(ListOf(S("build"), TestMember(state, "output")), I(0));
        dependency_139 = ReadJson(TestCall(Divide(TestMember(state, "output"), S("reports/ir.json")), "read_text"))[S("programs")][I(0)][S("dependencies")][I(0)];
        _ = CheckEqual(dependency_139[S("file")][S("minLength")], I(8));
        _ = CheckEqual(dependency_139[S("recordingMode")], S("F"));
        _ = TestCall(source_140, "write_text", TestCall(TestCall(source_140, "read_text"), "replace", S("RECORDING MODE F."), S("RECORDING MODE F LABEL RECORDS ARE OMITTED BLOCK CONTAINS 0 RECORDS DATA RECORD IS REC.")));
        _ = RunCli(ListOf(S("generate"), source_140, S("--output"), TestMember(state, "output")), I(0));
        _ = RunCli(ListOf(S("build"), TestMember(state, "output")), I(0));
        dependency_139 = ReadJson(TestCall(Divide(TestMember(state, "output"), S("reports/ir.json")), "read_text"))[S("programs")][I(0)][S("dependencies")][I(0)];
        _ = CheckEqual(dependency_139[S("blockClause")][S("max")], I(0));
        _ = CheckEqual(dependency_139[S("file")][S("minLength")], I(8));
        _ = CheckEqual(dependency_139[S("dataRecords")], ListOf(S("REC")));
        _ = CheckEqual(Length(dependency_139[S("descriptionClauses")]), I(5));
        _ = CheckIn(S("LABEL RECORDS ARE OMITTED"), TestCall(Divide(TestMember(state, "output"), S("reports/dependencies.json")), "read_text"));
    }

    /// <summary>procedure using comma keeps argument order の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestProcedureUsingCommaKeepsArgumentOrder()
    {
        V ir_141 = Nil;
        V source_142 = Nil;
        source_142 = Divide(TestMember(state, "base"), S("linkage.cbl"));
        _ = TestCall(source_142, "write_text", S("IDENTIFICATION DIVISION. PROGRAM-ID. TEST. DATA DIVISION. LINKAGE SECTION. 01 A PIC 9. 01 B PIC X. PROCEDURE DIVISION USING A, BY CONTENT B. GOBACK."));
        _ = RunCli(ListOf(S("generate"), source_142, S("--output"), TestMember(state, "output")), I(0));
        _ = RunCli(ListOf(S("build"), TestMember(state, "output")), I(0));
        ir_141 = ReadJson(TestCall(Divide(TestMember(state, "output"), S("reports/ir.json")), "read_text"));
        _ = CheckEqual(ListOf(from item143 in Items(ir_141[S("programs")][I(0)][S("using")]) let a_144 = item143 select a_144[S("mode")]), ListOf(S("reference"), S("content")));
        _ = RunCli(ListOf(S("run"), TestMember(state, "output"), S("--program"), S("TEST")), I(2));
    }

    /// <summary>unreachable stubs and program isolation の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestUnreachableStubsAndProgramIsolation()
    {
        V bad_145 = Nil;
        V diagnostics_146 = Nil;
        V good_147 = Nil;
        V result_148 = Nil;
        V source_149 = Nil;
        V sources_150 = Nil;
        sources_150 = Divide(TestMember(state, "base"), S("sources"));
        _ = TestCall(sources_150, "mkdir");
        good_147 = M_source(S("DISPLAY \"GOOD\" GOBACK."), program_: S("GOOD"));
        _ = TestCall(Divide(sources_150, S("good.cbl")), "write_text", TestCall(good_147, "read_text"));
        bad_145 = M_source(S("MOVE FUNCTION ABS(-1) TO T GOBACK."), S("01 T PIC X(3)."), program_: S("BAD"));
        _ = TestCall(Divide(sources_150, S("bad.cbl")), "write_text", TestCall(bad_145, "read_text"));
        _ = RunCli(ListOf(S("generate"), sources_150, S("--output"), TestMember(state, "output"), S("--mode"), S("strict")), I(1));
        _ = RunCli(ListOf(S("build"), TestMember(state, "output")), I(0));
        result_148 = RunCli(ListOf(S("run"), TestMember(state, "output"), S("--program"), S("GOOD")), I(0));
        _ = CheckEqual(ReadJson(TestMember(result_148, "stdout"))[S("display")], ListOf(S("GOOD")));
        diagnostics_146 = ReadJson(TestCall(Divide(TestMember(state, "output"), S("reports/diagnostics.json")), "read_text"));
        _ = CheckTrue(Any(Sequence(from item151 in Items(diagnostics_146) let d_152 = item151 select And(B(TestEqual(TestCall(d_152, "get", S("programId")), S("BAD"))), () => B(TestEqual(d_152[S("code")], S("CS_MOVE")))))));
        source_149 = M_source(S("IF N = 1 CALL \"UNREACHED\" END-CALL END-IF DISPLAY \"OK\" GOBACK."));
        _ = RunCli(ListOf(S("generate"), source_149, S("--output"), Divide(TestMember(state, "base"), S("unreachable"))), I(0));
        result_148 = RunCli(ListOf(S("run"), Divide(TestMember(state, "base"), S("unreachable")), S("--program"), S("TEST")), I(0));
        _ = CheckEqual(ReadJson(TestMember(result_148, "stdout"))[S("display")], ListOf(S("OK")));
    }

    /// <summary>sql typed stub updates and undefined response の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestSqlTypedStubUpdatesAndUndefinedResponse()
    {
        V path_153 = Nil;
        V response_type_154 = Nil;
        V result_155 = Nil;
        V source_156 = Nil;
        V stub_157 = Nil;
        source_156 = M_source(S("EXEC SQL SELECT VALUE INTO :N :IND FROM T WHERE K = :K END-EXEC IF SQLCODE = 0 DISPLAY N ELSE DISPLAY \"ERROR\" END-IF GOBACK."), S("01 N PIC 9(2).\n01 K PIC 9 VALUE 1.\n01 IND PIC S9(4).\n01 SQLCODE PIC S9(9)."));
        _ = RunCli(ListOf(S("generate"), source_156, S("--output"), TestMember(state, "output")), I(0));
        _ = RunCli(ListOf(S("build"), TestMember(state, "output")), I(0));
        stub_157 = ReadJson(TestCall(Divide(TestMember(state, "output"), S("reports/stubs.json")), "read_text"))[I(0)];
        response_type_154 = TestCall(stub_157[S("signature")], "split")[I(0)];
        _ = M_edit_stub(stub_157, AddText(S("return new "), Format(response_type_154, string.Empty), S("(\"00000\", DependencyOutcome.Normal, new Number(42), new Number(0), new Number(0));")));
        result_155 = RunCli(ListOf(S("run"), TestMember(state, "output"), S("--program"), S("TEST")), I(0));
        _ = CheckEqual(ReadJson(TestMember(result_155, "stdout"))[S("display")], ListOf(S("42")));
        path_153 = Divide(TestMember(state, "output"), stub_157[S("file")]);
        _ = TestCall(path_153, "write_text", TestRegexSub(S("return new [^\\n]*;"), S("return null!;"), TestCall(path_153, "read_text")));
        result_155 = RunCli(ListOf(S("run"), TestMember(state, "output"), S("--program"), S("TEST")), I(2));
        _ = CheckIn(S("Undefined dependency response"), TestMember(result_155, "stderr"));
        _ = CheckIn(S("s000000"), TestMember(result_155, "stderr"));
    }

    /// <summary>unsupported c to f are editable block stubs の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestUnsupportedCToFAreEditableBlockStubs()
    {
        V cases_158 = Nil;
        V declarations_159 = Nil;
        V index_160 = Nil;
        V kind_161 = Nil;
        V result_162 = Nil;
        V source_163 = Nil;
        V statement_164 = Nil;
        V stub_165 = Nil;
        V stubs_166 = Nil;
        cases_158 = ListOf(ListOf(S("STRING \"A\" DELIMITED BY SIZE INTO T END-STRING"), S("stringTransfer")), ListOf(S("STRING \"A\" DELIMITED BY \"X\" INTO T END-STRING"), S("stringTransfer")), ListOf(S("UNSTRING T DELIMITED BY \",\" INTO U END-UNSTRING"), S("unstringTransfer")), ListOf(S("INSPECT T TALLYING N FOR ALL \"A\""), S("inspectOperations")), ListOf(S("INITIALIZE T"), S("initialize")), ListOf(S("INITIALIZE T REPLACING ALPHANUMERIC DATA BY \"X\""), S("initialize")), ListOf(S("MOVE CORRESPONDING G TO H"), S("corresponding")), ListOf(S("SEARCH TAB WHEN ITEM(IX) = 1 DISPLAY \"INNER\" END-SEARCH"), S("search")), ListOf(S("SEARCH ALL TAB WHEN ITEM(IX) = 1 DISPLAY \"INNER\" END-SEARCH"), S("search")), ListOf(S("EVALUATE N ALSO N WHEN 1 ALSO ANY DISPLAY \"INNER\" WHEN OTHER DISPLAY \"OTHER\" END-EVALUATE"), S("controlStub")), ListOf(S("EVALUATE N WHEN 1 THRU 3 DISPLAY \"INNER\" END-EVALUATE"), S("controlStub")), ListOf(S("PERFORM VARYING N FROM 1 BY 1 UNTIL N > 2 AFTER IX FROM 1 BY 1 UNTIL IX > 2 DISPLAY \"INNER\" END-PERFORM"), S("loop")));
        declarations_159 = S("01 T PIC X(4).\n01 U PIC X(4).\n01 N PIC 9.\n01 G.\n 05 A PIC 9.\n01 H.\n 05 A PIC 9.\n01 TAB OCCURS 2 TIMES INDEXED BY IX.\n 05 ITEM PIC 9.");
        foreach (var item167 in Items(Enumerate(cases_158)))
        {
            var unpack168 = item167;
            index_160 = unpack168[I(0)];
            var unpack169 = unpack168[I(1)];
            statement_164 = unpack169[I(0)];
            kind_161 = unpack169[I(1)];
            SetMember(state, "output", Divide(TestMember(state, "base"), TestAdd(S("out"), TestStringValue(index_160))));
            source_163 = M_source(TestAdd(statement_164, S(" DISPLAY \"AFTER\" GOBACK.")), declarations_159);
            _ = RunCli(ListOf(S("generate"), source_163, S("--output"), TestMember(state, "output")), I(0));
            _ = RunCli(ListOf(S("build"), TestMember(state, "output")), I(0));
            stubs_166 = ReadJson(TestCall(Divide(TestMember(state, "output"), S("reports/stubs.json")), "read_text"));
            if (Truth(B(Contains(ListOf(S("STRING \"A\" DELIMITED BY SIZE INTO T END-STRING"), S("INITIALIZE T")), statement_164))))
            {
                _ = CheckFalse(stubs_166);
                result_162 = RunCli(ListOf(S("run"), TestMember(state, "output"), S("--program"), S("TEST")), I(0));
                _ = CheckEqual(ReadJson(TestMember(result_162, "stdout"))[S("display")], ListOf(S("AFTER")));
                _ = CheckEqual(ReadJson(TestMember(result_162, "stdout"))[S("fields")][S("T")], Truth(B(TestEqual(kind_161, S("stringTransfer")))) ? S("A   ") : S("    "));
                continue;
            }

            stub_165 = Next(Sequence(from item170 in Items(stubs_166) let s_171 = item170 where Truth(B(TestEqual(s_171[S("statement")][S("kind")], kind_161))) select s_171));
            result_162 = RunCli(ListOf(S("run"), TestMember(state, "output"), S("--program"), S("TEST")), I(2));
            _ = CheckIn(stub_165[S("statementId")], TestMember(result_162, "stderr"));
            _ = M_edit_stub(stub_165, S("return ControlResult.Continue;"));
            result_162 = RunCli(ListOf(S("run"), TestMember(state, "output"), S("--program"), S("TEST")), I(0));
            _ = CheckEqual(ReadJson(TestMember(result_162, "stdout"))[S("display")], ListOf(S("AFTER")));
        }
    }

    /// <summary>typed expression stub and table fixture の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestTypedExpressionStubAndTableFixture()
    {
        V entries_172 = Nil;
        V fixture_173 = Nil;
        V result_174 = Nil;
        V source_175 = Nil;
        V stub_176 = Nil;
        source_175 = M_source(S("MOVE FUNCTION SUM(1, 2) TO N(2) DISPLAY N(1) N(2) GOBACK."), S("01 N PIC 9 OCCURS 2 TIMES."));
        _ = RunCli(ListOf(S("generate"), source_175, S("--output"), TestMember(state, "output")), I(0));
        _ = RunCli(ListOf(S("build"), TestMember(state, "output")), I(0));
        stub_176 = ReadJson(TestCall(Divide(TestMember(state, "output"), S("reports/stubs.json")), "read_text"))[I(0)];
        _ = CheckTrue(TestCall(stub_176[S("signature")], "startswith", S("Number ")));
        _ = M_edit_stub(stub_176, S("return argument0 + argument1;"));
        fixture_173 = Divide(TestMember(state, "base"), S("fixture.json"));
        _ = TestCall(fixture_173, "write_text", WriteJson(Put(Dict(), S("tableFields"), ListOf(Put(Put(Put(Dict(), S("name"), S("N")), S("subscripts"), ListOf(I(1))), S("value"), S("7"))))));
        result_174 = RunCli(ListOf(S("run"), TestMember(state, "output"), S("--program"), S("TEST"), S("--fixture"), fixture_173), I(0));
        _ = CheckEqual(ReadJson(TestMember(result_174, "stdout"))[S("display")], ListOf(S("73")));
        foreach (var item177 in Items(ListOf(ListOf(Put(Put(Put(Dict(), S("name"), S("N")), S("subscripts"), ListOf(I(3))), S("value"), S("7"))), TestMultiply(ListOf(Put(Put(Put(Dict(), S("name"), S("N")), S("subscripts"), ListOf(I(1))), S("value"), S("7"))), I(2)))))
        {
            entries_172 = item177;
            _ = TestCall(fixture_173, "write_text", WriteJson(Put(Dict(), S("tableFields"), entries_172)));
            result_174 = RunCli(ListOf(S("run"), TestMember(state, "output"), S("--program"), S("TEST"), S("--fixture"), fixture_173), I(2));
            _ = CheckIn(S("subscript"), TestMember(result_174, "stderr"));
        }
    }

    /// <summary>file contract eof status and trace の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestFileContractEofStatusAndTrace()
    {
        V body_178 = Nil;
        V reads_179 = Nil;
        V response_180 = Nil;
        V result_181 = Nil;
        V source_182 = Nil;
        V stub_183 = Nil;
        V stubs_184 = Nil;
        source_182 = Divide(TestMember(state, "base"), S("file.cbl"));
        _ = TestCall(source_182, "write_text", S("IDENTIFICATION DIVISION.\nPROGRAM-ID. TEST.\nENVIRONMENT DIVISION.\nINPUT-OUTPUT SECTION.\nFILE-CONTROL.\nSELECT F ASSIGN TO \"fixture\" ORGANIZATION LINE SEQUENTIAL FILE STATUS ST.\nDATA DIVISION.\nFILE SECTION.\nFD F.\n01 REC.\n 05 LINE-TEXT PIC X(3).\nWORKING-STORAGE SECTION.\n01 ST PIC XX.\nPROCEDURE DIVISION.\nOPEN INPUT F\nREAD F AT END DISPLAY \"UNEXPECTED\" NOT AT END DISPLAY LINE-TEXT END-READ\nREAD F AT END DISPLAY \"EOF\" NOT AT END DISPLAY \"UNEXPECTED\" END-READ\nDISPLAY ST\nCLOSE F\nGOBACK.\n"));
        _ = RunCli(ListOf(S("generate"), source_182, S("--output"), TestMember(state, "output"), S("--mode"), S("strict")), I(0));
        _ = RunCli(ListOf(S("build"), TestMember(state, "output")), I(0));
        stubs_184 = ReadJson(TestCall(Divide(TestMember(state, "output"), S("reports/stubs.json")), "read_text"));
        reads_179 = I(0);
        foreach (var item185 in Items(stubs_184))
        {
            stub_183 = item185;
            response_180 = TestCall(stub_183[S("signature")], "split")[I(0)];
            if (Truth(TestCall(stub_183[S("statement")][S("operation")], "startswith", S("read"))))
            {
                reads_179 = Add(reads_179, I(1));
                body_178 = Truth(B(TestEqual(reads_179, I(1)))) ? AddText(S("return new "), Format(response_180, string.Empty), S("(\"00\", DependencyOutcome.Normal, \"ABC\", \"00\");")) : AddText(S("return new "), Format(response_180, string.Empty), S("(\"10\", DependencyOutcome.End, \"ABC\", \"10\");"));
            }
            else
            {
                body_178 = AddText(S("return new "), Format(response_180, string.Empty), S("(\"00\", DependencyOutcome.Normal, \"00\");"));
            }

            _ = M_edit_stub(stub_183, body_178);
        }

        result_181 = ReadJson(TestMember(RunCli(ListOf(S("run"), TestMember(state, "output"), S("--program"), S("TEST")), I(0)), "stdout"));
        _ = CheckEqual(result_181[S("display")], ListOf(S("ABC"), S("EOF"), S("10")));
        _ = CheckEqual(ListOf(from item186 in Items(result_181[S("dependencies")]) let d_187 = item186 select d_187[S("operation")]), ListOf(S("open input"), S("read"), S("read"), S("close")));
        _ = CheckEqual(result_181[S("dependencies")][I(2)][S("outcome")], S("End"));
    }

    /// <summary>sort merge are isolated stubs の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestSortMergeAreIsolatedStubs()
    {
        V operation_188 = Nil;
        V result_189 = Nil;
        V source_190 = Nil;
        V stub_191 = Nil;
        foreach (var item192 in Items(ListOf(S("SORT"), S("MERGE"))))
        {
            operation_188 = item192;
            source_190 = Divide(TestMember(state, "base"), TestAdd(operation_188, S(".cbl")));
            _ = TestCall(source_190, "write_text", TestAdd(TestAdd(S("IDENTIFICATION DIVISION. PROGRAM-ID. TEST.\nENVIRONMENT DIVISION. INPUT-OUTPUT SECTION. FILE-CONTROL.\nSELECT INPUT-F ASSIGN TO \"in\".\nSELECT OUTPUT-F ASSIGN TO \"out\".\nDATA DIVISION. FILE SECTION.\nFD INPUT-F.\n01 INPUT-R PIC X(4).\nFD OUTPUT-F.\n01 OUTPUT-R PIC X(4).\nSD SORT-F.\n01 SORT-R.\n 05 SORT-K PIC 9(2).\n 05 SORT-V PIC X(2).\nPROCEDURE DIVISION.\n"), operation_188), S(" SORT-F ON ASCENDING KEY SORT-K USING INPUT-F GIVING OUTPUT-F DISPLAY \"AFTER\" GOBACK.")));
            SetMember(state, "output", Divide(TestMember(state, "base"), TestAdd(operation_188, S("-out"))));
            _ = RunCli(ListOf(S("generate"), source_190, S("--output"), TestMember(state, "output")), I(0));
            _ = RunCli(ListOf(S("build"), TestMember(state, "output")), I(0));
            stub_191 = Next(Sequence(from item193 in Items(ReadJson(TestCall(Divide(TestMember(state, "output"), S("reports/stubs.json")), "read_text"))) let s_194 = item193 where Truth(B(TestEqual(s_194[S("statement")][S("kind")], S("sort")))) select s_194));
            _ = CheckEqual(stub_191[S("statement")][S("merge")], B(TestEqual(operation_188, S("MERGE"))));
            _ = RunCli(ListOf(S("run"), TestMember(state, "output"), S("--program"), S("TEST")), I(2));
            _ = M_edit_stub(stub_191, S("return ControlResult.Continue;"));
            result_189 = ReadJson(TestMember(RunCli(ListOf(S("run"), TestMember(state, "output"), S("--program"), S("TEST")), I(0)), "stdout"));
            _ = CheckEqual(result_189[S("display")], ListOf(S("AFTER")));
        }
    }

    /// <summary>accept fixture sequence and exhaustion の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestAcceptFixtureSequenceAndExhaustion()
    {
        V failure_195 = Nil;
        V result_196 = Nil;
        V source_197 = Nil;
        result_196 = M_run_source(S("ACCEPT T END-ACCEPT ACCEPT T FROM CONSOLE END-ACCEPT DISPLAY T GOBACK."), S("01 T PIC X(3)."), fixture_: Put(Dict(), S("inputs"), ListOf(S("A"), S("B"))));
        _ = CheckEqual(result_196[S("display")], ListOf(S("B  ")));
        failure_195 = RunCli(ListOf(S("run"), TestMember(state, "output"), S("--program"), S("TEST")), I(2));
        _ = CheckIn(S("ACCEPT fixture input exhausted"), TestMember(failure_195, "stderr"));
        source_197 = M_source(S("ACCEPT T FROM DATE DISPLAY T GOBACK."), S("01 T PIC X(8)."));
        _ = RunCli(ListOf(S("generate"), source_197, S("--output"), TestMember(state, "output"), S("--mode"), S("strict")), I(1));
        _ = CheckIn(S("Unsupported ACCEPT FROM DATE"), TestCall(Divide(TestMember(state, "output"), S("reports/diagnostics.json")), "read_text"));
    }

    /// <summary>paragraph fallthrough thru goto and size branch の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestParagraphFallthroughThruGotoAndSizeBranch()
    {
        V result_198 = Nil;
        result_198 = M_run_source(S("MAIN. PERFORM A THRU B DISPLAY \"MAIN\" GO TO LAST. A. DISPLAY \"A\". B. PERFORM 2 TIMES COMPUTE N = 10 ON SIZE ERROR EXIT PERFORM END-COMPUTE END-PERFORM DISPLAY \"B\". C. DISPLAY \"BAD\". LAST. DISPLAY \"LAST\" GOBACK."), S("01 N PIC 9."));
        _ = CheckEqual(result_198[S("display")], ListOf(S("A"), S("B"), S("MAIN"), S("LAST")));
        result_198 = M_run_source(S("FIRST. DISPLAY \"FIRST\". SECOND. DISPLAY \"SECOND\" GOBACK."));
        _ = CheckEqual(result_198[S("display")], ListOf(S("FIRST"), S("SECOND")));
    }

    /// <summary>names groups and condition properties の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestNamesGroupsAndConditionProperties()
    {
        V mapping_199 = Nil;
        V models_200 = Nil;
        V names_201 = Nil;
        V result_202 = Nil;
        result_202 = M_run_source(S("SET READY TO TRUE IF READY DISPLAY \"YES\" END-IF MOVE 3 TO class OF G MOVE 4 TO class OF H MOVE 5 TO 金額 DISPLAY class OF G class OF H 金額 GOBACK."), S("01 FLAG PIC 9.\n88 READY VALUE 1 THRU 2.\n01 G.\n 05 class PIC 9.\n01 H.\n 05 class PIC 9.\n01 金額 PIC 9."));
        _ = CheckEqual(result_202[S("display")], ListOf(S("YES"), S("345")));
        models_200 = TestCall(S("\n"), "join", Sequence(from item203 in Items(TestCall(Divide(TestMember(state, "output"), S("Models")), "glob", S("*.cs"))) let p_204 = item203 select TestCall(p_204, "read_text")));
        _ = CheckIn(S("internal bool"), models_200);
        _ = CheckIn(S("Group"), models_200);
        mapping_199 = ReadJson(TestCall(Divide(TestMember(state, "output"), S("reports/source-map.json")), "read_text"));
        names_201 = ListOf(from item205 in Items(mapping_199) let entry_206 = item205 where Truth(B(Contains(entry_206, S("symbolId")))) select entry_206[S("generatedName")]);
        _ = CheckEqual(Length(names_201), Length(SetCopy(names_201)));
    }

    /// <summary>unknown control block does not leak の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestUnknownControlBlockDoesNotLeak()
    {
        V result_207 = Nil;
        V source_208 = Nil;
        V stub_209 = Nil;
        source_208 = M_source(S("IF FUNCTION MYSTERY(N) DISPLAY \"INSIDE\" END-IF DISPLAY \"AFTER\" GOBACK."));
        _ = RunCli(ListOf(S("generate"), source_208, S("--output"), TestMember(state, "output")), I(0));
        _ = RunCli(ListOf(S("build"), TestMember(state, "output")), I(0));
        stub_209 = ReadJson(TestCall(Divide(TestMember(state, "output"), S("reports/stubs.json")), "read_text"))[I(0)];
        _ = CheckEqual(stub_209[S("statement")][S("kind")], S("if"));
        _ = M_edit_stub(stub_209, S("return ControlResult.Continue;"));
        result_207 = ReadJson(TestMember(RunCli(ListOf(S("run"), TestMember(state, "output"), S("--program"), S("TEST")), I(0)), "stdout"));
        _ = CheckEqual(result_207[S("display")], ListOf(S("AFTER")));
    }

    /// <summary>value and condition bounds are validated の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestValueAndConditionBoundsAreValidated()
    {
        V declaration_210 = Nil;
        V diagnostics_211 = Nil;
        V index_212 = Nil;
        V source_213 = Nil;
        foreach (var item214 in Items(Enumerate(ListOf(S("01 N PIC 9 VALUE FUNCTION MYSTERY(1)."), S("01 N PIC 9.\n88 FLAG VALUE FUNCTION MYSTERY(1) THRU 2."), S("01 N PIC 9.\n88 FLAG VALUE 1 THRU FUNCTION MYSTERY(2).")))))
        {
            var unpack215 = item214;
            index_212 = unpack215[I(0)];
            declaration_210 = unpack215[I(1)];
            source_213 = M_source(S("GOBACK."), declaration_210);
            SetMember(state, "output", Divide(TestMember(state, "base"), TestAdd(S("declaration"), TestStringValue(index_212))));
            _ = RunCli(ListOf(S("generate"), source_213, S("--output"), TestMember(state, "output"), S("--mode"), S("strict")), I(1));
            diagnostics_211 = ReadJson(TestCall(Divide(TestMember(state, "output"), S("reports/diagnostics.json")), "read_text"));
            _ = CheckTrue(Any(Sequence(from item216 in Items(diagnostics_211) let d_217 = item216 select And(B(TestEqual(d_217[S("code")], S("CS_FUNCTION"))), () => And(B(TestEqual(d_217[S("programId")], S("TEST"))), () => d_217[S("source")])))));
            _ = RunCli(ListOf(S("generate"), source_213, S("--output"), TestMember(state, "output")), I(0));
            _ = RunCli(ListOf(S("build"), TestMember(state, "output")), I(0));
        }
    }

    /// <summary>evaluate body thru remains native の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestEvaluateBodyThruRemainsNative()
    {
        V result_218 = Nil;
        result_218 = M_run_source(S("MAIN. EVALUATE N WHEN 0 PERFORM A THRU B WHEN OTHER DISPLAY \"BAD\" END-EVALUATE GOBACK. A. DISPLAY \"A\". B. DISPLAY \"B\"."));
        _ = CheckEqual(result_218[S("display")], ListOf(S("A"), S("B")));
        _ = CheckEqual(ReadJson(TestCall(Divide(TestMember(state, "output"), S("reports/stubs.json")), "read_text")), ListOf());
    }

    /// <summary>call reference indices are captured before updates の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestCallReferenceIndicesAreCapturedBeforeUpdates()
    {
        V response_219 = Nil;
        V result_220 = Nil;
        V source_221 = Nil;
        V stub_222 = Nil;
        source_221 = M_source(S("CALL \"S\" USING I A(I) END-CALL GOBACK."), S("01 I PIC 9 VALUE 1.\n01 A PIC 9(2) OCCURS 2 TIMES."));
        _ = RunCli(ListOf(S("generate"), source_221, S("--output"), TestMember(state, "output")), I(0));
        stub_222 = ReadJson(TestCall(Divide(TestMember(state, "output"), S("reports/stubs.json")), "read_text"))[I(0)];
        response_219 = TestCall(stub_222[S("signature")], "split")[I(0)];
        _ = M_edit_stub(stub_222, AddText(S("return new "), Format(response_219, string.Empty), S("(\"00\", DependencyOutcome.Normal, new Number(2), new Number(99));")));
        result_220 = ReadJson(TestMember(RunCli(ListOf(S("run"), TestMember(state, "output"), S("--program"), S("TEST")), I(0)), "stdout"));
        _ = CheckEqual(result_220[S("fields")], Put(Put(Dict(), S("I"), S("2")), S("A"), ListOf(S("99"), S("0"))));
    }

    /// <summary>remainder destination index is captured の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestRemainderDestinationIndexIsCaptured()
    {
        V result_223 = Nil;
        result_223 = M_run_source(S("DIVIDE N BY 2 GIVING I REMAINDER A(I) GOBACK."), S("01 N PIC 9 VALUE 7.\n01 I PIC 9 VALUE 2.\n01 A PIC 9 OCCURS 4 TIMES."));
        _ = CheckEqual(result_223[S("fields")], Put(Put(Put(Dict(), S("N"), S("7")), S("I"), S("3")), S("A"), ListOf(S("0"), S("1"), S("0"), S("0"))));
    }

    /// <summary>repeated call and exception branch の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestRepeatedCallAndExceptionBranch()
    {
        V response_224 = Nil;
        V result_225 = Nil;
        V source_226 = Nil;
        V stub_227 = Nil;
        source_226 = M_source(S("PERFORM 2 TIMES CALL \"S\" USING N ON EXCEPTION DISPLAY \"ERROR\" NOT ON EXCEPTION DISPLAY \"OK\" END-CALL END-PERFORM GOBACK."), S("01 N PIC 9."));
        _ = RunCli(ListOf(S("generate"), source_226, S("--output"), TestMember(state, "output")), I(0));
        stub_227 = ReadJson(TestCall(Divide(TestMember(state, "output"), S("reports/stubs.json")), "read_text"))[I(0)];
        response_224 = TestCall(stub_227[S("signature")], "split")[I(0)];
        _ = M_edit_stub(stub_227, AddText(S("return new "), Format(response_224, string.Empty), S("(\"test\", request.Argument0.Numerator.IsZero ? DependencyOutcome.Normal : DependencyOutcome.Exception, request.Argument0 + new Number(1));")));
        result_225 = ReadJson(TestMember(RunCli(ListOf(S("run"), TestMember(state, "output"), S("--program"), S("TEST")), I(0)), "stdout"));
        _ = CheckEqual(result_225[S("display")], ListOf(S("OK"), S("ERROR")));
        _ = CheckEqual(result_225[S("fields")][S("N")], S("2"));
        _ = CheckEqual(Length(result_225[S("dependencies")]), I(2));
    }

    /// <summary>control stub can jump to sentence or paragraph の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestControlStubCanJumpToSentenceOrParagraph()
    {
        V fixture_228 = Nil;
        V result_229 = Nil;
        V source_230 = Nil;
        V stub_231 = Nil;
        source_230 = M_source(S("IF N = 0 NEXT SENTENCE END-IF DISPLAY \"SKIP\". DISPLAY \"AFTER\" GOBACK."));
        _ = RunCli(ListOf(S("generate"), source_230, S("--output"), TestMember(state, "output")), I(0));
        stub_231 = Next(Sequence(from item232 in Items(ReadJson(TestCall(Divide(TestMember(state, "output"), S("reports/stubs.json")), "read_text"))) let s_233 = item232 where Truth(B(TestEqual(s_233[S("statement")][S("kind")], S("nextSentence")))) select s_233));
        _ = RunCli(ListOf(S("build"), TestMember(state, "output")), I(0));
        fixture_228 = Divide(TestMember(state, "base"), S("fixture.json"));
        _ = TestCall(fixture_228, "write_text", S("{\"fields\":{\"N\":\"1\"}}"));
        result_229 = ReadJson(TestMember(RunCli(ListOf(S("run"), TestMember(state, "output"), S("--program"), S("TEST"), S("--fixture"), fixture_228), I(0)), "stdout"));
        _ = CheckEqual(result_229[S("display")], ListOf(S("SKIP"), S("AFTER")));
        _ = M_edit_stub(stub_231, S("return ControlResult.JumpTo(source.NextSentenceTarget!);"));
        result_229 = ReadJson(TestMember(RunCli(ListOf(S("run"), TestMember(state, "output"), S("--program"), S("TEST")), I(0)), "stdout"));
        _ = CheckEqual(result_229[S("display")], ListOf(S("AFTER")));
        SetMember(state, "output", Divide(TestMember(state, "base"), S("paragraph-jump")));
        source_230 = M_source(S("MAIN. EVALUATE N ALSO N WHEN 0 ALSO 0 GO TO LAST END-EVALUATE DISPLAY \"SKIP\". LAST. DISPLAY \"AFTER\" GOBACK."));
        _ = RunCli(ListOf(S("generate"), source_230, S("--output"), TestMember(state, "output")), I(0));
        stub_231 = Next(Sequence(from item234 in Items(ReadJson(TestCall(Divide(TestMember(state, "output"), S("reports/stubs.json")), "read_text"))) let s_235 = item234 where Truth(B(TestEqual(s_235[S("statement")][S("kind")], S("controlStub")))) select s_235));
        _ = M_edit_stub(stub_231, S("return ControlResult.JumpTo(\"LAST\");"));
        result_229 = ReadJson(TestMember(RunCli(ListOf(S("run"), TestMember(state, "output"), S("--program"), S("TEST")), I(0)), "stdout"));
        _ = CheckEqual(result_229[S("display")], ListOf(S("AFTER")));
    }

    /// <summary>reference examples の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestReferenceExamples()
    {
        V copy_236 = Nil;
        V example_237 = Nil;
        V manifest_238 = Nil;
        V name_239 = Nil;
        V out_240 = Nil;
        V result_241 = Nil;
        foreach (var item242 in Items(ListOf(S("AMOUNT"), S("BRANCH"), S("TABLE"), S("FLOW"), S("NATIONAL"), S("COPYJP"))))
        {
            name_239 = item242;
            example_237 = Divide(Divide(Root, S("examples")), name_239);
            out_240 = Divide(TestMember(state, "base"), name_239);
            copy_236 = Truth(B(TestEqual(name_239, S("COPYJP")))) ? ListOf(S("--copybook-path"), Divide(example_237, S("copy"))) : ListOf();
            _ = RunCli(ListOf(Enumerable.Empty<V>().Concat(new V[] { S("generate") }).Concat(new V[] { example_237 }).Concat(new V[] { S("--output") }).Concat(new V[] { out_240 }).Concat(Items(copy_236))), I(0));
            result_241 = RunCli(ListOf(S("run"), out_240, S("--program"), name_239, S("--fixture"), Divide(example_237, S("fixture.json"))), I(0));
            _ = CheckEqual(ReadJson(TestMember(result_241, "stdout")), ReadJson(TestCall(Divide(example_237, S("expected.json")), "read_text", Named("encoding", S("utf-8")))));
            manifest_238 = ReadJson(TestCall(Divide(out_240, S("manifest.json")), "read_text"));
            _ = CheckTrue(manifest_238[S("judgments")][S("built")]);
            _ = CheckTrue(manifest_238[S("judgments")][S("executedWithFixtures")]);
            _ = CheckFalse(manifest_238[S("judgments")][S("matchedBaseline")]);
        }
    }

    /// <summary>strict and scaffold の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestStrictAndScaffold()
    {
        V diagnostics_243 = Nil;
        _ = RunCli(ListOf(S("generate"), Divide(Root, S("examples/UNSUPPORTED")), S("--output"), TestMember(state, "output"), S("--backend"), S("interpreter"), S("--mode"), S("strict")), I(1));
        _ = CheckFalse(TestCall(Divide(TestMember(state, "output"), S("CobolCS.Generated.csproj")), "exists"));
        diagnostics_243 = ReadJson(TestCall(Divide(TestMember(state, "output"), S("reports/diagnostics.json")), "read_text"));
        _ = CheckTrue(Any(Sequence(from item244 in Items(diagnostics_243) let d_245 = item244 select B(TestEqual(d_245[S("code")], S("CS_EXTERNAL"))))));
        _ = RunCli(ListOf(S("generate"), Divide(Root, S("examples/UNSUPPORTED")), S("--output"), TestMember(state, "output"), S("--mode"), S("scaffold")), I(0));
        _ = RunCli(ListOf(S("build"), TestMember(state, "output")), I(0));
        _ = RunCli(ListOf(S("run"), TestMember(state, "output"), S("--program"), S("UNSUPPORTED")), I(2));
    }

    /// <summary>deterministic and edit protection の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestDeterministicAndEditProtection()
    {
        V generated_246 = Nil;
        V manifest_247 = Nil;
        V source_248 = Nil;
        source_248 = M_source(S("ADD 1 TO N GOBACK."));
        _ = RunCli(ListOf(S("generate"), source_248, S("--output"), TestMember(state, "output")), I(0));
        manifest_247 = TestCall(Divide(TestMember(state, "output"), S("manifest.json")), "read_bytes");
        _ = RunCli(ListOf(S("generate"), source_248, S("--output"), TestMember(state, "output")), I(0));
        _ = CheckEqual(manifest_247, TestCall(Divide(TestMember(state, "output"), S("manifest.json")), "read_bytes"));
        generated_246 = Next(TestCall(Divide(TestMember(state, "output"), S("Programs")), "glob", S("*.g.cs")));
        _ = TestCall(generated_246, "write_text", TestAdd(TestCall(generated_246, "read_text"), S("// user edit\n")));
        _ = RunCli(ListOf(S("generate"), source_248, S("--output"), TestMember(state, "output")), I(2));
        _ = CheckTrue(TestCall(TestCall(generated_246, "read_text"), "endswith", S("// user edit\n")));
        _ = RunCli(ListOf(S("build"), TestMember(state, "output")), I(2));
    }

    /// <summary>unmanaged files preserved の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestUnmanagedFilesPreserved()
    {
        V source_249 = Nil;
        V user_250 = Nil;
        source_249 = M_source(S("GOBACK."));
        _ = RunCli(ListOf(S("generate"), source_249, S("--output"), TestMember(state, "output")), I(0));
        user_250 = Divide(TestMember(state, "output"), S("business.txt"));
        _ = TestCall(user_250, "write_text", S("keep"));
        _ = RunCli(ListOf(S("generate"), source_249, S("--output"), TestMember(state, "output")), I(2));
        _ = CheckEqual(TestCall(user_250, "read_text"), S("keep"));
    }

    /// <summary>compare and invalid options の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestCompareAndInvalidOptions()
    {
        V first_251 = Nil;
        V second_252 = Nil;
        first_251 = Divide(TestMember(state, "base"), S("first.json"));
        second_252 = Divide(TestMember(state, "base"), S("second.json"));
        _ = TestCall(first_251, "write_text", S("{\"a\":\"1\",\"b\":\"2\"}"));
        _ = TestCall(second_252, "write_text", S("{\"b\":\"2\",\"a\":\"1\"}"));
        _ = RunCli(ListOf(S("compare"), first_251, second_252), I(0));
        _ = TestCall(second_252, "write_text", S("{\"a\":\"2\"}"));
        _ = RunCli(ListOf(S("compare"), first_251, second_252), I(1));
        _ = RunCli(ListOf(S("generate"), S("x"), S("--unknown"), S("y")), I(2));
    }

    /// <summary>exact decimal and negative rounding の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestExactDecimalAndNegativeRounding()
    {
        V result_253 = Nil;
        result_253 = M_run_source(S("COMPUTE N ROUNDED = -1.235 DISPLAY N GOBACK."), S("01 N PIC S9V99 VALUE 0."));
        _ = CheckEqual(result_253[S("fields")][S("N")], S("-1.24"));
        _ = CheckEqual(result_253[S("display")], ListOf(S("-124")));
    }

    /// <summary>justified right truncation の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestJustifiedRightTruncation()
    {
        V result_254 = Nil;
        result_254 = M_run_source(S("MOVE \"ABCDE\" TO T DISPLAY T GOBACK."), S("01 T PIC X(3) JUSTIFIED RIGHT."));
        _ = CheckEqual(result_254[S("fields")][S("T")], S("CDE"));
    }

    /// <summary>division alias remainder の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestDivisionAliasRemainder()
    {
        V result_255 = Nil;
        result_255 = M_run_source(S("DIVIDE N BY 2 GIVING N REMAINDER R DISPLAY N R GOBACK."), S("01 N PIC 9 VALUE 7.\n01 R PIC 9 VALUE 0."));
        _ = CheckEqual(result_255[S("fields")], Put(Put(Dict(), S("N"), S("3")), S("R"), S("1")));
    }

    /// <summary>divide by zero size error の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestDivideByZeroSizeError()
    {
        V result_256 = Nil;
        result_256 = M_run_source(S("DIVIDE 0 INTO N ON SIZE ERROR DISPLAY \"zero\" END-DIVIDE GOBACK."), S("01 N PIC 9 VALUE 7."));
        _ = CheckEqual(result_256[S("display")], ListOf(S("zero")));
        _ = CheckEqual(result_256[S("fields")][S("N")], S("7"));
    }

    /// <summary>loop and exit の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestLoopAndExit()
    {
        V result_257 = Nil;
        result_257 = M_run_source(S("PERFORM 10 TIMES ADD 1 TO N IF N = 3 EXIT PERFORM END-IF END-PERFORM GOBACK."));
        _ = CheckEqual(result_257[S("fields")][S("N")], S("3"));
    }

    /// <summary>after の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestTestAfter()
    {
        V result_258 = Nil;
        result_258 = M_run_source(S("PERFORM WITH TEST AFTER UNTIL N > 0 ADD 1 TO N END-PERFORM GOBACK."), S("01 N PIC 9 VALUE 2."));
        _ = CheckEqual(result_258[S("fields")][S("N")], S("3"));
    }

    /// <summary>step limit の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestStepLimit()
    {
        V result_259 = Nil;
        result_259 = M_run_source(S("AGAIN. GO TO AGAIN."), config_: Put(Dict(), S("maxSteps"), I(20)), run_code_: I(2));
        _ = CheckIn(S("Execution limit"), TestMember(result_259, "stderr"));
    }

    /// <summary>goto depending and section perform の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestGotoDependingAndSectionPerform()
    {
        V result_260 = Nil;
        result_260 = M_run_source(S("MAIN. PERFORM WORK-SECTION GO TO A B DEPENDING ON N A. DISPLAY \"A\" GOBACK. B. DISPLAY \"B\" GOBACK. WORK-SECTION SECTION. FIRST-P. MOVE 2 TO N. LAST-P. CONTINUE."));
        _ = CheckEqual(result_260[S("display")], ListOf(S("B")));
    }

    /// <summary>unsupported profile の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestUnsupportedProfile()
    {
        V config_261 = Nil;
        V source_262 = Nil;
        source_262 = M_source(S("GOBACK."));
        config_261 = Divide(TestMember(state, "base"), S("config.json"));
        _ = TestCall(config_261, "write_text", S("{\"dataEncoding\":\"fujitsu-ebcdic-kana\"}"));
        _ = RunCli(ListOf(S("generate"), source_262, S("--config"), config_261, S("--output"), TestMember(state, "output"), S("--mode"), S("strict")), I(1));
        _ = CheckIn(S("CS_PROFILE"), TestCall(Divide(TestMember(state, "output"), S("reports/diagnostics.json")), "read_text"));
    }

    /// <summary>subscript bounds の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestSubscriptBounds()
    {
        V result_263 = Nil;
        result_263 = M_run_source(S("MOVE 1 TO N(3) GOBACK."), S("01 N PIC 9 OCCURS 2 TIMES VALUE 0."), run_code_: I(2));
        _ = CheckIn(S("subscript"), TestMember(result_263, "stderr"));
    }

    /// <summary>fixture and reference modification の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestFixtureAndReferenceModification()
    {
        V result_264 = Nil;
        result_264 = M_run_source(S("MOVE \"Z\" TO T(2:1) DISPLAY T GOBACK."), S("01 T PIC X(4) VALUE SPACES."), fixture_: Put(Dict(), S("fields"), Put(Dict(), S("T"), S("AB"))));
        _ = CheckEqual(result_264[S("fields")][S("T")], S("AZ  "));
    }

    /// <summary>unknown external fixture fails の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestUnknownExternalFixtureFails()
    {
        V result_265 = Nil;
        result_265 = M_run_source(S("GOBACK."), fixture_: Put(Dict(), S("calls"), ListOf(Put(Dict(), S("name"), S("UNKNOWN")))), run_code_: I(2));
        _ = CheckIn(S("fixture"), TestMember(result_265, "stderr"));
    }

    /// <summary>missing copy scaffold の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestMissingCopyScaffold()
    {
        V source_266 = Nil;
        source_266 = M_source(S("MOVE 1 TO UNKNOWN-FIELD GOBACK."), S("COPY MISSING."));
        _ = RunCli(ListOf(S("generate"), source_266, S("--output"), TestMember(state, "output"), S("--mode"), S("strict")), I(1));
        _ = RunCli(ListOf(S("generate"), source_266, S("--output"), TestMember(state, "output"), S("--mode"), S("scaffold")), I(0));
        _ = RunCli(ListOf(S("build"), TestMember(state, "output")), I(0));
        _ = RunCli(ListOf(S("run"), TestMember(state, "output"), S("--program"), S("TEST")), I(2));
    }

    /// <summary>redefines and group moves refused の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestRedefinesAndGroupMovesRefused()
    {
        V body_267 = Nil;
        V declarations_268 = Nil;
        V source_269 = Nil;
        foreach (var item270 in Items(ListOf(ListOf(S("01 N PIC X.\n01 R REDEFINES N PIC X."), S("GOBACK.")), ListOf(S("01 G.\n05 N PIC 9."), S("MOVE \"1\" TO G GOBACK.")))))
        {
            var unpack271 = item270;
            declarations_268 = unpack271[I(0)];
            body_267 = unpack271[I(1)];
            source_269 = M_source(body_267, declarations_268);
            _ = RunCli(ListOf(S("generate"), source_269, S("--output"), TestMember(state, "output"), S("--mode"), S("strict")), I(1));
        }
    }

    /// <summary>symlink and manifest traversal refused の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestSymlinkAndManifestTraversalRefused()
    {
        V link_272 = Nil;
        V manifest_273 = Nil;
        V manifest_path_274 = Nil;
        V source_275 = Nil;
        source_275 = M_source(S("GOBACK."));
        _ = RunCli(ListOf(S("generate"), source_275, S("--output"), TestMember(state, "output")), I(0));
        link_272 = Divide(TestMember(state, "base"), S("link"));
        _ = TestCall(link_272, "symlink_to", TestMember(state, "output"), Named("target_is_directory", B(true)));
        _ = RunCli(ListOf(S("generate"), source_275, S("--output"), link_272), I(2));
        manifest_path_274 = Divide(TestMember(state, "output"), S("manifest.json"));
        manifest_273 = ReadJson(TestCall(manifest_path_274, "read_text"));
        manifest_273[S("artifacts")][S("../outside.txt")] = TestMultiply(S("0"), I(64));
        _ = TestCall(manifest_path_274, "write_text", WriteJson(manifest_273));
        _ = RunCli(ListOf(S("build"), TestMember(state, "output")), I(2));
    }

    /// <summary>standalone multi program の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestStandaloneMultiProgram()
    {
        V name_276 = Nil;
        V result_277 = Nil;
        V sources_278 = Nil;
        sources_278 = Divide(TestMember(state, "base"), S("sources"));
        _ = TestCall(sources_278, "mkdir");
        foreach (var item279 in Items(ListOf(S("ONE"), S("TWO"))))
        {
            name_276 = item279;
            _ = TestCall(Divide(sources_278, TestAdd(name_276, S(".cbl"))), "write_text", AddText(S("IDENTIFICATION DIVISION. PROGRAM-ID. "), Format(name_276, string.Empty), S(". PROCEDURE DIVISION. DISPLAY \""), Format(name_276, string.Empty), S("\" GOBACK.")));
        }

        _ = RunCli(ListOf(S("generate"), sources_278, S("--output"), TestMember(state, "output")), I(0));
        _ = RunCli(ListOf(S("build"), TestMember(state, "output")), I(0));
        result_277 = RunProcess(ListOf(S("dotnet"), TestStringValue(Divide(TestMember(state, "output"), S("bin/Debug/net10.0/CobolCS.Generated.dll"))), S("TWO")), I(20));
        _ = CheckEqual(TestMember(result_277, "returncode"), I(0), TestMember(result_277, "stderr"));
        _ = CheckEqual(ReadJson(TestMember(result_277, "stdout"))[S("display")], ListOf(S("TWO")));
    }

    /// <summary>generated stylecop is active の回帰条件を検証します。</summary>
    [TestMethod]
    public void TestGeneratedStylecopIsActive()
    {
        V generated_280 = Nil;
        V project_281 = Nil;
        V result_282 = Nil;
        V source_283 = Nil;
        source_283 = M_source(S("GOBACK."));
        _ = RunCli(ListOf(S("generate"), source_283, S("--output"), TestMember(state, "output")), I(0));
        _ = RunCli(ListOf(S("build"), TestMember(state, "output")), I(0));
        project_281 = TestCall(Divide(TestMember(state, "output"), S("CobolCS.Generated.csproj")), "read_text", Named("encoding", S("utf-8")));
        _ = CheckIn(S("Include=\"StyleCop.Analyzers\""), project_281);
        _ = CheckIn(S("SA1101;SA1200;SA1309;SA1629;SA1633"), project_281);
        generated_280 = TestCall(Next(TestCall(Divide(TestMember(state, "output"), S("Programs")), "glob", S("*.g.cs"))), "read_text", Named("encoding", S("utf-8")));
        _ = CheckIn(S("/// <summary>"), generated_280);
        _ = CheckIn(S("期待値との比較"), generated_280);
        _ = TestCall(Divide(TestMember(state, "output"), S("Undocumented.cs")), "write_text", S("namespace CobolCS.Generated;\n\npublic class Undocumented\n{\n}\n"), Named("encoding", S("utf-8")));
        result_282 = RunProcess(ListOf(S("dotnet"), S("build"), TestStringValue(Divide(TestMember(state, "output"), S("CobolCS.Generated.csproj"))), S("--no-restore"), S("--nologo")), I(150));
        _ = CheckNotEqual(TestMember(result_282, "returncode"), I(0));
        _ = CheckIn(S("SA1600"), TestMember(result_282, "stdout"));
        _ = CheckIn(S("CS1591"), TestMember(result_282, "stdout"));
    }

    private V RunCli(V arguments, V code) => RunChecked(Add(CliArguments, arguments), code);

    /// <summary>
    /// 元の解析規則とIRの順序を保持します。
    /// </summary>
    /// <param name="procedure_">procedure の解析値。</param>
    /// <param name="declarations_">declarations の解析値。</param>
    /// <param name="program_">program の解析値。</param>
    /// <returns>解析結果または更新後の状態。</returns>
    private V M_source(V procedure_, V? declarations_ = null, V? program_ = null)
    {
        declarations_ ??= S("01 N PIC 9(6) VALUE 0.");
        program_ ??= S("TEST");
        V source_36 = Nil;
        source_36 = Divide(TestMember(state, "base"), S("source.cbl"));
        _ = TestCall(source_36, "write_text", AddText(S("IDENTIFICATION DIVISION.\nPROGRAM-ID. "), Format(program_, string.Empty), S(".\nDATA DIVISION.\nWORKING-STORAGE SECTION.\n"), Format(declarations_, string.Empty), S("\nPROCEDURE DIVISION.\n"), Format(procedure_, string.Empty), S("\n")), Named("encoding", S("utf-8")));
        return source_36;
    }

    /// <summary>
    /// 元の解析規則とIRの順序を保持します。
    /// </summary>
    /// <param name="procedure_">procedure の解析値。</param>
    /// <param name="declarations_">declarations の解析値。</param>
    /// <param name="config_">config の解析値。</param>
    /// <param name="fixture_">fixture の解析値。</param>
    /// <param name="run_code_">run_code の解析値。</param>
    /// <returns>解析結果または更新後の状態。</returns>
    private V M_run_source(V procedure_, V? declarations_ = null, V? config_ = null, V? fixture_ = null, V? run_code_ = null)
    {
        declarations_ ??= S("01 N PIC 9(6) VALUE 0.");
        config_ ??= Nil;
        fixture_ ??= Nil;
        run_code_ ??= I(0);
        V fixture_path_37 = Nil;
        V options_38 = Nil;
        V path_39 = Nil;
        V result_40 = Nil;
        V source_41 = Nil;
        source_41 = M_source(procedure_, declarations_);
        options_38 = ListOf();
        if (Truth(config_))
        {
            path_39 = Divide(TestMember(state, "base"), S("config.json"));
            _ = TestCall(path_39, "write_text", WriteJson(config_));
            options_38 = ListOf(S("--config"), path_39);
        }

        _ = RunCli(ListOf(Enumerable.Empty<V>().Concat(new V[] { S("generate") }).Concat(new V[] { source_41 }).Concat(new V[] { S("--output") }).Concat(new V[] { TestMember(state, "output") }).Concat(Items(options_38))), I(0));
        fixture_path_37 = Divide(TestMember(state, "base"), S("fixture.json"));
        _ = TestCall(fixture_path_37, "write_text", WriteJson(Or(fixture_, () => Dict())));
        result_40 = RunCli(ListOf(S("run"), TestMember(state, "output"), S("--program"), S("TEST"), S("--fixture"), fixture_path_37), run_code_);
        return Truth(B(TestEqual(run_code_, I(0)))) ? ReadJson(TestMember(result_40, "stdout")) : result_40;
    }

    /// <summary>
    /// 元の解析規則とIRの順序を保持します。
    /// </summary>
    /// <param name="stub_">stub の解析値。</param>
    /// <param name="body_">body の解析値。</param>
    /// <returns>解析結果または更新後の状態。</returns>
    private V M_edit_stub(V stub_, V body_)
    {
        V count_77 = Nil;
        V path_78 = Nil;
        V source_79 = Nil;
        path_78 = Divide(TestMember(state, "output"), stub_[S("file")]);
        source_79 = TestCall(path_78, "read_text");
        var unpack81 = TestRegexSubn(S("throw new NotImplementedException\\([^\\n]*\\);"), (arg80) => body_, source_79);
        source_79 = unpack81[I(0)];
        count_77 = unpack81[I(1)];
        _ = CheckEqual(count_77, I(1));
        _ = TestCall(path_78, "write_text", source_79);
        return path_78;
    }
}
