using static CobolCS.Frontend.Parsing.Values;

namespace CobolCS.Frontend.Parsing;

/// <summary>位置とIRを保持する declarations の解析処理です。</summary>
internal sealed partial class Parser : V
{
    /// <summary>
    /// 元の解析規則とIRの順序を保持します。
    /// </summary>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_declaration_boundary()
    {
        return Or(B(Contains(SetOf(S("PROCEDURE"), S("FD"), S("SD"), S("$"), S("CONDITIONAL-STUB")), M_peek())), () => Or(B(Contains(SetOf(S("DIVISION"), S("SECTION")), M_peek(I(1)))), () => And(Call(M_peek(), "isdigit"), () => B(Compare(Member(Member(this, "ts")[Member(this, "i")], "source")[S("start")][S("line")], Member(Member(this, "ts")[Subtract(Member(this, "i"), I(1))], "source")[S("start")][S("line")]) > 0))));
    }

    /// <summary>
    /// DATA DIVISIONを読み、レベル番号から親子関係を作る。
    ///
    /// stackには現在の祖先項目を置く。新しい05項目なら同じ05やそれ以下をpopし、上位の01等を親にする。
    /// PICは意味上の型、occursは最大件数、REDEFINESは共有先のIDとして保存する。
    /// 実際のoffsetやbyte長はTS compilerが明示プロファイルから計算する。
    /// </summary>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_declarations()
    {
        V a_258 = Nil;
        V alternatives_259 = Nil;
        V blank_zero_260 = Nil;
        V book_261 = Nil;
        V branch_262 = Nil;
        V candidates_263 = Nil;
        V clause_264 = Nil;
        V conditions_265 = Nil;
        V currency_266 = Nil;
        V current_file_267 = Nil;
        V dep_268 = Nil;
        V depending_269 = Nil;
        V direction_270 = Nil;
        V e_271 = Nil;
        V expanded_272 = Nil;
        V floating_273 = Nil;
        V following_274 = Nil;
        V high_275 = Nil;
        V index_276 = Nil;
        V index_id_277 = Nil;
        V index_name_278 = Nil;
        V indexes_279 = Nil;
        V info_280 = Nil;
        V initial_281 = Nil;
        V justified_282 = Nil;
        V keys_283 = Nil;
        V last_284 = Nil;
        V level_285 = Nil;
        V name_286 = Nil;
        V named_287 = Nil;
        V numeric_288 = Nil;
        V occurs_289 = Nil;
        V occurs_declared_290 = Nil;
        V occurs_min_291 = Nil;
        V owner_292 = Nil;
        V parent_293 = Nil;
        V parent_id_294 = Nil;
        V parsed_295 = Nil;
        V parser_296 = Nil;
        V parts_297 = Nil;
        V pending_keys_298 = Nil;
        V pic_299 = Nil;
        V point_300 = Nil;
        V position_301 = Nil;
        V previous_302 = Nil;
        V raw_303 = Nil;
        V records_304 = Nil;
        V redefine_305 = Nil;
        V s_306 = Nil;
        V scope_307 = Nil;
        V separate_308 = Nil;
        V sid_309 = Nil;
        V sign_clause_310 = Nil;
        V stack_311 = Nil;
        V stops_312 = Nil;
        V symbol_313 = Nil;
        V synchronized_314 = Nil;
        V t_315 = Nil;
        V token_index_316 = Nil;
        V tokens_317 = Nil;
        V usage_318 = Nil;
        V value_319 = Nil;
        V variants_320 = Nil;
        scope_307 = S("WORKING-STORAGE");
        stack_311 = ListOf();
        last_284 = Nil;
        current_file_267 = Nil;
        pending_keys_298 = ListOf();
        while (Truth(And(M_peek(), () => B(!Equal(M_peek(), S("PROCEDURE"))))))
        {
            var declarationFailed = false;
            a_258 = Member(this, "i");
            var oldScope = scope_307;
            var oldStack = stack_311;
            var oldLast = last_284;
            var oldFile = current_file_267;
            using var itemTransaction = syntaxRecovery == SyntaxRecoveryPolicy.Tolerant ? new ValueTransaction() : null;
            try
            {
                t_315 = M_take();
                if (Truth(And(B(Contains(ListOf(S("WORKING-STORAGE"), S("LOCAL-STORAGE"), S("LINKAGE"), S("FILE")), Member(t_315, "upper"))), () => B(Equal(M_peek(), S("SECTION"))))))
                {
                    scope_307 = Member(t_315, "upper");
                    _ = M_take();
                    _ = M_require(S("."));
                    stack_311 = ListOf();
                    continue;
                }

                if (Truth(B(Contains(ListOf(S("FD"), S("SD")), Member(t_315, "upper")))))
                {
                    name_286 = Member(M_take(), "text");
                    clause_264 = ListOf();
                    while (Truth(And(M_peek(), () => And(B(!Equal(M_peek(), S("."))), () => B(!Truth(M_declaration_boundary()))))))
                    {
                        _ = Call(clause_264, "append", M_take());
                    }

                    _ = M_require(S("."));
                    _ = M_file_record(name_286, Member(t_315, "upper"), clause_264, M_source(a_258));
                    current_file_267 = name_286;
                    stack_311 = ListOf();
                    continue;
                }

                if (Truth(B(Equal(Member(t_315, "text"), S(".")))))
                {
                    continue;
                }

                if (Truth(B(Equal(Member(t_315, "upper"), S("COPY-STUB")))))
                {
                    book_261 = Member(M_take(), "text");
                    _ = M_require(S("."));
                    _ = Call(Member(this, "missing_copy_scopes"), "append", Put(Put(Put(Dict(), S("book"), book_261), S("scope"), scope_307), S("source"), M_source(a_258)));
                    foreach (var item321 in Items(Range(a_258, Member(this, "i"))))
                    {
                        token_index_316 = item321;
                        Member(this, "owners")[token_index_316] = ListOf(S("declaration"), S("$copyStub"));
                    }

                    continue;
                }

                if (Truth(B(Equal(Member(t_315, "upper"), S("CONDITIONAL-STUB")))))
                {
                    info_280 = Member(t_315, "source")[S("conditional")];
                    alternatives_259 = Dict();
                    foreach (var item322 in Items(info_280[S("branches")]))
                    {
                        branch_262 = item322;
                        parser_296 = new Parser(Member(this, "f"), ListOf(from item323 in Items(branch_262) let t_324 = item323 select Token(t_324[S("text")], t_324[S("source")])));
                        _ = Call(parser_296, "declarations");
                        if (Truth(Or(Any(Sequence(from item327 in Items(Member(parser_296, "ds")) let d_328 = item327 select B(!Equal(d_328[S("code")], S("CONDITIONAL_LAYOUT"))))), () => Any(Sequence(from item325 in Items(Member(parser_296, "symbols")) let s_326 = item325 select Or(B(Equal(s_326[S("level")], I(88))), () => Or(s_326[S("parentId")], () => s_326[S("children")])))))))
                        {
                            alternatives_259 = Dict();
                            _ = M_diag(S("CONDITIONAL_LAYOUT_OPAQUE"), S("P64 is unset and branch structure is not safely recoverable; supply explicit condition and source column policy before implementing layout"), Member(t_315, "source"));
                            if (Truth(stack_311))
                            {
                                stack_311[I(0)][S("layoutStatus")] = S("conditional-unknown");
                            }

                            break;
                        }

                        foreach (var item329 in Items(Member(parser_296, "symbols")))
                        {
                            symbol_313 = item329;
                            _ = Call(Call(alternatives_259, "setdefault", Call(symbol_313[S("name")], "upper"), ListOf()), "append", symbol_313);
                        }
                    }

                    foreach (var item330 in Items(Call(alternatives_259, "items")))
                    {
                        var unpack331 = item330;
                        name_286 = unpack331[I(0)];
                        variants_320 = unpack331[I(1)];
                        level_285 = variants_320[I(0)][S("level")];
                        if (Truth(Any(Sequence(from item332 in Items(variants_320) let v_333 = item332 select B(!Equal(v_333[S("level")], level_285))))))
                        {
                            throw new ParseException(Text(S("Set p64 explicitly: conditional declaration levels differ")));
                        }

                        if (Truth(B(Contains(ListOf(I(1), I(77)), level_285))))
                        {
                            stack_311 = ListOf();
                        }

                        while (Truth(And(stack_311, () => B(Compare(stack_311[Subtract(I(0), I(1))][S("level")], level_285) >= 0))))
                        {
                            _ = Call(stack_311, "pop");
                        }

                        parent_293 = Truth(stack_311) ? stack_311[Subtract(I(0), I(1))] : Nil;
                        parent_id_294 = Truth(parent_293) ? parent_293[S("id")] : Nil;
                        if (Truth(And(B(!Equal(name_286, S("FILLER"))), () => Any(Sequence(from item334 in Items(Member(this, "symbols")) let s_335 = item334 select And(B(Equal(Call(s_335[S("name")], "upper"), name_286)), () => B(Equal(s_335[S("parentId")], parent_id_294))))))))
                        {
                            throw new ParseException(Text(Add(S("Ambiguous conditional declaration "), name_286)));
                        }

                        symbol_313 = Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Merge(Dict(), variants_320[I(0)]), S("id"), AddText(S("f"), Format(Add(Length(Member(this, "symbols")), I(1)), "06d"))), S("scope"), scope_307), S("pic"), Nil), S("usage"), S("UNKNOWN")), S("initial"), Nil), S("stub"), B(true)), S("source"), Member(t_315, "source")), S("parentId"), parent_id_294), S("children"), ListOf()), S("occurs"), I(1)), S("occursClause"), Nil), S("occursMin"), I(1)), S("dependingOn"), Nil), S("redefines"), Nil), S("qualifiedName"), Add(variants_320[I(0)][S("name")], Truth(parent_293) ? Add(S(" OF "), parent_293[S("qualifiedName")]) : S(string.Empty))), S("conditional"), info_280), S("layoutAlternatives"), variants_320);
                        if (Truth(parent_293))
                        {
                            _ = Call(parent_293[S("children")], "append", symbol_313[S("id")]);
                            parent_293[S("layoutStatus")] = S("conditional-unknown");
                        }

                        _ = Call(Member(this, "symbols"), "append", symbol_313);
                        last_284 = symbol_313;
                        _ = Call(stack_311, "append", symbol_313);
                    }

                    _ = M_diag(S("CONDITIONAL_LAYOUT"), S("P64 is unset; conditional declarations have unknown layout"), Member(t_315, "source"));
                    Member(this, "owners")[a_258] = ListOf(S("declaration"), S("$conditional"));
                    continue;
                }

                if (Truth(B(!Truth(Call(Member(t_315, "text"), "isdigit")))))
                {
                    while (Truth(And(M_peek(), () => And(B(!Equal(M_peek(), S("."))), () => B(!Truth(M_declaration_boundary()))))))
                    {
                        _ = M_take();
                    }

                    _ = M_accept(S("."));
                    _ = M_diag(S("UNKNOWN_DECLARATION"), S("Unsupported data syntax"), M_source(a_258));
                    if (syntaxRecovery == SyntaxRecoveryPolicy.Tolerant)
                    {
                        MarkUnknownLayout(stack_311);
                    }

                    foreach (var item336 in Items(Range(a_258, Member(this, "i"))))
                    {
                        token_index_316 = item336;
                        Member(this, "owners")[token_index_316] = ListOf(S("unknown"), S("$unknownDeclaration"));
                    }

                    continue;
                }

                level_285 = Integer(Member(t_315, "text"));
                if (syntaxRecovery == SyntaxRecoveryPolicy.Tolerant && Truth(M_declaration_boundary()))
                {
                    throw new ParseException("Missing declaration name");
                }

                name_286 = Member(M_take(), "text");
                pic_299 = Nil;
                usage_318 = S("DISPLAY");
                occurs_289 = I(1);
                occurs_declared_290 = B(false);
                redefine_305 = Nil;
                initial_281 = Nil;
                conditions_265 = ListOf();
                depending_269 = Nil;
                indexes_279 = ListOf();
                keys_283 = ListOf();
                occurs_min_291 = I(1);
                sign_clause_310 = Nil;
                var assign337 = B(false);
                justified_282 = assign337;
                synchronized_314 = assign337;
                blank_zero_260 = assign337;
                using var declarationTransaction = syntaxRecovery == SyntaxRecoveryPolicy.Tolerant ? new ValueTransaction() : null;
                try
                {
                    if (Truth(B(!Contains(ListOf(Enumerable.Empty<V>().Concat(Items(Range(I(1), I(50)))).Concat(new V[] { I(77) }).Concat(new V[] { I(88) })), level_285))))
                    {
                        throw new ParseException(Text(Add(S("Unsupported level "), StringValue(level_285))));
                    }

                    while (Truth(And(M_peek(), () => B(!Equal(M_peek(), S("."))))))
                    {
                        if (syntaxRecovery == SyntaxRecoveryPolicy.Tolerant && Truth(M_declaration_boundary()))
                        {
                            throw new ParseException("Missing declaration period");
                        }

                        clause_264 = Member(M_take(), "upper");
                        if (Truth(B(Contains(ListOf(S("PIC"), S("PICTURE")), clause_264))))
                        {
                            _ = M_accept(S("IS"));
                            parts_297 = ListOf();
                            stops_312 = SetCopy(Call(S("VALUE VALUES USAGE COMP COMP-1 COMP-2 COMP-3 COMP-5 COMPUTATIONAL COMPUTATIONAL-1 COMPUTATIONAL-2 COMPUTATIONAL-3 COMPUTATIONAL-5 BINARY PACKED-DECIMAL DISPLAY INDEX OCCURS INDEXED ASCENDING DESCENDING REDEFINES SIGN JUSTIFIED JUST SYNC SYNCHRONIZED BLANK"), "split"));
                            while (Truth(And(M_peek(), () => B(!Contains(stops_312, M_peek())))))
                            {
                                if (syntaxRecovery == SyntaxRecoveryPolicy.Tolerant && Truth(M_declaration_boundary()))
                                {
                                    break;
                                }

                                if (Truth(B(Equal(M_peek(), S(".")))))
                                {
                                    previous_302 = Member(Member(this, "ts")[Subtract(Member(this, "i"), I(1))], "source");
                                    following_274 = Truth(B(Compare(Add(Member(this, "i"), I(1)), Length(Member(this, "ts"))) < 0)) ? Member(this, "ts")[Add(Member(this, "i"), I(1))] : Nil;
                                    if (Truth(Or(B(!Truth(following_274)), () => Or(B(!Equal(previous_302[S("end")][S("byteOffset")], Member(Member(this, "ts")[Member(this, "i")], "source")[S("start")][S("byteOffset")])), () => Or(B(!Equal(Member(Member(this, "ts")[Member(this, "i")], "source")[S("end")][S("byteOffset")], Member(following_274, "source")[S("start")][S("byteOffset")])), () => B(!Truth(RegexFullmatch(S("[9Z*]+"), Member(following_274, "text")))))))))
                                    {
                                        break;
                                    }
                                }

                                _ = Call(parts_297, "append", Member(M_take(), "text"));
                            }

                            raw_303 = Call(Call(S(string.Empty), "join", parts_297), "upper");
                            if (Truth(Or(B(Compare(Length(raw_303), I(100)) > 0), () => Any(Sequence(from item338 in Items(RegexFindall(S("\\((\\d+)\\)"), raw_303)) let n_339 = item338 select Or(B(Compare(Integer(n_339), I(10000)) > 0), () => B(Compare(Integer(n_339), I(1)) < 0)))))))
                            {
                                throw new ParseException(Text(S("PIC size limit")));
                            }

                            expanded_272 = RegexSub(S("(.)\\((\\d+)\\)"), (arg340) => Multiply(Call(arg340, "group", I(1)), Integer(Call(arg340, "group", I(2)))), raw_303);
                            if (Truth(Or(B(Compare(Length(expanded_272), I(10000)) > 0), () => B(!Truth(expanded_272)))))
                            {
                                throw new ParseException(Text(S("PIC size limit")));
                            }

                            tokens_317 = RegexFindall(S("CR|DB|."), expanded_272);
                            if (Truth(RegexFullmatch(S("N+"), expanded_272)))
                            {
                                pic_299 = Put(Put(Put(Put(Put(Put(Dict(), S("raw"), raw_303), S("category"), S("national")), S("digits"), Length(expanded_272)), S("scale"), I(0)), S("signed"), B(false)), S("tokens"), tokens_317);
                            }
                            else
                            {
                                if (Truth(RegexFullmatch(S("X+"), expanded_272)))
                                {
                                    pic_299 = Put(Put(Put(Put(Put(Put(Dict(), S("raw"), raw_303), S("category"), S("text")), S("digits"), Length(expanded_272)), S("scale"), I(0)), S("signed"), B(false)), S("tokens"), tokens_317);
                                }
                                else
                                {
                                    if (Truth(RegexFullmatch(S("A+"), expanded_272)))
                                    {
                                        pic_299 = Put(Put(Put(Put(Put(Put(Dict(), S("raw"), raw_303), S("category"), S("alphabetic")), S("digits"), Length(expanded_272)), S("scale"), I(0)), S("signed"), B(false)), S("tokens"), tokens_317);
                                    }
                                    else
                                    {
                                        if (Truth(RegexFullmatch(S("S?9+(V9+)?"), expanded_272)))
                                        {
                                            parsed_295 = ParsePicture(raw_303);
                                            pic_299 = Put(Put(Put(Put(Put(Put(Dict(), S("raw"), raw_303), S("category"), S("numeric")), S("digits"), Member(parsed_295, "size")), S("scale"), Member(parsed_295, "decimals")), S("signed"), Member(parsed_295, "signed")), S("tokens"), tokens_317);
                                        }
                                        else
                                        {
                                            if (Truth(Or(B(Contains(expanded_272, S("X"))), () => B(Contains(expanded_272, S("A"))))))
                                            {
                                                if (Truth(B(!Truth(RegexFullmatch(S("[AX9B0/]+"), expanded_272)))))
                                                {
                                                    throw new ParseException(Text(Add(S("Unsupported alphanumeric edited PIC "), raw_303)));
                                                }

                                                pic_299 = Put(Put(Put(Put(Put(Put(Dict(), S("raw"), raw_303), S("category"), S("alphanumeric-edited")), S("digits"), Sum(Sequence(from item343 in Items(tokens_317) let t_344 = item343 select B(Contains(S("AX9"), t_344))))), S("scale"), I(0)), S("signed"), B(false)), S("tokens"), tokens_317);
                                            }
                                            else
                                            {
                                                currency_266 = Member(Member(this, "f"), "config")[S("currencySymbol")];
                                                if (Truth(Or(Any(Sequence(from item347 in Items(tokens_317) let t_348 = item347 select B(!Contains(ListOf(S("9"), S("Z"), S("*"), S("B"), S("0"), S("/"), S("."), S(","), S("V"), S("+"), S("-"), S("CR"), S("DB"), currency_266), t_348)))), () => B(!Truth(Any(Sequence(from item345 in Items(tokens_317) let t_346 = item345 select B(Contains(ListOf(S("9"), S("Z"), S("*")), t_346)))))))))
                                                {
                                                    throw new ParseException(Text(Add(S("Unsupported edited PIC "), raw_303)));
                                                }

                                                point_300 = Truth(B(Equal(Member(Member(this, "f"), "config")[S("decimalPoint")], S("comma")))) ? S(",") : S(".");
                                                position_301 = Next(Sequence(from item349 in Items(Enumerate(tokens_317)) let i_350 = item349[I(0)] let t_351 = item349[I(1)] where Truth(B(Contains(ListOf(point_300, S("V")), t_351))) select i_350), Length(tokens_317));
                                                numeric_288 = Sum(Sequence(from item352 in Items(tokens_317) let t_353 = item352 select B(Contains(ListOf(S("9"), S("Z"), S("*")), t_353))));
                                                floating_273 = Maximum(new V[] { Sequence(from item354 in Items(ListOf(S("+"), S("-"), currency_266)) let t_355 = item354 select Subtract(Call(tokens_317, "count", t_355), I(1))) }, null, I(0));
                                                pic_299 = Put(Put(Put(Put(Put(Put(Dict(), S("raw"), raw_303), S("category"), S("numeric-edited")), S("digits"), Add(numeric_288, Maximum(new V[] { I(0), floating_273 }, null, null))), S("scale"), Sum(Sequence(from item360 in Items(tokens_317[Slice(Add(position_301, I(1)), Nil, Nil)]) let t_361 = item360 select B(Contains(ListOf(S("9"), S("Z"), S("*")), t_361))))), S("signed"), Any(Sequence(from item362 in Items(tokens_317) let t_363 = item362 select B(Contains(ListOf(S("+"), S("-"), S("CR"), S("DB")), t_363))))), S("tokens"), tokens_317);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            if (Truth(B(Contains(ListOf(S("USAGE"), S("DISPLAY"), S("COMP"), S("COMP-3"), S("COMP-1"), S("COMP-2"), S("COMP-5"), S("INDEX"), S("COMPUTATIONAL-1"), S("COMPUTATIONAL-2"), S("COMPUTATIONAL-5"), S("COMPUTATIONAL"), S("COMPUTATIONAL-3"), S("BINARY"), S("PACKED-DECIMAL")), clause_264))))
                            {
                                if (Truth(B(Equal(clause_264, S("USAGE")))))
                                {
                                    _ = M_accept(S("IS"));
                                    clause_264 = Member(M_take(), "upper");
                                }

                                usage_318 = Call(Put(Put(Put(Put(Put(Put(Put(Dict(), S("COMPUTATIONAL"), S("COMP")), S("BINARY"), S("COMP")), S("COMPUTATIONAL-1"), S("COMP-1")), S("COMPUTATIONAL-2"), S("COMP-2")), S("COMPUTATIONAL-5"), S("COMP-5")), S("COMPUTATIONAL-3"), S("COMP-3")), S("PACKED-DECIMAL"), S("COMP-3")), "get", clause_264, clause_264);
                                if (Truth(B(!Contains(ListOf(S("DISPLAY"), S("COMP"), S("COMP-3"), S("COMP-1"), S("COMP-2"), S("COMP-5"), S("INDEX")), usage_318))))
                                {
                                    throw new ParseException(Text(Add(S("Unsupported usage "), usage_318)));
                                }
                            }
                            else
                            {
                                if (Truth(B(Equal(clause_264, S("OCCURS")))))
                                {
                                    occurs_declared_290 = B(true);
                                    occurs_289 = Integer(Member(M_take(), "text"));
                                    occurs_min_291 = occurs_289;
                                    if (Truth(M_accept(S("TO"))))
                                    {
                                        occurs_289 = Integer(Member(M_take(), "text"));
                                        _ = M_accept(S("TIMES"));
                                        _ = M_require(S("DEPENDING"));
                                        _ = M_require(S("ON"));
                                        depending_269 = M_resolve(Member(M_take(), "text"))[S("id")];
                                        if (Truth(Or(B(Compare(occurs_min_291, occurs_289) > 0), () => B(Compare(occurs_min_291, I(0)) < 0))))
                                        {
                                            throw new ParseException(Text(S("Invalid OCCURS range")));
                                        }
                                    }
                                    else
                                    {
                                        _ = M_accept(S("TIMES"));
                                    }

                                    if (Truth(B(!Truth(B((Compare(I(1), occurs_289) <= 0) && (Compare(occurs_289, I(10000)) <= 0))))))
                                    {
                                        throw new ParseException(Text(S("OCCURS must be 1..10000")));
                                    }
                                }
                                else
                                {
                                    if (Truth(B(Equal(clause_264, S("INDEXED")))))
                                    {
                                        if (Truth(B(!Truth(occurs_declared_290))))
                                        {
                                            throw new ParseException(Text(S("INDEXED BY requires OCCURS")));
                                        }

                                        _ = M_require(S("BY"));
                                        while (Truth(And(M_peek(), () => B(!Contains(SetCopy(Call(S(". ASCENDING DESCENDING PIC PICTURE VALUE VALUES REDEFINES USAGE SIGN SYNC SYNCHRONIZED JUSTIFIED BLANK"), "split")), M_peek())))))
                                        {
                                            _ = Call(indexes_279, "append", Member(M_take(), "text"));
                                        }

                                        if (Truth(B(!Truth(indexes_279))))
                                        {
                                            throw new ParseException(Text(S("INDEXED BY requires names")));
                                        }
                                    }
                                    else
                                    {
                                        if (Truth(B(Contains(ListOf(S("ASCENDING"), S("DESCENDING")), clause_264))))
                                        {
                                            if (Truth(B(!Truth(occurs_declared_290))))
                                            {
                                                throw new ParseException(Text(S("Table key requires OCCURS")));
                                            }

                                            _ = M_require(S("KEY"));
                                            _ = M_accept(S("IS"));
                                            while (Truth(And(M_peek(), () => B(!Contains(SetCopy(Call(S(". ASCENDING DESCENDING INDEXED PIC PICTURE VALUE VALUES USAGE SIGN SYNC SYNCHRONIZED JUSTIFIED BLANK"), "split")), M_peek())))))
                                            {
                                                _ = Call(keys_283, "append", ListOf(Member(M_take(), "text"), Call(clause_264, "lower")));
                                            }
                                        }
                                        else
                                        {
                                            if (Truth(B(Equal(clause_264, S("SIGN")))))
                                            {
                                                _ = M_accept(S("IS"));
                                                position_301 = Member(M_take(), "upper");
                                                if (Truth(B(!Contains(ListOf(S("LEADING"), S("TRAILING")), position_301))))
                                                {
                                                    throw new ParseException(Text(S("Invalid SIGN position")));
                                                }

                                                separate_308 = M_accept(S("SEPARATE"));
                                                if (Truth(separate_308))
                                                {
                                                    _ = M_accept(S("CHARACTER"));
                                                }

                                                sign_clause_310 = Put(Put(Dict(), S("position"), Call(position_301, "lower")), S("separate"), separate_308);
                                            }
                                            else
                                            {
                                                if (Truth(B(Contains(ListOf(S("JUSTIFIED"), S("JUST")), clause_264))))
                                                {
                                                    _ = M_accept(S("RIGHT"));
                                                    justified_282 = B(true);
                                                }
                                                else
                                                {
                                                    if (Truth(B(Contains(ListOf(S("SYNCHRONIZED"), S("SYNC")), clause_264))))
                                                    {
                                                        _ = Or(M_accept(S("LEFT")), () => M_accept(S("RIGHT")));
                                                        synchronized_314 = B(true);
                                                    }
                                                    else
                                                    {
                                                        if (Truth(B(Equal(clause_264, S("BLANK")))))
                                                        {
                                                            _ = M_require(S("WHEN"));
                                                            _ = M_require(S("ZERO"));
                                                            blank_zero_260 = B(true);
                                                        }
                                                        else
                                                        {
                                                            if (Truth(B(Equal(clause_264, S("REDEFINES")))))
                                                            {
                                                                redefine_305 = Member(M_take(), "text");
                                                            }
                                                            else
                                                            {
                                                                if (Truth(B(Contains(ListOf(S("VALUE"), S("VALUES")), clause_264))))
                                                                {
                                                                    _ = M_accept(S("IS"));
                                                                    _ = M_accept(S("ARE"));
                                                                    while (Truth(And(M_peek(), () => B(!Equal(M_peek(), S("."))))))
                                                                    {
                                                                        value_319 = M_expr(I(6));
                                                                        high_275 = value_319;
                                                                        if (Truth(B(Contains(ListOf(S("THRU"), S("THROUGH")), M_peek()))))
                                                                        {
                                                                            _ = M_take();
                                                                            high_275 = M_expr(I(6));
                                                                        }

                                                                        _ = Call(conditions_265, "append", Put(Put(Dict(), S("low"), value_319), S("high"), high_275));
                                                                        _ = M_accept(S(","));
                                                                        if (Truth(B(!Equal(level_285, I(88)))))
                                                                        {
                                                                            break;
                                                                        }
                                                                    }

                                                                    initial_281 = Truth(conditions_265) ? conditions_265[I(0)][S("low")] : Nil;
                                                                }
                                                                else
                                                                {
                                                                    throw new ParseException(Text(Add(S("Unsupported declaration clause "), clause_264)));
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
                    }

                    _ = M_require(S("."));
                    if (Truth(B(Contains(ListOf(S("COMP-1"), S("COMP-2"), S("INDEX")), usage_318))))
                    {
                        if (Truth(pic_299))
                        {
                            throw new ParseException(Text(Add(S("PIC not permitted for "), usage_318)));
                        }

                        pic_299 = Put(Put(Put(Put(Put(Dict(), S("raw"), S(string.Empty)), S("category"), S("numeric")), S("digits"), Truth(B(!Equal(usage_318, S("INDEX")))) ? I(0) : I(18)), S("scale"), I(0)), S("signed"), B(true));
                    }

                    declarationTransaction?.Commit();
                }
                catch (Exception error364) when (error364 is ParseException || error364 is ArgumentException)
                {
                    declarationTransaction?.Dispose();
                    if (syntaxRecovery == SyntaxRecoveryPolicy.Tolerant)
                    {
                        ResetSymbolIndex();
                        SetMember(this, "i", Add(a_258, I(2)));
                    }

                    declarationFailed = syntaxRecovery == SyntaxRecoveryPolicy.Tolerant;
                    e_271 = S(error364.Message);
                    while (Truth(And(M_peek(), () => And(B(!Equal(M_peek(), S("."))), () => B(!Truth(M_declaration_boundary()))))))
                    {
                        _ = M_take();
                    }

                    _ = M_accept(S("."));
                    _ = M_diag(S("UNSUPPORTED_DECLARATION"), StringValue(e_271), M_source(a_258));
                }

                if (Truth(B(Equal(level_285, I(88)))))
                {
                    parent_293 = last_284;
                    if (Truth(B(!Truth(parent_293))))
                    {
                        _ = M_diag(S("INVALID_88"), S("88 has no parent"), M_source(a_258));
                    }
                }
                else
                {
                    if (Truth(B(Contains(ListOf(I(1), I(77)), level_285))))
                    {
                        stack_311 = ListOf();
                    }

                    while (Truth(And(stack_311, () => B(Compare(stack_311[Subtract(I(0), I(1))][S("level")], level_285) >= 0))))
                    {
                        _ = Call(stack_311, "pop");
                    }

                    parent_293 = Truth(stack_311) ? stack_311[Subtract(I(0), I(1))] : Nil;
                }

                sid_309 = AddText(S("f"), Format(Add(Length(Member(this, "symbols")), I(1)), "06d"));
                s_306 = Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Dict(), S("id"), sid_309), S("name"), name_286), S("stub"), B(false)), S("qualifiedName"), Add(name_286, Truth(parent_293) ? Add(S(" OF "), parent_293[S("qualifiedName")]) : S(string.Empty))), S("scope"), scope_307), S("level"), level_285), S("pic"), pic_299), S("usage"), usage_318), S("signClause"), sign_clause_310), S("justified"), justified_282), S("synchronized"), synchronized_314), S("blankWhenZero"), blank_zero_260), S("children"), ListOf()), S("parentId"), Truth(parent_293) ? parent_293[S("id")] : Nil), S("occurs"), occurs_289), S("occursClause"), Truth(occurs_declared_290) ? Put(Put(Put(Put(Put(Dict(), S("min"), occurs_min_291), S("max"), occurs_289), S("dependingOn"), depending_269), S("indexIds"), ListOf()), S("keys"), ListOf()) : Nil), S("occursMin"), occurs_min_291), S("dependingOn"), depending_269), S("redefines"), Nil), S("initial"), Truth(B(Equal(level_285, I(88)))) ? Nil : initial_281), S("conditions"), Truth(B(Equal(level_285, I(88)))) ? conditions_265 : ListOf()), S("source"), M_source(a_258));
                if (declarationFailed)
                {
                    s_306[S("stub")] = B(true);
                    s_306[S("layoutStatus")] = S("unknown");
                    s_306[S("pic")] = Nil;
                    s_306[S("usage")] = S("UNKNOWN");
                    s_306[S("initial")] = Nil;
                    s_306[S("conditions")] = ListOf();
                    s_306[S("occurs")] = I(1);
                    s_306[S("occursClause")] = Nil;
                    s_306[S("dependingOn")] = Nil;
                    indexes_279 = ListOf();
                    keys_283 = ListOf();
                    redefine_305 = Nil;
                    for (var ancestor = parent_293; Truth(ancestor);)
                    {
                        ancestor[S("stub")] = B(true);
                        ancestor[S("layoutStatus")] = S("unknown");
                        var parentId = ancestor[S("parentId")];
                        ancestor = Items(Member(this, "symbols")).FirstOrDefault(s => Equal(s[S("id")], parentId)) ?? Nil;
                    }
                }

                if (Truth(redefine_305))
                {
                    try
                    {
                        s_306[S("redefines")] = M_resolve(redefine_305)[S("id")];
                    }
                    catch (Exception error365) when (error365 is ParseException)
                    {
                        e_271 = S(error365.Message);
                        _ = M_diag(S("INVALID_REDEFINES"), StringValue(e_271), s_306[S("source")]);
                    }
                }

                foreach (var item366 in Items(Range(a_258, Member(this, "i"))))
                {
                    token_index_316 = item366;
                    Member(this, "owners")[token_index_316] = ListOf(S("declaration"), sid_309);
                }

                if (Truth(And(parent_293, () => B(!Equal(level_285, I(88))))))
                {
                    _ = Call(parent_293[S("children")], "append", sid_309);
                }

                _ = Call(Member(this, "symbols"), "append", s_306);
                if (Truth(keys_283))
                {
                    _ = Call(pending_keys_298, "append", ListOf(s_306, keys_283));
                }

                foreach (var item367 in Items(indexes_279))
                {
                    index_name_278 = item367;
                    index_id_277 = AddText(S("f"), Format(Add(Length(Member(this, "symbols")), I(1)), "06d"));
                    index_276 = Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Put(Merge(Dict(), s_306), S("id"), index_id_277), S("name"), index_name_278), S("qualifiedName"), index_name_278), S("level"), I(77)), S("parentId"), Nil), S("pic"), Put(Put(Put(Put(Put(Dict(), S("raw"), S(string.Empty)), S("category"), S("numeric")), S("digits"), I(18)), S("scale"), I(0)), S("signed"), B(true))), S("usage"), S("INDEX")), S("occurs"), I(1)), S("occursClause"), Nil), S("dependingOn"), Nil), S("occursMin"), I(1)), S("indexFor"), sid_309), S("initial"), Nil), S("conditions"), ListOf()), S("children"), ListOf()), S("redefines"), Nil);
                    _ = Call(Member(this, "symbols"), "append", index_276);
                    _ = Call(s_306[S("occursClause")][S("indexIds")], "append", index_id_277);
                }

                if (Truth(B(!Equal(level_285, I(88)))))
                {
                    _ = Call(stack_311, "append", s_306);
                    last_284 = s_306;
                }

                if (Truth(And(B(Equal(scope_307, S("FILE"))), () => And(current_file_267, () => B(Equal(level_285, I(1)))))))
                {
                    dep_268 = Next(Sequence(from item368 in Items(Member(this, "dependencies")) let d_369 = item368 where Truth(And(B(Equal(d_369[S("kind")], S("file"))), () => B(Equal(Call(d_369[S("name")], "upper"), Call(current_file_267, "upper"))))) select d_369), Nil);
                    if (Truth(dep_268))
                    {
                        _ = Call(Call(dep_268, "setdefault", S("recordIds"), ListOf()), "append", sid_309);
                        dep_268[S("recordId")] = sid_309;
                    }
                }
            }
            catch (Exception error) when (syntaxRecovery == SyntaxRecoveryPolicy.Tolerant && error is ParseException or ArgumentException && Text(Member(Member(this, "ts")[a_258], "upper")) is not ("CONDITIONAL-STUB" or "CONDITIONAL-ERROR"))
            {
                itemTransaction!.Dispose();
                ResetSymbolIndex();
                scope_307 = oldScope;
                stack_311 = oldStack;
                last_284 = oldLast;
                current_file_267 = oldFile;
                SetMember(this, "i", a_258);
                M_take();
                while (Truth(M_peek()) && Text(M_peek()) != "." && !Truth(M_declaration_boundary()))
                {
                    M_take();
                }

                M_accept(S("."));
                RecoveredNode(a_258, S("$unknownDeclaration"), error.Message, "UNSUPPORTED_DECLARATION");
                MarkUnknownLayout(stack_311);
            }
            finally
            {
                itemTransaction?.Commit();
            }
        }

        foreach (var item370 in Items(Member(this, "dependencies")))
        {
            dep_268 = item370;
            if (Truth(B(!Equal(dep_268[S("kind")], S("file")))))
            {
                continue;
            }

            records_304 = Call(dep_268, "get", S("recordIds"), ListOf());
            named_287 = Call(dep_268, "get", S("dataRecords"), ListOf());
            if (Truth(named_287))
            {
                records_304 = ListOf(from item371 in Items(Member(this, "symbols")) let s_372 = item371 where Truth(And(B(Contains(records_304, s_372[S("id")])), () => B(Contains(SetOf(from item373 in Items(named_287) let n_374 = item373 select Call(n_374, "upper")), Call(s_372[S("name")], "upper"))))) select s_372[S("id")]);
                if (Truth(Or(B(!Equal(Length(named_287), I(1))), () => B(!Equal(Length(records_304), I(1))))))
                {
                    if (syntaxRecovery == SyntaxRecoveryPolicy.Tolerant)
                    {
                        M_diag(S("UNSUPPORTED_DECLARATION"), S("FD DATA RECORD requires one unambiguous 01 record"), dep_268[S("source")]);
                        dep_268[S("layoutStatus")] = S("unknown");
                        dep_268[S("recordId")] = Nil;
                        continue;
                    }

                    throw new ParseException(Text(S("FD DATA RECORD requires one unambiguous 01 record")));
                }
            }

            if (Truth(B(!Equal(Length(records_304), I(1)))))
            {
                if (syntaxRecovery == SyntaxRecoveryPolicy.Tolerant)
                {
                    M_diag(S("UNSUPPORTED_DECLARATION"), S("FD requires one unambiguous 01 record"), dep_268[S("source")]);
                    dep_268[S("layoutStatus")] = S("unknown");
                    dep_268[S("recordId")] = Nil;
                    continue;
                }

                throw new ParseException(Text(S("FD requires one unambiguous 01 record")));
            }

            dep_268[S("recordId")] = records_304[I(0)];
        }

        foreach (var item375 in Items(pending_keys_298))
        {
            var unpack376 = item375;
            owner_292 = unpack376[I(0)];
            keys_283 = unpack376[I(1)];
            foreach (var item377 in Items(keys_283))
            {
                var unpack378 = item377;
                name_286 = unpack378[I(0)];
                direction_270 = unpack378[I(1)];
                candidates_263 = ListOf(from item379 in Items(Member(this, "symbols")) let item_380 = item379 where Truth(B(Equal(Call(item_380[S("name")], "upper"), Call(name_286, "upper")))) select item_380);
                V Fn_inside(V item_381)
                {
                    V p_382 = Nil;
                    p_382 = item_381[S("parentId")];
                    while (Truth(p_382))
                    {
                        if (Truth(B(Equal(p_382, owner_292[S("id")]))))
                        {
                            return B(true);
                        }

                        p_382 = Next(Sequence(from item383 in Items(Member(this, "symbols")) let x_384 = item383 where Truth(B(Equal(x_384[S("id")], p_382))) select x_384))[S("parentId")];
                    }

                    return B(Equal(item_381[S("id")], owner_292[S("id")]));
                }

                candidates_263 = ListOf(from item385 in Items(candidates_263) let item_386 = item385 where Truth(Fn_inside(item_386)) select item_386);
                if (Truth(B(!Equal(Length(candidates_263), I(1)))))
                {
                    _ = M_diag(S("INVALID_TABLE_KEY"), Add(S("Unresolved/ambiguous table key "), name_286), owner_292[S("source")]);
                }
                else
                {
                    _ = Call(owner_292[S("occursClause")][S("keys")], "append", Put(Put(Dict(), S("symbolId"), candidates_263[I(0)][S("id")]), S("direction"), direction_270));
                }
            }
        }

        return Nil;
    }
}
