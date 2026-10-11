using static CobolCS.Frontend.Parsing.Values;

namespace CobolCS.Frontend.Parsing;

/// <summary>位置とIRを保持する expressions の解析処理です。</summary>
internal sealed partial class Parser : V
{
    /// <summary>
    /// 式を演算子の優先順位で木へ変換する（Pratt解析）。
    ///
    /// min_bpは、今の部分式が取り込んでよい演算子の最低優先度。
    /// 例えばA + B * Cでは*が+より強いため、右辺をB * Cとして先にまとめる。
    /// 結果はbinary/reference/literal等のIRで、PythonやTSのコード文字列ではない。
    /// </summary>
    /// <param name="min_bp_">min_bp の解析値。</param>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_expr(V? min_bp_ = null)
    {
        min_bp_ ??= I(0);
        V ignored223 = Nil;
        V args_224 = Nil;
        V bp_225 = Nil;
        V classes_226 = Nil;
        V comparator_227 = Nil;
        V count_228 = Nil;
        V first_229 = Nil;
        V inherited_230 = Nil;
        V j_231 = Nil;
        V left_232 = Nil;
        V length_233 = Nil;
        V mod_234 = Nil;
        V name_235 = Nil;
        V negate_236 = Nil;
        V negated_237 = Nil;
        V number_238 = Nil;
        V op_239 = Nil;
        V operand_240 = Nil;
        V parenthesized_241 = Nil;
        V powers_242 = Nil;
        V quals_243 = Nil;
        V quoted_244 = Nil;
        V right_245 = Nil;
        V signs_246 = Nil;
        V subs_247 = Nil;
        V symbol_248 = Nil;
        V t_249 = Nil;
        V u_250 = Nil;
        V value_251 = Nil;
        V word_252 = Nil;
        t_249 = M_take();
        u_250 = Member(t_249, "upper");
        if (Truth(B(Contains(ListOf(S("NOT"), S("-"), S("+")), u_250))))
        {
            operand_240 = M_expr(Truth(B(!Equal(u_250, S("NOT")))) ? I(7) : I(3));
            left_232 = Truth(B(Equal(u_250, S("+")))) ? operand_240 : (Truth(And(B(Equal(u_250, S("-"))), () => And(B(Equal(operand_240[S("kind")], S("literal"))), () => B(Equal(operand_240[S("valueType")], S("numeric")))))) ? Lit(Truth(Call(operand_240[S("value")], "startswith", S("-"))) ? operand_240[S("value")][Slice(I(1), Nil, Nil)] : Add(S("-"), operand_240[S("value")])) : Put(Put(Put(Dict(), S("kind"), S("unary")), S("op"), u_250), S("operand"), operand_240));
        }
        else
        {
            if (Truth(B(Equal(u_250, S("(")))))
            {
                left_232 = M_expr();
                _ = M_require(S(")"));
            }
            else
            {
                if (Truth(And(B(Contains(ListOf(S("NC"), S("N")), u_250)), () => And(Call(M_peek(), "startswith", ListOf(S("\""), S("'"))), () => And(B(Equal(Member(t_249, "source")[S("file")], Member(Member(this, "ts")[Member(this, "i")], "source")[S("file")])), () => B(Equal(Member(t_249, "source")[S("end")][S("byteOffset")], Member(Member(this, "ts")[Member(this, "i")], "source")[S("start")][S("byteOffset")])))))))
                {
                    quoted_244 = Member(M_take(), "text");
                    left_232 = Lit(Call(quoted_244[Slice(I(1), Subtract(I(0), I(1)), Nil)], "replace", Multiply(quoted_244[I(0)], I(2)), quoted_244[I(0)]), S("national"));
                }
                else
                {
                    if (Truth(Call(Member(t_249, "text"), "startswith", ListOf(S("\""), S("'")))))
                    {
                        left_232 = Lit(Call(Member(t_249, "text")[Slice(I(1), Subtract(I(0), I(1)), Nil)], "replace", Multiply(Member(t_249, "text")[I(0)], I(2)), Member(t_249, "text")[I(0)]), S("text"));
                    }
                    else
                    {
                        if (Truth(RegexFullmatch(S("\\d+(?:\\.\\d+)?"), Member(t_249, "text"))))
                        {
                            number_238 = Member(t_249, "text");
                            if (Truth(B(Equal(Member(Member(this, "f"), "config")[S("decimalPoint")], S("comma")))))
                            {
                                if (Truth(B(Contains(number_238, S(".")))))
                                {
                                    throw new ParseException(Text(S("Decimal literal requires comma")));
                                }

                                if (Truth(And(B(Equal(M_peek(), S(","))), () => And(Call(M_peek(I(1)), "isdigit"), () => And(B(Equal(Member(t_249, "source")[S("end")][S("byteOffset")], Member(Member(this, "ts")[Member(this, "i")], "source")[S("start")][S("byteOffset")])), () => B(Equal(Member(Member(this, "ts")[Member(this, "i")], "source")[S("end")][S("byteOffset")], Member(Member(this, "ts")[Add(Member(this, "i"), I(1))], "source")[S("start")][S("byteOffset")])))))))
                                {
                                    _ = M_take();
                                    number_238 = Add(number_238, Add(S("."), Member(M_take(), "text")));
                                }
                            }

                            left_232 = Lit(number_238);
                        }
                        else
                        {
                            if (Truth(B(Contains(ListOf(S("TRUE"), S("FALSE")), u_250))))
                            {
                                left_232 = Lit(Call(u_250, "lower"), S("boolean"));
                            }
                            else
                            {
                                if (Truth(B(Equal(u_250, S("FUNCTION")))))
                                {
                                    name_235 = Member(M_take(), "upper");
                                    args_224 = ListOf();
                                    if (Truth(M_accept(S("("))))
                                    {
                                        while (Truth(And(M_peek(), () => B(!Equal(M_peek(), S(")"))))))
                                        {
                                            if (Truth(And(B(Equal(name_235, S("TRIM"))), () => B(Contains(ListOf(S("LEADING"), S("TRAILING"), S("BOTH")), M_peek())))))
                                            {
                                                _ = Call(args_224, "append", Lit(Member(M_take(), "upper"), S("text")));
                                            }
                                            else
                                            {
                                                _ = Call(args_224, "append", M_expr());
                                            }

                                            _ = M_accept(S(","));
                                        }

                                        _ = M_require(S(")"));
                                    }

                                    left_232 = Put(Put(Put(Dict(), S("kind"), S("function")), S("name"), name_235), S("arguments"), args_224);
                                }
                                else
                                {
                                    if (Truth(B(Equal(u_250, S("ALL")))))
                                    {
                                        value_251 = M_expr(I(6));
                                        if (Truth(B(!Equal(value_251[S("kind")], S("literal")))))
                                        {
                                            throw new ParseException(Text(S("ALL requires literal")));
                                        }

                                        left_232 = Put(Put(Dict(), S("kind"), S("allLiteral")), S("value"), value_251);
                                    }
                                    else
                                    {
                                        if (Truth(B(Contains(ListOf(S("HIGH-VALUE"), S("HIGH-VALUES"), S("LOW-VALUE"), S("LOW-VALUES"), S("QUOTE"), S("QUOTES")), u_250))))
                                        {
                                            left_232 = Put(Put(Dict(), S("kind"), S("figurative")), S("value"), Truth(Call(u_250, "startswith", S("HIGH"))) ? S("high") : (Truth(Call(u_250, "startswith", S("LOW"))) ? S("low") : S("quote")));
                                        }
                                        else
                                        {
                                            if (Truth(B(Contains(ListOf(S("ZERO"), S("ZEROS"), S("ZEROES")), u_250))))
                                            {
                                                left_232 = Put(Put(Dict(), S("kind"), S("figurative")), S("value"), S("zero"));
                                            }
                                            else
                                            {
                                                if (Truth(B(Contains(ListOf(S("SPACE"), S("SPACES")), u_250))))
                                                {
                                                    left_232 = Put(Put(Dict(), S("kind"), S("figurative")), S("value"), S("space"));
                                                }
                                                else
                                                {
                                                    subs_247 = ListOf();
                                                    mod_234 = Nil;
                                                    quals_243 = ListOf();
                                                    if (Truth(M_accept(S("("))))
                                                    {
                                                        first_229 = M_expr();
                                                        if (Truth(M_accept(S(":"))))
                                                        {
                                                            length_233 = M_expr();
                                                            mod_234 = Put(Put(Dict(), S("start"), first_229), S("length"), length_233);
                                                        }
                                                        else
                                                        {
                                                            _ = Call(subs_247, "append", first_229);
                                                            while (Truth(B(!Equal(M_peek(), S(")")))))
                                                            {
                                                                _ = M_accept(S(","));
                                                                _ = Call(subs_247, "append", M_expr());
                                                            }
                                                        }

                                                        _ = M_require(S(")"));
                                                    }

                                                    while (Truth(B(Contains(ListOf(S("OF"), S("IN")), M_peek()))))
                                                    {
                                                        _ = M_take();
                                                        _ = Call(quals_243, "append", Member(M_take(), "text"));
                                                    }

                                                    symbol_248 = M_resolve(Member(t_249, "text"), quals_243);
                                                    if (Truth(And(B(Equal(symbol_248[S("level")], I(88))), () => mod_234)))
                                                    {
                                                        throw new ParseException(Text(S("88 reference modification is invalid")));
                                                    }

                                                    left_232 = Truth(B(Equal(symbol_248[S("level")], I(88)))) ? Put(Put(Put(Dict(), S("kind"), S("condition88")), S("symbolId"), symbol_248[S("id")]), S("subscripts"), subs_247) : Put(Put(Put(Put(Dict(), S("kind"), S("reference")), S("symbolId"), symbol_248[S("id")]), S("subscripts"), subs_247), S("modification"), mod_234);
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

        powers_242 = Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Dict(), S("OR"), I(1)), S("AND"), I(2)), S("="), I(3)), S("<>"), I(3)), S("<"), I(3)), S(">"), I(3)), S("<="), I(3)), S(">="), I(3)), S("+"), I(4)), S("-"), I(4)), S("*"), I(5)), S("/"), I(5)), S("**"), I(8));
        while (Truth(B(true)))
        {
            if (Truth(B(Compare(min_bp_, I(3)) <= 0)))
            {
                j_231 = I(0);
                if (Truth(B(Equal(M_peek(j_231), S("IS")))))
                {
                    j_231 = Add(j_231, I(1));
                }

                negated_237 = B(Equal(M_peek(j_231), S("NOT")));
                if (Truth(negated_237))
                {
                    j_231 = Add(j_231, I(1));
                }

                word_252 = M_peek(j_231);
                classes_226 = Put(Put(Put(Put(Dict(), S("NUMERIC"), S("numeric")), S("ALPHABETIC"), S("alphabetic")), S("ALPHABETIC-UPPER"), S("upper")), S("ALPHABETIC-LOWER"), S("lower"));
                signs_246 = Put(Put(Put(Dict(), S("POSITIVE"), S("positive")), S("NEGATIVE"), S("negative")), S("ZERO"), S("zero"));
                if (Truth(Or(B(Contains(classes_226, word_252)), () => B(Contains(signs_246, word_252)))))
                {
                    foreach (var item253 in Items(Range(Add(j_231, I(1)))))
                    {
                        ignored223 = item253;
                        _ = M_take();
                    }

                    left_232 = Truth(B(Contains(classes_226, word_252))) ? Put(Put(Put(Put(Dict(), S("kind"), S("classCondition")), S("operand"), left_232), S("category"), classes_226[word_252]), S("negated"), negated_237) : Put(Put(Put(Put(Dict(), S("kind"), S("signCondition")), S("operand"), left_232), S("sign"), signs_246[word_252]), S("negated"), negated_237);
                    continue;
                }
            }

            op_239 = M_peek();
            bp_225 = Call(powers_242, "get", op_239, I(0));
            count_228 = I(1);
            negate_236 = B(false);
            if (Truth(And(B(Compare(min_bp_, I(3)) <= 0), () => Or(B(Contains(ListOf(S("IS"), S("EQUAL"), S("GREATER"), S("LESS")), op_239)), () => And(B(Equal(op_239, S("NOT"))), () => B(Contains(ListOf(S("="), S("<>"), S("<"), S(">"), S("<="), S(">="), S("EQUAL"), S("GREATER"), S("LESS")), M_peek(I(1)))))))))
            {
                j_231 = I(0);
                if (Truth(B(Equal(M_peek(j_231), S("IS")))))
                {
                    j_231 = Add(j_231, I(1));
                }

                if (Truth(B(Equal(M_peek(j_231), S("NOT")))))
                {
                    negate_236 = B(true);
                    j_231 = Add(j_231, I(1));
                }

                word_252 = M_peek(j_231);
                j_231 = Add(j_231, I(1));
                if (Truth(B(Contains(ListOf(S("EQUAL"), S("GREATER"), S("LESS")), word_252))))
                {
                    op_239 = Put(Put(Put(Dict(), S("EQUAL"), S("=")), S("GREATER"), S(">")), S("LESS"), S("<"))[word_252];
                    if (Truth(B(Contains(ListOf(S("TO"), S("THAN")), M_peek(j_231)))))
                    {
                        j_231 = Add(j_231, I(1));
                    }

                    if (Truth(And(B(!Equal(word_252, S("EQUAL"))), () => And(B(Equal(M_peek(j_231), S("OR"))), () => B(Equal(M_peek(Add(j_231, I(1))), S("EQUAL")))))))
                    {
                        op_239 = Add(op_239, S("="));
                        j_231 = Add(j_231, I(2));
                        if (Truth(B(Equal(M_peek(j_231), S("TO")))))
                        {
                            j_231 = Add(j_231, I(1));
                        }
                    }
                }
                else
                {
                    if (Truth(B(Contains(ListOf(S("="), S("<>"), S("<"), S(">"), S("<="), S(">=")), word_252))))
                    {
                        op_239 = word_252;
                    }
                    else
                    {
                        throw new ParseException(Text(S("Unsupported comparison wording")));
                    }
                }

                count_228 = j_231;
                bp_225 = I(3);
            }

            if (Truth(Or(B(!Truth(bp_225)), () => B(Compare(bp_225, min_bp_) < 0))))
            {
                break;
            }

            foreach (var item254 in Items(Range(count_228)))
            {
                ignored223 = item254;
                _ = M_take();
            }

            V Fn_relation(V node_255)
            {
                if (Truth(And(B(Equal(node_255[S("kind")], S("binary"))), () => B(Contains(ListOf(S("="), S("<>"), S("<"), S(">"), S("<="), S(">=")), node_255[S("op")])))))
                {
                    return ListOf(node_255[S("left")], node_255[S("op")]);
                }

                if (Truth(And(B(Equal(node_255[S("kind")], S("binary"))), () => B(Contains(ListOf(S("AND"), S("OR")), node_255[S("op")])))))
                {
                    return Fn_relation(node_255[S("right")]);
                }

                return Nil;
            }

            inherited_230 = Truth(And(B(Contains(ListOf(S("AND"), S("OR")), op_239)), () => B(!Equal(u_250, S("("))))) ? Fn_relation(left_232) : Nil;
            if (Truth(And(inherited_230, () => B(Contains(ListOf(S("="), S("<>"), S("<"), S(">"), S("<="), S(">=")), M_peek())))))
            {
                comparator_227 = Member(M_take(), "upper");
                right_245 = Binary(comparator_227, inherited_230[I(0)], M_expr(I(4)));
            }
            else
            {
                parenthesized_241 = B(Equal(M_peek(), S("(")));
                right_245 = M_expr(Truth(B(Equal(op_239, S("**")))) ? bp_225 : Add(bp_225, I(1)));
                if (Truth(And(inherited_230, () => And(B(!Truth(parenthesized_241)), () => And(B(Equal(right_245[S("kind")], S("literal"))), () => B(!Equal(right_245[S("valueType")], S("boolean"))))))))
                {
                    right_245 = Binary(inherited_230[I(1)], inherited_230[I(0)], right_245);
                }
            }

            left_232 = Binary(op_239, left_232, right_245);
            if (Truth(negate_236))
            {
                left_232 = Put(Put(Put(Dict(), S("kind"), S("unary")), S("op"), S("NOT")), S("operand"), left_232);
            }
        }

        return left_232;
    }

    /// <summary>
    /// 式のうち、書込み可能な項目参照だけを受け入れる。MOVEの受け側へ定数を書けないようにする。
    /// </summary>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_reference()
    {
        V e_256 = Nil;
        e_256 = M_expr(I(6));
        if (Truth(B(!Equal(e_256[S("kind")], S("reference")))))
        {
            throw new ParseException(Text(S("Expected writable item reference")));
        }

        return e_256;
    }

    /// <summary>
    /// 次の命令・終端語・ピリオドに達するまで複数operandを読む。DISPLAY A Bのような列を扱う。
    /// </summary>
    /// <param name="stops_">stops の解析値。</param>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_expressions(V? stops_ = null)
    {
        stops_ ??= ListOf();
        V result_257 = Nil;
        result_257 = ListOf();
        while (Truth(And(M_peek(), () => B(!Contains(Union(Union(Union(SetCopy(stops_), Verbs), Ends), SetOf(S("."))), M_peek())))))
        {
            _ = M_accept(S(","));
            _ = Call(result_257, "append", M_expr());
        }

        if (Truth(B(!Truth(result_257))))
        {
            throw new ParseException(Text(S("Expected operand")));
        }

        return result_257;
    }
}
