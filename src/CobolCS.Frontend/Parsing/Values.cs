using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace CobolCS.Frontend.Parsing;

/// <summary>位置付き解析器のIR、文字列、集合、順序付き列を操作します。</summary>
internal static partial class Values
{
    /// <summary>未指定値。</summary>
    internal static readonly V Nil = new();

    /// <summary>文開始語。</summary>
    internal static readonly V Verbs = SetOf("CONDITIONAL-STUB ACCEPT SORT MERGE RELEASE RETURN REWRITE START DELETE CANCEL SEARCH NEXT INITIALIZE MOVE DISPLAY ADD SUBTRACT MULTIPLY DIVIDE COMPUTE IF EVALUATE PERFORM GO GOBACK STOP EXIT CONTINUE CALL EXEC OPEN READ WRITE CLOSE SET STRING UNSTRING INSPECT".Split(' ').Select(S));

    /// <summary>ブロック終端語。</summary>
    internal static readonly V Ends = SetOf("ELSE END-IF END-EVALUATE END-PERFORM END-ADD END-SUBTRACT END-MULTIPLY END-DIVIDE END-COMPUTE END-READ WHEN END-CALL ON NOT AT END-EXEC END-STRING END-UNSTRING END-SEARCH END-WRITE END-REWRITE END-DELETE END-START END-RETURN INVALID".Split(' ').Select(S));

    private static readonly UTF8Encoding Utf8 = new(false, true);

    /// <summary>文字列を原文のままIR構築値に包みます。Unicode正規化は行いません。</summary>
    /// <param name="value">value として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V S(string value) => new(value);

    /// <summary>整数を任意精度のIR構築値に包み、設定や位置の丸めを防ぎます。</summary>
    /// <param name="value">value として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V I(long value) => new(new BigInteger(value));

    /// <summary>COBOL解析状態の真偽値をIR構築値に包みます。</summary>
    /// <param name="value">value として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V B(bool value) => new(value);

    /// <summary>列挙順を固定した可変リストを作り、要素の参照は共有します。</summary>
    /// <param name="items">items として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V ListOf(params V[] items) => new(items.ToList());

    /// <summary>列挙順を固定した可変リストを作り、要素の参照は共有します。</summary>
    /// <param name="items">items として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V ListOf(IEnumerable<V> items) => new(items.ToList());

    /// <summary>解析値の Sequence 操作を行い、IRの値と順序を維持します。</summary>
    /// <param name="items">items として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V Sequence(IEnumerable<V> items) => new(items);

    /// <summary>IRスカラーの等値性を用いて集合を作ります。</summary>
    /// <param name="items">items として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V SetOf(params V[] items) => SetOf((IEnumerable<V>)items);

    /// <summary>IRスカラーの等値性を用いて集合を作ります。</summary>
    /// <param name="items">items として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V SetOf(IEnumerable<V> items) => new(new HashSet<V>(items, ValueEquality.Instance));

    /// <summary>IRのキーを扱う辞書を作り、フィールドの挿入順を保持します。</summary>
    /// <returns>操作の結果。</returns>
    internal static V Dict() => new(new Dictionary<V, V>(ValueEquality.Instance));

    /// <summary>リストの外側だけを複製し、解析途中の要素の参照は共有します。</summary>
    /// <param name="value">value として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V ListCopy(V? value = null) => ListOf(value is null ? [] : Items(value));

    /// <summary>列挙値から独立した集合を作り、要素の参照は共有します。</summary>
    /// <param name="value">value として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V SetCopy(V? value = null) => SetOf(value is null ? [] : Items(value));

    /// <summary>解析値の Text 操作を行い、IRの値と順序を維持します。</summary>
    /// <param name="value">value として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static string Text(V value) => value.Data switch
    {
        null => "None",
        string s => s,
        bool b => b ? "True" : "False",
        BigInteger i => i.ToString(CultureInfo.InvariantCulture),
        FilePath p => p.Path,
        _ => value.Data.ToString()!,
    };

    /// <summary>解析値の Number 操作を行い、IRの値と順序を維持します。</summary>
    /// <param name="value">value として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static int Number(V value) => checked((int)(BigInteger)Integer(value).Data!);

