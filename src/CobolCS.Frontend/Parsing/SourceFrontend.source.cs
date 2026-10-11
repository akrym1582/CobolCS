using System.Text;
using static CobolCS.Frontend.Parsing.Values;

namespace CobolCS.Frontend.Parsing;

/// <summary>位置とIRを保持する source の解析処理です。</summary>
internal sealed partial class SourceFrontend : V
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SourceFrontend"/> class. 解析状態を初期化します。
    /// </summary>
    /// <param name="config_">config の解析値。</param>
    /// <param name="copies_">copies の解析値。</param>
    /// <param name="base_">base の解析値。</param>
    /// <param name="allow_missing_copy_">allow_missing_copy の解析値。</param>
    internal SourceFrontend(V config_, V copies_, V base_, V? allow_missing_copy_ = null)
    {
        allow_missing_copy_ ??= B(false);
        var unpack46 = ListOf(config_, copies_, base_);
        SetMember(this, "config", unpack46[I(0)]);
        SetMember(this, "copies", unpack46[I(1)]);
        SetMember(this, "base", unpack46[I(2)]);
        SetMember(this, "allow_missing_copy", allow_missing_copy_);
        SetMember(this, "missing_copies", ListOf());
        var unpack47 = ListOf(Dict(), ListOf(), ListOf());
        SetMember(this, "sources", unpack47[I(0)]);
        SetMember(this, "diagnostics", unpack47[I(1)]);
        SetMember(this, "excluded", unpack47[I(2)]);
        SetMember(this, "rejected", ListOf());
        SetMember(this, "conditionals", ListOf());
    }

    /// <summary>
    /// 原文位置付きの診断を蓄積する。error / warning / reviewを区別し、実行対応状況も残す。
    /// </summary>
    /// <param name="code_">code の解析値。</param>
    /// <param name="message_">message の解析値。</param>
    /// <param name="source_">source の解析値。</param>
    /// <param name="severity_">severity の解析値。</param>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_diagnostic(V code_, V message_, V source_, V? severity_ = null)
    {
        severity_ ??= S("error");
        V d_48 = Nil;
        d_48 = Put(Put(Put(Put(Put(Put(Dict(), S("code"), code_), S("message"), message_), S("source"), source_), S("severity"), severity_), S("stage"), S("frontend")), S("supported"), B(false));
        _ = Call(Member(this, "diagnostics"), "append", d_48);
        return d_48;
    }

    /// <summary>
    /// 前処理しながら、各文字が元ファイルのどこから来たかを一緒に記録する。
    ///
    /// stackはCOPY展開元の経路、activeは今読み込んでいるファイルの経路。
    /// activeへの再訪を循環COPYとして止め、同じCOPYを別の場所で使うこととは区別する。
    /// 日本語は1文字でもUTF-8では複数byteなので、columnとbyteOffsetは別々に計算する。
    /// </summary>
    /// <param name="path_">path の解析値。</param>
    /// <param name="stack_">stack の解析値。</param>
    /// <param name="active_">active の解析値。</param>
    /// <returns>解析結果または更新後の状態。</returns>
    internal V M_load(V path_, V? stack_ = null, V? active_ = null)
    {
        stack_ ??= ListOf();
        active_ ??= ListOf();
        V byte_49 = Nil;
        V c_50 = Nil;
        V chars_51 = Nil;
        V content_52 = Nil;
        V continuation_53 = Nil;
        V cursor_54 = Nil;
        V cut_55 = Nil;
        V documentary_56 = Nil;
        V documentary_allowed_57 = Nil;
        V documentary_headers_58 = Nil;
        V documentary_ranges_59 = Nil;
        V end_60 = Nil;
        V free_continuation_61 = Nil;
        V header_62 = Nil;
        V i_63 = Nil;
        V indicator_64 = Nil;
        V j_65 = Nil;
        V joined_66 = Nil;
        V leading_67 = Nil;
        V line_68 = Nil;
        V line_no_69 = Nil;
        V line_start_70 = Nil;
        V m_71 = Nil;
        V name_72 = Nil;
        V offset_73 = Nil;
        V open_quote_74 = Nil;
        var positions = new List<(int Line, int Column, int ByteOffset, int EndColumn, int EndByteOffset)>();
        V prose_start_76 = Nil;
        V q_77 = Nil;
        V raw_78 = Nil;
        V segment_79 = Nil;
        V start_80 = Nil;
        V structural_81 = Nil;
        V text_82 = Nil;
        V tokens_83 = Nil;
        V trim_end_84 = Nil;
        path_ = Call(path_, "resolve");
        if (Truth(B(Contains(active_, path_))))
        {
            throw new ArgumentException(Text(Add(S("COPY_CYCLE: "), StringValue(path_))));
        }

        raw_78 = Call(path_, "read_bytes");
        text_82 = Call(raw_78, "decode", S("utf-8"), Named("errors", S("strict")));
        name_72 = Truth(Call(path_, "is_relative_to", Member(this, "base"))) ? Call(Call(path_, "relative_to", Member(this, "base")), "as_posix") : Member(path_, "name");
        if (Truth(And(B(Contains(Member(this, "sources"), name_72)), () => B(!Equal(Member(this, "sources")[name_72][S("sha256")], Call(Digest(raw_78), "hexdigest"))))))
        {
            throw new ArgumentException(Text(Add(S("SOURCE_PATH_COLLISION: "), name_72)));
        }

        Member(this, "sources")[name_72] = Put(Put(Put(Put(Put(Dict(), S("path"), name_72), S("sha256"), Call(Digest(raw_78), "hexdigest")), S("encoding"), S("utf-8")), S("text"), text_82), S("bytesHex"), Call(raw_78, "hex"));
        chars_51 = ListOf();
        byte_49 = I(0);
        open_quote_74 = Nil;
        free_continuation_61 = B(false);
        documentary_56 = B(false);
        documentary_allowed_57 = B(false);
        documentary_ranges_59 = ListOf();
        documentary_headers_58 = S("(?:AUTHOR|INSTALLATION|DATE-WRITTEN|DATE-COMPILED|SECURITY|REMARKS)");
        foreach (var item85 in Items(Enumerate(Call(text_82, "splitlines", Named("keepends", B(true))), I(1))))
        {
            var unpack86 = item85;
            line_no_69 = unpack86[I(0)];
            line_68 = unpack86[I(1)];
            line_start_70 = byte_49;
            byte_49 = Add(byte_49, Length(Call(line_68, "encode", S("utf-8"))));
            content_52 = Call(line_68, "rstrip", S("\r\n"));
            var byteOffsets = new List<int> { 0 };
            foreach (var rune in Text(content_52).EnumerateRunes())
            {
                byteOffsets.Add(byteOffsets[^1] + rune.Utf8SequenceLength);
            }

            start_80 = I(0);
            end_60 = Length(content_52);
            continuation_53 = B(false);
            int PrefixByteOffset(int column)
            {
                var length = byteOffsets.Count - 1;
                var index = column < 0 ? Math.Max(0, length + column) : Math.Min(column, length);
                return byteOffsets[index];
            }

            V Fn_pos(V col_87)
            {
                return Put(Put(Put(Dict(), S("line"), line_no_69), S("column"), Add(col_87, I(1))), S("byteOffset"), Add(line_start_70, I(PrefixByteOffset(Number(col_87)))));
            }

            V Fn_span(V a_88, V b_89)
            {
                return Put(Put(Put(Put(Put(Dict(), S("file"), name_72), S("start"), Fn_pos(a_88)), S("end"), Fn_pos(b_89)), S("copyStack"), ListCopy(stack_)), S("raw"), content_52[Slice(a_88, b_89, Nil)]);
            }

            void AppendPosition(V start, V end)
            {
                var first = Number(start);
                var last = Number(end);
                var lineOffset = Number(line_start_70);
                positions.Add((Number(line_no_69), first + 1, lineOffset + PrefixByteOffset(first), last + 1, lineOffset + PrefixByteOffset(last)));
            }

            if (Truth(B(Equal(Member(this, "config")[S("sourceFormat")], S("fixed")))))
            {
                if (Truth(And(B(!Contains(ListOf(S("*"), S("/")), content_52[Slice(I(6), I(7), Nil)])), () => And(B(Equal(Member(this, "config")[S("fixedColumnPolicy")], S("unicode-ascii"))), () => Any(Sequence(from item90 in Items(content_52) let c_91 = item90 select B(Compare(Ord(c_91), I(127)) > 0)))))))
                {
                    _ = M_diagnostic(S("FIXED_JAPANESE_COLUMNS"), S("Non-ASCII fixed columns require host verification"), Fn_span(I(0), Length(content_52)));
                }

                _ = Call(Member(this, "excluded"), "append", Put(Put(Dict(), S("reason"), S("sequence-columns")), S("source"), Fn_span(I(0), Minimum(new V[] { I(6), Length(content_52) }, null, null))));
                if (Truth(B(Compare(Length(content_52), I(72)) > 0)))
                {
                    _ = Call(Member(this, "excluded"), "append", Put(Put(Dict(), S("reason"), S("identification-columns")), S("source"), Fn_span(I(72), Length(content_52))));
                }

                indicator_64 = content_52[Slice(I(6), I(7), Nil)];
                if (Truth(B(Contains(ListOf(S("*"), S("/")), indicator_64))))
                {
                    _ = Call(Member(this, "excluded"), "append", Put(Put(Dict(), S("reason"), S("comment")), S("source"), Fn_span(I(0), Length(content_52))));
                    continue;
                }

                if (Truth(B(!Contains(ListOf(S(string.Empty), S(" "), S("-")), indicator_64))))
                {
                    _ = M_diagnostic(S("FIXED_INDICATOR"), S("Unsupported fixed indicator"), Fn_span(I(6), Minimum(new V[] { I(7), Length(content_52) }, null, null)));
                }

                start_80 = I(7);
                end_60 = Minimum(new V[] { I(72), end_60 }, null, null);
                continuation_53 = B(Equal(indicator_64, S("-")));
            }

            if (Truth(B(Equal(Member(this, "config")[S("sourceFormat")], S("free")))))
            {
                continuation_53 = free_continuation_61;
                if (Truth(continuation_53))
                {
                    start_80 = Subtract(Length(content_52), Length(Call(content_52, "lstrip")));
                    if (Truth(B(Equal(content_52[Slice(start_80, Add(start_80, I(1)), Nil)], S("&")))))
                    {
                        start_80 = Add(start_80, I(1));
                    }
                }

                trim_end_84 = Length(Call(content_52, "rstrip"));
                free_continuation_61 = And(B(Compare(trim_end_84, start_80) > 0), () => B(Equal(content_52[Slice(Subtract(trim_end_84, I(1)), trim_end_84, Nil)], S("&"))));
                if (Truth(free_continuation_61))
                {
                    end_60 = Subtract(trim_end_84, I(1));
                }
            }

            segment_79 = content_52[Slice(start_80, end_60, Nil)];
            if (Truth(And(B(!Truth(continuation_53)), () => RegexMatch(S("\\s*IDENTIFICATION\\s+DIVISION\\s*\\."), segment_79, I(1)))))
            {
                documentary_allowed_57 = B(true);
            }

            header_62 = Truth(And(documentary_allowed_57, () => B(!Truth(continuation_53)))) ? RegexMatch(Add(Add(S("\\s*("), documentary_headers_58), S(")\\s*\\.")), segment_79, I(1)) : Nil;
            structural_81 = RegexMatch(S("\\s*(?:(?:IDENTIFICATION|ENVIRONMENT|DATA|PROCEDURE)\\s+DIVISION|CONFIGURATION\\s+SECTION)\\s*\\."), segment_79, I(1));
            if (Truth(And(structural_81, () => B(!Truth(RegexMatch(S("\\s*IDENTIFICATION\\b"), segment_79, I(1)))))))
            {
                documentary_allowed_57 = B(false);
            }

            if (Truth(And(documentary_56, () => Or(header_62, () => structural_81))))
            {
                documentary_56 = B(false);
            }

            if (Truth(Or(header_62, () => documentary_56)))
            {
                prose_start_76 = Truth(header_62) ? Call(header_62, "end") : I(0);
                if (Truth(chars_51))
                {
                    _ = Call(chars_51, "append", S("\n"));
                    AppendPosition(start_80, start_80);
                }

                offset_73 = Length(chars_51);
                foreach (var item92 in Items(Enumerate(segment_79)))
                {
                    var unpack93 = item92;
                    j_65 = unpack93[I(0)];
                    c_50 = unpack93[I(1)];
                    _ = Call(chars_51, "append", c_50);
                    AppendPosition(Add(start_80, j_65), Add(Add(start_80, j_65), I(1)));
                }

                if (Truth(B(Compare(Length(segment_79), prose_start_76) > 0)))
                {
                    _ = Call(documentary_ranges_59, "append", ListOf(Add(offset_73, prose_start_76), Length(chars_51)));
                }

                documentary_56 = B(true);
                open_quote_74 = Nil;
                continue;
            }

            if (Truth(And(open_quote_74, () => B(!Truth(continuation_53)))))
            {
                _ = M_diagnostic(S("UNTERMINATED_LITERAL"), S("Quoted literal requires an explicit continuation"), PositionSource(positions.Count - 1));
            }

            segment_79 = content_52[Slice(start_80, end_60, Nil)];
            cut_55 = Length(segment_79);
            q_77 = Truth(continuation_53) ? open_quote_74 : Nil;
            i_63 = I(0);
            if (Truth(And(continuation_53, () => open_quote_74)))
            {
                leading_67 = Subtract(Length(segment_79), Length(Call(segment_79, "lstrip")));
                if (Truth(B(Equal(segment_79[Slice(leading_67, Add(leading_67, I(1)), Nil)], open_quote_74))))
                {
                    start_80 = Add(start_80, Add(leading_67, I(1)));
                    segment_79 = content_52[Slice(start_80, end_60, Nil)];
                }
            }

            if (Truth(And(chars_51, () => B(!Truth(And(continuation_53, () => open_quote_74))))))
            {
                _ = Call(chars_51, "append", S("\n"));
                AppendPosition(start_80, start_80);
            }

            q_77 = Truth(continuation_53) ? open_quote_74 : Nil;
            while (Truth(B(Compare(i_63, Length(segment_79)) < 0)))
            {
                c_50 = segment_79[i_63];
                if (Truth(And(B(!Truth(q_77)), () => B(Equal(segment_79[Slice(i_63, Add(i_63, I(2)), Nil)], S("*>"))))))
                {
                    cut_55 = i_63;
                    break;
                }

                if (Truth(B(Contains(ListOf(S("\""), S("'")), c_50))))
                {
                    if (Truth(And(B(Equal(q_77, c_50)), () => B(Equal(segment_79[Slice(Add(i_63, I(1)), Add(i_63, I(2)), Nil)], c_50)))))
                    {
                        i_63 = Add(i_63, I(2));
                        continue;
                    }

                    q_77 = Truth(B(Equal(q_77, c_50))) ? Nil : (Truth(B(ReferenceEquals(q_77, Nil))) ? c_50 : q_77);
                }

                i_63 = Add(i_63, I(1));
            }

            foreach (var item94 in Items(Enumerate(segment_79[Slice(Nil, cut_55, Nil)])))
            {
                var unpack95 = item94;
                j_65 = unpack95[I(0)];
                c_50 = unpack95[I(1)];
                _ = Call(chars_51, "append", c_50);
                AppendPosition(Add(start_80, j_65), Add(Add(start_80, j_65), I(1)));
            }

            if (Truth(B(Compare(cut_55, Length(segment_79)) < 0)))
            {
                _ = Call(Member(this, "excluded"), "append", Put(Put(Dict(), S("reason"), S("comment")), S("source"), Fn_span(Add(start_80, cut_55), end_60)));
            }

            open_quote_74 = q_77;
        }

        if (Truth(open_quote_74))
        {
            _ = M_diagnostic(S("UNTERMINATED_LITERAL"), S("Unterminated quoted literal"), PositionSource(positions.Count - 1));
        }

        V PositionSource(int index)
        {
            var position = positions[index];
            V Point() => Put(Put(Put(Dict(), S("line"), I(position.Line)), S("column"), I(position.Column)), S("byteOffset"), I(position.ByteOffset));
            return Put(Put(Put(Put(Put(Dict(), S("file"), name_72), S("start"), Point()), S("end"), Point()), S("copyStack"), ListCopy(stack_)), S("raw"), S(string.Empty));
        }

        joined_66 = Call(S(string.Empty), "join", chars_51);
        tokens_83 = ListOf();
        V Fn_emit(V start_96, V end_97, V value_98, V? documentary_99 = null)
        {
            documentary_99 ??= B(false);
            V src_100 = Nil;
            src_100 = PositionSource(Number(start_96));
            var last = positions[Number(end_97) - 1];
            src_100[S("end")] = Put(Put(Put(Dict(), S("line"), I(last.Line)), S("column"), I(last.EndColumn)), S("byteOffset"), I(last.EndByteOffset));
            src_100[S("raw")] = value_98;
            if (Truth(documentary_99))
            {
                src_100[S("documentary")] = B(true);
            }

            _ = Call(tokens_83, "append", Token(value_98, src_100));
            return Nil;
        }

        cursor_54 = I(0);
        foreach (var item101 in Items(ListOf(Enumerable.Empty<V>().Concat(Items(documentary_ranges_59)).Concat(new V[] { ListOf(Length(joined_66), Length(joined_66)) }))))
        {
            var unpack102 = item101;
            start_80 = unpack102[I(0)];
            end_60 = unpack102[I(1)];
            foreach (var item103 in Items(Call(TokenPattern, "finditer", joined_66, cursor_54, start_80)))
            {
                m_71 = item103;
                _ = Fn_emit(Call(m_71, "start"), Call(m_71, "end"), Call(m_71, "group"));
            }

            if (Truth(B(Compare(end_60, start_80) > 0)))
            {
                _ = Fn_emit(start_80, end_60, joined_66[Slice(start_80, end_60, Nil)], documentary_99: B(true));
            }

            cursor_54 = end_60;
        }

        return M_expand_copies(M_conditional_tokens(tokens_83), path_, stack_, active_, name_72, raw_78);
    }
}
