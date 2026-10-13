using static CobolCS.Frontend.Parsing.Values;

namespace CobolCS.Frontend.Parsing;

/// <summary>位置とIRを保持する control の解析処理です。</summary>
internal sealed partial class Parser : V
{
    /// <summary>
    /// 元の解析規則とIRの順序を保持します。
    /// </summary>
    /// <param name="start_">start の解析値。</param>
    /// <param name="end_">end の解析値。</param>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_raw_tokens(V start_, V end_)
    {
        return ListOf(from item490 in Items(Member(this, "ts")[Slice(start_, end_, Nil)]) let t_491 = item490 select Put(Put(Dict(), S("text"), Member(t_491, "text")), S("source"), Member(t_491, "source")));
    }

    /// <summary>
    /// 元の解析規則とIRの順序を保持します。
    /// </summary>
    /// <param name="index_">index の解析値。</param>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_out_of_line(V index_)
    {
        V j_492 = Nil;
        V word_493 = Nil;
        j_492 = index_;
        if (Truth(B(Equal(Member(Member(this, "ts")[j_492], "upper"), S("WITH")))))
        {
            j_492 = Add(j_492, I(3));
        }

        if (Truth(B(Compare(j_492, Length(Member(this, "ts"))) >= 0)))
        {
            return Nil;
        }

        word_493 = Member(Member(this, "ts")[j_492], "upper");
        if (Truth(Or(B(Contains(Union(Verbs, SetOf(S("UNTIL"), S("VARYING"), S("-"), S("+"), S("("), S("."), S("END-PERFORM"))), word_493)), () => RegexFullmatch(S("\\d+(?:\\.\\d+)?"), word_493))))
        {
            return Nil;
        }

        if (Truth(And(B(Compare(Add(j_492, I(1)), Length(Member(this, "ts"))) < 0), () => B(Contains(ListOf(S("TIMES"), S("(")), Member(Member(this, "ts")[Add(j_492, I(1))], "upper"))))))
        {
            return Nil;
        }

        return j_492;
    }

    /// <summary>
    /// 元の解析規則とIRの順序を保持します。
    /// </summary>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_find_stub_targets()
    {
        V end_494 = Nil;
        V i_495 = Nil;
        V j_496 = Nil;
        V labels_497 = Nil;
        V ranges_498 = Nil;
        V start_499 = Nil;
        V targets_500 = Nil;
        targets_500 = SetCopy();
        ranges_498 = ListOf();
        foreach (var item501 in Items(Range(Member(this, "i"), Subtract(Length(Member(this, "ts")), I(1)))))
        {
            i_495 = item501;
            if (Truth(B(!Equal(Member(Member(this, "ts")[i_495], "upper"), S("PERFORM")))))
            {
                continue;
            }

            j_496 = M_out_of_line(Add(i_495, I(1)));
            if (Truth(B(!ReferenceEquals(j_496, Nil))))
            {
                start_499 = Member(Member(this, "ts")[j_496], "upper");
                _ = Call(targets_500, "add", start_499);
                if (Truth(And(B(Compare(Add(j_496, I(2)), Length(Member(this, "ts"))) < 0), () => B(Contains(ListOf(S("THRU"), S("THROUGH")), Member(Member(this, "ts")[Add(j_496, I(1))], "upper"))))))
                {
                    end_494 = Member(Member(this, "ts")[Add(j_496, I(2))], "upper");
                    _ = Call(targets_500, "add", end_494);
                    _ = Call(ranges_498, "append", ListOf(start_499, end_494));
                }
            }
        }

        labels_497 = ListOf(from item502 in Items(Range(Member(this, "i"), Subtract(Length(Member(this, "ts")), I(1)))) let i_503 = item502 where Truth(And(B(Contains(ListOf(S("."), S("SECTION")), Member(Member(this, "ts")[Add(i_503, I(1))], "upper"))), () => And(B(!Contains(Verbs, Member(Member(this, "ts")[i_503], "upper"))), () => B(Equal(Member(Member(this, "ts")[Subtract(i_503, I(1))], "text"), S(".")))))) select Member(Member(this, "ts")[i_503], "upper"));
        foreach (var item504 in Items(ranges_498))
        {
            var unpack505 = item504;
            start_499 = unpack505[I(0)];
            end_494 = unpack505[I(1)];
            if (Truth(And(B(Contains(labels_497, start_499)), () => And(B(Contains(labels_497, end_494)), () => B(Compare(Call(labels_497, "index", start_499), Call(labels_497, "index", end_494)) <= 0)))))
            {
                _ = Call(targets_500, "update", labels_497[Slice(Call(labels_497, "index", start_499), Add(Call(labels_497, "index", end_494), I(1)), Nil)]);
            }
        }

        return targets_500;
    }

