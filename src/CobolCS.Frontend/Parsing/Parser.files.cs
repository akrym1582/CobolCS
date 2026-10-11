using static CobolCS.Frontend.Parsing.Values;

namespace CobolCS.Frontend.Parsing;

/// <summary>位置とIRを保持する files の解析処理です。</summary>
internal sealed partial class Parser : V
{
    /// <summary>
    /// 元の解析規則とIRの順序を保持します。
    /// </summary>
    /// <param name="name_">name の解析値。</param>
    /// <param name="kind_">kind の解析値。</param>
    /// <param name="clause_">clause の解析値。</param>
    /// <param name="source_">source の解析値。</param>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_file_record(V name_, V kind_, V clause_, V source_)
    {
        V dep_668 = Nil;
        V depending_669 = Nil;
        V first_670 = Nil;
        V high_671 = Nil;
        V i_672 = Nil;
        V last_673 = Nil;
        V low_674 = Nil;
        V mode_675 = Nil;
        V names_676 = Nil;
        V raw_677 = Nil;
        V record_678 = Nil;
        V seen_679 = Nil;
        V span_680 = Nil;
        V start_681 = Nil;
        V unit_682 = Nil;
        V varying_683 = Nil;
        V word_684 = Nil;
        dep_668 = Next(Sequence(from item685 in Items(Member(this, "dependencies")) let d_686 = item685 where Truth(And(B(Equal(d_686[S("kind")], S("file"))), () => B(Equal(Call(d_686[S("name")], "upper"), Call(name_, "upper"))))) select d_686), Nil);
        if (Truth(B(Equal(kind_, S("SD")))))
        {
            if (Truth(clause_))
            {
                throw new ParseException(Text(S("Unsupported SD clause")));
            }

            if (Truth(B(ReferenceEquals(dep_668, Nil))))
            {
                dep_668 = M_dependency(S("file"), name_, Add(S("SD "), name_), source_);
            }

            dep_668[S("sortFile")] = B(true);
        }

        if (Truth(B(ReferenceEquals(dep_668, Nil))))
        {
            throw new ParseException(Text(Add(S("FD has no SELECT "), name_)));
        }

        dep_668[S("descriptionSource")] = source_;
        dep_668[S("descriptionClauses")] = ListOf();
        i_672 = I(0);
        seen_679 = SetCopy();
        V Fn_accept(V word_687)
        {
            if (Truth(And(B(Compare(i_672, Length(clause_)) < 0), () => B(Equal(Member(clause_[i_672], "upper"), word_687)))))
            {
                i_672 = Add(i_672, I(1));
                return B(true);
            }

            return B(false);
        }

        V Fn_require(V word_688)
        {
            if (Truth(B(!Truth(Fn_accept(word_688)))))
            {
                throw new ParseException(Text(Add(S("FD requires "), word_688)));
            }

            return Nil;
        }

        V Fn_take()
        {
            V value_689 = Nil;
            if (Truth(B(Compare(i_672, Length(clause_)) >= 0)))
            {
                throw new ParseException(Text(S("Incomplete FD clause")));
            }

            value_689 = Member(clause_[i_672], "text");
            i_672 = Add(i_672, I(1));
            return value_689;
        }

        V Fn_number()
        {
            V value_690 = Nil;
            value_690 = Fn_take();
            if (Truth(B(!Truth(Call(value_690, "isdigit")))))
            {
                throw new ParseException(Text(S("FD requires unsigned integer")));
            }

            return Integer(value_690);
        }

        while (Truth(B(Compare(i_672, Length(clause_)) < 0)))
        {
            start_681 = i_672;
            word_684 = Call(Fn_take(), "upper");
            if (Truth(B(Contains(seen_679, word_684))))
            {
                throw new ParseException(Text(Add(S("Duplicate FD clause "), word_684)));
            }

            _ = Call(seen_679, "add", word_684);
            if (Truth(B(Equal(word_684, S("RECORDING")))))
            {
                _ = Fn_require(S("MODE"));
                _ = Fn_accept(S("IS"));
                mode_675 = Call(Fn_take(), "upper");
                if (Truth(B(!Contains(ListOf(S("F"), S("V")), mode_675))))
                {
                    throw new ParseException(Text(Add(S("Unsupported FD recording mode "), mode_675)));
                }

                dep_668[S("recordingMode")] = mode_675;
            }
            else
            {
                if (Truth(B(Equal(word_684, S("LABEL")))))
                {
                    if (Truth(B(!Truth(Or(Fn_accept(S("RECORDS")), () => Fn_accept(S("RECORD")))))))
                    {
                        throw new ParseException(Text(S("FD LABEL requires RECORDS")));
                    }

                    _ = Or(Fn_accept(S("ARE")), () => Fn_accept(S("IS")));
                    if (Truth(Fn_accept(S("STANDARD"))))
                    {
                        dep_668[S("labelRecords")] = S("STANDARD");
                    }
                    else
                    {
                        _ = Fn_require(S("OMITTED"));
                        dep_668[S("labelRecords")] = S("OMITTED");
                    }
                }
                else
                {
                    if (Truth(B(Equal(word_684, S("BLOCK")))))
                    {
                        _ = Fn_require(S("CONTAINS"));
                        low_674 = Fn_number();
                        high_671 = Truth(Fn_accept(S("TO"))) ? Fn_number() : low_674;
                        unit_682 = Call(Fn_take(), "upper");
                        if (Truth(Or(B(!Contains(ListOf(S("RECORDS"), S("CHARACTERS")), unit_682)), () => B(Compare(high_671, low_674) < 0))))
                        {
                            throw new ParseException(Text(S("Invalid FD block range/unit")));
                        }

                        dep_668[S("blockClause")] = Put(Put(Put(Dict(), S("min"), low_674), S("max"), high_671), S("unit"), unit_682);
                    }
                    else
                    {
                        if (Truth(B(Equal(word_684, S("DATA")))))
                        {
                            if (Truth(B(!Truth(Or(Fn_accept(S("RECORDS")), () => Fn_accept(S("RECORD")))))))
                            {
                                throw new ParseException(Text(S("FD DATA requires RECORD")));
                            }

                            _ = Or(Fn_accept(S("IS")), () => Fn_accept(S("ARE")));
                            names_676 = ListOf(Fn_take());
                            while (Truth(And(B(Compare(i_672, Length(clause_)) < 0), () => B(!Contains(SetOf(S("RECORDING"), S("LABEL"), S("BLOCK"), S("DATA"), S("RECORD")), Member(clause_[i_672], "upper"))))))
                            {
                                _ = Call(names_676, "append", Fn_take());
                            }

                            dep_668[S("dataRecords")] = names_676;
                        }
                        else
                        {
                            if (Truth(B(Equal(word_684, S("RECORD")))))
                            {
                                varying_683 = B(!Truth(Fn_accept(S("CONTAINS"))));
                                if (Truth(varying_683))
                                {
                                    _ = Fn_accept(S("IS"));
                                    _ = Fn_require(S("VARYING"));
                                    _ = Fn_accept(S("IN"));
                                    _ = Fn_require(S("SIZE"));
                                    _ = Fn_accept(S("FROM"));
                                }

                                low_674 = Fn_number();
                                if (Truth(varying_683))
                                {
                                    _ = Fn_require(S("TO"));
                                    high_671 = Fn_number();
                                }
                                else
                                {
                                    high_671 = low_674;
                                }

                                _ = Fn_accept(S("CHARACTERS"));
                                depending_669 = Nil;
                                if (Truth(varying_683))
                                {
                                    _ = Fn_require(S("DEPENDING"));
                                    _ = Fn_accept(S("ON"));
                                    depending_669 = Fn_take();
                                }

                                if (Truth(Or(B(Compare(low_674, I(0)) <= 0), () => B(Compare(high_671, low_674) < 0))))
                                {
                                    throw new ParseException(Text(S("Invalid FD record length range")));
                                }

                                dep_668[S("recordClause")] = Put(Put(Put(Dict(), S("min"), low_674), S("max"), high_671), S("depending"), depending_669);
                            }
                            else
                            {
                                throw new ParseException(Text(Add(S("Unsupported FD clause "), word_684)));
                            }
                        }
                    }
                }
            }

            var unpack691 = ListOf(Member(clause_[start_681], "source"), Member(clause_[Subtract(i_672, I(1))], "source"));
            first_670 = unpack691[I(0)];
            last_673 = unpack691[I(1)];
            raw_677 = Call(Member(Member(this, "f"), "sources")[first_670[S("file")]][S("text")], "encode", S("utf-8"));
            span_680 = Put(Put(Merge(Dict(), first_670), S("end"), last_673[S("end")]), S("raw"), Call(raw_677[Slice(first_670[S("start")][S("byteOffset")], last_673[S("end")][S("byteOffset")], Nil)], "decode", S("utf-8")));
            _ = Call(dep_668[S("descriptionClauses")], "append", Put(Put(Dict(), S("kind"), word_684), S("source"), span_680));
        }

        record_678 = Call(dep_668, "get", S("recordClause"));
        if (Truth(And(record_678, () => And(B(Equal(Call(dep_668, "get", S("recordingMode")), S("F"))), () => record_678[S("depending")]))))
        {
            throw new ParseException(Text(S("FD fixed recording mode contradicts varying record")));
        }

        return Nil;
    }

