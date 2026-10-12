using CobolCS.Frontend.Parsing;
using Xunit;
using static CobolCS.Frontend.Parsing.Values;
using static CobolCS.IntegrationTests.RegressionSupport;

namespace CobolCS.IntegrationTests;

/// <summary>既存の test_remaining18 の入力、期待値、失敗条件を保持する.NET回帰テストです。</summary>
public sealed class Remaining18Tests
{
    private readonly V state = new();

    /// <summary>accept terminator preserves next statement の回帰条件を検証します。</summary>
    [Fact]
    public void TestAcceptTerminatorPreservesNextStatement()
    {
        V origin_428 = Nil;
        V p_429 = Nil;
        V statements_430 = Nil;
        foreach (var item431 in Items(ListOf(S(string.Empty), S(" FROM CONSOLE"), S(" FROM COMMAND-LINE"))))
        {
            origin_428 = item431;
            var unpack432 = M_parse(TestAdd(TestAdd(S("IDENTIFICATION DIVISION. PROGRAM-ID. TEST. DATA DIVISION. WORKING-STORAGE SECTION. 01 T PIC X(20). PROCEDURE DIVISION. ACCEPT T"), origin_428), S(" END-ACCEPT DISPLAY T GOBACK.")));
            p_429 = unpack432[I(0)];
            _ = unpack432[I(1)];
            statements_430 = p_429[S("paragraphs")][I(0)][S("statements")];
            _ = CheckEqual(ListOf(from item433 in Items(statements_430) let s_434 = item433 select s_434[S("kind")]), ListOf(S("acceptInput"), S("display"), S("halt"), S("sentenceBoundary")));
            _ = CheckTrue(TestCall(statements_430[I(0)][S("source")][S("raw")], "endswith", S("END-ACCEPT")));
            _ = CheckEqual(statements_430[I(0)][S("inputSource")], Or(TestCall(origin_428, "removeprefix", S(" FROM ")), () => S("CONSOLE")));
        }
    }

    /// <summary>unstring comma receivers retain metadata の回帰条件を検証します。</summary>
    [Fact]
    public void TestUnstringCommaReceiversRetainMetadata()
    {
        V p_436 = Nil;
        V receivers_437 = Nil;
        V statements_438 = Nil;
        var unpack439 = M_parse(S("IDENTIFICATION DIVISION. PROGRAM-ID. TEST. DATA DIVISION. WORKING-STORAGE SECTION. 01 T PIC X(20). 01 A PIC X(10). 01 B PIC X(10). 01 D PIC X. 01 N PIC 99. PROCEDURE DIVISION. UNSTRING T DELIMITED BY SPACE INTO A DELIMITER IN D COUNT IN N, B END-UNSTRING DISPLAY B GOBACK."));
        p_436 = unpack439[I(0)];
        _ = unpack439[I(1)];
        statements_438 = p_436[S("paragraphs")][I(0)][S("statements")];
        _ = CheckEqual(statements_438[I(0)][S("kind")], S("unstringTransfer"));
        receivers_437 = statements_438[I(0)][S("receivers")];
        _ = CheckEqual(Length(receivers_437), I(2));
        _ = CheckIsNotNone(receivers_437[I(0)][S("delimiterIn")]);
        _ = CheckIsNotNone(receivers_437[I(0)][S("countIn")]);
        _ = CheckIsNone(receivers_437[I(1)][S("delimiterIn")]);
        _ = CheckEqual(statements_438[I(1)][S("kind")], S("display"));
    }

    /// <summary>documentary apostrophe の回帰条件を検証します。</summary>
    [Fact]
    public void TestDocumentaryApostrophe()
    {
        V p_441 = Nil;
        var unpack442 = M_parse(S("IDENTIFICATION DIVISION.\nPROGRAM-ID. TEST.\nAUTHOR. James O'Grady.\n  DATA and PROCEDURE are words here.\nPROCEDURE DIVISION.\nDISPLAY 'KEPT' GOBACK."));
        p_441 = unpack442[I(0)];
        _ = unpack442[I(1)];
        _ = CheckIn(S("O'Grady"), p_441[S("documentation")][I(0)][S("source")][S("raw")]);
        _ = CheckEqual(p_441[S("paragraphs")][I(0)][S("statements")][I(0)][S("kind")], S("display"));
    }

