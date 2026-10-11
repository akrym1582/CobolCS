using static CobolCS.Frontend.Parsing.Values;

namespace CobolCS.Frontend.Parsing;

/// <summary>位置とIRを保持する models の解析処理です。</summary>
internal sealed partial class Parser : V
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Parser"/> class. 解析状態を初期化します。
    /// </summary>
    /// <param name="front_">front の解析値。</param>
    /// <param name="tokens_">tokens の解析値。</param>
    internal Parser(V front_, V tokens_)
    {
        V files_1 = Nil;
        var unpack2 = ListOf(front_, tokens_, I(0));
        SetMember(this, "f", unpack2[I(0)]);
        SetMember(this, "ts", unpack2[I(1)]);
        SetMember(this, "i", unpack2[I(2)]);
        SetMember(this, "symbols", ListOf());
        SetMember(this, "paragraphs", ListOf());
        SetMember(this, "dependencies", ListOf());
        files_1 = SetOf(from item3 in Items(tokens_) let t_4 = item3 select Member(t_4, "source")[S("file")]);
        SetMember(this, "ds", ListOf(from item5 in Items(Member(front_, "diagnostics")) let d_6 = item5 where Truth(And(B(Equal(d_6[S("code")], S("UNTERMINATED_LITERAL"))), () => B(Contains(files_1, d_6[S("source")][S("file")])))) select d_6));
        SetMember(this, "sid", I(0));
        SetMember(this, "owners", Dict());
        SetMember(this, "procedure_start", I(0));
        SetMember(this, "missing_copy_scopes", ListOf());
    }

    /// <summary>
    /// 次のトークンを消費せず大文字で見る。n=1ならさらに1つ先を見る。末尾では空文字を返す。
    /// </summary>
    /// <param name="n_">n の解析値。</param>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_peek(V? n_ = null)
    {
        n_ ??= I(0);
        return Truth(B(Compare(Add(Member(this, "i"), n_), Length(Member(this, "ts"))) < 0)) ? Member(Member(this, "ts")[Add(Member(this, "i"), n_)], "upper") : S(string.Empty);
    }

    /// <summary>
    /// トークンを1つ取り出して読み取り位置を進める。入力不足を空文字などの正常値へ置換しない。
    /// </summary>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_take()
    {
        V t_7 = Nil;
        if (Truth(B(Compare(Member(this, "i"), Length(Member(this, "ts"))) >= 0)))
        {
            throw new ParseException(Text(S("Unexpected end of source")));
        }

        t_7 = Member(this, "ts")[Member(this, "i")];
        SetMember(this, "i", Add(Member(this, "i"), I(1)));
        return t_7;
    }

    /// <summary>
    /// 期待語と一致した場合だけ1つ進める。THENなど、省略可能な語の処理で使う。
    /// </summary>
    /// <param name="s_">s の解析値。</param>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_accept(V s_)
    {
        if (Truth(B(Equal(M_peek(), s_))))
        {
            SetMember(this, "i", Add(Member(this, "i"), I(1)));
            return B(true);
        }

        return B(false);
    }

    /// <summary>
    /// 必須語を検査して進める。不一致は解析エラーにし、acceptのように読み飛ばさない。
    /// </summary>
    /// <param name="s_">s の解析値。</param>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_require(V s_)
    {
        if (Truth(B(!Truth(M_accept(s_)))))
        {
            throw new ParseException(Text(Add(Add(Add(S("Expected "), s_), S(", got ")), M_peek())));
        }

        return Nil;
    }

    /// <summary>
    /// 消費した範囲の元位置をまとめる。endは範囲の直後を指す。
    /// 同じ原文を検索して位置を復元せず、トークンの位置と保存済み原文byteから取り出す。
    /// </summary>
    /// <param name="a_">a の解析値。</param>
    /// <param name="b_">b の解析値。</param>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_source(V a_, V? b_ = null)
    {
        b_ ??= Nil;
        V data_8 = Nil;
        V first_9 = Nil;
        V last_10 = Nil;
        V src_11 = Nil;
        b_ = Truth(B(ReferenceEquals(b_, Nil))) ? Member(this, "i") : b_;
        first_9 = Member(Member(this, "ts")[a_], "source");
        last_10 = Member(Member(this, "ts")[Maximum(new V[] { a_, Subtract(b_, I(1)) }, null, null)], "source");
        src_11 = Merge(Dict(), first_9);
        if (Truth(B(Equal(first_9[S("file")], last_10[S("file")]))))
        {
            src_11[S("end")] = last_10[S("end")];
            data_8 = Call(Member(Member(this, "f"), "sources")[first_9[S("file")]][S("text")], "encode", S("utf-8"));
            src_11[S("raw")] = Call(data_8[Slice(first_9[S("start")][S("byteOffset")], last_10[S("end")][S("byteOffset")], Nil)], "decode", S("utf-8"));
        }
        else
        {
            src_11[S("raw")] = Call(S(" "), "join", Sequence(from item12 in Items(Member(this, "ts")[Slice(a_, b_, Nil)]) let t_13 = item12 select Member(t_13, "text")));
        }

        return src_11;
    }

    /// <summary>
    /// 元の解析規則とIRの順序を保持します。
    /// </summary>
    /// <param name="code_">code の解析値。</param>
    /// <param name="message_">message の解析値。</param>
    /// <param name="src_">src の解析値。</param>
    /// <param name="severity_">severity の解析値。</param>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_diag(V code_, V message_, V src_, V? severity_ = null)
    {
        severity_ ??= S("error");
        V d_14 = Nil;
        d_14 = Call(Member(this, "f"), "diagnostic", code_, message_, src_, severity_);
        _ = Call(Member(this, "ds"), "append", d_14);
        return Nil;
    }

    /// <summary>
    /// 名前とOF/IN修飾から1つの宣言を選び、安定したsymbol IDへ解決する。
    /// 例えば金額 OF Aと金額 OF Bを区別する。修飾なしで候補が複数あれば曖昧として拒否する。
    /// 欠落COPYを許容したscaffoldだけは、未確定項目をstubとして保持し、型や値を推測しない。
    /// </summary>
    /// <param name="name_">name の解析値。</param>
    /// <param name="qualifiers_">qualifiers の解析値。</param>
    /// <param name="required_scope_">required_scope の解析値。</param>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_resolve(V name_, V? qualifiers_ = null, V? required_scope_ = null)
    {
        qualifiers_ ??= ListOf();
        required_scope_ ??= Nil;
        V candidates_15 = Nil;
        V item_16 = Nil;
        V old_17 = Nil;
        V origins_18 = Nil;
        V qualified_19 = Nil;
        V s_20 = Nil;
        V sid_21 = Nil;
        V src_22 = Nil;
        candidates_15 = ListOf(from item23 in Items(Member(this, "symbols")) let s_24 = item23 where Truth(And(B(Equal(Call(s_24[S("name")], "upper"), Call(name_, "upper"))), () => B(!Equal(Call(s_24[S("name")], "upper"), S("FILLER"))))) select s_24);
        V Fn_qualified(V s_25)
        {
            V index_26 = Nil;
            V names_27 = Nil;
            V p_28 = Nil;
            V parent_29 = Nil;
            V q_30 = Nil;
            names_27 = ListOf();
            p_28 = s_25[S("parentId")];
            while (Truth(p_28))
            {
                parent_29 = Next(Sequence(from item31 in Items(Member(this, "symbols")) let x_32 = item31 where Truth(B(Equal(x_32[S("id")], p_28))) select x_32));
                _ = Call(names_27, "append", Call(parent_29[S("name")], "upper"));
                p_28 = parent_29[S("parentId")];
            }

            index_26 = I(0);
            foreach (var item33 in Items(qualifiers_))
            {
                q_30 = item33;
                try
                {
                    index_26 = Add(Call(names_27, "index", Call(q_30, "upper"), index_26), I(1));
                }
                catch (Exception error34) when (error34 is ArgumentException)
                {
                    return B(false);
                }
            }

            return B(true);
        }

        candidates_15 = ListOf(from item35 in Items(candidates_15) let s_36 = item35 where Truth(Fn_qualified(s_36)) select s_36);
        if (Truth(And(B(!Truth(candidates_15)), () => B(Contains(ListOf(S("RETURN-CODE"), S("SORT-RETURN")), Call(name_, "upper"))))))
        {
            sid_21 = AddText(S("f"), Format(Add(Length(Member(this, "symbols")), I(1)), "06d"));
            item_16 = Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Dict(), S("id"), sid_21), S("name"), Call(name_, "upper")), S("qualifiedName"), Call(name_, "upper")), S("stub"), B(false)), S("scope"), S("WORKING-STORAGE")), S("level"), I(77)), S("pic"), Put(Put(Put(Put(Put(Dict(), S("raw"), S("S9(9)")), S("category"), S("numeric")), S("digits"), I(9)), S("scale"), I(0)), S("signed"), B(true))), S("usage"), S("COMP-5")), S("children"), ListOf()), S("parentId"), Nil), S("occurs"), I(1)), S("occursClause"), Nil), S("redefines"), Nil), S("initial"), Lit(S("0"))), S("conditions"), ListOf()), S("source"), Member(Member(this, "ts")[Maximum(new V[] { I(0), Subtract(Member(this, "i"), I(1)) }, null, null)], "source"));
            _ = Call(Member(this, "symbols"), "append", item_16);
            return item_16;
        }

        if (Truth(And(B(!Truth(candidates_15)), () => And(Member(Member(this, "f"), "allow_missing_copy"), () => Member(Member(this, "f"), "missing_copies")))))
        {
            origins_18 = ListOf(from item37 in Items(Member(this, "missing_copy_scopes")) let o_38 = item37 where Truth(Or(B(ReferenceEquals(required_scope_, Nil)), () => B(Equal(o_38[S("scope")], required_scope_)))) select o_38);
            if (Truth(And(required_scope_, () => B(!Truth(Any(Sequence(from item39 in Items(origins_18) let o_40 = item39 select B(Equal(Call(Call(o_40[S("book")], "rsplit", S("."), I(1))[I(0)], "upper"), Call(name_, "upper"))))))))))
            {
                throw new ParseException(Text(Add(Add(Add(Add(S("Unresolved "), required_scope_), S(" root ")), name_), S("; provide the missing COPY layout"))));
            }

            if (Truth(B(!Truth(origins_18))))
            {
                throw new ParseException(Text(Add(S("Unresolved reference without a missing COPY declaration: "), name_)));
            }

            qualified_19 = Add(name_, Call(S(string.Empty), "join", Sequence(from item41 in Items(qualifiers_) let q_42 = item41 select Add(S(" OF "), q_42))));
            old_17 = Next(Sequence(from item43 in Items(Member(this, "symbols")) let s_44 = item43 where Truth(And(s_44[S("stub")], () => B(Equal(Call(s_44[S("qualifiedName")], "upper"), Call(qualified_19, "upper"))))) select s_44), Nil);
            if (Truth(old_17))
            {
                return old_17;
            }

            sid_21 = AddText(S("f"), Format(Add(Length(Member(this, "symbols")), I(1)), "06d"));
            src_22 = Member(Member(this, "ts")[Maximum(new V[] { I(0), Subtract(Member(this, "i"), I(1)) }, null, null)], "source");
            s_20 = Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Dict(), S("id"), sid_21), S("name"), name_), S("qualifiedName"), qualified_19), S("stub"), B(true)), S("scope"), Or(required_scope_, () => S("UNKNOWN"))), S("level"), I(77)), S("pic"), Nil), S("usage"), S("DISPLAY")), S("children"), ListOf()), S("parentId"), Nil), S("occurs"), I(1)), S("occursClause"), Nil), S("redefines"), Nil), S("initial"), Nil), S("conditions"), ListOf()), S("source"), src_22), S("missingCopyOrigins"), origins_18), S("layoutStatus"), S("unknown"));
            _ = Call(Member(this, "symbols"), "append", s_20);
            _ = M_diag(S("MISSING_COPY_SYMBOL"), Add(qualified_19, S(" requires an explicit fixture PIC/layout; no type inferred")), src_22);
            Member(this, "ds")[Subtract(I(0), I(1))][S("severity")] = S("review");
            return s_20;
        }

        if (Truth(B(!Equal(Length(candidates_15), I(1)))))
        {
            throw new ParseException(Text(Add(S("Unresolved or ambiguous reference: "), name_)));
        }

        return candidates_15[I(0)];
    }

    /// <summary>
    /// CALL/SQL/fileを、実行時にfixtureを注入できる独立した依存契約としてIRへ保存する。
    /// </summary>
    /// <param name="kind_">kind の解析値。</param>
    /// <param name="name_">name の解析値。</param>
    /// <param name="text_">text の解析値。</param>
    /// <param name="src_">src の解析値。</param>
    /// <param name="inputs_">inputs の解析値。</param>
    /// <param name="outputs_">outputs の解析値。</param>
    /// <param name="modes_">modes の解析値。</param>
    /// <param name="status_">status の解析値。</param>
    /// <param name="record_">record の解析値。</param>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_dependency(V kind_, V name_, V text_, V src_, V? inputs_ = null, V? outputs_ = null, V? modes_ = null, V? status_ = null, V? record_ = null)
    {
        inputs_ ??= ListOf();
        outputs_ ??= ListOf();
        modes_ ??= ListOf();
        status_ ??= Nil;
        record_ ??= Nil;
        V d_45 = Nil;
        d_45 = Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Dict(), S("id"), AddText(S("d"), Format(Add(Length(Member(this, "dependencies")), I(1)), "06d"))), S("kind"), kind_), S("name"), name_), S("text"), text_), S("inputs"), ListCopy(inputs_)), S("outputs"), ListCopy(outputs_)), S("modes"), ListCopy(modes_)), S("status"), status_), S("recordId"), record_), S("source"), src_);
        _ = Call(Member(this, "dependencies"), "append", d_45);
        return d_45;
    }
}