    /// <summary>解析値の Integer 操作を行い、IRの値と順序を維持します。</summary>
    /// <param name="value">value として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V Integer(V value)
    {
        if (value.Data is BigInteger)
        {
            return value;
        }

        if (value.Data is bool boolean)
        {
            return I(boolean ? 1 : 0);
        }

        var text = Text(value);
        var normalized = string.Concat(text.EnumerateRunes().Select(r => Rune.GetUnicodeCategory(r) == UnicodeCategory.DecimalDigitNumber ? ((int)Rune.GetNumericValue(r)).ToString(CultureInfo.InvariantCulture) : r.ToString()));
        if (!BigInteger.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            throw new ArgumentException("invalid literal for int() with base 10: '" + text + "'");
        }

        return new(number);
    }

    /// <summary>解析値の Boolean 操作を行い、IRの値と順序を維持します。</summary>
    /// <param name="value">value として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V Boolean(V value) => B(Truth(value));

    /// <summary>解析値の StringValue 操作を行い、IRの値と順序を維持します。</summary>
    /// <param name="value">value として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V StringValue(V value) => S(Text(value));

    /// <summary>空の文字列・コレクション、null、数値ゼロを偽として条件を評価します。</summary>
    /// <param name="value">value として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static bool Truth(V value) => value.Data switch
    {
        null => value is Parser or SourceFrontend,
        bool b => b,
        BigInteger n => n != 0,
        string s => s.Length != 0,
        byte[] b => b.Length != 0,
        Dictionary<V, V> d => d.Count != 0,
        ICollection<V> c => c.Count != 0,
        _ => true,
    };

    /// <summary>左辺が偽なら右辺を評価せず、真なら右辺の構築値を返します。</summary>
    /// <param name="left">left として渡す操作対象。</param>
    /// <param name="right">right として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V And(V left, Func<V> right) => Truth(left) ? right() : left;

    /// <summary>左辺が真なら右辺を評価せず、偽なら右辺の構築値を返します。</summary>
    /// <param name="left">left として渡す操作対象。</param>
    /// <param name="right">right として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V Or(V left, Func<V> right) => Truth(left) ? left : right();

    /// <summary>宣言やIRの値が可変リストであるかを判定します。</summary>
    /// <param name="value">value として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static bool IsList(V value) => value.Data is List<V>;

    /// <summary>宣言やIRの値がフィールド辞書であるかを判定します。</summary>
    /// <param name="value">value として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static bool IsDict(V value) => value.Data is Dictionary<V, V>;

    /// <summary>文字列はUnicodeスカラー、辞書はキー、配列は格納順で列挙します。</summary>
    /// <param name="value">value として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static IEnumerable<V> Items(V value) => value.Data switch
    {
        string s => s.EnumerateRunes().Select(r => S(r.ToString())),
        Dictionary<V, V> d => d.Keys,
        IEnumerable<V> s => s,
        byte[] b => b.Select(x => I(x)),
        _ => throw new InvalidOperationException("Not iterable: " + Text(value)),
    };