    /// <summary>documentary fixed positions の回帰条件を検証します。</summary>
    [Fact]
    public void TestDocumentaryFixedPositions()
    {
        V p_444 = Nil;
        V src_445 = Nil;
        V statement_446 = Nil;
        V text_447 = Nil;
        text_447 = S("       IDENTIFICATION DIVISION.\n       PROGRAM-ID. TEST.\n       AUTHOR. James O'Grady.\n           says DATA and PROCEDURE are words.\n       PROCEDURE DIVISION.\n           DISPLAY 'KEPT' GOBACK.\n");
        var unpack448 = M_parse(text_447, fixed_: B(true));
        p_444 = unpack448[I(0)];
        _ = unpack448[I(1)];
        statement_446 = p_444[S("paragraphs")][I(0)][S("statements")][I(0)];
        src_445 = statement_446[S("source")];
        _ = CheckEqual(ListOf(src_445[S("start")][S("line")], src_445[S("start")][S("column")]), ListOf(I(6), I(12)));
        _ = CheckEqual(TestCall(TestCall(text_447, "encode")[Slice(src_445[S("start")][S("byteOffset")], src_445[S("end")][S("byteOffset")], Nil)], "decode"), src_445[S("raw")]);
        _ = CheckIn(S("says DATA"), p_444[S("documentation")][I(0)][S("source")][S("raw")]);
    }

    /// <summary>literal escape and continuation の回帰条件を検証します。</summary>
    [Fact]
    public void TestLiteralEscapeAndContinuation()
    {
        V body_449 = Nil;
        V f_450 = Nil;
        V fixed_451 = Nil;
        V header_452 = Nil;
        V p_453 = Nil;
        foreach (var item454 in Items(ListOf(ListOf(B(false), S("DISPLAY 'O''Brien' GOBACK.")), ListOf(B(false), S("DISPLAY 'AB&\n&'CD' GOBACK.")), ListOf(B(true), S("           DISPLAY 'AB\n      -    'CD' GOBACK.")))))
        {
            var unpack455 = item454;
            fixed_451 = unpack455[I(0)];
            body_449 = unpack455[I(1)];
            header_452 = S("IDENTIFICATION DIVISION.\nPROGRAM-ID. TEST.\nPROCEDURE DIVISION.\n");
            if (Truth(fixed_451))
            {
                header_452 = TestCall(S(string.Empty), "join", Sequence(from item456 in Items(TestCall(header_452, "splitlines")) let line_457 = item456 select TestAdd(TestAdd(S("       "), line_457), S("\n"))));
            }

            var unpack458 = M_parse(TestAdd(header_452, body_449), fixed_: fixed_451);
            p_453 = unpack458[I(0)];
            f_450 = unpack458[I(1)];
            _ = CheckEqual(p_453[S("paragraphs")][I(0)][S("statements")][I(0)][S("kind")], S("display"));
            _ = CheckNotIn(S("UNTERMINATED_LITERAL"), ListOf(from item459 in Items(TestMember(f_450, "diagnostics")) let d_460 = item459 select d_460[S("code")]));
        }
    }

    /// <summary>unterminated literal does not swallow next line の回帰条件を検証します。</summary>
    [Fact]
    public void TestUnterminatedLiteralDoesNotSwallowNextLine()
    {
        V f_461 = Nil;
        V p_462 = Nil;
        var unpack463 = M_parse(S("IDENTIFICATION DIVISION.\nPROGRAM-ID. TEST.\nPROCEDURE DIVISION.\nDISPLAY 'BROKEN\nDISPLAY 'KEPT' GOBACK."));
        p_462 = unpack463[I(0)];
        f_461 = unpack463[I(1)];
        _ = CheckIn(S("UNTERMINATED_LITERAL"), ListOf(from item464 in Items(TestMember(f_461, "diagnostics")) let d_465 = item464 select d_465[S("code")]));
        _ = CheckTrue(Any(Sequence(from item466 in Items(p_462[S("tokens")]) let t_467 = item466 select B(TestEqual(t_467[S("text")], S("'KEPT'"))))));
    }

