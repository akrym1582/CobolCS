using static CobolCS.Frontend.Parsing.Values;

namespace CobolCS.Frontend.Parsing;

/// <summary>位置とIRを保持する statements の解析処理です。</summary>
internal sealed partial class Parser : V
{
    /// <summary>
    /// 1命令を型付きIRへ変換する。各verbの分岐を見れば、対応する構文と拒否する句が分かる。
    /// MOVEやIFを実行する場所ではない。ここではoperandと子ブロックを構造化する。
    /// </summary>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_statement()
    {
        V a_568 = Nil;
        V after_569 = Nil;
        V all_delimiter_570 = Nil;
        V all_search_571 = Nil;
        V at_end_572 = Nil;
        V body_573 = Nil;
        V by_574 = Nil;
        V cases_575 = Nil;
        V categories_576 = Nil;
        V category_577 = Nil;
        V clause_578 = Nil;
        V compound_579 = Nil;
        V cond_580 = Nil;
        V condition_581 = Nil;
        V count_582 = Nil;
        V d_583 = Nil;
        V delimiter_584 = Nil;
        V delimiters_585 = Nil;
        V end_586 = Nil;
        V frm_587 = Nil;
        V has_588 = Nil;
        V inner_589 = Nil;
        V inputs_590 = Nil;
        V mode_591 = Nil;
        V modes_592 = Nil;
        V name_593 = Nil;
        V nested_594 = Nil;
        V no_595 = Nil;
        V not_on_596 = Nil;
        V on_597 = Nil;
        V operation_598 = Nil;
        V operations_599 = Nil;
        V other_600 = Nil;
        V pattern_601 = Nil;
        V pending_602 = Nil;
        V pointer_603 = Nil;
        V receivers_604 = Nil;
        V ref_605 = Nil;
        V reference_606 = Nil;
        V replacement_607 = Nil;
        V replacing_608 = Nil;
        V returning_609 = Nil;
        V selection_610 = Nil;
        V sendings_611 = Nil;
        V source_612 = Nil;
        V start_613 = Nil;
        V step_614 = Nil;
        V subject_615 = Nil;
        V table_616 = Nil;
        V tallying_617 = Nil;
        V target_618 = Nil;
        V targets_619 = Nil;
        V times_620 = Nil;
        V token_621 = Nil;
        V until_622 = Nil;
        V value_623 = Nil;
        V values_624 = Nil;
        V varying_625 = Nil;
        V varying_after_626 = Nil;
        V verb_627 = Nil;
        V with_filler_628 = Nil;
        V word_629 = Nil;
        V yes_630 = Nil;
        verb_627 = Member(M_take(), "upper");
        if (Truth(B(Equal(verb_627, S("CONDITIONAL-STUB")))))
        {
            return Put(Put(Put(Dict(), S("kind"), S("unknown")), S("reason"), S("P64 is unset; implement the conditional block or set p64 explicitly")), S("conditional"), Member(Member(this, "ts")[Subtract(Member(this, "i"), I(1))], "source")[S("conditional")]);
        }

        if (Truth(B(Equal(verb_627, S("COPY-STUB")))))
        {
            name_593 = Member(M_take(), "text");
            throw new ParseException(Text(Add(Add(S("Missing procedure COPY "), name_593), S("; behavior must be implemented explicitly"))));
        }

        if (Truth(B(Equal(verb_627, S("MOVE")))))
        {
            if (Truth(B(Contains(ListOf(S("CORRESPONDING"), S("CORR")), M_peek()))))
            {
                _ = M_take();
                source_612 = M_reference();
                _ = M_require(S("TO"));
                target_618 = M_reference();
                return Put(Put(Put(Put(Put(Put(Put(Dict(), S("kind"), S("corresponding")), S("verb"), S("MOVE")), S("value"), source_612), S("target"), target_618), S("onError"), ListOf()), S("notError"), ListOf()), S("hasSizeError"), B(false));
            }

            value_623 = M_expr();
            _ = M_require(S("TO"));
            targets_619 = ListOf();
            while (Truth(And(M_peek(), () => B(!Contains(Union(Union(Verbs, Ends), SetOf(S("."))), M_peek())))))
            {
                _ = Call(targets_619, "append", M_reference());
            }

            if (Truth(B(!Truth(targets_619))))
            {
                throw new ParseException(Text(S("MOVE requires target")));
            }

            return Put(Put(Put(Dict(), S("kind"), S("move")), S("value"), value_623), S("targets"), targets_619);
        }

        if (Truth(B(Equal(verb_627, S("INITIALIZE")))))
        {
            targets_619 = ListOf();
            while (Truth(And(M_peek(), () => B(!Contains(Union(Union(Verbs, Ends), SetOf(S("WITH"), S("REPLACING"), S("."))), M_peek())))))
            {
                _ = Call(targets_619, "append", M_reference());
            }

            with_filler_628 = B(false);
            if (Truth(M_accept(S("WITH"))))
            {
                _ = M_require(S("FILLER"));
                with_filler_628 = B(true);
            }

            replacing_608 = ListOf();
            if (Truth(M_accept(S("REPLACING"))))
            {
                categories_576 = Put(Put(Put(Put(Put(Dict(), S("NUMERIC"), S("numeric")), S("ALPHANUMERIC"), S("text")), S("ALPHABETIC"), S("alphabetic")), S("NATIONAL"), S("national")), S("NUMERIC-EDITED"), S("numeric-edited"));
                while (Truth(B(Contains(categories_576, M_peek()))))
                {
                    category_577 = categories_576[Member(M_take(), "upper")];
                    _ = M_accept(S("DATA"));
                    _ = M_require(S("BY"));
                    _ = Call(replacing_608, "append", Put(Put(Dict(), S("category"), category_577), S("value"), M_expr(I(6))));
                }
            }

            if (Truth(B(!Truth(targets_619))))
            {
                throw new ParseException(Text(S("INITIALIZE requires targets")));
            }

            return Put(Put(Put(Put(Dict(), S("kind"), S("initialize")), S("targets"), targets_619), S("withFiller"), with_filler_628), S("replacing"), replacing_608);
        }

        if (Truth(B(Equal(verb_627, S("STRING")))))
        {
            sendings_611 = ListOf();
            pending_602 = ListOf();
            while (Truth(B(!Contains(ListOf(S("INTO"), S(string.Empty)), M_peek()))))
            {
                _ = Call(pending_602, "append", M_expr(I(6)));
                if (Truth(M_accept(S("DELIMITED"))))
                {
                    _ = M_require(S("BY"));
                    delimiter_584 = Truth(M_accept(S("SIZE"))) ? Nil : M_expr(I(6));
                    _ = Call(sendings_611, "extend", Sequence(from item631 in Items(pending_602) let value_632 = item631 select Put(Put(Dict(), S("value"), value_632), S("delimiter"), delimiter_584)));
                    pending_602 = ListOf();
                }
            }

            if (Truth(Or(pending_602, () => B(!Truth(sendings_611)))))
            {
                throw new ParseException(Text(S("STRING requires DELIMITED BY for each group")));
            }

            _ = M_require(S("INTO"));
            target_618 = M_reference();
            pointer_603 = Nil;
            if (Truth(Or(M_accept(S("WITH")), () => B(Equal(M_peek(), S("POINTER"))))))
            {
                _ = M_require(S("POINTER"));
                pointer_603 = M_reference();
            }

            var unpack633 = M_overflow_clauses(S("END-STRING"));
            on_597 = unpack633[I(0)];
            not_on_596 = unpack633[I(1)];
            has_588 = unpack633[I(2)];
            return Put(Put(Put(Put(Put(Put(Put(Dict(), S("kind"), S("stringTransfer")), S("sendings"), sendings_611), S("target"), target_618), S("pointer"), pointer_603), S("onOverflow"), on_597), S("notOverflow"), not_on_596), S("hasOverflow"), has_588);
        }

        if (Truth(B(Equal(verb_627, S("UNSTRING")))))
        {
            value_623 = M_reference();
            _ = M_require(S("DELIMITED"));
            _ = M_require(S("BY"));
            delimiters_585 = ListOf();
            while (Truth(B(true)))
            {
                all_delimiter_570 = M_accept(S("ALL"));
                _ = Call(delimiters_585, "append", Put(Put(Dict(), S("value"), M_expr(I(6))), S("all"), all_delimiter_570));
                if (Truth(B(!Truth(M_accept(S("OR"))))))
                {
                    break;
                }
            }

            _ = M_require(S("INTO"));
            receivers_604 = ListOf();
            while (Truth(And(M_peek(), () => B(!Contains(Union(Union(Verbs, Ends), SetOf(S("."), S("WITH"), S("POINTER"), S("TALLYING"))), M_peek())))))
            {
                target_618 = M_reference();
                var assign634 = Nil;
                delimiter_584 = assign634;
                count_582 = assign634;
                if (Truth(M_accept(S("DELIMITER"))))
                {
                    _ = M_require(S("IN"));
                    delimiter_584 = M_reference();
                }

                if (Truth(M_accept(S("COUNT"))))
                {
                    _ = M_require(S("IN"));
                    count_582 = M_reference();
                }

                _ = Call(receivers_604, "append", Put(Put(Put(Dict(), S("target"), target_618), S("delimiterIn"), delimiter_584), S("countIn"), count_582));
                _ = M_accept(S(","));
            }

            if (Truth(B(!Truth(receivers_604))))
            {
                throw new ParseException(Text(S("UNSTRING requires receivers")));
            }

            var assign635 = Nil;
            pointer_603 = assign635;
            tallying_617 = assign635;
            if (Truth(Or(M_accept(S("WITH")), () => B(Equal(M_peek(), S("POINTER"))))))
            {
                _ = M_require(S("POINTER"));
                pointer_603 = M_reference();
            }

            if (Truth(M_accept(S("TALLYING"))))
            {
                _ = M_require(S("IN"));
                tallying_617 = M_reference();
            }

            var unpack636 = M_overflow_clauses(S("END-UNSTRING"));
            on_597 = unpack636[I(0)];
            not_on_596 = unpack636[I(1)];
            has_588 = unpack636[I(2)];
            return Put(Put(Put(Put(Put(Put(Put(Put(Put(Dict(), S("kind"), S("unstringTransfer")), S("value"), value_623), S("delimiters"), delimiters_585), S("receivers"), receivers_604), S("pointer"), pointer_603), S("tallying"), tallying_617), S("onOverflow"), on_597), S("notOverflow"), not_on_596), S("hasOverflow"), has_588);
        }

        if (Truth(B(Equal(verb_627, S("INSPECT")))))
        {
            value_623 = M_reference();
            operations_599 = ListOf();
            V Fn_bounds()
            {
                V after_637 = Nil;
                V before_638 = Nil;
                V bound_639 = Nil;
                V key_640 = Nil;
                var assign641 = Nil;
                before_638 = assign641;
                after_637 = assign641;
                while (Truth(B(Contains(ListOf(S("BEFORE"), S("AFTER")), M_peek()))))
                {
                    key_640 = Member(M_take(), "upper");
                    _ = M_accept(S("INITIAL"));
                    bound_639 = M_expr(I(6));
                    if (Truth(B(Equal(key_640, S("BEFORE")))))
                    {
                        before_638 = bound_639;
                    }
                    else
                    {
                        after_637 = bound_639;
                    }
                }

                return Put(Put(Dict(), S("before"), before_638), S("after"), after_637);
            }

            while (Truth(B(Contains(ListOf(S("TALLYING"), S("REPLACING"), S("CONVERTING")), M_peek()))))
            {
                clause_578 = Member(M_take(), "upper");
                if (Truth(B(Equal(clause_578, S("CONVERTING")))))
                {
                    pattern_601 = M_expr(I(6));
                    _ = M_require(S("TO"));
                    replacement_607 = M_expr(I(6));
                    _ = Call(operations_599, "append", Merge(Put(Put(Put(Put(Put(Dict(), S("verb"), S("convert")), S("mode"), S("all")), S("target"), Nil), S("pattern"), pattern_601), S("replacement"), replacement_607), Fn_bounds()));
                }
                else
                {
                    target_618 = Nil;
                    while (Truth(And(M_peek(), () => B(!Contains(Union(Union(Verbs, Ends), SetOf(S("."), S("TALLYING"), S("REPLACING"), S("CONVERTING"))), M_peek())))))
                    {
                        if (Truth(And(B(Equal(clause_578, S("TALLYING"))), () => B(!Contains(ListOf(S("CHARACTERS"), S("ALL"), S("LEADING")), M_peek())))))
                        {
                            target_618 = M_reference();
                            _ = M_require(S("FOR"));
                        }

                        mode_591 = Member(M_take(), "upper");
                        if (Truth(Or(B(!Contains(ListOf(S("CHARACTERS"), S("ALL"), S("LEADING"), S("FIRST")), mode_591)), () => And(B(Equal(clause_578, S("TALLYING"))), () => B(Equal(mode_591, S("FIRST")))))))
                        {
                            throw new ParseException(Text(S("Invalid INSPECT mode")));
                        }

                        pattern_601 = Truth(B(Equal(mode_591, S("CHARACTERS")))) ? Nil : M_expr(I(6));
                        replacement_607 = Nil;
                        if (Truth(B(Equal(clause_578, S("REPLACING")))))
                        {
                            _ = M_require(S("BY"));
                            replacement_607 = M_expr(I(6));
                        }

                        if (Truth(And(B(Equal(clause_578, S("TALLYING"))), () => B(ReferenceEquals(target_618, Nil)))))
                        {
                            throw new ParseException(Text(S("INSPECT requires tally target")));
                        }

                        _ = Call(operations_599, "append", Merge(Put(Put(Put(Put(Put(Dict(), S("verb"), Truth(B(Equal(clause_578, S("TALLYING")))) ? S("tally") : S("replace")), S("mode"), Call(mode_591, "lower")), S("target"), target_618), S("pattern"), pattern_601), S("replacement"), replacement_607), Fn_bounds()));
                        if (Truth(B(Contains(Union(Union(Verbs, Ends), SetOf(S("."), S("TALLYING"), S("REPLACING"), S("CONVERTING"))), M_peek()))))
                        {
                            break;
                        }
                    }
                }
            }

            if (Truth(B(!Truth(operations_599))))
            {
                throw new ParseException(Text(S("INSPECT requires operations")));
            }

            return Put(Put(Put(Dict(), S("kind"), S("inspectOperations")), S("value"), value_623), S("operations"), operations_599);
        }

        if (Truth(B(Equal(verb_627, S("ACCEPT")))))
        {
            target_618 = M_reference();
            source_612 = Truth(M_accept(S("FROM"))) ? Member(M_take(), "upper") : S("CONSOLE");
            _ = M_accept(S("END-ACCEPT"));
            return Put(Put(Put(Dict(), S("kind"), S("acceptInput")), S("target"), target_618), S("inputSource"), source_612);
        }

        if (Truth(B(Equal(verb_627, S("DISPLAY")))))
        {
            var display = Put(Put(Dict(), S("kind"), S("display")), S("values"), M_expressions(ListOf(S("WITH"), S("NO"), S("UPON"), S("END-DISPLAY"))));
            var noAdvancing = false;
            var upon = false;
            while (Text(M_peek()) is "WITH" or "NO" or "UPON")
            {
                if (Truth(M_accept(S("UPON"))))
                {
                    if (upon || !Truth(M_peek()) || Contains(Verbs, M_peek()) || Contains(Ends, M_peek()) || Text(M_peek()) is "." or "WITH" or "NO")
                    {
                        throw new ParseException("DISPLAY UPON requires one destination");
                    }

                    _ = Put(display, S("upon"), Member(M_take(), "text"));
                    upon = true;
                }
                else
                {
                    if (noAdvancing)
                    {
                        throw new ParseException("Duplicate DISPLAY NO ADVANCING");
                    }

                    _ = M_accept(S("WITH"));
                    _ = M_require(S("NO"));
                    _ = M_require(S("ADVANCING"));
                    _ = Put(display, S("noAdvancing"), B(true));
                    noAdvancing = true;
                }
            }

            _ = M_accept(S("END-DISPLAY"));
            return display;
        }

        if (Truth(B(Equal(verb_627, S("CONTINUE")))))
        {
            return Put(Dict(), S("kind"), S("continue"));
        }

        if (Truth(B(Equal(verb_627, S("SET")))))
        {
            targets_619 = ListOf();
            while (Truth(B(!Contains(ListOf(S("TO"), S("UP"), S("DOWN"), S(string.Empty), S(".")), M_peek()))))
            {
                _ = Call(targets_619, "append", M_expr(I(6)));
            }

            if (Truth(B(!Truth(targets_619))))
            {
                throw new ParseException(Text(S("SET requires target")));
            }

            if (Truth(M_accept(S("TO"))))
            {
                if (Truth(M_accept(S("TRUE"))))
                {
                    if (Truth(Or(B(!Equal(Length(targets_619), I(1))), () => B(!Equal(targets_619[I(0)][S("kind")], S("condition88"))))))
                    {
                        throw new ParseException(Text(S("SET TRUE requires condition-name")));
                    }

                    return Put(Put(Put(Dict(), S("kind"), S("set88")), S("symbolId"), targets_619[I(0)][S("symbolId")]), S("subscripts"), targets_619[I(0)][S("subscripts")]);
                }

                value_623 = M_expr(I(6));
                return Put(Put(Put(Put(Dict(), S("kind"), S("setIndex")), S("targets"), targets_619), S("operation"), S("to")), S("value"), value_623);
            }

            operation_598 = Truth(M_accept(S("UP"))) ? S("up") : (Truth(M_accept(S("DOWN"))) ? S("down") : Nil);
            if (Truth(B(!Truth(operation_598))))
            {
                throw new ParseException(Text(S("Invalid SET operation")));
            }

            _ = M_require(S("BY"));
            return Put(Put(Put(Put(Dict(), S("kind"), S("setIndex")), S("targets"), targets_619), S("operation"), operation_598), S("value"), M_expr(I(6)));
        }

        if (Truth(B(Equal(verb_627, S("SEARCH")))))
        {
            all_search_571 = M_accept(S("ALL"));
            table_616 = M_reference();
            if (Truth(Or(table_616[S("subscripts")], () => table_616[S("modification")])))
            {
                throw new ParseException(Text(S("SEARCH requires whole table name")));
            }

            varying_625 = Truth(M_accept(S("VARYING"))) ? M_reference() : Nil;
            at_end_572 = ListOf();
            if (Truth(M_accept(S("AT"))))
            {
                _ = M_require(S("END"));
                at_end_572 = M_block(SetOf(S("WHEN"), S("END-SEARCH")));
            }

            cases_575 = ListOf();
            while (Truth(M_accept(S("WHEN"))))
            {
                condition_581 = M_expr();
                body_573 = M_block(SetOf(S("WHEN"), S("END-SEARCH")));
                _ = Call(cases_575, "append", Put(Put(Dict(), S("condition"), condition_581), S("body"), body_573));
            }

            _ = M_end(S("END-SEARCH"));
            if (Truth(B(!Truth(cases_575))))
            {
                throw new ParseException(Text(S("SEARCH requires WHEN")));
            }

            return Put(Put(Put(Put(Put(Put(Dict(), S("kind"), S("search")), S("tableId"), table_616[S("symbolId")]), S("all"), all_search_571), S("varying"), varying_625), S("atEnd"), at_end_572), S("cases"), cases_575);
        }

        if (Truth(B(Equal(verb_627, S("IF")))))
        {
            cond_580 = M_expr();
            _ = M_accept(S("THEN"));
            yes_630 = M_block(SetOf(S("ELSE"), S("END-IF")));
            no_595 = ListOf();
            if (Truth(M_accept(S("ELSE"))))
            {
                no_595 = M_block(SetOf(S("END-IF")));
            }

            _ = M_end(S("END-IF"));
            return Merge(Put(Put(Put(Dict(), S("kind"), S("if")), S("condition"), cond_580), S("then"), yes_630), Put(Dict(), S("else"), no_595));
        }

        if (Truth(B(Equal(verb_627, S("EVALUATE")))))
        {
            compound_579 = B(false);
            selection_610 = B(true);
            nested_594 = I(0);
            foreach (var item642 in Items(Member(this, "ts")[Slice(Member(this, "i"), Nil, Nil)]))
            {
                token_621 = item642;
                word_629 = Member(token_621, "upper");
                if (Truth(B(Equal(word_629, S(".")))))
                {
                    break;
                }

                if (Truth(B(Equal(word_629, S("END-EVALUATE")))))
                {
                    if (Truth(B(Equal(nested_594, I(0)))))
                    {
                        break;
                    }

                    nested_594 = Subtract(nested_594, I(1));
                    continue;
                }

                if (Truth(B(Equal(word_629, S("EVALUATE")))))
                {
                    nested_594 = Add(nested_594, I(1));
                    selection_610 = B(false);
                }

                if (Truth(nested_594))
                {
                    continue;
                }

                if (Truth(B(Equal(word_629, S("WHEN")))))
                {
                    selection_610 = B(true);
                }
                else
                {
                    if (Truth(B(Contains(Verbs, word_629))))
                    {
                        selection_610 = B(false);
                    }
                }

                if (Truth(And(selection_610, () => B(Contains(ListOf(S("ALSO"), S("ANY"), S("THRU"), S("THROUGH")), word_629)))))
                {
                    compound_579 = B(true);
                    break;
                }
            }

            if (Truth(Or(B(Equal(Member(Member(this, "f"), "config")[S("controlFlowMode")], S("stub-control"))), () => compound_579)))
            {
                return M_evaluate_stub();
            }
        }

        if (Truth(B(Equal(verb_627, S("EVALUATE")))))
        {
            subject_615 = M_expr();
            cases_575 = ListOf();
            other_600 = ListOf();
            while (Truth(M_accept(S("WHEN"))))
            {
                if (Truth(M_accept(S("OTHER"))))
                {
                    other_600 = M_block(SetOf(S("WHEN"), S("END-EVALUATE")));
                    if (Truth(B(Equal(M_peek(), S("WHEN")))))
                    {
                        throw new ParseException(Text(S("WHEN OTHER must be last")));
                    }
                }
                else
                {
                    values_624 = ListOf(M_expr());
                    while (Truth(M_accept(S("WHEN"))))
                    {
                        _ = Call(values_624, "append", M_expr());
                    }

                    _ = Call(cases_575, "append", Put(Put(Dict(), S("values"), values_624), S("body"), M_block(SetOf(S("WHEN"), S("END-EVALUATE")))));
                }
            }

            _ = M_end(S("END-EVALUATE"));
            return Put(Put(Put(Put(Dict(), S("kind"), S("evaluate")), S("subject"), subject_615), S("cases"), cases_575), S("other"), other_600);
        }

        if (Truth(And(B(Equal(verb_627, S("PERFORM"))), () => And(B(Equal(Member(Member(this, "f"), "config")[S("controlFlowMode")], S("stub-control"))), () => B(!ReferenceEquals(M_out_of_line(Member(this, "i")), Nil))))))
        {
            return M_perform_stub();
        }

        if (Truth(B(Equal(verb_627, S("PERFORM")))))
        {
            after_569 = B(false);
            times_620 = Nil;
            until_622 = Nil;
            varying_625 = Nil;
            varying_after_626 = ListOf();
            if (Truth(M_accept(S("WITH"))))
            {
                _ = M_require(S("TEST"));
                after_569 = M_accept(S("AFTER"));
                if (Truth(B(!Truth(after_569))))
                {
                    _ = M_require(S("BEFORE"));
                }
            }

            if (Truth(M_accept(S("VARYING"))))
            {
                reference_606 = M_reference();
                _ = M_require(S("FROM"));
                start_613 = M_expr();
                _ = M_require(S("BY"));
                by_574 = M_expr();
                _ = M_require(S("UNTIL"));
                until_622 = M_expr();
                varying_625 = Merge(Put(Put(Dict(), S("reference"), reference_606), S("by"), by_574), Put(Dict(), S("from"), start_613));
                while (Truth(M_accept(S("AFTER"))))
                {
                    ref_605 = M_reference();
                    _ = M_require(S("FROM"));
                    frm_587 = M_expr();
                    _ = M_require(S("BY"));
                    step_614 = M_expr();
                    _ = M_require(S("UNTIL"));
                    condition_581 = M_expr();
                    _ = Call(varying_after_626, "append", Merge(Put(Put(Put(Dict(), S("reference"), ref_605), S("by"), step_614), S("until"), condition_581), Put(Dict(), S("from"), frm_587)));
                }
            }
            else
            {
                if (Truth(M_accept(S("UNTIL"))))
                {
                    until_622 = M_expr();
                }
                else
                {
                    if (Truth(And(B(!Contains(Verbs, M_peek())), () => B(!Contains(ListOf(S("."), S("END-PERFORM")), M_peek())))))
                    {
                        if (Truth(Or(RegexFullmatch(S("\\d+(?:\\.\\d+)?"), M_peek()), () => Or(B(Contains(ListOf(S("-"), S("+")), M_peek())), () => Or(B(Equal(M_peek(I(1)), S("TIMES"))), () => B(Equal(M_peek(I(1)), S("("))))))))
                        {
                            times_620 = M_expr();
                            _ = M_require(S("TIMES"));
                        }
                        else
                        {
                            start_613 = Member(M_take(), "text");
                            end_586 = start_613;
                            if (Truth(B(Contains(ListOf(S("THRU"), S("THROUGH")), M_peek()))))
                            {
                                _ = M_take();
                                end_586 = Member(M_take(), "text");
                            }

                            if (Truth(B(!Contains(Union(Union(Verbs, Ends), SetOf(S("."), S(string.Empty))), M_peek()))))
                            {
                                times_620 = M_expr(I(6));
                                _ = M_require(S("TIMES"));
                                inner_589 = Merge(Put(Put(Put(Dict(), S("kind"), S("perform")), S("id"), AddText(S("s"), Format(Member(this, "sid"), "06d"))), S("source"), M_source(Subtract(Member(this, "i"), I(1)))), Put(Put(Dict(), S("from"), start_613), S("thru"), end_586));
                                SetMember(this, "sid", Add(Member(this, "sid"), I(1)));
                                return Put(Put(Put(Put(Put(Put(Dict(), S("kind"), S("loop")), S("body"), ListOf(inner_589)), S("times"), times_620), S("until"), Nil), S("varying"), Nil), S("testAfter"), B(false));
                            }

                            return Merge(Put(Dict(), S("kind"), S("perform")), Put(Put(Dict(), S("from"), start_613), S("thru"), end_586));
                        }
                    }
                }
            }

            body_573 = M_block(SetOf(S("END-PERFORM")));
            _ = M_end(S("END-PERFORM"));
            if (Truth(And(B(ReferenceEquals(times_620, Nil)), () => B(ReferenceEquals(until_622, Nil)))))
            {
                times_620 = Lit(S("1"));
            }

            return Put(Put(Put(Put(Put(Put(Put(Dict(), S("kind"), S("loop")), S("body"), body_573), S("times"), times_620), S("until"), until_622), S("varying"), varying_625), S("varyingAfter"), varying_after_626), S("testAfter"), after_569);
        }

        if (Truth(B(Equal(verb_627, S("GO")))))
        {
            _ = M_require(S("TO"));
            targets_619 = ListOf(Member(M_take(), "text"));
            while (Truth(And(M_peek(), () => B(!Contains(Union(Union(Verbs, Ends), SetOf(S("DEPENDING"), S("."))), M_peek())))))
            {
                if (Truth(And(B(Equal(M_peek(I(1)), S("."))), () => B(!Equal(Member(Member(this, "ts")[Member(this, "i")], "source")[S("start")][S("line")], Member(Member(this, "ts")[Subtract(Member(this, "i"), I(1))], "source")[S("start")][S("line")])))))
                {
                    break;
                }

                _ = Call(targets_619, "append", Member(M_take(), "text"));
            }

            if (Truth(M_accept(S("DEPENDING"))))
            {
                _ = M_require(S("ON"));
                return Put(Put(Put(Dict(), S("kind"), S("gotoDepending")), S("targets"), targets_619), S("depending"), M_expr());
            }

            if (Truth(B(!Equal(Length(targets_619), I(1)))))
            {
                throw new ParseException(Text(S("GO TO requires one target or DEPENDING")));
            }

            return Put(Put(Dict(), S("kind"), S("goto")), S("target"), targets_619[I(0)]);
        }

        if (Truth(B(Equal(verb_627, S("NEXT")))))
        {
            _ = M_require(S("SENTENCE"));
            return Put(Put(Dict(), S("kind"), S("nextSentence")), S("sentenceId"), S(string.Empty));
        }

        if (Truth(B(Equal(verb_627, S("GOBACK")))))
        {
            return Put(Put(Dict(), S("kind"), S("halt")), S("signal"), S("goback"));
        }

        if (Truth(B(Equal(verb_627, S("STOP")))))
        {
            _ = M_require(S("RUN"));
            return Put(Put(Dict(), S("kind"), S("halt")), S("signal"), S("stopRun"));
        }

        if (Truth(B(Equal(verb_627, S("EXIT")))))
        {
            if (Truth(M_accept(S("PERFORM"))))
            {
                return Put(Dict(), S("kind"), S("exitPerform"));
            }

            if (Truth(M_accept(S("PROGRAM"))))
            {
                return Put(Put(Dict(), S("kind"), S("halt")), S("signal"), S("exitProgram"));
            }

            return Put(Dict(), S("kind"), S("continue"));
        }

        if (Truth(B(Contains(ListOf(S("ADD"), S("SUBTRACT"), S("MULTIPLY"), S("DIVIDE"), S("COMPUTE")), verb_627))))
        {
            return M_arithmetic(verb_627);
        }

        if (Truth(B(Contains(ListOf(S("SORT"), S("MERGE"), S("RELEASE"), S("RETURN")), verb_627))))
        {
            return M_sort_statement(verb_627);
        }

        if (Truth(B(Equal(verb_627, S("CANCEL")))))
        {
            return Put(Put(Dict(), S("kind"), S("cancel")), S("targets"), M_expressions());
        }

        if (Truth(B(Equal(verb_627, S("CALL")))))
        {
            a_568 = Subtract(Member(this, "i"), I(1));
            target_618 = M_expr(I(6));
            inputs_590 = ListOf();
            modes_592 = ListOf();
            mode_591 = S("reference");
            if (Truth(M_accept(S("USING"))))
            {
                while (Truth(And(M_peek(), () => B(!Contains(Union(Union(Verbs, Ends), SetOf(S("."), S("RETURNING"))), M_peek())))))
                {
                    if (Truth(M_accept(S("BY"))))
                    {
                        word_629 = Member(M_take(), "upper");
                        if (Truth(B(!Contains(ListOf(S("REFERENCE"), S("CONTENT"), S("VALUE")), word_629))))
                        {
                            throw new ParseException(Text(S("Invalid CALL mode")));
                        }

                        mode_591 = Call(word_629, "lower");
                    }
                    else
                    {
                        _ = Call(inputs_590, "append", Truth(B(Equal(mode_591, S("reference")))) ? M_reference() : M_expr(I(6)));
                        _ = Call(modes_592, "append", mode_591);
                    }
                }
            }

            returning_609 = Truth(M_accept(S("RETURNING"))) ? M_reference() : Nil;
            on_597 = ListOf();
            not_on_596 = ListOf();
            if (Truth(M_accept(S("ON"))))
            {
                _ = M_require(S("EXCEPTION"));
                on_597 = M_block(SetOf(S("NOT"), S("END-CALL")));
            }

            if (Truth(M_accept(S("NOT"))))
            {
                _ = M_require(S("ON"));
                _ = M_require(S("EXCEPTION"));
                not_on_596 = M_block(SetOf(S("END-CALL")));
            }

            _ = M_accept(S("END-CALL"));
            name_593 = Truth(And(B(Equal(target_618[S("kind")], S("literal"))), () => B(Equal(target_618[S("valueType")], S("text"))))) ? target_618[S("value")] : S("$dynamic");
            d_583 = M_dependency(S("call"), name_593, M_source(a_568)[S("raw")], M_source(a_568), inputs_: inputs_590, modes_: modes_592);
            d_583[S("call")] = Put(Put(Dict(), S("target"), target_618), S("returning"), returning_609);
            return Put(Put(Put(Put(Put(Dict(), S("kind"), S("external")), S("dependencyId"), d_583[S("id")]), S("operation"), S("call")), S("atEnd"), on_597), S("notEnd"), not_on_596);
        }

        if (Truth(B(Equal(verb_627, S("EXEC")))))
        {
            return M_sql_statement();
        }

        if (Truth(B(Contains(ListOf(S("OPEN"), S("READ"), S("WRITE"), S("CLOSE"), S("REWRITE"), S("START"), S("DELETE")), verb_627))))
        {
            return M_file_statement(verb_627);
        }

        throw new ParseException(Text(Add(S("Unsupported verb "), verb_627)));
    }