    /// <summary>文字列はUnicodeスカラー数、配列・辞書は要素数を返します。</summary>
    /// <param name="value">value として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V Length(V value) => I(value.Data switch
    {
        byte[] bytes => bytes.Length,
        string text => value.ScalarOffsets is { } offsets ? offsets.Length - 1 : text.Length,
        ICollection<V> collection => collection.Count,
        Dictionary<V, V> map => map.Count,
        _ => Items(value).Count(),
    });

    /// <summary>スカラーの値とリストの要素順を比較し、解析状態自体は参照で区別します。</summary>
    /// <param name="left">left として渡す操作対象。</param>
    /// <param name="right">right として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static bool Equal(V left, V right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is Parser or SourceFrontend || right is Parser or SourceFrontend)
        {
            return false;
        }

        if (left.Data is List<V> a && right.Data is List<V> b)
        {
            return a.SequenceEqual(b, ValueEquality.Instance);
        }

        return Equals(left.Data, right.Data);
    }

    /// <summary>整数は任意精度、文字列はUnicodeスカラー順で比較します。</summary>
    /// <param name="left">left として渡す操作対象。</param>
    /// <param name="right">right として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static int Compare(V left, V right)
    {
        if (left.Data is BigInteger a && right.Data is BigInteger b)
        {
            return a.CompareTo(b);
        }

        var aa = Text(left).EnumerateRunes().Select(r => r.Value).ToArray();
        var bb = Text(right).EnumerateRunes().Select(r => r.Value).ToArray();
        for (var i = 0; i < Math.Min(aa.Length, bb.Length); i++)
        {
            if (aa[i] != bb[i])
            {
                return aa[i].CompareTo(bb[i]);
            }
        }

        return aa.Length.CompareTo(bb.Length);
    }

    /// <summary>文字列の部分一致、辞書のキー、集合・リストの要素の存在を判定します。</summary>
    /// <param name="collection">collection として渡す操作対象。</param>
    /// <param name="value">value として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static bool Contains(V collection, V value) => collection.Data switch
    {
        string text => text.Contains(Text(value), StringComparison.Ordinal),
        Dictionary<V, V> map => map.ContainsKey(value),
        HashSet<V> set => set.Contains(value),
        _ => Items(collection).Contains(value, ValueEquality.Instance),
    };

    /// <summary>辞書のフィールドを更新して同じ辞書を返し、IR構築の共有参照を維持します。</summary>
    /// <param name="map">map として渡す操作対象。</param>
    /// <param name="key">key として渡す操作対象。</param>
    /// <param name="value">value として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V Put(V map, V key, V value)
    {
        ((Dictionary<V, V>)map.Data!)[key] = value;
        return map;
    }

    /// <summary>別の辞書のフィールドを挿入順で取り込み、値の参照を共有します。</summary>
    /// <param name="map">map として渡す操作対象。</param>
    /// <param name="other">other として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V Merge(V map, V other)
    {
        foreach (var (key, value) in (Dictionary<V, V>)other.Data!)
        {
            Put(map, key, value);
        }

        return map;
    }

    /// <summary>負の添字と範囲を解釈します。文字列ではUTF-16でなくUnicodeスカラーを数えます。</summary>
    /// <param name="value">value として渡す操作対象。</param>
    /// <param name="key">key として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V Index(V value, V key)
    {
        if (value.Data is Dictionary<V, V> map)
        {
            return map[key];
        }

        if (key.Data is SliceRange range)
        {
            var count = Number(Length(value));
            var start = ReferenceEquals(range.Start, Nil) ? 0 : Bound(Number(range.Start), count);
            var end = ReferenceEquals(range.End, Nil) ? count : Bound(Number(range.End), count);
            var length = Math.Max(0, end - start);
            if (value.Data is byte[] bytes)
            {
                return new V(bytes.AsSpan(start, length).ToArray());
            }

            if (value.Data is List<V> sequence)
            {
                return new V(sequence.GetRange(start, length));
            }

            if (value.Data is string text)
            {
                var offsets = value.ScalarOffsets;
                var first = offsets is null ? start : offsets[start];
                var last = offsets is null ? start + length : offsets[start + length];
                return S(text[first..last]);
            }

            return ListOf(Items(value).Skip(start).Take(length));
        }

        var index = Number(key);
        if (value.Data is List<V> list)
        {
            return list[index < 0 ? list.Count + index : index];
        }

        if (value.Data is string characters)
        {
            var offsets = value.ScalarOffsets;
            var count = offsets is null ? characters.Length : offsets.Length - 1;
            index = index < 0 ? count + index : index;
            return S(offsets is null ? characters[index].ToString() : characters[offsets[index]..offsets[index + 1]]);
        }

        if (value.Data is byte[] data)
        {
            return I(data[index < 0 ? data.Length + index : index]);
        }

        var values = Items(value).ToArray();
        return values[index < 0 ? values.Length + index : index];
    }

    /// <summary>辞書のキーまたはリストの添字で値の参照を置き換えます。</summary>
    /// <param name="value">value として渡す操作対象。</param>
    /// <param name="key">key として渡す操作対象。</param>
    /// <param name="item">item として渡す操作対象。</param>
    internal static void SetIndex(V value, V key, V item)
    {
        if (value.Data is Dictionary<V, V> map)
        {
            map[key] = item;
        }
        else
        {
            var list = (List<V>)value.Data!;
            var index = Number(key);
            list[index < 0 ? list.Count + index : index] = item;
        }
    }

    /// <summary>省略可能な開始・終了位置を格納し、対象の長さが判明した時点で境界を解決します。</summary>
    /// <param name="start">start として渡す操作対象。</param>
    /// <param name="end">end として渡す操作対象。</param>
    /// <param name="step">step として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V Slice(V start, V end, V step) => new(new SliceRange(start, end, step));

    /// <summary>解析値の Add 操作を行い、IRの値と順序を維持します。</summary>
    /// <param name="a">a として渡す操作対象。</param>
    /// <param name="b">b として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V Add(V a, V b) => a.Data switch
    {
        string s => S(s + Text(b)),
        List<V> list => ListOf(list.Concat(Items(b))),
        _ => new((BigInteger)a.Data! + (BigInteger)b.Data!),
    };

    /// <summary>解析値の Subtract 操作を行い、IRの値と順序を維持します。</summary>
    /// <param name="a">a として渡す操作対象。</param>
    /// <param name="b">b として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V Subtract(V a, V b) => new((BigInteger)a.Data! - (BigInteger)b.Data!);

    /// <summary>解析値の Multiply 操作を行い、IRの値と順序を維持します。</summary>
    /// <param name="a">a として渡す操作対象。</param>
    /// <param name="b">b として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V Multiply(V a, V b) => a.Data is string s ? S(string.Concat(Enumerable.Repeat(s, Number(b)))) : new((BigInteger)a.Data! * (BigInteger)b.Data!);

    /// <summary>解析値の Divide 操作を行い、IRの値と順序を維持します。</summary>
    /// <param name="a">a として渡す操作対象。</param>
    /// <param name="b">b として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V Divide(V a, V b) => new(new FilePath(System.IO.Path.Combine(Text(a), Text(b))));

    /// <summary>既存要素を変更せずに両集合の和を作ります。</summary>
    /// <param name="a">a として渡す操作対象。</param>
    /// <param name="b">b として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V Union(V a, V b) => SetOf(Items(a).Concat(Items(b)));

    /// <summary>解析値の AddText 操作を行い、IRの値と順序を維持します。</summary>
    /// <param name="values">values として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V AddText(params V[] values) => S(string.Concat(values.Select(Text)));

    /// <summary>解析値の Format 操作を行い、IRの値と順序を維持します。</summary>
    /// <param name="value">value として渡す操作対象。</param>
    /// <param name="format">format として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V Format(V value, string format) => format.Length == 0 ? StringValue(value) : S(((BigInteger)value.Data!).ToString("D" + int.Parse(format.TrimEnd('d'), CultureInfo.InvariantCulture), CultureInfo.InvariantCulture));

    /// <summary>解析値の Range 操作を行い、IRの値と順序を維持します。</summary>
    /// <param name="a">a として渡す操作対象。</param>
    /// <param name="b">b として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V Range(V a, V? b = null) => Sequence(Enumerable.Range(b is null ? 0 : Number(a), Math.Max(0, b is null ? Number(a) : Number(b) - Number(a))).Select(x => I(x)));

    /// <summary>開始番号を付けて、番号と要素参照の組を遅延列挙します。</summary>
    /// <param name="value">value として渡す操作対象。</param>
    /// <param name="start">start として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V Enumerate(V value, V? start = null) => Sequence(Items(value).Select((v, i) => ListOf(I(i + (start is null ? 0 : Number(start))), v)));

    /// <summary>短い側の末尾まで、対応する位置の要素を組にします。</summary>
    /// <param name="a">a として渡す操作対象。</param>
    /// <param name="b">b として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V Zip(V a, V b) => Sequence(Items(a).Zip(Items(b), (x, y) => ListOf(x, y)));

    /// <summary>最初の真の構築値で列挙を打ち切ります。</summary>
    /// <param name="values">values として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V Any(V values) => B(Items(values).Any(Truth));

    /// <summary>最初の偽の構築値で列挙を打ち切ります。空列挙は真です。</summary>
    /// <param name="values">values として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V All(V values) => B(Items(values).All(Truth));

    /// <summary>位置や長さの整数を任意精度で合計します。</summary>
    /// <param name="values">values として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V Sum(V values) => new(Items(values).Aggregate(BigInteger.Zero, (sum, value) => sum + (BigInteger)Integer(value).Data!));

    /// <summary>最初の要素を返します。要素も既定値もなければ失敗します。</summary>
    /// <param name="values">values として渡す操作対象。</param>
    /// <param name="fallback">fallback として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V Next(V values, V? fallback = null) => Items(values).FirstOrDefault() ?? fallback ?? throw new InvalidOperationException("No matching value");

    /// <summary>Unicodeスカラー・整数の比較規則を使い、同一キーの元の順序を保持します。</summary>
    /// <param name="values">values として渡す操作対象。</param>
    /// <param name="key">key として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V Sorted(V values, Func<V, V>? key = null) => ListOf(Items(values).OrderBy(v => key is null ? v : key(v), Comparer<V>.Create(Compare)));

    /// <summary>解析値の Minimum 操作を行い、IRの値と順序を維持します。</summary>
    /// <param name="args">args として渡す操作対象。</param>
    /// <param name="key">key として渡す操作対象。</param>
    /// <param name="fallback">fallback として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V Minimum(V[] args, Func<V, V>? key, V? fallback) => Extreme(args, key, fallback, false);

    /// <summary>解析値の Maximum 操作を行い、IRの値と順序を維持します。</summary>
    /// <param name="args">args として渡す操作対象。</param>
    /// <param name="key">key として渡す操作対象。</param>
    /// <param name="fallback">fallback として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V Maximum(V[] args, Func<V, V>? key, V? fallback) => Extreme(args, key, fallback, true);

    /// <summary>解析値の Ord 操作を行い、IRの値と順序を維持します。</summary>
    /// <param name="value">value として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V Ord(V value) => I(Text(value).EnumerateRunes().Single().Value);

    /// <summary>解析値の Token 操作を行い、IRの値と順序を維持します。</summary>
    /// <param name="text">text として渡す操作対象。</param>
    /// <param name="source">source として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V Token(V text, V source)
    {
        var value = new V(new TokenMarker());
        value.Fields["text"] = text;
        value.Fields["source"] = source;
        return value;
    }

    /// <summary>解析値の Member 操作を行い、IRの値と順序を維持します。</summary>
    /// <param name="value">value として渡す操作対象。</param>
    /// <param name="name">name として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V Member(V value, string name)
    {
        if (value.Data is TokenMarker && name == "upper")
        {
            return S(SourceCasing.Upper(Text(value.Fields["text"])));
        }

        if (value.Data is FilePath path)
        {
            return name switch
            {
                "name" => S(System.IO.Path.GetFileName(path.Path)),
                "parent" => PathValue(System.IO.Path.GetDirectoryName(path.Path)!),
                _ => throw new InvalidOperationException(name),
            };
        }

        return value.Fields[name];
    }

    /// <summary>解析値の SetMember 操作を行い、IRの値と順序を維持します。</summary>
    /// <param name="value">value として渡す操作対象。</param>
    /// <param name="name">name として渡す操作対象。</param>
    /// <param name="item">item として渡す操作対象。</param>
    internal static void SetMember(V value, string name, V item) => value.Fields[name] = item;

    /// <summary>解析値の GetAttr 操作を行い、IRの値と順序を維持します。</summary>
    /// <param name="value">value として渡す操作対象。</param>
    /// <param name="name">name として渡す操作対象。</param>
    /// <param name="fallback">fallback として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V GetAttr(V value, V name, V fallback) => value.Fields.GetValueOrDefault(Text(name), fallback);

    /// <summary>解析値の Named 操作を行い、IRの値と順序を維持します。</summary>
    /// <param name="name">name として渡す操作対象。</param>
    /// <param name="value">value として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V Named(string name, V value) => new(new NamedValue(name, value));

    /// <summary>解析値の PathValue 操作を行い、IRの値と順序を維持します。</summary>
    /// <param name="path">path として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V PathValue(string path) => new(new FilePath(path));

    /// <summary>解析値の FromHex 操作を行い、IRの値と順序を維持します。</summary>
    /// <param name="value">value として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V FromHex(V value) => new(Convert.FromHexString(Text(value)));

    /// <summary>解析値の Digest 操作を行い、IRの値と順序を維持します。</summary>
    /// <param name="value">value として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V Digest(V value) => S(Convert.ToHexStringLower(SHA256.HashData((byte[])value.Data!)));

    /// <summary>解析値の Lit 操作を行い、IRの値と順序を維持します。</summary>
    /// <param name="value">value として渡す操作対象。</param>
    /// <param name="typ">typ として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V Lit(V value, V? typ = null) => Put(Put(Put(Dict(), S("kind"), S("literal")), S("value"), value), S("valueType"), typ ?? S("numeric"));

    /// <summary>解析値の Binary 操作を行い、IRの値と順序を維持します。</summary>
    /// <param name="op">op として渡す操作対象。</param>
    /// <param name="a">a として渡す操作対象。</param>
    /// <param name="b">b として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V Binary(V op, V a, V b) => Put(Put(Put(Put(Dict(), S("kind"), S("binary")), S("op"), op), S("left"), a), S("right"), b);

    /// <summary>解析値の Same 操作を行い、IRの値と順序を維持します。</summary>
    /// <param name="a">a として渡す操作対象。</param>
    /// <param name="b">b として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V Same(V a, V b)
    {
        var aa = Text(Member(a, "text"));
        var bb = Text(Member(b, "text"));
        return B(aa.StartsWith('"') || aa.StartsWith('\'') || bb.StartsWith('"') || bb.StartsWith('\'') ? aa == bb : SourceCasing.Upper(aa) == SourceCasing.Upper(bb));
    }

    /// <summary>解析値の ParsePicture 操作を行い、IRの値と順序を維持します。</summary>
    /// <param name="raw">raw として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V ParsePicture(V raw)
    {
        var pic = PictureParser.Parse(Text(raw));
        var value = new V();
        value.Fields["size"] = I(pic.Size);
        value.Fields["decimals"] = I(pic.Decimals);
        value.Fields["signed"] = B(pic.Signed);
        return value;
    }

    /// <summary>解析値の FromJson 操作を行い、IRの値と順序を維持します。</summary>
    /// <param name="node">node として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V FromJson(JsonNode? node) => node switch
    {
        null => Nil,
        JsonObject obj => obj.Aggregate(Dict(), (map, pair) => Put(map, S(pair.Key), FromJson(pair.Value))),
        JsonArray array => ListOf(array.Select(FromJson)),
        JsonValue value when value.TryGetValue<string>(out var s) => S(s),
        JsonValue value when value.TryGetValue<bool>(out var b) => B(b),
        _ => BigInteger.TryParse(node.ToJsonString(), out var n) ? new(n) : new(node.DeepClone()),
    };

    private static int Bound(int index, int count) => Math.Clamp(index < 0 ? count + index : index, 0, count);

    private static V Extreme(V[] args, Func<V, V>? key, V? fallback, bool maximum)
    {
        var sequence = args.Length == 1 ? Items(args[0]) : args;
        var sorted = Items(Sorted(Sequence(sequence), key));
        return (maximum ? sorted.LastOrDefault() : sorted.FirstOrDefault()) ?? fallback ?? throw new ArgumentException("Empty extrema");
    }

    private sealed record SliceRange(V Start, V End, V Step);

    private sealed record FilePath(string Path);

    private sealed record NamedValue(string Name, V Value);

    private sealed record TokenMarker;

    private sealed class ValueEquality : IEqualityComparer<V>
    {
        // Nested initialization keeps static keyword sets independent of outer field order.
        internal static readonly ValueEquality Instance = new();

        public bool Equals(V? x, V? y) => x is not null && y is not null && Equal(x, y);

        public int GetHashCode(V obj)
        {
            if (obj.Data is List<V> list)
            {
                HashCode hash = default;
                foreach (var item in list)
                {
                    hash.Add(GetHashCode(item));
                }

                return hash.ToHashCode();
            }

            return obj.Data?.GetHashCode() ?? 0;
        }
    }
}