    /// <summary>
    /// 元の解析規則とIRの順序を保持します。
    /// </summary>
    /// <param name="dep_">dep の解析値。</param>
    /// <param name="ts_">ts の解析値。</param>
    /// <param name="source_">source の解析値。</param>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_file_control(V dep_, V ts_, V source_)
    {
        V access_692 = Nil;
        V assign_693 = Nil;
        V depending_694 = Nil;
        V duplicates_695 = Nil;
        V item_696 = Nil;
        V j_697 = Nil;
        V keys_698 = Nil;
        V optional_699 = Nil;
        V organization_700 = Nil;
        V record_701 = Nil;
        V relative_702 = Nil;
        V word_703 = Nil;
        V words_704 = Nil;
        words_704 = ListOf(from item705 in Items(ts_) let t_706 = item705 select Member(t_706, "upper"));
        j_697 = I(1);
        optional_699 = B(false);
        if (Truth(B(Equal(words_704[j_697], S("OPTIONAL")))))
        {
            optional_699 = B(true);
            j_697 = Add(j_697, I(1));
        }

        j_697 = Add(j_697, I(1));
        organization_700 = S("sequential");
        access_692 = S("sequential");
        assign_693 = S(string.Empty);
        keys_698 = ListOf();
        relative_702 = Nil;
        while (Truth(And(B(Compare(j_697, Length(ts_)) < 0), () => B(!Equal(words_704[j_697], S("."))))))
        {
            word_703 = words_704[j_697];
            j_697 = Add(j_697, I(1));
            if (Truth(B(Equal(word_703, S("ASSIGN")))))
            {
                if (Truth(B(Equal(words_704[j_697], S("TO")))))
                {
                    j_697 = Add(j_697, I(1));
                }

                assign_693 = Member(ts_[j_697], "text");
                j_697 = Add(j_697, I(1));
            }
            else
            {
                if (Truth(B(Equal(word_703, S("ORGANIZATION")))))
                {
                    if (Truth(B(Equal(words_704[j_697], S("IS")))))
                    {
                        j_697 = Add(j_697, I(1));
                    }

                    if (Truth(And(B(Equal(words_704[j_697], S("LINE"))), () => B(Equal(words_704[Add(j_697, I(1))], S("SEQUENTIAL"))))))
                    {
                        organization_700 = S("line-sequential");
                        j_697 = Add(j_697, I(2));
                    }
                    else
                    {
                        organization_700 = Call(words_704[j_697], "lower");
                        j_697 = Add(j_697, I(1));
                    }

                    if (Truth(B(!Contains(ListOf(S("sequential"), S("indexed"), S("relative"), S("line-sequential")), organization_700))))
                    {
                        throw new ParseException(Text(S("Unsupported file organization")));
                    }
                }
                else
                {
                    if (Truth(B(Equal(word_703, S("ACCESS")))))
                    {
                        if (Truth(B(!Equal(words_704[j_697], S("MODE")))))
                        {
                            throw new ParseException(Text(S("ACCESS requires MODE")));
                        }

                        j_697 = Add(j_697, I(1));
                        if (Truth(B(Equal(words_704[j_697], S("IS")))))
                        {
                            j_697 = Add(j_697, I(1));
                        }

                        access_692 = Call(words_704[j_697], "lower");
                        j_697 = Add(j_697, I(1));
                        if (Truth(B(!Contains(ListOf(S("sequential"), S("random"), S("dynamic")), access_692))))
                        {
                            throw new ParseException(Text(S("Invalid ACCESS MODE")));
                        }
                    }
                    else
                    {
                        if (Truth(B(Equal(word_703, S("FILE")))))
                        {
                            if (Truth(B(!Equal(words_704[j_697], S("STATUS")))))
                            {
                                throw new ParseException(Text(S("FILE requires STATUS")));
                            }

                            j_697 = Add(j_697, I(1));
                            if (Truth(B(Equal(words_704[j_697], S("IS")))))
                            {
                                j_697 = Add(j_697, I(1));
                            }

                            j_697 = Add(j_697, I(1));
                        }
                        else
                        {
                            if (Truth(B(Contains(ListOf(S("RECORD"), S("ALTERNATE"), S("RELATIVE")), word_703))))
                            {
                                if (Truth(B(Equal(word_703, S("ALTERNATE")))))
                                {
                                    if (Truth(B(!Equal(words_704[j_697], S("RECORD")))))
                                    {
                                        throw new ParseException(Text(S("ALTERNATE RECORD KEY required")));
                                    }

                                    j_697 = Add(j_697, I(1));
                                }

                                if (Truth(B(!Equal(words_704[j_697], S("KEY")))))
                                {
                                    throw new ParseException(Text(S("File KEY required")));
                                }

                                j_697 = Add(j_697, I(1));
                                if (Truth(B(Equal(words_704[j_697], S("IS")))))
                                {
                                    j_697 = Add(j_697, I(1));
                                }

                                item_696 = M_resolve(Member(ts_[j_697], "text"));
                                j_697 = Add(j_697, I(1));
                                if (Truth(B(Equal(word_703, S("RELATIVE")))))
                                {
                                    relative_702 = Put(Put(Put(Put(Dict(), S("kind"), S("reference")), S("symbolId"), item_696[S("id")]), S("subscripts"), ListOf()), S("modification"), Nil);
                                }
                                else
                                {
                                    duplicates_695 = B(false);
                                    if (Truth(B(Equal(words_704[j_697], S("WITH")))))
                                    {
                                        j_697 = Add(j_697, I(1));
                                        if (Truth(B(!Equal(words_704[j_697], S("DUPLICATES")))))
                                        {
                                            throw new ParseException(Text(S("Invalid alternate KEY option")));
                                        }

                                        j_697 = Add(j_697, I(1));
                                        duplicates_695 = B(true);
                                    }

                                    _ = Call(keys_698, "append", Put(Put(Dict(), S("symbolId"), item_696[S("id")]), S("duplicates"), duplicates_695));
                                }
                            }
                            else
                            {
                                throw new ParseException(Text(Add(S("Unsupported SELECT clause "), word_703)));
                            }
                        }
                    }
                }
            }
        }

        record_701 = Call(dep_, "pop", S("recordClause"), Nil);
        depending_694 = Nil;
        if (Truth(And(record_701, () => record_701[S("depending")])))
        {
            item_696 = M_resolve(record_701[S("depending")]);
            if (Truth(And(B(!Truth(Call(item_696, "get", S("stub")))), () => Or(B(!Truth(Call(item_696, "get", S("pic")))), () => Or(B(!Equal(item_696[S("pic")][S("category")], S("numeric"))), () => Or(B(!Equal(item_696[S("pic")][S("scale")], I(0))), () => B(!Equal(item_696[S("occurs")], I(1)))))))))
            {
                throw new ParseException(Text(S("FD DEPENDING ON requires a scalar integer item")));
            }

            depending_694 = Put(Put(Put(Put(Dict(), S("kind"), S("reference")), S("symbolId"), item_696[S("id")]), S("subscripts"), ListOf()), S("modification"), Nil);
        }

        dep_[S("file")] = Put(Put(Put(Put(Put(Put(Put(Put(Put(Dict(), S("organization"), organization_700), S("access"), access_692), S("optional"), optional_699), S("assign"), assign_693), S("keys"), keys_698), S("relativeKey"), relative_702), S("minLength"), Truth(record_701) ? record_701[S("min")] : Nil), S("maxLength"), Truth(record_701) ? record_701[S("max")] : Nil), S("dependingOn"), depending_694);
        return Nil;
    }

