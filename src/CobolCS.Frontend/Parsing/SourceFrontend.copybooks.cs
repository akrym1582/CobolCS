using static CobolCS.Frontend.Parsing.Values;

namespace CobolCS.Frontend.Parsing;

/// <summary>位置とIRを保持する copybooks の解析処理です。</summary>
internal sealed partial class SourceFrontend : V
{
    /// <summary>
    /// 元の解析規則とIRの順序を保持します。
    /// </summary>
    /// <param name="book_">book の解析値。</param>
    /// <param name="path_">path の解析値。</param>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_find_copy(V book_, V path_)
    {
        V directory_104 = Nil;
        V matches_105 = Nil;
        V names_106 = Nil;
        V parent_107 = Nil;
        V requested_108 = Nil;
        book_ = Call(book_, "replace", S("\\"), S("/"));
        foreach (var item109 in Items(ListOf(Enumerable.Empty<V>().Concat(new V[] { Member(path_, "parent") }).Concat(Items(Member(this, "copies"))))))
        {
            directory_104 = item109;
            requested_108 = Divide(directory_104, book_);
            parent_107 = Member(requested_108, "parent");
            if (Truth(B(!Truth(Call(parent_107, "is_dir")))))
            {
                continue;
            }

            names_106 = SetOf(from item110 in Items(ListOf(S(string.Empty), S(".cpy"), S(".copy"))) let suffix_111 = item110 select Call(Add(Member(requested_108, "name"), suffix_111), "upper"));
            matches_105 = Sorted(Sequence(from item112 in Items(Call(parent_107, "iterdir")) let p_113 = item112 where Truth(And(Call(p_113, "is_file"), () => B(Contains(names_106, Call(Member(p_113, "name"), "upper"))))) select p_113));
            if (Truth(B(Compare(Length(matches_105), I(1)) > 0)))
            {
                throw new ArgumentException(Text(Add(S("COPY_AMBIGUOUS: "), book_)));
            }

            if (Truth(matches_105))
            {
                return matches_105[I(0)];
            }
        }

        return Nil;
    }

    /// <summary>
    /// 元の解析規則とIRの順序を保持します。
    /// </summary>
    /// <param name="tokens_">tokens の解析値。</param>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_replacement_pairs(V tokens_)
    {
        V i_114 = Nil;
        V mode_115 = Nil;
        V new_116 = Nil;
        V new_text_117 = Nil;
        V old_118 = Nil;
        V old_text_119 = Nil;
        V pairs_120 = Nil;
        pairs_120 = ListOf();
        i_114 = I(0);
        V Fn_operand()
        {
            V start_121 = Nil;
            V value_122 = Nil;
            if (Truth(B(Compare(i_114, Length(tokens_)) >= 0)))
            {
                throw new ArgumentException(Text(S("COPY_REPLACING: missing operand")));
            }

            if (Truth(B(!Equal(Member(tokens_[i_114], "text"), S("==")))))
            {
                value_122 = ListOf(tokens_[i_114]);
                i_114 = Add(i_114, I(1));
                return value_122;
            }

            i_114 = Add(i_114, I(1));
            start_121 = i_114;
            while (Truth(And(B(Compare(i_114, Length(tokens_)) < 0), () => B(!Equal(Member(tokens_[i_114], "text"), S("=="))))))
            {
                i_114 = Add(i_114, I(1));
            }

            if (Truth(B(Equal(i_114, Length(tokens_)))))
            {
                throw new ArgumentException(Text(S("COPY_REPLACING: unclosed pseudo-text")));
            }

            value_122 = tokens_[Slice(start_121, i_114, Nil)];
            i_114 = Add(i_114, I(1));
            return value_122;
        }

        while (Truth(B(Compare(i_114, Length(tokens_)) < 0)))
        {
            mode_115 = S("exact");
            if (Truth(B(Contains(ListOf(S("LEADING"), S("TRAILING")), Member(tokens_[i_114], "upper")))))
            {
                mode_115 = Call(Member(tokens_[i_114], "upper"), "lower");
                i_114 = Add(i_114, I(1));
            }

            old_118 = Fn_operand();
            if (Truth(Or(B(!Truth(old_118)), () => Or(B(Compare(i_114, Length(tokens_)) >= 0), () => B(!Equal(Member(tokens_[i_114], "upper"), S("BY")))))))
            {
                throw new ArgumentException(Text(S("COPY_REPLACING: requires nonempty pattern and BY")));
            }

            i_114 = Add(i_114, I(1));
            new_116 = Fn_operand();
            if (Truth(B(!Equal(mode_115, S("exact")))))
            {
                old_text_119 = Call(S(string.Empty), "join", Sequence(from item123 in Items(old_118) let t_124 = item123 select Member(t_124, "text")));
                new_text_117 = Call(S(string.Empty), "join", Sequence(from item125 in Items(new_116) let t_126 = item125 select Member(t_126, "text")));
                if (Truth(Or(B(!Truth(RegexFullmatch(S("[\\w\\-\uFF0D\u2212]+"), old_text_119))), () => And(new_text_117, () => B(!Truth(RegexFullmatch(S("[\\w\\-\uFF0D\u2212]+"), new_text_117)))))))
                {
                    throw new ArgumentException(Text(S("COPY_REPLACING: invalid partial-word replacement")));
                }

                old_118 = ListOf(Token(old_text_119, Member(old_118[I(0)], "source")));
                new_116 = Truth(new_116) ? ListOf(Token(new_text_117, Member(new_116[I(0)], "source"))) : ListOf();
            }

            _ = Call(pairs_120, "append", ListOf(mode_115, old_118, new_116));
        }

        if (Truth(B(!Truth(pairs_120))))
        {
            throw new ArgumentException(Text(S("COPY_REPLACING: missing pairs")));
        }

        return pairs_120;
    }

