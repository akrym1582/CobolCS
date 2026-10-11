using static CobolCS.Frontend.Parsing.Values;

namespace CobolCS.Frontend.Parsing;

/// <summary>位置とIRを保持する sql の解析処理です。</summary>
internal sealed partial class Parser : V
{
    /// <summary>
    /// 元の解析規則とIRの順序を保持します。
    /// </summary>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_sql_statement()
    {
        V a_759 = Nil;
        V bindings_760 = Nil;
        V cursor_761 = Nil;
        V d_762 = Nil;
        V direction_763 = Nil;
        V expression_764 = Nil;
        V handlers_765 = Nil;
        V indicator_766 = Nil;
        V inputs_767 = Nil;
        V j_768 = Nil;
        V key_769 = Nil;
        V kind_770 = Nil;
        V line_771 = Nil;
        V original_772 = Nil;
        V output_773 = Nil;
        V outputs_774 = Nil;
        V query_775 = Nil;
        V start_776 = Nil;
        V t_777 = Nil;
        V target_778 = Nil;
        V ts_779 = Nil;
        V words_780 = Nil;
        a_759 = Subtract(Member(this, "i"), I(1));
        _ = M_require(S("SQL"));
        original_772 = ListOf();
        while (Truth(And(M_peek(), () => B(!Equal(M_peek(), S("END-EXEC"))))))
        {
            _ = Call(original_772, "append", M_take());
        }

        _ = M_require(S("END-EXEC"));
        ts_779 = ListOf();
        j_768 = I(0);
        while (Truth(B(Compare(j_768, Length(original_772)) < 0)))
        {
            if (Truth(And(B(Compare(Add(j_768, I(1)), Length(original_772)) < 0), () => And(B(Equal(Member(original_772[j_768], "text"), S("-"))), () => B(Equal(Member(original_772[Add(j_768, I(1))], "text"), S("-")))))))
            {
                line_771 = Member(original_772[j_768], "source")[S("start")][S("line")];
                while (Truth(And(B(Compare(j_768, Length(original_772)) < 0), () => B(Equal(Member(original_772[j_768], "source")[S("start")][S("line")], line_771)))))
                {
                    j_768 = Add(j_768, I(1));
                }
            }
            else
            {
                if (Truth(And(B(Compare(Add(j_768, I(1)), Length(original_772)) < 0), () => And(B(Equal(Member(original_772[j_768], "text"), S("/"))), () => B(Equal(Member(original_772[Add(j_768, I(1))], "text"), S("*")))))))
                {
                    j_768 = Add(j_768, I(2));
                    while (Truth(And(B(Compare(Add(j_768, I(1)), Length(original_772)) < 0), () => B(!Truth(And(B(Equal(Member(original_772[j_768], "text"), S("*"))), () => B(Equal(Member(original_772[Add(j_768, I(1))], "text"), S("/")))))))))
                    {
                        j_768 = Add(j_768, I(1));
                    }

                    if (Truth(B(Compare(Add(j_768, I(1)), Length(original_772)) >= 0)))
                    {
                        throw new ParseException(Text(S("Unterminated SQL comment")));
                    }

                    j_768 = Add(j_768, I(2));
                }
                else
                {
                    _ = Call(ts_779, "append", original_772[j_768]);
                    j_768 = Add(j_768, I(1));
                }
            }
        }

        words_780 = ListOf(from item781 in Items(ts_779) let t_782 = item781 select Member(t_782, "upper"));
        if (Truth(B(!Truth(words_780))))
        {
            throw new ParseException(Text(S("Empty SQL")));
        }

        handlers_765 = GetAttr(this, S("sql_handlers"), Put(Put(Put(Dict(), S("error"), Nil), S("warning"), Nil), S("notFound"), Nil));
        if (Truth(B(Equal(words_780[I(0)], S("WHENEVER")))))
        {
            j_768 = I(1);
            if (Truth(And(B(Equal(words_780[j_768], S("NOT"))), () => B(Equal(words_780[Add(j_768, I(1))], S("FOUND"))))))
            {
                key_769 = S("notFound");
                j_768 = Add(j_768, I(2));
            }
            else
            {
                if (Truth(B(Contains(ListOf(S("SQLERROR"), S("SQLWARNING")), words_780[j_768]))))
                {
                    key_769 = Truth(B(Equal(words_780[j_768], S("SQLERROR")))) ? S("error") : S("warning");
                    j_768 = Add(j_768, I(1));
                }
                else
                {
                    throw new ParseException(Text(S("Invalid WHENEVER condition")));
                }
            }

            if (Truth(B(Equal(words_780[Slice(j_768, Nil, Nil)], ListOf(S("CONTINUE"))))))
            {
                target_778 = Nil;
            }
            else
            {
                if (Truth(And(B(Equal(words_780[Slice(j_768, Add(j_768, I(2)), Nil)], ListOf(S("GO"), S("TO")))), () => B(Equal(Length(words_780), Add(j_768, I(3)))))))
                {
                    target_778 = Member(ts_779[Add(j_768, I(2))], "text");
                }
                else
                {
                    throw new ParseException(Text(S("Unsupported WHENEVER action")));
                }
            }

            SetMember(this, "sql_handlers", Merge(Merge(Dict(), handlers_765), Put(Dict(), key_769, target_778)));
            return Put(Dict(), S("kind"), S("continue"));
        }

        kind_770 = Call(words_780[I(0)], "lower");
        if (Truth(B(!Contains(ListOf(S("select"), S("insert"), S("update"), S("delete"), S("commit"), S("rollback"), S("declare"), S("open"), S("fetch"), S("close")), kind_770))))
        {
            throw new ParseException(Text(Add(S("Unsupported SQL "), words_780[I(0)])));
        }

        if (Truth(Any(Sequence(from item783 in Items(ListOf(S("WITH"), S("HOLD"), S("SCROLL"), S("PREPARE"), S("EXECUTE"), S("FORWARD"), S("ROWSET"))) let w_784 = item783 select B(Contains(words_780, w_784))))))
        {
            throw new ParseException(Text(S("Unsupported SQL option")));
        }

        cursor_761 = Nil;
        start_776 = I(0);
        if (Truth(B(Equal(kind_770, S("declare")))))
        {
            if (Truth(Or(B(Compare(Length(words_780), I(5)) < 0), () => Or(B(!Equal(words_780[Slice(I(2), I(4), Nil)], ListOf(S("CURSOR"), S("FOR")))), () => B(!Equal(words_780[I(4)], S("SELECT")))))))
            {
                throw new ParseException(Text(S("Invalid DECLARE cursor")));
            }

            cursor_761 = Member(ts_779[I(1)], "text");
            start_776 = I(4);
        }
        else
        {
            if (Truth(B(Contains(ListOf(S("open"), S("close")), kind_770))))
            {
                if (Truth(B(!Equal(Length(words_780), I(2)))))
                {
                    throw new ParseException(Text(S("Unsupported cursor option")));
                }

                cursor_761 = Member(ts_779[I(1)], "text");
            }
            else
            {
                if (Truth(B(Equal(kind_770, S("fetch")))))
                {
                    if (Truth(Or(B(Compare(Length(words_780), I(4)) < 0), () => B(!Equal(words_780[I(2)], S("INTO"))))))
                    {
                        throw new ParseException(Text(S("FETCH requires single row INTO")));
                    }

                    cursor_761 = Member(ts_779[I(1)], "text");
                }
            }
        }

        inputs_767 = ListOf();
        outputs_774 = ListOf();
        bindings_760 = ListOf();
        query_775 = ListOf();
        output_773 = B(false);
        j_768 = start_776;
        V Fn_host(V index_785)
        {
            V name_786 = Nil;
            V qualifiers_787 = Nil;
            V symbol_788 = Nil;
            if (Truth(B(Compare(index_785, Length(ts_779)) >= 0)))
            {
                throw new ParseException(Text(S("Missing SQL host")));
            }

            name_786 = Member(ts_779[index_785], "text");
            index_785 = Add(index_785, I(1));
            qualifiers_787 = ListOf();
            while (Truth(And(B(Compare(Add(index_785, I(1)), Length(ts_779)) < 0), () => B(Contains(ListOf(S("OF"), S("IN")), Member(ts_779[index_785], "upper"))))))
            {
                _ = Call(qualifiers_787, "append", Member(ts_779[Add(index_785, I(1))], "text"));
                index_785 = Add(index_785, I(2));
            }

            symbol_788 = M_resolve(name_786, qualifiers_787);
            return ListOf(Put(Put(Put(Put(Dict(), S("kind"), S("reference")), S("symbolId"), symbol_788[S("id")]), S("subscripts"), ListOf()), S("modification"), Nil), index_785);
        }

        while (Truth(B(Compare(j_768, Length(ts_779)) < 0)))
        {
            t_777 = ts_779[j_768];
            if (Truth(And(B(Equal(Member(t_777, "upper"), S("INTO"))), () => B(Contains(ListOf(S("select"), S("fetch")), kind_770)))))
            {
                output_773 = B(true);
                j_768 = Add(j_768, I(1));
                continue;
            }

            if (Truth(B(Equal(Member(t_777, "upper"), S("FROM")))))
            {
                output_773 = B(false);
            }

            if (Truth(B(Equal(Member(t_777, "text"), S(":")))))
            {
                var unpack789 = Fn_host(Add(j_768, I(1)));
                expression_764 = unpack789[I(0)];
                j_768 = unpack789[I(1)];
                indicator_766 = Nil;
                if (Truth(And(B(Compare(j_768, Length(ts_779)) < 0), () => B(Equal(Member(ts_779[j_768], "upper"), S("INDICATOR"))))))
                {
                    j_768 = Add(j_768, I(1));
                    if (Truth(Or(B(Compare(j_768, Length(ts_779)) >= 0), () => B(!Equal(Member(ts_779[j_768], "text"), S(":"))))))
                    {
                        throw new ParseException(Text(S("INDICATOR requires host marker")));
                    }

                    var unpack790 = Fn_host(Add(j_768, I(1)));
                    indicator_766 = unpack790[I(0)];
                    j_768 = unpack790[I(1)];
                }
                else
                {
                    if (Truth(And(B(Compare(j_768, Length(ts_779)) < 0), () => B(Equal(Member(ts_779[j_768], "text"), S(":"))))))
                    {
                        var unpack791 = Fn_host(Add(j_768, I(1)));
                        indicator_766 = unpack791[I(0)];
                        j_768 = unpack791[I(1)];
                    }
                }

                direction_763 = Truth(output_773) ? S("output") : S("input");
                _ = Call(bindings_760, "append", Put(Put(Put(Dict(), S("host"), expression_764), S("indicator"), indicator_766), S("direction"), direction_763));
                _ = Call(Truth(output_773) ? outputs_774 : inputs_767, "append", expression_764);
                if (Truth(B(!Truth(output_773))))
                {
                    _ = Call(query_775, "append", S("?"));
                }

                continue;
            }

            if (Truth(B(!Truth(output_773))))
            {
                _ = Call(query_775, "append", Member(t_777, "text"));
            }
            else
            {
                if (Truth(B(!Equal(Member(t_777, "text"), S(",")))))
                {
                    throw new ParseException(Text(S("SQL INTO requires only host/indicator operands")));
                }
            }

            j_768 = Add(j_768, I(1));
        }

        V Fn_reference(V name_792, V? optional_793 = null)
        {
            optional_793 ??= B(false);
            V s_794 = Nil;
            if (Truth(And(optional_793, () => B(!Truth(Any(Sequence(from item795 in Items(Member(this, "symbols")) let s_796 = item795 select B(Equal(Call(s_796[S("name")], "upper"), name_792)))))))))
            {
                return Nil;
            }

            s_794 = M_resolve(name_792);
            return Put(Put(Put(Put(Dict(), S("kind"), S("reference")), S("symbolId"), s_794[S("id")]), S("subscripts"), ListOf()), S("modification"), Nil);
        }

        d_762 = M_dependency(S("sql"), words_780[I(0)], Call(S(" "), "join", Sequence(from item797 in Items(original_772) let t_798 = item797 select Member(t_798, "text"))), M_source(a_759), inputs_: inputs_767, outputs_: outputs_774, status_: Fn_reference(S("SQLCODE")));
        d_762[S("sql")] = Put(Put(Put(Put(Put(Put(Dict(), S("kind"), kind_770), S("cursor"), cursor_761), S("query"), Call(S(" "), "join", query_775)), S("bindings"), bindings_760), S("sqlstate"), Fn_reference(S("SQLSTATE"), B(true))), S("whenever"), Merge(Dict(), handlers_765));
        return Put(Put(Put(Put(Put(Dict(), S("kind"), S("external")), S("dependencyId"), d_762[S("id")]), S("operation"), kind_770), S("atEnd"), ListOf()), S("notEnd"), ListOf());
    }
}