    /// <summary>program attributes の回帰条件を検証します。</summary>
    [Fact]
    public void TestProgramAttributes()
    {
        V attr_469 = Nil;
        V declaration_470 = Nil;
        V p_471 = Nil;
        foreach (var item472 in Items(ListOf(ListOf(S("TEST IS INITIAL"), S("INITIAL")), ListOf(S("\"TEST\" RECURSIVE"), S("RECURSIVE")))))
        {
            var unpack473 = item472;
            declaration_470 = unpack473[I(0)];
            attr_469 = unpack473[I(1)];
            var unpack474 = M_parse(AddText(S("IDENTIFICATION DIVISION. PROGRAM-ID. "), Format(declaration_470, string.Empty), S(". PROCEDURE DIVISION. DISPLAY \"KEPT\" GOBACK.")));
            p_471 = unpack474[I(0)];
            _ = unpack474[I(1)];
            _ = CheckEqual(p_471[S("id")], S("TEST"));
            _ = CheckEqual(p_471[S("programDeclaration")][S("attributes")], ListOf(attr_469));
            _ = CheckIn(S("PROGRAM_LIFECYCLE"), ListOf(from item475 in Items(p_471[S("diagnostics")]) let d_476 = item475 select d_476[S("code")]));
        }
    }

    /// <summary>program attribute rejections and end name の回帰条件を検証します。</summary>
    [Fact]
    public void TestProgramAttributeRejectionsAndEndName()
    {
        V declaration_478 = Nil;
        V p_479 = Nil;
        foreach (var item480 in Items(ListOf(S("TEST IS MYSTERY"), S("TEST UNKNOWN"), S("TEST INITIAL RECURSIVE"))))
        {
            declaration_478 = item480;
            Assert.Throws<ParseException>(() =>
            {
                _ = M_parse(AddText(S("IDENTIFICATION DIVISION. PROGRAM-ID. "), Format(declaration_478, string.Empty), S(". PROCEDURE DIVISION. GOBACK.")));
            });
        }

        var unpack481 = M_parse(S("IDENTIFICATION DIVISION. PROGRAM-ID. \"TEST\" RECURSIVE\nPROCEDURE DIVISION. GOBACK. END PROGRAM TEST."));
        p_479 = unpack481[I(0)];
        _ = unpack481[I(1)];
        _ = CheckIn(S("PROGRAM_ID_PERIOD"), ListOf(from item482 in Items(p_479[S("diagnostics")]) let d_483 = item482 select d_483[S("code")]));
        Assert.Throws<ParseException>(() =>
        {
            _ = M_parse(S("IDENTIFICATION DIVISION. PROGRAM-ID. \"TEST\". PROCEDURE DIVISION. GOBACK. END PROGRAM OTHER."));
        });
    }

    /// <summary>fd clauses の回帰条件を検証します。</summary>
    [Fact]
    public void TestFdClauses()
    {
        V dep_485 = Nil;
        V p_486 = Nil;
        var unpack487 = M_parse(S("IDENTIFICATION DIVISION. PROGRAM-ID. TEST.\nENVIRONMENT DIVISION. INPUT-OUTPUT SECTION. FILE-CONTROL.\nSELECT F ASSIGN TO \"FILE\".\nDATA DIVISION. FILE SECTION.\nFD F LABEL RECORDS ARE OMITTED BLOCK CONTAINS 0 RECORDS\nRECORDING MODE IS V RECORD IS VARYING IN SIZE FROM 10 TO 80\nDEPENDING ON N DATA RECORD IS REC.\n01 REC PIC X(80).\nWORKING-STORAGE SECTION. 01 N PIC 99.\nPROCEDURE DIVISION. GOBACK."));
        p_486 = unpack487[I(0)];
        _ = unpack487[I(1)];
        dep_485 = p_486[S("dependencies")][I(0)];
        _ = CheckEqual(dep_485[S("recordingMode")], S("V"));
        _ = CheckEqual(dep_485[S("file")][S("minLength")], I(10));
        _ = CheckEqual(dep_485[S("blockClause")][S("max")], I(0));
        _ = CheckEqual(Length(dep_485[S("descriptionClauses")]), I(5));
    }