    /// <summary>
    /// 元の解析規則とIRの順序を保持します。
    /// </summary>
    /// <param name="tokens_">tokens の解析値。</param>
    /// <param name="pairs_">pairs の解析値。</param>
    /// <param name="origin_">origin の解析値。</param>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_replace_copy(V tokens_, V pairs_, V origin_)
    {
        V consumed_127 = Nil;
        V fragment_128 = Nil;
        V i_129 = Nil;
        V last_130 = Nil;
        V match_131 = Nil;
        V mode_132 = Nil;
        V new_133 = Nil;
        V old_134 = Nil;
        V output_136 = Nil;
        V replaced_137 = Nil;
        V replacement_138 = Nil;
        V selected_139 = Nil;
        V source_140 = Nil;
        V text_141 = Nil;
        V values_142 = Nil;
        V word_143 = Nil;
        var unpack144 = ListOf(ListOf(), I(0));
        output_136 = unpack144[I(0)];
        i_129 = unpack144[I(1)];
        while (Truth(B(Compare(i_129, Length(tokens_)) < 0)))
        {
            replaced_137 = B(false);
            foreach (var item145 in Items(pairs_))
            {
                var unpack146 = item145;
                mode_132 = unpack146[I(0)];
                old_134 = unpack146[I(1)];
                new_133 = unpack146[I(2)];
                selected_139 = tokens_[Slice(i_129, Add(i_129, Length(old_134)), Nil)];
                if (Truth(B(Equal(mode_132, S("exact")))))
                {
                    match_131 = And(B(Equal(Length(selected_139), Length(old_134))), () => All(Sequence(from item147 in Items(Zip(selected_139, old_134)) let a_148 = item147[I(0)] let b_149 = item147[I(1)] select Same(a_148, b_149))));
                    values_142 = ListOf(from item150 in Items(new_133) let t_151 = item150 select Member(t_151, "text"));
                    consumed_127 = Length(old_134);
                }
                else
                {
                    var unpack152 = ListOf(Member(tokens_[i_129], "text"), Member(old_134[I(0)], "text"));
                    word_143 = unpack152[I(0)];
                    fragment_128 = unpack152[I(1)];
                    match_131 = And(Boolean(RegexFullmatch(S("[^\\W\\d][\\w\\-\uFF0D\u2212]*"), word_143)), () => (Truth(B(Equal(mode_132, S("leading")))) ? Call(Call(word_143, "upper"), "startswith", Call(fragment_128, "upper")) : Call(Call(word_143, "upper"), "endswith", Call(fragment_128, "upper"))));
                    replacement_138 = Truth(new_133) ? Member(new_133[I(0)], "text") : S(string.Empty);
                    values_142 = ListOf(Truth(B(Equal(mode_132, S("leading")))) ? Add(replacement_138, word_143[Slice(Length(fragment_128), Nil, Nil)]) : Add(word_143[Slice(Nil, Subtract(I(0), Length(fragment_128)), Nil)], replacement_138));
                    consumed_127 = I(1);
                }

                if (Truth(match_131))
                {
                    foreach (var item153 in Items(values_142))
                    {
                        text_141 = item153;
                        source_140 = Merge(Dict(), Member(tokens_[i_129], "source"));
                        last_130 = Member(tokens_[Subtract(Add(i_129, consumed_127), I(1))], "source");
                        if (Truth(B(Equal(source_140[S("file")], last_130[S("file")]))))
                        {
                            source_140[S("end")] = last_130[S("end")];
                            source_140[S("raw")] = OriginalText(source_140);
                        }

                        source_140[S("replacementOrigins")] = ListOf(Enumerable.Empty<V>().Concat(Items(Call(source_140, "get", S("replacementOrigins"), ListOf()))).Concat(new V[] { origin_ }));
                        _ = Call(output_136, "append", Token(text_141, source_140));
                    }

                    i_129 = Add(i_129, consumed_127);
                    replaced_137 = B(true);
                    break;
                }
            }

            if (Truth(B(!Truth(replaced_137))))
            {
                _ = Call(output_136, "append", tokens_[i_129]);
                i_129 = Add(i_129, I(1));
            }
        }

        return output_136;
    }