    /// <summary>
    /// 算術命令の受け側・ROUNDED・GIVING・SIZE ERROR・REMAINDERをIRへまとめる。
    /// 例: ADD 1 TO Aは旧Aへの加算、ADD 1 TO A GIVING Bは結果をBへ格納するという違いを保存する。
    /// 中間値の精度や丸めはruntimeが扱うので、解析時にfloatで答えを計算しない。
    /// </summary>
    /// <param name="verb_">verb の解析値。</param>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_arithmetic(V verb_)
    {
        V base_643 = Nil;
        V divisor_644 = Nil;
        V error_645 = Nil;
        V has_646 = Nil;
        V normal_647 = Nil;
        V receivers_648 = Nil;
        V rem_649 = Nil;
        V stop_650 = Nil;
        V target_651 = Nil;
        V targets_652 = Nil;
        V update_653 = Nil;
        V value_654 = Nil;
        V values_655 = Nil;
        V x_656 = Nil;
        update_653 = B(false);
        rem_649 = Nil;
        targets_652 = ListOf();
        if (Truth(And(B(Contains(ListOf(S("ADD"), S("SUBTRACT")), verb_)), () => B(Contains(ListOf(S("CORRESPONDING"), S("CORR")), M_peek())))))
        {
            _ = M_take();
            value_654 = M_reference();
            _ = M_require(Truth(B(Equal(verb_, S("ADD")))) ? S("TO") : S("FROM"));
            target_651 = M_reference();
            var unpack657 = M_size_error(verb_);
            error_645 = unpack657[I(0)];
            normal_647 = unpack657[I(1)];
            has_646 = unpack657[I(2)];
            return Put(Put(Put(Put(Put(Put(Put(Dict(), S("kind"), S("corresponding")), S("verb"), verb_), S("value"), value_654), S("target"), target_651), S("onError"), error_645), S("notError"), normal_647), S("hasSizeError"), has_646);
        }

        if (Truth(B(Equal(verb_, S("COMPUTE")))))
        {
            while (Truth(And(M_peek(), () => B(!Equal(M_peek(), S("="))))))
            {
                target_651 = M_reference();
                _ = Call(targets_652, "append", Put(Put(Dict(), S("reference"), target_651), S("rounded"), M_accept(S("ROUNDED"))));
            }

            _ = M_require(S("="));
            value_654 = M_expr();
        }
        else
        {
            stop_650 = Put(Put(Put(Put(Dict(), S("ADD"), S("TO")), S("SUBTRACT"), S("FROM")), S("MULTIPLY"), S("BY")), S("DIVIDE"), S("INTO"))[verb_];
            values_655 = M_expressions(SetOf(stop_650, S("BY"), S("GIVING")));
            value_654 = values_655[I(0)];
            if (Truth(B(Contains(ListOf(S("ADD"), S("SUBTRACT")), verb_))))
            {
                foreach (var item658 in Items(values_655[Slice(I(1), Nil, Nil)]))
                {
                    x_656 = item658;
                    value_654 = Binary(S("+"), value_654, x_656);
                }
            }
            else
            {
                if (Truth(B(!Equal(Length(values_655), I(1)))))
                {
                    throw new ParseException(Text(S("Expected one multiplicand/divisor")));
                }
            }

            if (Truth(And(B(Equal(verb_, S("DIVIDE"))), () => M_accept(S("BY")))))
            {
                divisor_644 = M_expr();
                value_654 = Binary(S("/"), value_654, divisor_644);
                _ = M_require(S("GIVING"));
            }
            else
            {
                if (Truth(And(B(Equal(verb_, S("ADD"))), () => M_accept(S("GIVING")))))
                {
                }
                else
                {
                    _ = M_require(stop_650);
                    receivers_648 = ListOf();
                    while (Truth(And(M_peek(), () => B(!Contains(Union(Union(Verbs, Ends), SetOf(S("GIVING"), S("ROUNDED"), S("REMAINDER"), S("."))), M_peek())))))
                    {
                        _ = Call(receivers_648, "append", M_reference());
                        if (Truth(M_accept(S("ROUNDED"))))
                        {
                            receivers_648[Subtract(I(0), I(1))] = Put(Put(Dict(), S("ref"), receivers_648[Subtract(I(0), I(1))]), S("rounded"), B(true));
                        }
                    }

                    if (Truth(B(!Truth(receivers_648))))
                    {
                        throw new ParseException(Text(S("Missing arithmetic receiver")));
                    }

                    if (Truth(M_accept(S("GIVING"))))
                    {
                        if (Truth(B(!Equal(Length(receivers_648), I(1)))))
                        {
                            throw new ParseException(Text(S("GIVING requires single base operand")));
                        }

                        base_643 = receivers_648[I(0)];
                        if (Truth(B(Contains(base_643, S("ref")))))
                        {
                            throw new ParseException(Text(S("ROUNDED belongs after GIVING")));
                        }

                        value_654 = Binary(Put(Put(Put(Put(Dict(), S("ADD"), S("+")), S("SUBTRACT"), S("-")), S("MULTIPLY"), S("*")), S("DIVIDE"), S("/"))[verb_], base_643, value_654);
                    }
                    else
                    {
                        update_653 = B(true);
                        targets_652 = ListOf(from item659 in Items(receivers_648) let x_660 = item659 select Put(Put(Dict(), S("reference"), Call(x_660, "get", S("ref"), x_660)), S("rounded"), Call(x_660, "get", S("rounded"), B(false))));
                    }
                }
            }

            if (Truth(B(!Truth(targets_652))))
            {
                while (Truth(And(M_peek(), () => B(!Contains(Union(Union(Verbs, Ends), SetOf(S("REMAINDER"), S("."))), M_peek())))))
                {
                    target_651 = M_reference();
                    _ = Call(targets_652, "append", Put(Put(Dict(), S("reference"), target_651), S("rounded"), M_accept(S("ROUNDED"))));
                }
            }
        }

        if (Truth(M_accept(S("REMAINDER"))))
        {
            if (Truth(Or(B(!Equal(verb_, S("DIVIDE"))), () => Or(update_653, () => Or(B(!Equal(Length(targets_652), I(1))), () => Or(targets_652[I(0)][S("rounded")], () => Or(B(!Equal(value_654[S("kind")], S("binary"))), () => B(!Equal(value_654[S("op")], S("/"))))))))))
            {
                throw new ParseException(Text(S("Unsupported DIVIDE REMAINDER combination")));
            }

            rem_649 = M_reference();
        }

        var unpack661 = M_size_error(verb_);
        error_645 = unpack661[I(0)];
        normal_647 = unpack661[I(1)];
        has_646 = unpack661[I(2)];
        if (Truth(B(!Truth(targets_652))))
        {
            throw new ParseException(Text(S("No arithmetic targets")));
        }

        return Put(Put(Put(Put(Put(Put(Put(Put(Put(Dict(), S("kind"), S("arithmetic")), S("verb"), verb_), S("value"), value_654), S("update"), update_653), S("targets"), targets_652), S("remainder"), rem_649), S("onError"), error_645), S("notError"), normal_647), S("hasSizeError"), has_646);
    }

