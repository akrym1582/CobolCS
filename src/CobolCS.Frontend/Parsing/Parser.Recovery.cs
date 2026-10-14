using static CobolCS.Frontend.Parsing.Values;

namespace CobolCS.Frontend.Parsing;

/// <summary>未知範囲を保持し、親の終端と共有IRを保護して解析を再開します。</summary>
internal sealed partial class Parser
{
    private static readonly HashSet<string> ScopedVerbs = new("IF EVALUATE PERFORM SEARCH STRING UNSTRING INSPECT CALL EXEC READ WRITE REWRITE DELETE START RETURN ADD SUBTRACT MULTIPLY DIVIDE COMPUTE ACCEPT DISPLAY".Split(), StringComparer.Ordinal);
    private readonly List<V> recoveredHeaders = [];

    /// <summary>未知宣言がある親領域の配置を未確定にします。</summary>
    /// <param name="ancestors">現在の宣言の祖先。</param>
    private static void MarkUnknownLayout(V ancestors)
    {
        foreach (var ancestor in Items(ancestors))
        {
            ancestor[S("stub")] = B(true);
            ancestor[S("layoutStatus")] = S("unknown");
        }
    }

    /// <summary>失敗した解析が作った索引を破棄します。次の参照解決時に確定symbolから再構築します。</summary>
    private void ResetSymbolIndex()
    {
        if (indexedSymbolCount <= Number(Length(Member(this, "symbols"))))
        {
            return;
        }

        indexedSymbolCount = 0;
        symbolsByName.Clear();
        symbolsById.Clear();
    }

    /// <summary>後処理で意味を確定できない文も、全programを捨てず未知範囲に戻します。</summary>
    /// <param name="statement">構造解析済みの文。</param>
    /// <param name="reason">確定できない理由。</param>
    private void RecoverExistingStatement(V statement, string reason)
    {
        statement[S("kind")] = S("unknown");
        statement[S("reason")] = S(reason);
        statement[S("recovery")] = Put(Put(Dict(), S("policy"), S("tolerant")), S("boundary"), S(string.Empty));
        var indices = Items(Range(I(0), Length(Member(this, "ts")))).Where(index =>
        {
            var owner = Call(Member(this, "owners"), "get", index);
            return Truth(owner) && Equal(owner[I(1)], statement[S("id")]);
        }).ToArray();
        statement[S("tokens")] = ListOf(indices.Select(index => Put(Put(Dict(), S("text"), Member(Member(this, "ts")[index], "text")), S("source"), Member(Member(this, "ts")[index], "source"))));
        foreach (var index in indices)
        {
            Member(this, "owners")[index] = ListOf(S("unknown"), statement[S("id")]);
        }

        M_diag(S("UNSUPPORTED_STATEMENT"), S(reason), statement[S("source")]);
        Put(Member(this, "ds")[I(-1)], S("recovery"), statement[S("recovery")]);
    }

    /// <summary>段落、SECTION、END PROGRAMの境界を検出します。</summary>
    /// <param name="recovering">復旧中は引数末尾を段落名と誤認しないよう、改行も確認します。</param>
    /// <returns>手続きの構造境界かどうか。</returns>
    private bool ProcedureBoundary(bool recovering = false)
    {
        if (Text(M_peek()) == "END" && Text(M_peek(I(1))) == "PROGRAM")
        {
            return true;
        }

        if (Contains(Verbs, M_peek()) || Text(M_peek(I(1))) is not ("." or "SECTION"))
        {
            return false;
        }

        if (!recovering || Text(M_peek(I(1))) == "SECTION")
        {
            return true;
        }

        var index = Number(Member(this, "i"));
        var currentSource = Member(Member(this, "ts")[I(index)], "source");
        var previousSource = Member(Member(this, "ts")[I(index - 1)], "source");
        return Text(currentSource[S("file")]) == Text(previousSource[S("file")]) && Number(currentSource[S("start")][S("line")]) > Number(previousSource[S("end")][S("line")]);
    }

    /// <summary>原文と再開位置を含む未知範囲を作り、全tokenに一つの所属を付けます。</summary>
    /// <param name="start">範囲の開始。</param>
    /// <param name="id">補完対象の識別子。</param>
    /// <param name="reason">解析失敗理由。</param>
    /// <param name="code">診断コード。</param>
    /// <returns>未知文のIR。</returns>
    private V RecoveredNode(V start, V id, string reason, string code)
    {
        var source = M_source(start);
        var recovery = Put(Put(Dict(), S("policy"), S("tolerant")), S("boundary"), M_peek());
        if (Truth(M_peek()))
        {
            Put(recovery, S("resumeSource"), Member(Member(this, "ts")[Member(this, "i")], "source"));
        }

        M_diag(S(code), S(reason), source);
        Put(Member(this, "ds")[I(-1)], S("recovery"), recovery);
        foreach (var index in Items(Range(start, Member(this, "i"))))
        {
            Member(this, "owners")[index] = ListOf(S("unknown"), id);
        }

        return Put(Put(Put(Put(Put(Put(Dict(), S("kind"), S("unknown")), S("id"), id), S("source"), source), S("reason"), S(reason)), S("tokens"), M_raw_tokens(start, Member(this, "i"))), S("recovery"), recovery);
    }