    /// <summary>
    /// 元の解析規則とIRの順序を保持します。
    /// </summary>
    /// <param name="kind_">kind の解析値。</param>
    /// <param name="start_">start の解析値。</param>
    /// <param name="subjects_">subjects の解析値。</param>
    /// <param name="cases_">cases の解析値。</param>
    /// <param name="perform_">perform の解析値。</param>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_stub(V kind_, V start_, V? subjects_ = null, V? cases_ = null, V? perform_ = null)
    {
        subjects_ ??= ListOf();
        cases_ ??= ListOf();
        perform_ ??= Nil;
        _ = M_diag(S("CONTROL_STUB"), Add(kind_, S(" requires explicit ControlStubPort implementation; native equivalence unverified")), M_source(start_), S("review"));
        return Put(Put(Put(Put(Put(Put(Put(Put(Dict(), S("kind"), S("controlStub")), S("stubId"), AddText(S("control-"), Format(Member(this, "program_id"), string.Empty), S("-s"), Format(Subtract(Member(this, "sid"), I(1)), "06d"))), S("controlKind"), kind_), S("subjects"), ListCopy(subjects_)), S("cases"), ListCopy(cases_)), S("perform"), perform_), S("tokens"), M_raw_tokens(start_, Member(this, "i"))), S("stateDependency"), S("program-state"));
    }