    /// <summary>
    /// 元の解析規則とIRの順序を保持します。
    /// </summary>
    /// <param name="verb_">verb の解析値。</param>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_size_error(V verb_)
    {
        V error_662 = Nil;
        V has_663 = Nil;
        V normal_664 = Nil;
        error_662 = ListOf();
        normal_664 = ListOf();
        has_663 = B(false);
        if (Truth(M_accept(S("ON"))))
        {
            _ = M_require(S("SIZE"));
            _ = M_require(S("ERROR"));
            has_663 = B(true);
            error_662 = M_block(SetOf(S("NOT"), Add(S("END-"), verb_)));
        }

        if (Truth(M_accept(S("NOT"))))
        {
            _ = M_require(S("ON"));
            _ = M_require(S("SIZE"));
            _ = M_require(S("ERROR"));
            normal_664 = M_block(SetOf(Add(S("END-"), verb_)));
        }

        _ = M_accept(Add(S("END-"), verb_));
        return ListOf(error_662, normal_664, has_663);
    }

    /// <summary>
    /// 元の解析規則とIRの順序を保持します。
    /// </summary>
    /// <param name="end_">end の解析値。</param>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_overflow_clauses(V end_)
    {
        V has_665 = Nil;
        V not_on_666 = Nil;
        V on_667 = Nil;
        on_667 = ListOf();
        not_on_666 = ListOf();
        has_665 = B(false);
        if (Truth(M_accept(S("ON"))))
        {
            _ = M_require(S("OVERFLOW"));
            has_665 = B(true);
            on_667 = M_block(SetOf(S("NOT"), end_, S("ELSE"), S("END-IF"), S("END-PERFORM"), S("END-EVALUATE"), S("END-SEARCH")));
        }

        if (Truth(M_accept(S("NOT"))))
        {
            _ = M_require(S("ON"));
            _ = M_require(S("OVERFLOW"));
            not_on_666 = M_block(SetOf(end_, S("ELSE"), S("END-IF"), S("END-PERFORM"), S("END-EVALUATE"), S("END-SEARCH")));
        }

        if (Truth(And(B(!Truth(M_accept(end_))), () => B(!Contains(Union(Union(Verbs, Ends), SetOf(S("."), S(string.Empty))), M_peek())))))
        {
            throw new ParseException(Text(Add(S("Missing "), end_)));
        }

        return ListOf(on_667, not_on_666, has_665);
    }
}
