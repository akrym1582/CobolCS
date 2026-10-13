using static CobolCS.Frontend.Parsing.Values;

namespace CobolCS.Frontend.Parsing;

/// <summary>位置とIRを保持する procedure の解析処理です。</summary>
internal sealed partial class Parser : V
{
    /// <summary>
    /// 1プログラムの部・宣言・段落・手続きと依存契約を組み立てる。
    /// 最後に全トークンの所属を確認し、未消費の意味トークンはunknownと診断へ残す。
    /// 構文復旧で命令が消えても正常生成できたように見えることを防ぐための確認。
    /// </summary>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_parse()
    {
        V a_387 = Nil;
        V all_statements_388 = Nil;
        V attributes_389 = Nil;
        V boundary_390 = Nil;
        V closest_391 = Nil;
        V dep_392 = Nil;
        V diagnostic_393 = Nil;
        V documentary_394 = Nil;
        V documentation_395 = Nil;
        V e_396 = Nil;
        V env_397 = Nil;
        V has_is_398 = Nil;
        V headers_399 = Nil;
        V index_400 = Nil;
        V item_401 = Nil;
        V location_402 = Nil;
        V mode_404 = Nil;
        V n_405 = Nil;
        V name_406 = Nil;
        V owner_407 = Nil;
        V p_408 = Nil;
        V paragraph_409 = Nil;
        V program_declaration_410 = Nil;
        V program_id_411 = Nil;
        V raw_name_412 = Nil;
        V s_413 = Nil;
        V section_414 = Nil;
        V sid_415 = Nil;
        V src_416 = Nil;
        V symbol_417 = Nil;
        V t_418 = Nil;
        V target_419 = Nil;
        V token_index_420 = Nil;
        V tokens_421 = Nil;
        V ts_422 = Nil;
        V using_423 = Nil;
        V word_424 = Nil;
        V words_425 = Nil;
        if (Truth(B(Equal(M_peek(), S("CONDITIONAL-ERROR")))))
        {
            throw new ParseException(Text(Member(Member(this, "ts")[I(0)], "source")[S("error")]));
        }

        program_id_411 = S(string.Empty);
        program_declaration_410 = Nil;
        env_397 = ListOf();
        documentary_394 = SetOf(S("AUTHOR"), S("INSTALLATION"), S("DATE-WRITTEN"), S("DATE-COMPILED"), S("SECURITY"), S("REMARKS"));
        headers_399 = Union(documentary_394, SetOf(S("IDENTIFICATION"), S("ENVIRONMENT"), S("CONFIGURATION"), S("DATA"), S("PROCEDURE")));
        documentation_395 = ListOf();
        while (Truth(And(M_peek(), () => And(B(!Equal(M_peek(), S("DATA"))), () => B(!Equal(M_peek(), S("PROCEDURE")))))))
        {
            a_387 = Member(this, "i");
            t_418 = M_take();
            if (Truth(B(Equal(Member(t_418, "upper"), S("PROGRAM-ID")))))
            {
                _ = M_require(S("."));
                raw_name_412 = Member(M_take(), "text");
                program_id_411 = Truth(Call(raw_name_412, "startswith", ListOf(S("\""), S("'")))) ? raw_name_412[Slice(I(1), Subtract(I(0), I(1)), Nil)] : raw_name_412;
                attributes_389 = ListOf();
                has_is_398 = M_accept(S("IS"));
                if (Truth(B(Contains(SetOf(S("INITIAL"), S("RECURSIVE")), M_peek()))))
                {
                    _ = Call(attributes_389, "append", Member(M_take(), "upper"));
                }
                else
                {
                    if (Truth(has_is_398))
                    {
                        throw new ParseException(Text(Add(S("Unknown PROGRAM-ID attribute "), M_peek())));
                    }
                }

                if (Truth(B(!Truth(M_accept(S("."))))))
                {
                    if (Truth(B(!Contains(headers_399, M_peek()))))
                    {
                        _ = M_require(S("."));
                    }

                    _ = M_diag(S("PROGRAM_ID_PERIOD"), S("Missing PROGRAM-ID terminal period accepted for public source review"), M_source(a_387), S("warning"));
                }

                program_declaration_410 = Put(Put(Put(Put(Dict(), S("name"), program_id_411), S("rawName"), raw_name_412), S("attributes"), attributes_389), S("source"), M_source(a_387));
                if (Truth(attributes_389))
                {
                    _ = M_diag(S("PROGRAM_LIFECYCLE"), Add(Add(S("PROGRAM-ID "), attributes_389[I(0)]), S(" requires an explicit call lifecycle implementation")), M_source(a_387));
                }
            }
            else
            {
                if (Truth(B(Contains(documentary_394, Member(t_418, "upper")))))
                {
                    _ = M_require(S("."));
                    while (Truth(And(M_peek(), () => B(!Truth(Or(And(B(Contains(documentary_394, M_peek())), () => And(B(Equal(M_peek(I(1)), S("."))), () => B(Compare(Member(Member(this, "ts")[Member(this, "i")], "source")[S("start")][S("line")], Member(t_418, "source")[S("start")][S("line")]) > 0))), () => Or(And(B(Contains(SetOf(S("IDENTIFICATION"), S("ENVIRONMENT"), S("DATA"), S("PROCEDURE")), M_peek())), () => B(Equal(M_peek(I(1)), S("DIVISION")))), () => And(B(Equal(M_peek(), S("CONFIGURATION"))), () => B(Equal(M_peek(I(1)), S("SECTION")))))))))))
                    {
                        _ = M_take();
                    }

                    _ = Call(documentation_395, "append", Put(Put(Dict(), S("kind"), Member(t_418, "upper")), S("source"), M_source(a_387)));
                }
                else
                {
                    if (Truth(B(Contains(ListOf(S("IDENTIFICATION"), S("ENVIRONMENT")), Member(t_418, "upper")))))
                    {
                        _ = M_require(S("DIVISION"));
                        _ = M_require(S("."));
                    }
                    else
                    {
                        if (Truth(B(Equal(Member(t_418, "upper"), S("CONFIGURATION")))))
                        {
                            _ = M_require(S("SECTION"));
                            _ = M_require(S("."));
                        }
                        else
                        {
                            if (Truth(B(Equal(Member(t_418, "upper"), S("SPECIAL-NAMES")))))
                            {
                                _ = M_require(S("."));
                            }
                            else
                            {
                                if (Truth(B(Equal(Member(t_418, "upper"), S("DECIMAL-POINT")))))
                                {
                                    _ = M_require(S("IS"));
                                    _ = M_require(S("COMMA"));
                                    _ = M_require(S("."));
                                    Member(Member(this, "f"), "config")[S("decimalPoint")] = S("comma");
                                }
                                else
                                {
                                    if (Truth(B(Equal(Member(t_418, "upper"), S("CURRENCY")))))
                                    {
                                        _ = M_require(S("SIGN"));
                                        _ = M_require(S("IS"));
                                        symbol_417 = Member(M_take(), "text");
                                        if (Truth(Or(B(!Truth(Call(symbol_417, "startswith", ListOf(S("\""), S("'"))))), () => B(!Equal(Length(symbol_417), I(3))))))
                                        {
                                            throw new ParseException(Text(S("CURRENCY SIGN requires one literal character")));
                                        }

                                        _ = M_require(S("."));
                                        Member(Member(this, "f"), "config")[S("currencySymbol")] = symbol_417[I(1)];
                                    }
                                    else
                                    {
                                        if (Truth(B(Contains(ListOf(S("INPUT-OUTPUT"), S("FILE-CONTROL")), Member(t_418, "upper")))))
                                        {
                                            if (Truth(B(Equal(Member(t_418, "upper"), S("INPUT-OUTPUT")))))
                                            {
                                                _ = M_require(S("SECTION"));
                                            }

                                            _ = M_require(S("."));
                                        }
                                        else
                                        {
                                            if (Truth(B(Equal(Member(t_418, "upper"), S("SELECT")))))
                                            {
                                                _ = M_accept(S("OPTIONAL"));
                                                name_406 = Member(M_take(), "text");
                                                while (Truth(And(M_peek(), () => B(!Equal(M_peek(), S("."))))))
                                                {
                                                    _ = M_take();
                                                }

                                                _ = M_require(S("."));
                                                _ = Call(env_397, "append", ListOf(name_406, Member(this, "ts")[Slice(a_387, Member(this, "i"), Nil)], M_source(a_387)));
                                            }
                                            else
                                            {
                                                if (Truth(B(Equal(Member(t_418, "text"), S(".")))))
                                                {
                                                }
                                                else
                                                {
                                                    while (Truth(And(M_peek(), () => B(!Equal(M_peek(), S("."))))))
                                                    {
                                                        _ = M_take();
                                                    }

                                                    _ = M_accept(S("."));
                                                    _ = M_diag(S("UNKNOWN_HEADER"), S("Unsupported header syntax"), M_source(a_387));
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        foreach (var item426 in Items(env_397))
        {
            var unpack427 = item426;
            name_406 = unpack427[I(0)];
            ts_422 = unpack427[I(1)];
            src_416 = unpack427[I(2)];
            _ = M_dependency(S("file"), name_406, Call(S(" "), "join", Sequence(from item428 in Items(ts_422) let t_429 = item428 select Member(t_429, "text"))), src_416);
        }

        if (Truth(M_accept(S("DATA"))))
        {
            _ = M_require(S("DIVISION"));
            _ = M_require(S("."));
            _ = M_declarations();
        }

        foreach (var item430 in Items(env_397))
        {
            var unpack431 = item430;
            name_406 = unpack431[I(0)];
            ts_422 = unpack431[I(1)];
            src_416 = unpack431[I(2)];
            words_425 = ListOf(from item432 in Items(ts_422) let t_433 = item432 select Member(t_433, "upper"));
            dep_392 = Next(Sequence(from item434 in Items(Member(this, "dependencies")) let d_435 = item434 where Truth(B(Equal(d_435[S("name")], name_406))) select d_435));
            if (Truth(B(Contains(words_425, S("STATUS")))))
            {
                n_405 = Add(Call(words_425, "index", S("STATUS")), I(1));
                if (Truth(B(Equal(words_425[n_405], S("IS")))))
                {
                    n_405 = Add(n_405, I(1));
                }

                try
                {
                    s_413 = M_resolve(Member(ts_422[n_405], "text"));
                    dep_392[S("status")] = Put(Put(Put(Put(Dict(), S("kind"), S("reference")), S("symbolId"), s_413[S("id")]), S("subscripts"), ListOf()), S("modification"), Nil);
                }
                catch (Exception error436) when (error436 is ParseException)
                {
                    e_396 = S(error436.Message);
                    _ = M_diag(S("FILE_STATUS"), StringValue(e_396), src_416);
                }
            }

            _ = M_file_control(dep_392, ts_422, src_416);
        }

        if (Truth(B(!Truth(program_id_411))))
        {
            throw new ParseException(Text(S("Missing PROGRAM-ID")));
        }

        _ = M_require(S("PROCEDURE"));
        _ = M_require(S("DIVISION"));
        using_423 = ListOf();
        if (Truth(M_accept(S("USING"))))
        {
            mode_404 = S("reference");
            while (Truth(And(M_peek(), () => B(!Equal(M_peek(), S("."))))))
            {
                if (Truth(M_accept(S(","))))
                {
                    continue;
                }

                if (Truth(M_accept(S("BY"))))
                {
                    word_424 = Member(M_take(), "upper");
                    if (Truth(B(!Contains(ListOf(S("REFERENCE"), S("CONTENT"), S("VALUE")), word_424))))
                    {
                        throw new ParseException(Text(S("Invalid PROCEDURE USING mode")));
                    }

                    mode_404 = Call(word_424, "lower");
                }
                else
                {
                    symbol_417 = M_resolve(Member(M_take(), "text"), required_scope_: S("LINKAGE"));
                    if (Truth(Or(B(!Equal(symbol_417[S("scope")], S("LINKAGE"))), () => symbol_417[S("parentId")])))
                    {
                        throw new ParseException(Text(S("PROCEDURE USING requires LINKAGE roots")));
                    }

                    _ = Call(using_423, "append", Put(Put(Dict(), S("symbolId"), symbol_417[S("id")]), S("mode"), mode_404));
                }
            }
        }

        _ = M_require(S("."));
        SetMember(this, "procedure_start", Member(this, "i"));
        section_414 = Nil;
        SetMember(this, "program_id", program_id_411);
        SetMember(this, "stub_target_names", Truth(B(Equal(Member(Member(this, "f"), "config")[S("controlFlowMode")], S("stub-control")))) ? M_find_stub_targets() : SetCopy());
        while (Truth(M_peek()))
        {
            if (Truth(M_accept(S("."))))
            {
                continue;
            }

            if (Truth(And(B(Equal(M_peek(), S("END"))), () => B(Equal(M_peek(I(1)), S("PROGRAM"))))))
            {
                _ = M_take();
                _ = M_take();
                name_406 = Member(M_take(), "text");
                if (Truth(Call(name_406, "startswith", ListOf(S("\""), S("'")))))
                {
                    name_406 = name_406[Slice(I(1), Subtract(I(0), I(1)), Nil)];
                }

                _ = M_require(S("."));
                foreach (var item437 in Items(Range(Subtract(Member(this, "i"), I(4)), Member(this, "i"))))
                {
                    token_index_420 = item437;
                    Member(this, "owners")[token_index_420] = ListOf(S("declaration"), S("$endProgram"));
                }

                var nameComparison = Truth(Call(raw_name_412, "startswith", ListOf(S("\""), S("'")))) ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                if (!string.Equals(Text(name_406), Text(program_id_411), nameComparison) || Truth(M_peek()))
                {
                    throw new ParseException(Text(S("Invalid END PROGRAM or multiple programs in one source")));
                }

                break;
            }

            if (Truth(B(Equal(M_peek(I(1)), S("SECTION")))))
            {
                a_387 = Member(this, "i");
                section_414 = Member(M_take(), "text");
                _ = M_take();
                _ = M_require(S("."));
                foreach (var item438 in Items(Range(a_387, Member(this, "i"))))
                {
                    token_index_420 = item438;
                    Member(this, "owners")[token_index_420] = ListOf(S("declaration"), S("$section"));
                }

                if (Truth(And(B(!Truth(And(B(Equal(M_peek(I(1)), S("."))), () => B(!Contains(Verbs, M_peek()))))), () => B(!Equal(M_peek(I(1)), S("SECTION"))))))
                {
                    _ = Call(Member(this, "paragraphs"), "append", Put(Put(Put(Put(Put(Put(Dict(), S("id"), AddText(S("p"), Format(Length(Member(this, "paragraphs")), "06d"))), S("name"), Add(S("$section-"), section_414)), S("section"), section_414), S("order"), Length(Member(this, "paragraphs"))), S("statements"), ListOf()), S("source"), M_source(a_387)));
                }

                continue;
            }

            if (Truth(And(B(Equal(M_peek(I(1)), S("."))), () => B(!Contains(Verbs, M_peek())))))
            {
                a_387 = Member(this, "i");
                name_406 = Member(M_take(), "text");
                _ = M_take();
                p_408 = Put(Put(Put(Put(Put(Put(Dict(), S("id"), AddText(S("p"), Format(Length(Member(this, "paragraphs")), "06d"))), S("name"), name_406), S("section"), section_414), S("order"), Length(Member(this, "paragraphs"))), S("statements"), ListOf()), S("source"), M_source(a_387));
                _ = Call(Member(this, "paragraphs"), "append", p_408);
                foreach (var item439 in Items(Range(a_387, Member(this, "i"))))
                {
                    token_index_420 = item439;
                    Member(this, "owners")[token_index_420] = ListOf(S("declaration"), p_408[S("id")]);
                }
            }
            else
            {
                if (Truth(B(!Truth(Member(this, "paragraphs")))))
                {
                    _ = Call(Member(this, "paragraphs"), "append", Put(Put(Put(Put(Put(Put(Dict(), S("id"), S("p000000")), S("name"), S("$entry")), S("section"), section_414), S("order"), I(0)), S("statements"), ListOf()), S("source"), Member(Member(this, "ts")[Member(this, "i")], "source")));
                }
            }

            paragraph_409 = Member(this, "paragraphs")[Subtract(I(0), I(1))];
            if (Truth(Or(B(Contains(Member(this, "stub_target_names"), Call(paragraph_409[S("name")], "upper"))), () => B(Contains(Member(this, "stub_target_names"), Call(Or(section_414, () => S(string.Empty)), "upper"))))))
            {
                a_387 = Member(this, "i");
                while (Truth(And(M_peek(), () => And(B(!Truth(And(B(Contains(ListOf(S("."), S("SECTION")), M_peek(I(1)))), () => And(B(!Contains(Verbs, M_peek())), () => Or(B(Equal(Member(this, "i"), a_387)), () => B(Equal(Member(Member(this, "ts")[Subtract(Member(this, "i"), I(1))], "text"), S(".")))))))), () => B(!Truth(And(B(Equal(M_peek(), S("END"))), () => B(Equal(M_peek(I(1)), S("PROGRAM"))))))))))
                {
                    _ = M_take();
                }

                if (Truth(B(Compare(Member(this, "i"), a_387) >= 0)))
                {
                    sid_415 = AddText(S("s"), Format(Member(this, "sid"), "06d"));
                    SetMember(this, "sid", Add(Member(this, "sid"), I(1)));
                    _ = Call(paragraph_409[S("statements")], "append", Put(Put(Put(Put(Put(Dict(), S("kind"), S("controlTarget")), S("id"), sid_415), S("name"), paragraph_409[S("name")]), S("source"), Truth(B(Compare(Member(this, "i"), a_387) > 0)) ? M_source(a_387) : paragraph_409[S("source")]), S("tokens"), M_raw_tokens(a_387, Member(this, "i"))));
                    foreach (var item440 in Items(Range(a_387, Member(this, "i"))))
                    {
                        token_index_420 = item440;
                        Member(this, "owners")[token_index_420] = ListOf(S("stubbed"), sid_415);
                    }
                }
            }
            else
            {
                paragraph_409[S("statements")] = Add(paragraph_409[S("statements")], M_block(SetCopy(), top_: B(true)));
            }
        }

        foreach (var item441 in Items(Member(this, "paragraphs")))
        {
            paragraph_409 = item441;
            if (Truth(And(B(!Truth(paragraph_409[S("statements")])), () => Or(B(Contains(Member(this, "stub_target_names"), Call(paragraph_409[S("name")], "upper"))), () => B(Contains(Member(this, "stub_target_names"), Call(Or(paragraph_409[S("section")], () => S(string.Empty)), "upper")))))))
            {
                _ = Call(paragraph_409[S("statements")], "append", Put(Put(Put(Put(Put(Dict(), S("kind"), S("controlTarget")), S("id"), AddText(S("s"), Format(Member(this, "sid"), "06d"))), S("name"), paragraph_409[S("name")]), S("source"), paragraph_409[S("source")]), S("tokens"), ListOf()));
                SetMember(this, "sid", Add(Member(this, "sid"), I(1)));
            }
        }

        // 文のないPROCEDURE DIVISIONも有効なプログラムとして保持します。
        IEnumerable<V> Fn_statements(V body_442)
        {
            V case_443 = Nil;
            V key_444 = Nil;
            V s_445 = Nil;
            foreach (var item446 in Items(body_442))
            {
                s_445 = item446;
                yield return s_445;
                foreach (var item447 in Items(ListOf(S("body"), S("then"), S("else"), S("onError"), S("notError"), S("atEnd"), S("notEnd"), S("onOverflow"), S("notOverflow"))))
                {
                    key_444 = item447;
                    if (Truth(B(IsList(Call(s_445, "get", key_444)))))
                    {
                        foreach (var yieldItem448 in Items(Sequence(Fn_statements(s_445[key_444]))))
                        {
                            yield return yieldItem448;
                        }
                    }
                }

                foreach (var item449 in Items(Call(s_445, "get", S("cases"), ListOf())))
                {
                    case_443 = item449;
                    if (Truth(B(IsList(Call(case_443, "get", S("body"))))))
                    {
                        foreach (var yieldItem450 in Items(Sequence(Fn_statements(case_443[S("body")]))))
                        {
                            yield return yieldItem450;
                        }
                    }
                }
            }
        }

        all_statements_388 = Sorted(Sequence(from item451 in Items(Member(this, "paragraphs")) let p_452 = item451 from item453 in Items(Sequence(Fn_statements(p_452[S("statements")]))) let s_454 = item453 select s_454), (arg455) => Integer(arg455[S("id")][Slice(I(1), Nil, Nil)]));
        foreach (var item456 in Items(Enumerate(all_statements_388)))
        {
            var unpack457 = item456;
            index_400 = unpack457[I(0)];
            s_413 = unpack457[I(1)];
            if (Truth(B(Equal(s_413[S("kind")], S("nextSentence")))))
            {
                boundary_390 = Next(Sequence(from item458 in Items(all_statements_388[Slice(Add(index_400, I(1)), Nil, Nil)]) let b_459 = item458 where Truth(B(Equal(b_459[S("kind")], S("sentenceBoundary")))) select b_459), Nil);
                if (Truth(B(ReferenceEquals(boundary_390, Nil))))
                {
                    throw new ParseException(Text(S("NEXT SENTENCE requires following separator period")));
                }

                s_413[S("sentenceId")] = boundary_390[S("id")];
            }
        }

        _ = M_resolve_stub_targets();
        tokens_421 = ListOf();
        foreach (var item460 in Items(Enumerate(Member(this, "ts"))))
        {
            var unpack461 = item460;
            token_index_420 = unpack461[I(0)];
            t_418 = unpack461[I(1)];
            owner_407 = Call(Member(this, "owners"), "get", token_index_420);
            if (Truth(B(ReferenceEquals(owner_407, Nil))))
            {
                if (Truth(Or(B(Compare(token_index_420, Member(this, "procedure_start")) < 0), () => B(Equal(Member(t_418, "text"), S("."))))))
                {
                    owner_407 = ListOf(S("declaration"), S("$structure"));
                }
                else
                {
                    owner_407 = ListOf(S("unknown"), S("$unconsumed"));
                    _ = M_diag(S("UNCONSUMED_TOKEN"), Member(t_418, "text"), Member(t_418, "source"));
                }
            }

            _ = Call(tokens_421, "append", Put(Put(Put(Put(Dict(), S("text"), Member(t_418, "text")), S("ownerKind"), owner_407[I(0)]), S("ownerId"), owner_407[I(1)]), S("source"), Member(t_418, "source")));
        }

        foreach (var item462 in Items(Member(this, "symbols")))
        {
            item_401 = item462;
            if (Truth(And(B(ReferenceEquals(item_401[S("pic")], Nil)), () => And(item_401[S("initial")], () => B(Equal(Call(item_401[S("initial")], "get", S("valueType")), S("numeric")))))))
            {
                _ = M_diag(S("GROUP_NUMERIC_VALUE"), S("Group VALUE cannot be numeric"), item_401[S("source")]);
            }

            if (Truth(item_401[S("redefines")]))
            {
                target_419 = Next(Sequence(from item463 in Items(Member(this, "symbols")) let s_464 = item463 where Truth(B(Equal(s_464[S("id")], item_401[S("redefines")]))) select s_464));
                if (Truth(Or(B(!Equal(target_419[S("level")], item_401[S("level")])), () => B(!Equal(target_419[S("parentId")], item_401[S("parentId")])))))
                {
                    _ = M_diag(S("REDEFINES_RELATION"), S("REDEFINES requires same-level preceding sibling"), item_401[S("source")]);
                }

                if (Truth(Or(B(!Equal(target_419[S("occurs")], I(1))), () => B(!Equal(item_401[S("occurs")], I(1))))))
                {
                    _ = M_diag(S("REDEFINES_OCCURS"), S("REDEFINES of OCCURS items unsupported"), item_401[S("source")]);
                }
            }
        }

        var diagnosticStatements = new StatementSourceIndex(all_statements_388);
        foreach (var item465 in Items(Member(this, "ds")))
        {
            diagnostic_393 = item465;
            diagnostic_393[S("programId")] = program_id_411;
            location_402 = Or(Call(diagnostic_393, "get", S("source")), () => Dict());
            closest_391 = diagnosticStatements.Find(location_402);
            if (Truth(closest_391))
            {
                diagnostic_393[S("statementId")] = closest_391[S("id")];
            }
        }

        return Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Dict(), S("id"), program_id_411), S("documentation"), documentation_395), S("programDeclaration"), program_declaration_410), S("conditionalDirectives"), ListOf(from item473 in Items(Member(Member(this, "f"), "conditionals")) let c_474 = item473 where Truth(B(Contains(SetOf(from item475 in Items(Member(this, "ts")) let t_476 = item475 select Member(t_476, "source")[S("file")]), c_474[S("source")][S("file")]))) select c_474)), S("symbols"), Member(this, "symbols")), S("tokens"), tokens_421), S("paragraphs"), Member(this, "paragraphs")), S("dependencies"), Member(this, "dependencies")), S("using"), using_423), S("diagnostics"), Member(this, "ds"));
    }

    /// <summary>
    /// ELSE/END-IF等の指定終端かピリオドまで、命令のブロックを読む。
    /// 命令が解析できない場合は原文付きunknownを作り、次の既知命令から復旧する。
    /// 回復しても未知処理を正常なCONTINUEへ変換したことにはしない。
    /// </summary>
    /// <param name="stops_">stops の解析値。</param>
    /// <param name="top_">top の解析値。</param>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_block(V stops_, V? top_ = null)
    {
        top_ ??= B(false);
        V a_477 = Nil;
        V block_verb_478 = Nil;
        V body_479 = Nil;
        V depth_480 = Nil;
        V e_481 = Nil;
        V node_482 = Nil;
        V sid_483 = Nil;
        V src_484 = Nil;
        V token_485 = Nil;
        V token_index_486 = Nil;
        V word_487 = Nil;
        body_479 = ListOf();
        while (Truth(And(M_peek(), () => And(B(!Contains(stops_, M_peek())), () => B(!Equal(M_peek(), S(".")))))))
        {
            if (Truth(And(top_, () => Or(And(B(Contains(ListOf(S("."), S("SECTION")), M_peek(I(1)))), () => B(!Contains(Verbs, M_peek()))), () => And(B(Equal(M_peek(), S("END"))), () => B(Equal(M_peek(I(1)), S("PROGRAM"))))))))
            {
                break;
            }

            a_477 = Member(this, "i");
            sid_483 = AddText(S("s"), Format(Member(this, "sid"), "06d"));
            SetMember(this, "sid", Add(Member(this, "sid"), I(1)));
            try
            {
                node_482 = M_statement();
            }
            catch (Exception error488) when (error488 is ParseException || error488 is ArgumentException)
            {
                e_481 = S(error488.Message);
                block_verb_478 = Member(Member(this, "ts")[a_477], "upper");
                if (Truth(B(Contains(SetOf(S("IF"), S("PERFORM"), S("EVALUATE"), S("SEARCH"), S("STRING"), S("UNSTRING"), S("INSPECT"), S("CALL"), S("EXEC"), S("READ"), S("WRITE"), S("REWRITE"), S("DELETE"), S("START"), S("ADD"), S("SUBTRACT"), S("MULTIPLY"), S("DIVIDE"), S("COMPUTE")), block_verb_478))))
                {
                    SetMember(this, "i", Add(a_477, I(1)));
                    depth_480 = I(1);
                    while (Truth(And(M_peek(), () => B(!Equal(M_peek(), S("."))))))
                    {
                        word_487 = Member(M_take(), "upper");
                        if (Truth(B(Equal(word_487, block_verb_478))))
                        {
                            depth_480 = Add(depth_480, I(1));
                        }
                        else
                        {
                            if (Truth(B(Equal(word_487, Add(S("END-"), block_verb_478)))))
                            {
                                depth_480 = Subtract(depth_480, I(1));
                                if (Truth(B(Equal(depth_480, I(0)))))
                                {
                                    break;
                                }
                            }
                        }
                    }
                }
                else
                {
                    if (Truth(B(Equal(Member(this, "i"), a_477))))
                    {
                        _ = M_take();
                    }

                    while (Truth(And(M_peek(), () => B(!Contains(Union(Union(Verbs, stops_), SetOf(S("."))), M_peek())))))
                    {
                        _ = M_take();
                    }
                }

                src_484 = M_source(a_477);
                _ = M_diag(S("UNSUPPORTED_STATEMENT"), StringValue(e_481), src_484);
                node_482 = Put(Put(Dict(), S("kind"), S("unknown")), S("reason"), StringValue(e_481));
            }

            foreach (var item489 in Items(Range(a_477, Member(this, "i"))))
            {
                token_index_486 = item489;
                if (Truth(Or(B(Equal(node_482[S("kind")], S("unknown"))), () => B(!Contains(Member(this, "owners"), token_index_486)))))
                {
                    Member(this, "owners")[token_index_486] = ListOf(Truth(B(Equal(node_482[S("kind")], S("unknown")))) ? S("unknown") : (Truth(B(Equal(node_482[S("kind")], S("controlStub")))) ? S("stubbed") : S("statement")), sid_483);
                }
            }

            _ = Merge(node_482, Put(Put(Dict(), S("id"), sid_483), S("source"), M_source(a_477)));
            _ = Call(body_479, "append", node_482);
            if (Truth(And(top_, () => B(Equal(M_peek(), S("."))))))
            {
                token_485 = M_take();
                _ = Call(body_479, "append", Put(Put(Put(Dict(), S("kind"), S("sentenceBoundary")), S("id"), AddText(S("s"), Format(Member(this, "sid"), "06d"))), S("source"), Member(token_485, "source")));
                SetMember(this, "sid", Add(Member(this, "sid"), I(1)));
                break;
            }
        }

        return body_479;
    }

    /// <summary>
    /// END-IFなどの明示終端を確認する。ピリオドは文全体のscopeを閉じる別の終端として認める。
    /// </summary>
    /// <param name="word_">word の解析値。</param>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_end(V word_)
    {
        if (Truth(And(B(!Truth(M_accept(word_))), () => B(!Equal(M_peek(), S("."))))))
        {
            throw new ParseException(Text(Add(S("Missing "), word_)));
        }

        return Nil;
    }
}