    /// <summary>fixed fd orders and invalid clauses の回帰条件を検証します。</summary>
    [Fact]
    public void TestFixedFdOrdersAndInvalidClauses()
    {
        V clauses_489 = Nil;
        V p_490 = Nil;
        foreach (var item491 in Items(ListOf(S("RECORDING MODE F RECORD CONTAINS 80 CHARACTERS"), S("RECORD CONTAINS 80 CHARACTERS RECORDING MODE IS F"))))
        {
            clauses_489 = item491;
            var unpack492 = M_fd(clauses_489);
            p_490 = unpack492[I(0)];
            _ = unpack492[I(1)];
            _ = CheckEqual(p_490[S("dependencies")][I(0)][S("file")][S("maxLength")], I(80));
        }

        foreach (var item493 in Items(ListOf(S("RECORDING MODE F RECORDING MODE V"), S("RECORDING MODE Z"), S("RECORD CONTAINS 0 CHARACTERS"), S("RECORD VARYING SIZE 80 TO 10 DEPENDING ON N"), S("RECORDING MODE F RECORD VARYING SIZE 1 TO 8 DEPENDING ON N"), S("MYSTERY SOMETHING"), S("DATA RECORDS ARE REC OTHER"), S("DATA RECORD IS OTHER"))))
        {
            clauses_489 = item493;
            Assert.Throws<ParseException>(() =>
            {
                _ = M_fd(clauses_489);
            });
        }
    }

    /// <summary>fd missing period preserves boundary の回帰条件を検証します。</summary>
    [Fact]
    public void TestFdMissingPeriodPreservesBoundary()
    {
        V boundary_494 = Nil;
        foreach (var item495 in Items(ListOf(S("01 REC PIC X."), S("PROCEDURE DIVISION. GOBACK."))))
        {
            boundary_494 = item495;
            Assert.Throws<ParseException>(() =>
            {
                _ = M_parse(TestAdd(S("IDENTIFICATION DIVISION. PROGRAM-ID. TEST. ENVIRONMENT DIVISION. INPUT-OUTPUT SECTION. FILE-CONTROL. SELECT F ASSIGN TO \"FILE\". DATA DIVISION. FILE SECTION. FD F RECORDING MODE F\n"), boundary_494));
            });
        }
    }

    /// <summary>missing linkage copy の回帰条件を検証します。</summary>
    [Fact]
    public void TestMissingLinkageCopy()
    {
        V missing_497 = Nil;
        V p_498 = Nil;
        var unpack499 = M_parse(S("IDENTIFICATION DIVISION. PROGRAM-ID. TEST.\nDATA DIVISION. LINKAGE SECTION.\nCOPY \"missing.cpy\".\n01 KNOWN PIC X.\nPROCEDURE DIVISION USING MISSING KNOWN. DISPLAY KNOWN GOBACK."), scaffold_: B(true));
        p_498 = unpack499[I(0)];
        _ = unpack499[I(1)];
        missing_497 = Next(Sequence(from item500 in Items(p_498[S("symbols")]) let s_501 = item500 where Truth(B(TestEqual(s_501[S("name")], S("MISSING")))) select s_501));
        _ = CheckEqual(missing_497[S("scope")], S("LINKAGE"));
        _ = CheckTrue(missing_497[S("stub")]);
        _ = CheckIsNone(missing_497[S("pic")]);
        _ = CheckTrue(missing_497[S("missingCopyOrigins")]);
    }