    /// <summary>
    /// 元の解析規則とIRの順序を保持します。
    /// </summary>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_evaluate_stub()
    {
        V a_506 = Nil;
        V body_507 = Nil;
        V cases_508 = Nil;
        V error_509 = Nil;
        V expected_510 = Nil;
        V expression_511 = Nil;
        V high_512 = Nil;
        V kind_513 = Nil;
        V low_514 = Nil;
        V negated_515 = Nil;
        V nested_516 = Nil;
        V operator_517 = Nil;
        V other_518 = Nil;
        V selections_519 = Nil;
        V start_520 = Nil;
        V subjects_521 = Nil;
        V unresolved_522 = Nil;
        V word_523 = Nil;
        start_520 = Subtract(Member(this, "i"), I(1));
        subjects_521 = ListOf();
        while (Truth(B(true)))
        {
            a_506 = Member(this, "i");
            expression_511 = Nil;
            unresolved_522 = Nil;
            try
            {
                expression_511 = M_expr();
                if (Truth(B(!Contains(ListOf(S("ALSO"), S("WHEN")), M_peek()))))
                {
                    throw new ParseException(Text(S("Unresolved EVALUATE subject")));
                }
            }
            catch (Exception error524) when (error524 is ParseException)
            {
                error_509 = S(error524.Message);
                unresolved_522 = StringValue(error_509);
                expression_511 = Nil;
                while (Truth(And(M_peek(), () => B(!Contains(ListOf(S("ALSO"), S("WHEN"), S("END-EVALUATE"), S(".")), M_peek())))))
                {
                    _ = M_take();
                }
            }

            if (Truth(B(Equal(a_506, Member(this, "i")))))
            {
                throw new ParseException(Text(S("Missing EVALUATE subject")));
            }

            _ = Call(subjects_521, "append", Put(Put(Put(Dict(), S("expression"), expression_511), S("raw"), M_source(a_506)[S("raw")]), S("unresolved"), unresolved_522));
            if (Truth(B(!Truth(M_accept(S("ALSO"))))))
            {
                break;
            }
        }

        cases_508 = ListOf();
        while (Truth(M_accept(S("WHEN"))))
        {
            if (Truth(Any(Sequence(from item525 in Items(cases_508) let c_526 = item525 select c_526[S("other")]))))
            {
                throw new ParseException(Text(S("WHEN OTHER must be last")));
            }

            selections_519 = ListOf();
            other_518 = M_accept(S("OTHER"));
            if (Truth(B(!Truth(other_518))))
            {
                while (Truth(B(true)))
                {
                    a_506 = Member(this, "i");
                    negated_515 = M_accept(S("NOT"));
                    var unpack527 = ListOf(Nil, Nil);
                    low_514 = unpack527[I(0)];
                    high_512 = unpack527[I(1)];
                    kind_513 = S("value");
                    operator_517 = Nil;
                    if (Truth(M_accept(S("ANY"))))
                    {
                        kind_513 = S("any");
                    }
                    else
                    {
                        try
                        {
                            if (Truth(B(Contains(ListOf(S("="), S("<"), S(">"), S("<="), S(">="), S("<>")), M_peek()))))
                            {
                                operator_517 = Member(M_take(), "upper");
                                kind_513 = S("partial");
                            }

                            low_514 = M_expr(I(4));
                            if (Truth(B(Contains(ListOf(S("THRU"), S("THROUGH")), M_peek()))))
                            {
                                _ = M_take();
                                high_512 = M_expr(I(4));
                                kind_513 = S("range");
                            }
                        }
                        catch (Exception error528) when (error528 is ParseException)
                        {
                            kind_513 = S("unresolved");
                            while (Truth(And(M_peek(), () => B(!Contains(Union(Verbs, SetOf(S("ALSO"), S("WHEN"), S("END-EVALUATE"), S("."))), M_peek())))))
                            {
                                _ = M_take();
                            }
                        }
                    }

                    if (Truth(B(Equal(Member(this, "i"), a_506))))
                    {
                        throw new ParseException(Text(S("Missing WHEN selection")));
                    }

                    _ = Call(selections_519, "append", Put(Put(Put(Put(Put(Put(Dict(), S("kind"), kind_513), S("raw"), M_source(a_506)[S("raw")]), S("negated"), negated_515), S("operator"), operator_517), S("low"), low_514), S("high"), high_512));
                    if (Truth(B(!Truth(M_accept(S("ALSO"))))))
                    {
                        break;
                    }
                }

                if (Truth(B(!Equal(Length(selections_519), Length(subjects_521)))))
                {
                    throw new ParseException(Text(S("EVALUATE selection count mismatch")));
                }
            }

            a_506 = Member(this, "i");
            nested_516 = ListOf();
            while (Truth(M_peek()))
            {
                word_523 = M_peek();
                if (Truth(And(B(!Truth(nested_516)), () => B(Contains(ListOf(S("WHEN"), S("END-EVALUATE"), S(".")), word_523)))))
                {
                    break;
                }

                if (Truth(B(Contains(ListOf(S("EVALUATE"), S("SEARCH")), word_523))))
                {
                    _ = Call(nested_516, "append", word_523);
                }
                else
                {
                    if (Truth(B(Contains(ListOf(S("END-EVALUATE"), S("END-SEARCH")), word_523))))
                    {
                        expected_510 = word_523[Slice(I(4), Nil, Nil)];
                        if (Truth(Or(B(!Truth(nested_516)), () => B(!Equal(nested_516[Subtract(I(0), I(1))], expected_510)))))
                        {
                            throw new ParseException(Text(S("Invalid nested stub scope")));
                        }

                        _ = Call(nested_516, "pop");
                    }
                }

                _ = M_take();
            }

            if (Truth(nested_516))
            {
                throw new ParseException(Text(S("Unclosed nested control stub")));
            }

            body_507 = Truth(B(Compare(Member(this, "i"), a_506) > 0)) ? M_source(a_506) : Member(Member(this, "ts")[Maximum(new V[] { I(0), Subtract(a_506, I(1)) }, null, null)], "source");
            _ = Call(cases_508, "append", Put(Put(Put(Put(Put(Dict(), S("id"), AddText(S("case-"), Format(Add(Length(cases_508), I(1)), string.Empty))), S("other"), other_518), S("selections"), selections_519), S("body"), body_507), S("tokens"), M_raw_tokens(a_506, Member(this, "i"))));
        }

        if (Truth(B(!Truth(cases_508))))
        {
            throw new ParseException(Text(S("EVALUATE requires WHEN")));
        }

        _ = M_end(S("END-EVALUATE"));
        return M_stub(S("evaluate"), start_520, subjects_521, cases_508);
    }