    /// <summary>失敗した文の開始から、安全な文末または親の境界まで走査します。</summary>
    /// <param name="start">失敗文の開始。</param>
    /// <param name="stops">親の終端。</param>
    private void RecoverStatement(V start, V stops)
    {
        SetMember(this, "i", start);
        var verb = Text(Member(M_take(), "upper"));
        var scopes = new Stack<string>();
        if (ScopedVerbs.Contains(verb))
        {
            scopes.Push(verb);
        }

        while (Truth(M_peek()) && Text(M_peek()) != ".")
        {
            var word = Text(M_peek());
            var closing = word == "END-EXEC" ? "EXEC" : word.StartsWith("END-", StringComparison.Ordinal) ? word[4..] : string.Empty;
            while (scopes.Count > 1 && scopes.Peek() is not ("IF" or "EVALUATE" or "SEARCH" or "PERFORM") && (closing.Length != 0 || Contains(Verbs, M_peek())))
            {
                if (closing == scopes.Peek())
                {
                    break;
                }

                scopes.Pop();
            }

            if (scopes.Count != 0 && closing == scopes.Peek())
            {
                M_take();
                scopes.Pop();
                if (scopes.Count == 0)
                {
                    break;
                }

                continue;
            }

            if (ProcedureBoundary(recovering: true) || (Contains(stops, M_peek()) && scopes.Count <= 1))
            {
                break;
            }

            if (scopes.Count == 0 && Contains(Verbs, M_peek()))
            {
                break;
            }

            if (ScopedVerbs.Contains(word) && (word != "PERFORM" || Text(M_peek(I(1))) is "UNTIL" or "VARYING" or "WITH" or "TEST" || char.IsAsciiDigit(Text(M_peek(I(1))).FirstOrDefault())))
            {
                scopes.Push(word);
            }

            M_take();
        }
    }

    /// <summary>文ごとの変更を確定し、失敗文だけを原文付き補完対象にします。</summary>
    /// <param name="stops">親ブロックの終端。</param>
    /// <param name="top">段落の直下かどうか。</param>
    /// <returns>既知文と未知文を含む列。</returns>
    private V TolerantBlock(V stops, bool top)
    {
        var body = ListOf();
        while (Truth(M_peek()) && !Contains(stops, M_peek()) && Text(M_peek()) != ".")
        {
            if (ProcedureBoundary(recovering: !top) || (!top && Text(M_peek()).StartsWith("END-", StringComparison.Ordinal)))
            {
                break;
            }

            var start = Member(this, "i");
            var id = AddText(S("s"), Format(Member(this, "sid"), "06d"));
            SetMember(this, "sid", Add(Member(this, "sid"), I(1)));
            V node;
            using (var transaction = new ValueTransaction())
            {
                try
                {
                    node = M_statement();
                    transaction.Commit();
                }
                catch (Exception error) when (error is ParseException or ArgumentException)
                {
                    transaction.Dispose();
                    ResetSymbolIndex();
                    var failedVerb = Text(Member(Member(this, "ts")[start], "upper"));
                    if (ScopedVerbs.Contains(failedVerb) && Contains(stops, S("END-" + failedVerb)))
                    {
                        // 同種の親と子のELSEや終端を区別できない失敗は、親全体へ復旧を委ねます。
                        throw new ParseException("Ambiguous nested " + failedVerb + ": " + error.Message);
                    }

                    RecoverStatement(start, stops);
                    node = RecoveredNode(start, id, error.Message, "UNSUPPORTED_STATEMENT");
                }
            }

            foreach (var index in Items(Range(start, Member(this, "i"))))
            {
                if (!Contains(Member(this, "owners"), index))
                {
                    Member(this, "owners")[index] = ListOf(Text(node[S("kind")]) == "controlStub" ? S("stubbed") : S("statement"), id);
                }
            }

            Merge(node, Put(Put(Dict(), S("id"), id), S("source"), M_source(start)));
            Call(body, "append", node);
            if (top && Text(M_peek()) == ".")
            {
                var token = M_take();
                Call(body, "append", Put(Put(Put(Dict(), S("kind"), S("sentenceBoundary")), S("id"), AddText(S("s"), Format(Member(this, "sid"), "06d"))), S("source"), Member(token, "source")));
                SetMember(this, "sid", Add(Member(this, "sid"), I(1)));
                break;
            }
        }

        return body;
    }

    /// <summary>未知ヘッダーの意味を実行前に補完できるように保持します。</summary>
    /// <param name="start">句の開始。</param>
    /// <param name="reason">未知の理由。</param>
    private void RecoverHeader(V start, string reason)
    {
        var id = AddText(S("s"), Format(Member(this, "sid"), "06d"));
        SetMember(this, "sid", Add(Member(this, "sid"), I(1)));
        recoveredHeaders.Add(RecoveredNode(start, id, reason, "UNKNOWN_HEADER"));
    }

    /// <summary>未知ヘッダーを最初の段落へ配置して、未補完の意味設定で実行しないようにします。</summary>
    private void AddRecoveredHeaders()
    {
        if (recoveredHeaders.Count == 0)
        {
            return;
        }

        var paragraphs = Member(this, "paragraphs");
        if (!Truth(paragraphs))
        {
            Call(paragraphs, "append", Put(Put(Put(Put(Put(Put(Dict(), S("id"), S("p000000")), S("name"), S("$entry")), S("section"), Nil), S("order"), I(0)), S("statements"), ListOf()), S("source"), recoveredHeaders[0][S("source")]));
        }

        var entry = paragraphs[I(0)];
        entry[S("statements")] = Add(ListOf(recoveredHeaders), entry[S("statements")]);
    }
}