    /// <summary>
    /// 元の解析規則とIRの順序を保持します。
    /// </summary>
    /// <param name="tokens_">tokens の解析値。</param>
    /// <param name="path_">path の解析値。</param>
    /// <param name="stack_">stack の解析値。</param>
    /// <param name="active_">active の解析値。</param>
    /// <param name="name_">name の解析値。</param>
    /// <param name="raw_">raw の解析値。</param>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_expand_copies(V tokens_, V path_, V stack_, V active_, V name_, V raw_)
    {
        V book_154 = Nil;
        V book_index_155 = Nil;
        V builtin_156 = Nil;
        V candidate_157 = Nil;
        V code_158 = Nil;
        V declaration_159 = Nil;
        V error_160 = Nil;
        V expanded_161 = Nil;
        V i_162 = Nil;
        V j_163 = Nil;
        V message_164 = Nil;
        V pairs_165 = Nil;
        V pseudo_166 = Nil;
        V rest_167 = Nil;
        V result_168 = Nil;
        V source_169 = Nil;
        V sql_include_170 = Nil;
        V t_171 = Nil;
        var unpack172 = ListOf(ListOf(), I(0));
        result_168 = unpack172[I(0)];
        i_162 = unpack172[I(1)];
        while (Truth(B(Compare(i_162, Length(tokens_)) < 0)))
        {
            t_171 = tokens_[i_162];
            sql_include_170 = B(Equal(ListOf(from item173 in Items(tokens_[Slice(i_162, Add(i_162, I(3)), Nil)]) let x_174 = item173 select Member(x_174, "upper")), ListOf(S("EXEC"), S("SQL"), S("INCLUDE"))));
            if (Truth(Or(Call(Member(t_171, "source"), "get", S("documentary")), () => And(B(!Equal(Member(t_171, "upper"), S("COPY"))), () => B(!Truth(sql_include_170))))))
            {
                _ = Call(result_168, "append", t_171);
                i_162 = Add(i_162, I(1));
                continue;
            }

            var unpack175 = ListOf(Add(i_162, I(1)), B(false));
            j_163 = unpack175[I(0)];
            pseudo_166 = unpack175[I(1)];
            while (Truth(B(Compare(j_163, Length(tokens_)) < 0)))
            {
                if (Truth(B(Equal(Member(tokens_[j_163], "text"), S("==")))))
                {
                    pseudo_166 = B(!Truth(pseudo_166));
                }

                if (Truth(Or(And(sql_include_170, () => B(Equal(Member(tokens_[j_163], "upper"), S("END-EXEC")))), () => And(B(!Truth(sql_include_170)), () => And(B(Equal(Member(tokens_[j_163], "text"), S("."))), () => B(!Truth(pseudo_166)))))))
                {
                    break;
                }

                j_163 = Add(j_163, I(1));
            }

            source_169 = Merge(Dict(), Member(t_171, "source"));
            if (Truth(B(Compare(j_163, Length(tokens_)) < 0)))
            {
                source_169[S("end")] = Member(tokens_[j_163], "source")[S("end")];
                source_169[S("raw")] = OriginalText(source_169);
            }

            try
            {
                if (Truth(B(Equal(j_163, Length(tokens_)))))
                {
                    throw new ArgumentException(Text(S("COPY_TERMINATOR: unterminated include")));
                }

                book_index_155 = Add(i_162, Truth(sql_include_170) ? I(3) : I(1));
                if (Truth(B(Compare(book_index_155, j_163) >= 0)))
                {
                    throw new ArgumentException(Text(S("COPY_REPLACING: missing book name")));
                }

                book_154 = Call(Member(tokens_[book_index_155], "text"), "strip", S("'\""));
                rest_167 = tokens_[Slice(Add(book_index_155, I(1)), j_163, Nil)];
                pairs_165 = ListOf();
                if (Truth(rest_167))
                {
                    if (Truth(Or(sql_include_170, () => B(!Equal(Member(rest_167[I(0)], "upper"), S("REPLACING"))))))
                    {
                        throw new ArgumentException(Text(S("COPY_REPLACING: unexpected clause")));
                    }

                    pairs_165 = M_replacement_pairs(rest_167[Slice(I(1), Nil, Nil)]);
                }

                candidate_157 = M_find_copy(book_154, path_);
                if (Truth(And(B(ReferenceEquals(candidate_157, Nil)), () => And(sql_include_170, () => And(B(Equal(Call(book_154, "upper"), S("SQLCA"))), () => B(Equal(Member(this, "config")[S("sqlcaInclude")], S("portable-minimal"))))))))
                {
                    builtin_156 = Put(Put(Merge(Dict(), source_169), S("origin"), S("portable-minimal-sqlca")), S("copyStack"), ListOf(Enumerable.Empty<V>().Concat(Items(source_169[S("copyStack")])).Concat(new V[] { S("builtin:portable-minimal-sqlca") })));
                    _ = M_diagnostic(S("PORTABLE_SQLCA"), S("Explicit portable SQLCODE/SQLSTATE declaration only; not full DB2 SQLCA"), builtin_156, S("review"));
                    declaration_159 = S("01 SQLCODE PIC S9(9) VALUE 0. 01 SQLSTATE PIC X(5) VALUE \"00000\".");
                    _ = Call(result_168, "extend", Sequence(from item176 in Items(Call(TokenPattern, "finditer", declaration_159)) let m_177 = item176 select Token(Call(m_177, "group"), builtin_156)));
                }
                else
                {
                    if (Truth(B(ReferenceEquals(candidate_157, Nil))))
                    {
                        _ = Call(Member(this, "missing_copies"), "append", book_154);
                        _ = M_diagnostic(Truth(Member(this, "allow_missing_copy")) ? S("COPY_MISSING_STUB") : S("COPY_MISSING"), Add(book_154, S(" (missing COPY; no declarations or behavior inferred)")), source_169, Truth(Member(this, "allow_missing_copy")) ? S("review") : S("error"));
                        if (Truth(Member(this, "allow_missing_copy")))
                        {
                            _ = Call(result_168, "extend", ListOf(Token(S("COPY-STUB"), source_169), Token(book_154, Member(tokens_[book_index_155], "source")), Token(S("."), Member(tokens_[j_163], "source"))));
                        }
                    }
                    else
                    {
                        expanded_161 = M_load(candidate_157, ListOf(Enumerable.Empty<V>().Concat(Items(stack_)).Concat(new V[] { AddText(Format(name_, string.Empty), S(":"), Format(source_169[S("start")][S("line")], string.Empty)) })), ListOf(Enumerable.Empty<V>().Concat(Items(active_)).Concat(new V[] { path_ })));
                        _ = Call(result_168, "extend", Truth(pairs_165) ? M_replace_copy(expanded_161, pairs_165, source_169) : expanded_161);
                    }
                }
            }
            catch (Exception error178) when (error178 is ArgumentException || error178 is IOException || error178 is System.Text.DecoderFallbackException)
            {
                error_160 = S(error178.Message);
                message_164 = StringValue(error_160);
                code_158 = Truth(Call(message_164, "startswith", ListOf(S("COPY_REPLACING:"), S("COPY_AMBIGUOUS:"), S("COPY_TERMINATOR:")))) ? Call(message_164, "split", S(":"), I(1))[I(0)] : S("COPY_ERROR");
                _ = M_diagnostic(code_158, message_164, source_169);
            }

            i_162 = Add(j_163, I(1));
        }

        return result_168;
    }
}
