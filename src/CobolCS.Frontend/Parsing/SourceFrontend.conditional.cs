using static CobolCS.Frontend.Parsing.Values;

namespace CobolCS.Frontend.Parsing;

/// <summary>位置とIRを保持する conditional の解析処理です。</summary>
internal sealed partial class SourceFrontend : V
{
    /// <summary>
    /// 元の解析規則とIRの順序を保持します。
    /// </summary>
    /// <param name="tokens_">tokens の解析値。</param>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_conditional_tokens(V tokens_)
    {
        V directive_indices_179 = Nil;
        V error_180 = Nil;
        V index_181 = Nil;
        V result_182 = Nil;
        V source_183 = Nil;
        index_181 = I(0);
        directive_indices_179 = SetCopy();
        V Fn_directive()
        {
            if (Truth(Or(B(Compare(index_181, Length(tokens_)) >= 0), () => Or(B(!Equal(Member(tokens_[index_181], "text"), S("$"))), () => Call(Member(tokens_[index_181], "source"), "get", S("documentary"))))))
            {
                return Nil;
            }

            if (Truth(And(index_181, () => B(Equal(Member(tokens_[Subtract(index_181, I(1))], "source")[S("start")][S("line")], Member(tokens_[index_181], "source")[S("start")][S("line")])))))
            {
                return Nil;
            }

            return Truth(B(Compare(Add(index_181, I(1)), Length(tokens_)) < 0)) ? Member(tokens_[Add(index_181, I(1))], "upper") : S(string.Empty);
        }

        V Fn_line()
        {
            V location_184 = Nil;
            V start_185 = Nil;
            start_185 = index_181;
            location_184 = Member(tokens_[index_181], "source");
            while (Truth(And(B(Compare(index_181, Length(tokens_)) < 0), () => B(Equal(Member(tokens_[index_181], "source")[S("start")][S("line")], location_184[S("start")][S("line")])))))
            {
                index_181 = Add(index_181, I(1));
            }

            _ = Call(directive_indices_179, "update", Range(start_185, index_181));
            return ListOf(from item186 in Items(tokens_[Slice(start_185, index_181, Nil)]) let t_187 = item186 select Member(t_187, "upper"));
        }

        V Fn_region(V? nested_188 = null)
        {
            nested_188 ??= B(false);
            V excluded_189 = Nil;
            V first_190 = Nil;
            V info_191 = Nil;
            V last_192 = Nil;
            V no_193 = Nil;
            V raw_194 = Nil;
            V result_195 = Nil;
            V selected_196 = Nil;
            V source_197 = Nil;
            V start_198 = Nil;
            V token_199 = Nil;
            V value_200 = Nil;
            V word_201 = Nil;
            V yes_202 = Nil;
            result_195 = ListOf();
            while (Truth(B(Compare(index_181, Length(tokens_)) < 0)))
            {
                word_201 = Fn_directive();
                if (Truth(B(Contains(ListOf(S("ELSE"), S("END")), word_201))))
                {
                    if (Truth(B(!Truth(nested_188))))
                    {
                        throw new ArgumentException(Text(Add(S("CONDITIONAL_SYNTAX: unmatched $"), word_201)));
                    }

                    break;
                }

                if (Truth(B(ReferenceEquals(word_201, Nil))))
                {
                    _ = Call(result_195, "append", tokens_[index_181]);
                    index_181 = Add(index_181, I(1));
                    continue;
                }

                start_198 = index_181;
                if (Truth(B(!Equal(Fn_line(), ListOf(S("$"), S("IF"), S("P64"), S("SET"))))))
                {
                    throw new ArgumentException(Text(S("CONDITIONAL_SYNTAX: expected $IF P64 SET")));
                }

                yes_202 = Fn_region(B(true));
                no_193 = ListOf();
                if (Truth(B(Equal(Fn_directive(), S("ELSE")))))
                {
                    if (Truth(B(!Equal(Fn_line(), ListOf(S("$"), S("ELSE"))))))
                    {
                        throw new ArgumentException(Text(S("CONDITIONAL_SYNTAX: malformed $ELSE")));
                    }

                    no_193 = Fn_region(B(true));
                }

                if (Truth(Or(B(!Equal(Fn_directive(), S("END"))), () => B(!Equal(Fn_line(), ListOf(S("$"), S("END")))))))
                {
                    throw new ArgumentException(Text(S("CONDITIONAL_SYNTAX: missing $END or duplicate $ELSE")));
                }

                var unpack203 = ListOf(Member(tokens_[start_198], "source"), Member(tokens_[Subtract(index_181, I(1))], "source"));
                first_190 = unpack203[I(0)];
                last_192 = unpack203[I(1)];
                raw_194 = Call(Member(this, "sources")[first_190[S("file")]][S("text")], "encode", S("utf-8"));
                source_197 = Put(Put(Merge(Dict(), first_190), S("end"), last_192[S("end")]), S("raw"), Call(raw_194[Slice(first_190[S("start")][S("byteOffset")], last_192[S("end")][S("byteOffset")], Nil)], "decode", S("utf-8")));
                value_200 = Member(this, "config")[S("p64")];
                info_191 = Put(Put(Put(Put(Put(Dict(), S("condition"), S("P64")), S("value"), value_200), S("source"), source_197), S("branches"), ListOf(from item210 in Items(ListOf(yes_202, no_193)) let branch_211 = item210 select ListOf(from item212 in Items(branch_211) let t_213 = item212 select Put(Put(Dict(), S("text"), Member(t_213, "text")), S("source"), Member(t_213, "source"))))), S("tokens"), ListOf(from item214 in Items(tokens_[Slice(start_198, index_181, Nil)]) let t_215 = item214 select Put(Put(Put(Put(Dict(), S("text"), Member(t_215, "text")), S("source"), Member(t_215, "source")), S("ownerKind"), S("conditional")), S("reason"), S("P64 selection or opaque region"))));
                _ = Call(Member(this, "conditionals"), "append", info_191);
                if (Truth(B(ReferenceEquals(value_200, Nil))))
                {
                    _ = Call(result_195, "append", Token(S("CONDITIONAL-STUB"), Put(Merge(Dict(), source_197), S("conditional"), info_191)));
                }
                else
                {
                    var unpack216 = Truth(value_200) ? ListOf(yes_202, no_193) : ListOf(no_193, yes_202);
                    selected_196 = unpack216[I(0)];
                    excluded_189 = unpack216[I(1)];
                    _ = Call(result_195, "extend", selected_196);
                    foreach (var item217 in Items(excluded_189))
                    {
                        token_199 = item217;
                        _ = Call(Member(this, "excluded"), "append", Put(Put(Dict(), S("reason"), S("conditional-P64-branch-not-selected")), S("source"), Member(token_199, "source")));
                    }
                }
            }

            return result_195;
        }

        try
        {
            result_182 = Fn_region();
            if (Truth(B(!ReferenceEquals(Member(this, "config")[S("p64")], Nil))))
            {
                _ = Call(Member(this, "excluded"), "extend", Sequence(from item218 in Items(Sorted(directive_indices_179)) let i_219 = item218 select Put(Put(Dict(), S("reason"), S("conditional-directive-evaluated")), S("source"), Member(tokens_[i_219], "source"))));
            }

            return result_182;
        }
        catch (Exception error220) when (error220 is ArgumentException)
        {
            error_180 = S(error220.Message);
            source_183 = Member(tokens_[Minimum(new V[] { index_181, Subtract(Length(tokens_), I(1)) }, null, null)], "source");
            _ = M_diagnostic(S("CONDITIONAL_SYNTAX"), StringValue(error_180), source_183);
            _ = Call(Member(this, "rejected"), "extend", Sequence(from item221 in Items(tokens_) let t_222 = item221 select Put(Put(Put(Put(Dict(), S("text"), Member(t_222, "text")), S("source"), Member(t_222, "source")), S("ownerKind"), S("unknown")), S("ownerId"), S("$conditionalRejected"))));
            return ListOf(Token(S("CONDITIONAL-ERROR"), Put(Merge(Dict(), source_183), S("error"), StringValue(error_180))));
        }
    }
}