    /// <summary>
    /// 元の解析規則とIRの順序を保持します。
    /// </summary>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_perform_stub()
    {
        V by_529 = Nil;
        V condition_530 = Nil;
        V initial_531 = Nil;
        V metadata_532 = Nil;
        V name_533 = Nil;
        V reference_534 = Nil;
        V start_535 = Nil;
        V test_536 = Nil;
        V thru_537 = Nil;
        V times_538 = Nil;
        V until_539 = Nil;
        V varying_540 = Nil;
        start_535 = Subtract(Member(this, "i"), I(1));
        test_536 = S("before");
        if (Truth(M_accept(S("WITH"))))
        {
            _ = M_require(S("TEST"));
            test_536 = Call(Member(M_take(), "upper"), "lower");
            if (Truth(B(!Contains(ListOf(S("before"), S("after")), test_536))))
            {
                throw new ParseException(Text(S("Invalid TEST phrase")));
            }
        }

        name_533 = Member(M_take(), "text");
        thru_537 = name_533;
        if (Truth(B(Contains(ListOf(S("THRU"), S("THROUGH")), M_peek()))))
        {
            _ = M_take();
            thru_537 = Member(M_take(), "text");
        }

        var unpack541 = ListOf(Nil, Nil, ListOf());
        times_538 = unpack541[I(0)];
        until_539 = unpack541[I(1)];
        varying_540 = unpack541[I(2)];
        if (Truth(M_accept(S("WITH"))))
        {
            _ = M_require(S("TEST"));
            test_536 = Call(Member(M_take(), "upper"), "lower");
            if (Truth(B(!Contains(ListOf(S("before"), S("after")), test_536))))
            {
                throw new ParseException(Text(S("Invalid TEST phrase")));
            }
        }

        if (Truth(M_accept(S("UNTIL"))))
        {
            until_539 = M_expr();
        }
        else
        {
            if (Truth(M_accept(S("VARYING"))))
            {
                while (Truth(B(true)))
                {
                    reference_534 = M_reference();
                    _ = M_require(S("FROM"));
                    initial_531 = M_expr();
                    _ = M_require(S("BY"));
                    by_529 = M_expr();
                    _ = M_require(S("UNTIL"));
                    condition_530 = M_expr();
                    _ = Call(varying_540, "append", Put(Put(Merge(Put(Dict(), S("reference"), reference_534), Put(Dict(), S("from"), initial_531)), S("by"), by_529), S("until"), condition_530));
                    if (Truth(B(!Truth(M_accept(S("AFTER"))))))
                    {
                        break;
                    }
                }
            }
            else
            {
                if (Truth(B(!Contains(Union(Verbs, SetOf(S("."), S(string.Empty), S("END-IF"), S("ELSE"), S("END-PERFORM"))), M_peek()))))
                {
                    times_538 = M_expr(I(6));
                    _ = M_require(S("TIMES"));
                }
            }
        }

        if (Truth(B(!Contains(Union(Verbs, SetOf(S("."), S(string.Empty), S("END-IF"), S("ELSE"), S("END-PERFORM"))), M_peek()))))
        {
            throw new ParseException(Text(S("Unconsumed PERFORM clause")));
        }

        metadata_532 = Put(Put(Put(Put(Put(Put(Put(Merge(Put(Dict(), S("targetKind"), S("paragraph")), Put(Dict(), S("from"), name_533)), S("thru"), thru_537), S("test"), test_536), S("times"), times_538), S("until"), until_539), S("varying"), varying_540), S("targets"), ListOf()), S("missingTarget"), B(true));
        return M_stub(S("perform"), start_535, perform_: metadata_532);
    }