    /// <summary>using known storage and unrelated unknown are rejected の回帰条件を検証します。</summary>
    [Fact]
    public void TestUsingKnownStorageAndUnrelatedUnknownAreRejected()
    {
        V data_502 = Nil;
        V using_503 = Nil;
        foreach (var item504 in Items(ListOf(ListOf(S("WORKING-STORAGE SECTION. 01 X PIC X."), S("X")), ListOf(S("LINKAGE SECTION. 01 X PIC X."), S("TYPO")), ListOf(S("WORKING-STORAGE SECTION. COPY \"missing.cpy\"."), S("MISSING")), ListOf(S("LINKAGE SECTION. COPY \"missing.cpy\"."), S("TYPO")))))
        {
            var unpack505 = item504;
            data_502 = unpack505[I(0)];
            using_503 = unpack505[I(1)];
            Assert.Throws<ParseException>(() =>
            {
                _ = M_parse(AddText(S("IDENTIFICATION DIVISION. PROGRAM-ID. TEST. DATA DIVISION. "), Format(data_502, string.Empty), S(" PROCEDURE DIVISION USING "), Format(using_503, string.Empty), S(". GOBACK.")), scaffold_: B(true));
            });
        }
    }

    /// <summary>provided copy replacing and ambiguity の回帰条件を検証します。</summary>
    [Fact]
    public void TestProvidedCopyReplacingAndAmbiguity()
    {
        V p_507 = Nil;
        V text_508 = Nil;
        text_508 = S("IDENTIFICATION DIVISION. PROGRAM-ID. TEST. DATA DIVISION. LINKAGE SECTION. COPY \"custom.cpy\" REPLACING ==OLD== BY ==ARG==. PROCEDURE DIVISION USING ARG. DISPLAY ARG GOBACK.");
        var unpack509 = M_parse(text_508, copies_: Put(Dict(), S("custom.cpy"), S("01 OLD PIC X(3).")));
        p_507 = unpack509[I(0)];
        _ = unpack509[I(1)];
        _ = CheckEqual(p_507[S("symbols")][I(0)][S("name")], S("ARG"));
        _ = CheckFalse(p_507[S("symbols")][I(0)][S("stub")]);
        _ = CheckTrue(Any(Sequence(from item510 in Items(p_507[S("tokens")]) let t_511 = item510 select TestCall(t_511[S("source")], "get", S("replacementOrigins")))));
        Assert.Throws<ParseException>(() =>
        {
            _ = M_parse(text_508, scaffold_: B(true), copies_: Put(Put(Dict(), S("custom.cpy"), S("01 OLD PIC X.")), S("CUSTOM.CPY"), S("01 OLD PIC X.")));
        });
    }

    /// <summary>unset condition retains procedure の回帰条件を検証します。</summary>
    [Fact]
    public void TestUnsetConditionRetainsProcedure()
    {
        V p_513 = Nil;
        var unpack514 = M_parse(S("IDENTIFICATION DIVISION. PROGRAM-ID. TEST.\nDATA DIVISION. WORKING-STORAGE SECTION.\n$IF P64 SET\n77 PTR PIC S9(18) COMP-5.\n$ELSE\n77 PTR PIC S9(9) COMP-5.\n$END\nPROCEDURE DIVISION. DISPLAY \"KEPT\" GOBACK."), scaffold_: B(true));
        p_513 = unpack514[I(0)];
        _ = unpack514[I(1)];
        _ = CheckEqual(Length(ListOf(from item515 in Items(p_513[S("symbols")]) let s_516 = item515 where Truth(B(TestEqual(s_516[S("name")], S("PTR")))) select s_516)), I(1));
        _ = CheckTrue(p_513[S("symbols")][I(0)][S("stub")]);
        _ = CheckEqual(p_513[S("paragraphs")][I(0)][S("statements")][I(0)][S("kind")], S("display"));
    }