    /// <summary>
    /// 元の解析規則とIRの順序を保持します。
    /// </summary>
    /// <param name="verb_">verb の解析値。</param>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_file_statement(V verb_)
    {
        V a_707 = Nil;
        V advancing_708 = Nil;
        V at_709 = Nil;
        V before_710 = Nil;
        V condition_711 = Nil;
        V dep_712 = Nil;
        V end_713 = Nil;
        V has_end_714 = Nil;
        V has_invalid_715 = Nil;
        V invalid_716 = Nil;
        V key_717 = Nil;
        V lines_718 = Nil;
        V mode_719 = Nil;
        V name_720 = Nil;
        V next_record_721 = Nil;
        V node_722 = Nil;
        V nodes_723 = Nil;
        V not_end_724 = Nil;
        V not_invalid_725 = Nil;
        V operation_726 = Nil;
        V record_727 = Nil;
        V transfer_728 = Nil;
        a_707 = Subtract(Member(this, "i"), I(1));
        if (Truth(B(Contains(ListOf(S("OPEN"), S("CLOSE")), verb_))))
        {
            nodes_723 = ListOf();
            mode_719 = S(string.Empty);
            while (Truth(And(M_peek(), () => B(!Contains(Union(Union(Verbs, Ends), SetOf(S("."))), M_peek())))))
            {
                if (Truth(And(B(Equal(verb_, S("OPEN"))), () => B(Contains(ListOf(S("INPUT"), S("OUTPUT"), S("I-O"), S("EXTEND")), M_peek())))))
                {
                    mode_719 = Member(M_take(), "upper");
                }

                if (Truth(And(B(Equal(verb_, S("OPEN"))), () => B(!Truth(mode_719)))))
                {
                    throw new ParseException(Text(S("OPEN requires mode")));
                }

                dep_712 = M_file_dependency(Member(M_take(), "text"));
                node_722 = Put(Put(Put(Put(Put(Put(Put(Dict(), S("kind"), S("external")), S("id"), AddText(S("s"), Format(Member(this, "sid"), "06d"))), S("source"), M_source(a_707)), S("dependencyId"), dep_712[S("id")]), S("operation"), Call(Call(Add(Add(verb_, S(" ")), mode_719), "strip"), "lower")), S("atEnd"), ListOf()), S("notEnd"), ListOf());
                SetMember(this, "sid", Add(Member(this, "sid"), I(1)));
                _ = Call(nodes_723, "append", node_722);
            }

            if (Truth(B(!Truth(nodes_723))))
            {
                throw new ParseException(Text(S("File operand required")));
            }

            return Put(Put(Dict(), S("kind"), S("sequence")), S("body"), nodes_723);
        }

        name_720 = Member(M_take(), "text");
        if (Truth(B(Contains(ListOf(S("WRITE"), S("REWRITE")), verb_))))
        {
            record_727 = M_resolve(name_720);
            dep_712 = Next(Sequence(from item729 in Items(Member(this, "dependencies")) let d_730 = item729 where Truth(And(B(Equal(d_730[S("kind")], S("file"))), () => B(Equal(d_730[S("recordId")], record_727[S("id")])))) select d_730), Nil);
            if (Truth(B(!Truth(dep_712))))
            {
                throw new ParseException(Text(S("Unknown file record")));
            }
        }
        else
        {
            dep_712 = M_file_dependency(name_720);
        }

        var assign731 = Nil;
        transfer_728 = assign731;
        key_717 = assign731;
        condition_711 = Nil;
        advancing_708 = Nil;
        if (Truth(B(Equal(verb_, S("READ")))))
        {
            next_record_721 = M_accept(S("NEXT"));
            _ = M_accept(S("RECORD"));
            if (Truth(M_accept(S("INTO"))))
            {
                transfer_728 = M_reference();
            }

            if (Truth(M_accept(S("KEY"))))
            {
                _ = M_accept(S("IS"));
                key_717 = M_expr(I(6));
            }

            operation_726 = Truth(next_record_721) ? S("read next") : S("read");
        }
        else
        {
            operation_726 = Call(verb_, "lower");
            _ = M_accept(S("RECORD"));
            if (Truth(M_accept(S("FROM"))))
            {
                transfer_728 = M_expr(I(6));
            }

            if (Truth(B(Equal(verb_, S("START")))))
            {
                _ = M_require(S("KEY"));
                _ = M_accept(S("IS"));
                condition_711 = Member(M_take(), "upper");
                if (Truth(B(!Contains(ListOf(S("="), S(">"), S(">="), S("<"), S("<=")), condition_711))))
                {
                    throw new ParseException(Text(S("Unsupported START relation")));
                }

                key_717 = M_expr(I(6));
            }

            if (Truth(And(B(Equal(verb_, S("WRITE"))), () => B(Contains(ListOf(S("BEFORE"), S("AFTER")), M_peek())))))
            {
                before_710 = B(Equal(Member(M_take(), "upper"), S("BEFORE")));
                _ = M_require(S("ADVANCING"));
                lines_718 = M_expr(I(6));
                _ = Or(M_accept(S("LINES")), () => M_accept(S("LINE")));
                advancing_708 = Put(Put(Dict(), S("before"), before_710), S("lines"), lines_718);
            }
        }

        at_709 = ListOf();
        not_end_724 = ListOf();
        invalid_716 = ListOf();
        not_invalid_725 = ListOf();
        var assign732 = B(false);
        has_end_714 = assign732;
        has_invalid_715 = assign732;
        end_713 = Add(S("END-"), verb_);
        if (Truth(M_accept(S("AT"))))
        {
            _ = M_require(S("END"));
            has_end_714 = B(true);
            at_709 = M_block(SetOf(S("NOT"), end_713));
        }
        else
        {
            if (Truth(M_accept(S("INVALID"))))
            {
                _ = M_require(S("KEY"));
                has_invalid_715 = B(true);
                invalid_716 = M_block(SetOf(S("NOT"), end_713));
            }
        }

        if (Truth(M_accept(S("NOT"))))
        {
            if (Truth(M_accept(S("AT"))))
            {
                _ = M_require(S("END"));
                not_end_724 = M_block(SetOf(end_713));
            }
            else
            {
                _ = M_require(S("INVALID"));
                _ = M_require(S("KEY"));
                not_invalid_725 = M_block(SetOf(end_713));
            }
        }

        _ = M_accept(end_713);
        return Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Dict(), S("kind"), S("external")), S("dependencyId"), dep_712[S("id")]), S("operation"), operation_726), S("transfer"), transfer_728), S("key"), key_717), S("startCondition"), condition_711), S("advancing"), advancing_708), S("atEnd"), at_709), S("notEnd"), not_end_724), S("onInvalid"), invalid_716), S("notInvalid"), not_invalid_725), S("hasInvalid"), has_invalid_715), S("hasEnd"), has_end_714);
    }

    /// <summary>
    /// 元の解析規則とIRの順序を保持します。
    /// </summary>
    /// <param name="name_">name の解析値。</param>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_file_dependency(V name_)
    {
        V dep_733 = Nil;
        dep_733 = Next(Sequence(from item734 in Items(Member(this, "dependencies")) let d_735 = item734 where Truth(And(B(Equal(d_735[S("kind")], S("file"))), () => B(Equal(Call(d_735[S("name")], "upper"), Call(name_, "upper"))))) select d_735), Nil);
        if (Truth(Or(B(!Truth(dep_733)), () => B(!Truth(dep_733[S("recordId")])))))
        {
            throw new ParseException(Text(Add(S("Unresolved file/record "), name_)));
        }

        return dep_733;
    }

    /// <summary>
    /// 元の解析規則とIRの順序を保持します。
    /// </summary>
    /// <param name="verb_">verb の解析値。</param>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_sort_statement(V verb_)
    {
        V at_736 = Nil;
        V dep_737 = Nil;
        V direction_738 = Nil;
        V duplicates_739 = Nil;
        V giving_740 = Nil;
        V input_proc_741 = Nil;
        V item_742 = Nil;
        V keys_743 = Nil;
        V name_744 = Nil;
        V not_end_745 = Nil;
        V output_proc_746 = Nil;
        V record_747 = Nil;
        V transfer_748 = Nil;
        V using_749 = Nil;
        if (Truth(B(Contains(ListOf(S("RELEASE"), S("RETURN")), verb_))))
        {
            name_744 = Member(M_take(), "text");
            if (Truth(B(Equal(verb_, S("RELEASE")))))
            {
                record_747 = M_resolve(name_744);
                dep_737 = Next(Sequence(from item750 in Items(Member(this, "dependencies")) let d_751 = item750 where Truth(And(Call(d_751, "get", S("sortFile")), () => B(Equal(d_751[S("recordId")], record_747[S("id")])))) select d_751), Nil);
            }
            else
            {
                dep_737 = Next(Sequence(from item752 in Items(Member(this, "dependencies")) let d_753 = item752 where Truth(And(Call(d_753, "get", S("sortFile")), () => B(Equal(Call(d_753[S("name")], "upper"), Call(name_744, "upper"))))) select d_753), Nil);
            }

            if (Truth(B(!Truth(dep_737))))
            {
                throw new ParseException(Text(S("RELEASE/RETURN requires SD")));
            }

            transfer_748 = Truth(M_accept(Truth(B(Equal(verb_, S("RELEASE")))) ? S("FROM") : S("INTO"))) ? M_expr(I(6)) : Nil;
            at_736 = ListOf();
            not_end_745 = ListOf();
            if (Truth(M_accept(S("AT"))))
            {
                _ = M_require(S("END"));
                at_736 = M_block(SetOf(S("NOT"), S("END-RETURN")));
            }

            if (Truth(M_accept(S("NOT"))))
            {
                _ = M_require(S("AT"));
                _ = M_require(S("END"));
                not_end_745 = M_block(SetOf(S("END-RETURN")));
            }

            _ = M_accept(S("END-RETURN"));
            return Put(Put(Put(Put(Put(Put(Dict(), S("kind"), S("sortIo")), S("operation"), Call(verb_, "lower")), S("fileId"), dep_737[S("id")]), S("transfer"), transfer_748), S("atEnd"), at_736), S("notEnd"), not_end_745);
        }

        name_744 = Member(M_take(), "text");
        dep_737 = Next(Sequence(from item754 in Items(Member(this, "dependencies")) let d_755 = item754 where Truth(And(Call(d_755, "get", S("sortFile")), () => B(Equal(Call(d_755[S("name")], "upper"), Call(name_744, "upper"))))) select d_755), Nil);
        if (Truth(B(!Truth(dep_737))))
        {
            throw new ParseException(Text(S("SORT requires SD")));
        }

        _ = M_resolve(S("SORT-RETURN"));
        keys_743 = ListOf();
        while (Truth(B(Contains(ListOf(S("ON"), S("ASCENDING"), S("DESCENDING")), M_peek()))))
        {
            _ = M_accept(S("ON"));
            direction_738 = Member(M_take(), "upper");
            if (Truth(B(!Contains(ListOf(S("ASCENDING"), S("DESCENDING")), direction_738))))
            {
                throw new ParseException(Text(S("Invalid SORT key direction")));
            }

            _ = M_require(S("KEY"));
            while (Truth(And(M_peek(), () => B(!Contains(Union(Union(Verbs, Ends), SetOf(S("ON"), S("ASCENDING"), S("DESCENDING"), S("USING"), S("GIVING"), S("INPUT"), S("OUTPUT"), S("WITH"), S("."))), M_peek())))))
            {
                item_742 = M_reference();
                if (Truth(Or(item_742[S("subscripts")], () => item_742[S("modification")])))
                {
                    throw new ParseException(Text(S("SORT key must be SD field")));
                }

                _ = Call(keys_743, "append", Put(Put(Dict(), S("symbolId"), item_742[S("symbolId")]), S("direction"), Call(direction_738, "lower")));
            }
        }

        duplicates_739 = B(false);
        if (Truth(M_accept(S("WITH"))))
        {
            _ = M_require(S("DUPLICATES"));
            _ = M_require(S("IN"));
            _ = M_require(S("ORDER"));
            duplicates_739 = B(true);
        }

        using_749 = ListOf();
        giving_740 = ListOf();
        var assign756 = Nil;
        input_proc_741 = assign756;
        output_proc_746 = assign756;
        V Fn_procedure()
        {
            V first_757 = Nil;
            V last_758 = Nil;
            _ = M_require(S("PROCEDURE"));
            _ = M_accept(S("IS"));
            first_757 = Member(M_take(), "text");
            last_758 = first_757;
            if (Truth(B(Contains(ListOf(S("THRU"), S("THROUGH")), M_peek()))))
            {
                _ = M_take();
                last_758 = Member(M_take(), "text");
            }

            return Merge(Dict(), Put(Put(Dict(), S("from"), first_757), S("thru"), last_758));
        }

        if (Truth(M_accept(S("USING"))))
        {
            while (Truth(And(M_peek(), () => B(!Contains(Union(Union(Verbs, Ends), SetOf(S("GIVING"), S("OUTPUT"), S("."))), M_peek())))))
            {
                _ = Call(using_749, "append", M_file_dependency(Member(M_take(), "text"))[S("id")]);
            }
        }
        else
        {
            if (Truth(M_accept(S("INPUT"))))
            {
                input_proc_741 = Fn_procedure();
            }
        }

        if (Truth(M_accept(S("GIVING"))))
        {
            while (Truth(And(M_peek(), () => B(!Contains(Union(Union(Verbs, Ends), SetOf(S("."))), M_peek())))))
            {
                _ = Call(giving_740, "append", M_file_dependency(Member(M_take(), "text"))[S("id")]);
            }
        }
        else
        {
            if (Truth(M_accept(S("OUTPUT"))))
            {
                output_proc_746 = Fn_procedure();
            }
        }

        if (Truth(Or(B(!Truth(keys_743)), () => Or(And(B(!Truth(using_749)), () => B(ReferenceEquals(input_proc_741, Nil))), () => And(B(!Truth(giving_740)), () => B(ReferenceEquals(output_proc_746, Nil)))))))
        {
            throw new ParseException(Text(S("SORT requires keys/input/output")));
        }

        if (Truth(And(B(Equal(verb_, S("MERGE"))), () => input_proc_741)))
        {
            throw new ParseException(Text(S("MERGE requires USING")));
        }

        return Put(Put(Put(Put(Put(Put(Put(Put(Put(Dict(), S("kind"), S("sort")), S("fileId"), dep_737[S("id")]), S("merge"), B(Equal(verb_, S("MERGE")))), S("keys"), keys_743), S("using"), using_749), S("giving"), giving_740), S("inputProcedure"), input_proc_741), S("outputProcedure"), output_proc_746), S("duplicatesInOrder"), duplicates_739);
    }
}