    /// <summary>
    /// 元の解析規則とIRの順序を保持します。
    /// </summary>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_resolve_stub_targets()
    {
        V end_542 = Nil;
        V last_543 = Nil;
        V m_544 = Nil;
        V p_546 = Nil;
        V paragraph_547 = Nil;
        V s_548 = Nil;
        V selected_549 = Nil;
        V source_550 = Nil;
        V start_551 = Nil;
        IEnumerable<V> Fn_walk(V body_552)
        {
            V key_553 = Nil;
            V s_554 = Nil;
            foreach (var item555 in Items(body_552))
            {
                s_554 = item555;
                yield return s_554;
                foreach (var item556 in Items(ListOf(S("body"), S("then"), S("else"), S("onError"), S("notError"), S("atEnd"), S("notEnd"))))
                {
                    key_553 = item556;
                    if (Truth(B(IsList(Call(s_554, "get", key_553)))))
                    {
                        foreach (var yieldItem557 in Items(Sequence(Fn_walk(s_554[key_553]))))
                        {
                            yield return yieldItem557;
                        }
                    }
                }
            }
        }

        foreach (var item558 in Items(Member(this, "paragraphs")))
        {
            p_546 = item558;
            foreach (var item559 in Items(Sequence(Fn_walk(p_546[S("statements")]))))
            {
                s_548 = item559;
                if (Truth(Or(B(!Equal(s_548[S("kind")], S("controlStub"))), () => B(!Truth(s_548[S("perform")])))))
                {
                    continue;
                }

                m_544 = s_548[S("perform")];
                V Fn_indices(V name_560)
                {
                    return ListOf(from item561 in Items(Enumerate(Member(this, "paragraphs"))) let i_562 = item561[I(0)] let p_563 = item561[I(1)] where Truth(Or(B(Equal(Call(p_563[S("name")], "upper"), Call(name_560, "upper"))), () => B(Equal(Call(Or(p_563[S("section")], () => S(string.Empty)), "upper"), Call(name_560, "upper"))))) select i_562);
                }

                var unpack564 = ListOf(Fn_indices(m_544[S("from")]), Fn_indices(m_544[S("thru")]));
                start_551 = unpack564[I(0)];
                end_542 = unpack564[I(1)];
                m_544[S("targetKind")] = Truth(Any(Sequence(from item565 in Items(Member(this, "paragraphs")) let p_566 = item565 select B(Equal(Call(Or(p_566[S("section")], () => S(string.Empty)), "upper"), Call(m_544[S("from")], "upper")))))) ? S("section") : S("paragraph");
                if (Truth(And(start_551, () => end_542)))
                {
                    if (Truth(B(Compare(start_551[I(0)], end_542[Subtract(I(0), I(1))]) > 0)))
                    {
                        throw new ParseException(Text(S("Invalid stub PERFORM range")));
                    }

                    selected_549 = Member(this, "paragraphs")[Slice(start_551[I(0)], Add(end_542[Subtract(I(0), I(1))], I(1)), Nil)];
                    m_544[S("targets")] = ListOf();
                    foreach (var item567 in Items(selected_549))
                    {
                        paragraph_547 = item567;
                        source_550 = Merge(Dict(), paragraph_547[S("source")]);
                        if (Truth(paragraph_547[S("statements")]))
                        {
                            last_543 = paragraph_547[S("statements")][Subtract(I(0), I(1))][S("source")];
                            if (Truth(B(Equal(last_543[S("file")], source_550[S("file")]))))
                            {
                                source_550[S("end")] = last_543[S("end")];
                                source_550[S("raw")] = ((SourceFrontend)Member(this, "f")).OriginalText(source_550);
                            }
                        }

                        _ = Call(m_544[S("targets")], "append", Put(Put(Dict(), S("name"), paragraph_547[S("name")]), S("source"), source_550));
                    }

                    m_544[S("missingTarget")] = B(false);
                }
            }
        }

        return Nil;
    }
}