    /// <summary>condition selection and procedure block の回帰条件を検証します。</summary>
    [Fact]
    public void TestConditionSelectionAndProcedureBlock()
    {
        V body_518 = Nil;
        V digits_519 = Nil;
        V p_520 = Nil;
        V text_521 = Nil;
        V value_522 = Nil;
        text_521 = S("IDENTIFICATION DIVISION. PROGRAM-ID. TEST.\nDATA DIVISION. WORKING-STORAGE SECTION.\n$IF P64 SET\n77 PTR PIC S9(18) COMP-5.\n$ELSE\n77 PTR PIC S9(9) COMP-5.\n$END\nPROCEDURE DIVISION.\n$IF P64 SET\nDISPLAY \"64\"\n$ELSE\nDISPLAY \"32\"\n$END\nDISPLAY \"AFTER\" GOBACK.");
        foreach (var item523 in Items(ListOf(ListOf(B(true), I(18)), ListOf(B(false), I(9)), ListOf(Nil, Nil))))
        {
            var unpack524 = item523;
            value_522 = unpack524[I(0)];
            digits_519 = unpack524[I(1)];
            var unpack525 = M_parse(text_521, p64_: value_522);
            p_520 = unpack525[I(0)];
            _ = unpack525[I(1)];
            _ = CheckEqual(Truth(digits_519) ? p_520[S("symbols")][I(0)][S("pic")][S("digits")] : p_520[S("symbols")][I(0)][S("pic")], digits_519);
            body_518 = p_520[S("paragraphs")][I(0)][S("statements")];
            _ = CheckEqual(body_518[I(0)][S("kind")], Truth(B(ReferenceEquals(value_522, Nil))) ? S("unknown") : S("display"));
            _ = CheckEqual(body_518[I(1)][S("kind")], S("display"));
            _ = CheckEqual(Length(p_520[S("conditionalDirectives")]), I(2));
        }
    }

    /// <summary>nested and malformed conditions の回帰条件を検証します。</summary>
    [Fact]
    public void TestNestedAndMalformedConditions()
    {
        V body_527 = Nil;
        V header_528 = Nil;
        V nested_529 = Nil;
        V p_530 = Nil;
        V value_531 = Nil;
        header_528 = S("IDENTIFICATION DIVISION. PROGRAM-ID. TEST.\nPROCEDURE DIVISION.\n");
        nested_529 = S("$IF P64 SET\n$IF P64 SET\nDISPLAY \"X\"\n$END\n$ELSE\nDISPLAY \"Y\"\n$END\nGOBACK.");
        foreach (var item532 in Items(ListOf(B(true), B(false), Nil)))
        {
            value_531 = item532;
            var unpack533 = M_parse(TestAdd(header_528, nested_529), p64_: value_531);
            p_530 = unpack533[I(0)];
            _ = unpack533[I(1)];
            _ = CheckEqual(p_530[S("paragraphs")][I(0)][S("statements")][I(0)][S("kind")], Truth(B(ReferenceEquals(value_531, Nil))) ? S("unknown") : S("display"));
        }

        foreach (var item534 in Items(ListOf(S("$END"), S("$ELSE"), S("$IF P64 SET\nGOBACK."), S("$IF P64 SET\n$ELSE\n$ELSE\n$END"))))
        {
            body_527 = item534;
            Assert.Throws<ParseException>(() =>
            {
                _ = M_parse(TestAdd(header_528, body_527));
            });
        }
    }

    /// <summary>unknown declaration recovery の回帰条件を検証します。</summary>
    [Fact]
    public void TestUnknownDeclarationRecovery()
    {
        V p_536 = Nil;
        var unpack537 = M_parse(S("IDENTIFICATION DIVISION. PROGRAM-ID. TEST. DATA DIVISION. WORKING-STORAGE SECTION.\nMYSTERY\n01 N PIC 9.\nMYSTERY\nPROCEDURE DIVISION. DISPLAY N GOBACK."));
        p_536 = unpack537[I(0)];
        _ = unpack537[I(1)];
        _ = CheckEqual(p_536[S("symbols")][I(0)][S("name")], S("N"));
        _ = CheckEqual(p_536[S("paragraphs")][I(0)][S("statements")][I(0)][S("kind")], S("display"));
        _ = CheckEqual(Length(ListOf(from item538 in Items(p_536[S("tokens")]) let t_539 = item538 where Truth(B(TestEqual(t_539[S("ownerId")], S("$unknownDeclaration")))) select t_539)), I(2));
    }

    /// <summary>selected statement is not marked excluded の回帰条件を検証します。</summary>
    [Fact]
    public void TestSelectedStatementIsNotMarkedExcluded()
    {
        V front_540 = Nil;
        V p_541 = Nil;
        V selected_542 = Nil;
        V text_543 = Nil;
        text_543 = S("IDENTIFICATION DIVISION. PROGRAM-ID. TEST.\nPROCEDURE DIVISION.\n$IF P64 SET\nIF 1 = 1 DISPLAY \"KEPT\" END-IF\n$END\nGOBACK.");
        var unpack544 = M_parse(text_543, p64_: B(true));
        p_541 = unpack544[I(0)];
        front_540 = unpack544[I(1)];
        selected_542 = Next(Sequence(from item545 in Items(p_541[S("tokens")]) let t_546 = item545 where Truth(B(TestEqual(t_546[S("text")], S("IF")))) select t_546));
        _ = CheckFalse(Any(Sequence(from item547 in Items(TestMember(front_540, "excluded")) let e_548 = item547 select B(TestEqual(e_548[S("source")], selected_542[S("source")])))));
    }

    /// <summary>conditional children keep known siblings typed の回帰条件を検証します。</summary>
    [Fact]
    public void TestConditionalChildrenKeepKnownSiblingsTyped()
    {
        V after_550 = Nil;
        V known_551 = Nil;
        V p_552 = Nil;
        V pad_553 = Nil;
        V text_554 = Nil;
        text_554 = S("IDENTIFICATION DIVISION. PROGRAM-ID. TEST.\nDATA DIVISION. WORKING-STORAGE SECTION.\n01 GROUP-A.\n05 KNOWN PIC 9.\n$IF P64 SET\n05 PAD PIC X(4).\n$END\n05 AFTER-PAD PIC 9.\nPROCEDURE DIVISION. DISPLAY KNOWN AFTER-PAD GOBACK.");
        var unpack555 = M_parse(text_554);
        p_552 = unpack555[I(0)];
        _ = unpack555[I(1)];
        known_551 = Next(Sequence(from item556 in Items(p_552[S("symbols")]) let s_557 = item556 where Truth(B(TestEqual(s_557[S("name")], S("KNOWN")))) select s_557));
        pad_553 = Next(Sequence(from item558 in Items(p_552[S("symbols")]) let s_559 = item558 where Truth(B(TestEqual(s_559[S("name")], S("PAD")))) select s_559));
        after_550 = Next(Sequence(from item560 in Items(p_552[S("symbols")]) let s_561 = item560 where Truth(B(TestEqual(s_561[S("name")], S("AFTER-PAD")))) select s_561));
        _ = CheckFalse(known_551[S("stub")]);
        _ = CheckFalse(after_550[S("stub")]);
        _ = CheckEqual(known_551[S("parentId")], after_550[S("parentId")]);
        _ = CheckEqual(pad_553[S("parentId")], known_551[S("parentId")]);
        _ = CheckTrue(pad_553[S("stub")]);
        _ = CheckIsNone(pad_553[S("pic")]);
    }

    /// <summary>
    /// 元の解析規則とIRの順序を保持します。
    /// </summary>
    /// <param name="clauses_">clauses の解析値。</param>
    /// <param name="records_">records の解析値。</param>
    /// <returns>解析結果または更新後の状態。</returns>
    private V M_fd(V clauses_, V? records_ = null)
    {
        records_ ??= S("01 REC PIC X(80).");
        return M_parse(AddText(S("IDENTIFICATION DIVISION. PROGRAM-ID. TEST.\nENVIRONMENT DIVISION. INPUT-OUTPUT SECTION. FILE-CONTROL.\nSELECT F ASSIGN TO \"FILE\".\nDATA DIVISION. FILE SECTION. FD F "), Format(clauses_, string.Empty), S(".\n"), Format(records_, string.Empty), S("\nPROCEDURE DIVISION. GOBACK.")));
    }

    private V M_parse(V source_, V? scaffold_ = null, V? fixed_ = null, V? copies_ = null, V? p64_ = null) => ParseSource(source_, scaffold_, fixed_, copies_, p64_);
}
