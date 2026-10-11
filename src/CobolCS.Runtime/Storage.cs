using System.Numerics;
using System.Text.Json.Nodes;

namespace CobolCS.Runtime;

/// <summary>
/// 一つの COBOL プログラムの項目値を、宣言された PIC と表の形に合わせて保持します。
/// 実機のメモリ配置ではなく、数値の桁合わせと文字列の固定長という値の規則を担当します。
/// </summary>
public sealed class Storage
{
    private readonly Dictionary<string, object[]> cells = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="Storage"/> class.
    /// 宣言から必要な値の領域を確保します。VALUE 句の反映は、式を評価できる状態になってから行います。
    /// </summary>
    /// <param name="program">一つの PROGRAM-ID に対応する宣言を含んだ解析済み IR。</param>
    /// <exception cref="InvalidOperationException">一つの項目の表要素数が確保上限を超える場合。</exception>
    public Storage(JsonNode program)
    {
        Symbols = program.A("symbols").ToDictionary(s => s.S("id"));
        foreach (var s in Symbols.Values.Where(s => s["pic"] != null && s.I("level") != 88))
        {
            var count = Dimensions(s).Aggregate(1, (a, b) => checked(a * b));
            if (count > 1000000)
            {
                throw new InvalidOperationException("Table allocation limit");
            }

            cells[s.S("id")] = Enumerable.Repeat<object>(s["pic"]!.S("category") == "numeric" ? new Number(0) : string.Empty, count).ToArray();
        }
    }

    /// <summary>
    /// Gets the declarations used to interpret stored values.
    /// 同名項目を取り違えないよう、解析器が割り当てた ID から PIC や親項目の宣言を参照できます。
    /// </summary>
    /// <value>シンボル ID をキーにした宣言情報。項目の値を保存する領域とは別です。</value>
    public Dictionary<string, JsonNode> Symbols { get; }

    /// <summary>
    /// 文字項目の受け取り側が固定長になるよう、空白埋めと切り詰めを行います。
    /// 右寄せでは、長い送信文字列の右端を残す点が通常の MOVE と異なります。
    /// </summary>
    /// <param name="value">送信側の文字列。</param>
    /// <param name="length">受け取り側の文字数。</param>
    /// <param name="right">JUSTIFIED RIGHT の場合は true。それ以外は左端を基準にします。</param>
    /// <returns>必ず指定された文字数になる受け取り側の文字列。</returns>
    public static string Fit(string value, int length, bool right) => SourceSupport.Fit(value, length, right);

    /// <summary>
    /// 項目の特定に必要な添字の数と各次元の大きさを、親の OCCURS も含めて求めます。
    /// 3 行の親の中に 2 列の項目がある場合は、外側から [3, 2] を返します。
    /// </summary>
    /// <param name="symbol">自分自身と親項目の OCCURS を調べる宣言。</param>
    /// <returns>外側から内側の順に並ぶ表の要素数。表でない項目では空の配列です。</returns>
    public int[] Dimensions(JsonNode symbol)
    {
        var dimensions = new List<int>();
        JsonNode? s = symbol;
        while (s != null)
        {
            if (s.I("occurs", 1) > 1 || s["occursClause"] != null)
            {
                dimensions.Insert(0, s.I("occurs", 1));
            }

            s = s["parentId"] == null ? null : Symbols[s.S("parentId")];
        }

        return dimensions.ToArray();
    }

    /// <summary>
    /// 演算や条件判定に渡すため、指定された項目の値を取り出します。
    /// 文字列の参照変更がある場合は、T(2:3) のように指定された部分だけを返します。
    /// </summary>
    /// <param name="reference">シンボル ID、添字、省略可能な参照変更を含む参照。</param>
    /// <param name="eval">添字や部分文字列の開始位置・長さを評価する処理。</param>
    /// <returns>数値は <see cref="Number"/>、文字項目は固定長の文字列。</returns>
    /// <exception cref="IndexOutOfRangeException">添字や参照変更が項目の範囲を超える場合。</exception>
    public object Get(JsonNode reference, Func<JsonNode, object> eval)
    {
        var value = cells[reference.S("symbolId")][Index(reference, eval)];
        if (reference["modification"] is JsonNode m)
        {
            var text = Machine.Text(value);
            var start = Machine.Integer(eval(m["start"]!));
            var length = Machine.Integer(eval(m["length"]!));
            if (start < 1 || length < 1 || start - 1 + length > text.Length)
            {
                throw new IndexOutOfRangeException("Reference modification");
            }

            return text.Substring(start - 1, length);
        }

        return value;
    }

    /// <summary>
    /// MOVE などの受け取り項目へ値を格納し、PIC に合わせた桁数と文字列の長さに揃えます。
    /// 空白埋めや切り捨てを、個々の命令が別々に実装する必要をなくします。
    /// </summary>
    /// <param name="reference">格納先のシンボル ID、添字、省略可能な参照変更。</param>
    /// <param name="value">格納する数値または文字列。</param>
    /// <param name="eval">格納先の添字などを現在の状態で評価する処理。</param>
    /// <param name="rounded">数値の余分な小数桁を四捨五入する場合は true。</param>
    /// <param name="sizeError">桁あふれを ON SIZE ERROR へ渡す例外にする場合は true。false では保持できる桁へ切り詰めます。</param>
    public void Set(JsonNode reference, object value, Func<JsonNode, object> eval, bool rounded = false, bool sizeError = false) => Prepare(reference, value, eval, rounded, sizeError)();

    /// <summary>
    /// 格納の可否を確認し、値をまだ変更しない状態で確定用の処理を用意します。
    /// DIVIDE の商と余りなどで、片方を先に書き換えて元の計算材料を失うことを防ぎます。
    /// </summary>
    /// <param name="reference">確定時の格納先。添字と参照変更は準備時の値で決めます。</param>
    /// <param name="value">PIC へ変換・検査する格納予定の値。</param>
    /// <param name="eval">準備時点の添字や参照変更を評価する処理。</param>
    /// <param name="rounded">数値の小数桁を四捨五入する場合は true。</param>
    /// <param name="sizeError">PIC に収まらない値を桁あふれとして通知する場合は true。</param>
    /// <returns>呼び出すと検査済みの値を確定する処理。</returns>
    /// <exception cref="SizeError">桁あふれ通知が有効で、値を PIC へ格納できない場合。</exception>
    public Action Prepare(JsonNode reference, object value, Func<JsonNode, object> eval, bool rounded = false, bool sizeError = false)
    {
        var id = reference.S("symbolId");
        var s = Symbols[id];
        var index = Index(reference, eval);
        if (reference["modification"] is JsonNode m)
        {
            var old = Machine.Text(cells[id][index]);
            var start = Machine.Integer(eval(m["start"]!));
            var length = Machine.Integer(eval(m["length"]!));
            if (start < 1 || length < 1 || start - 1 + length > old.Length)
            {
                throw new IndexOutOfRangeException("Reference modification");
            }

            var modified = old[..(start - 1)] + Fit(Machine.Text(value), length, false) + old[(start - 1 + length)..];
            return () => cells[id][index] = modified;
        }

        var converted = Convert(s, value, rounded, sizeError);
        return () => cells[id][index] = converted;
    }

    /// <summary>
    /// COBOL プログラムの開始時に、VALUE 句または型に応じた既定値を反映します。
    /// 表では各要素を同じ規則で初期化し、88 条件名には独立した保存領域を割り当てません。
    /// </summary>
    /// <param name="eval">VALUE 句の解析済みの式を評価する処理。</param>
    public void Initialize(Func<JsonNode, object> eval)
    {
        foreach (var s in Symbols.Values.Where(s => cells.ContainsKey(s.S("id"))))
        {
            var value = s["initial"] is JsonNode initial ? eval(initial) : s["pic"]!.S("category") == "numeric" ? new Number(0) : string.Empty;
            for (var i = 0; i < cells[s.S("id")].Length; i++)
            {
                cells[s.S("id")][i] = Convert(s, value, false, false);
            }
        }
    }

    /// <summary>
    /// DISPLAY 用に、PIC の総桁数・符号・暗黙小数点を反映した表示文字列を用意します。
    /// 例として S9(3)V99 の 1.68 は、JSON の項目値とは違い「+00168」と表示します。
    /// </summary>
    /// <param name="reference">表示する項目の参照。参照変更では指定された部分だけを表示します。</param>
    /// <param name="eval">添字や参照変更を評価する処理。</param>
    /// <returns>元 COBOL の DISPLAY と比較するための文字列。</returns>
    public string Display(JsonNode reference, Func<JsonNode, object> eval)
    {
        var s = Symbols[reference.S("symbolId")];
        var pic = s["pic"]!;
        var value = Get(reference, eval);
        if (value is not Number n || reference["modification"] != null)
        {
            return Machine.Text(value);
        }

        return (pic.B("signed") ? n.Numerator.Sign < 0 ? "-" : "+" : string.Empty) + BigInteger.Abs(n.Units(pic.I("scale"))).ToString().PadLeft(pic.I("digits"), '0');
    }

    /// <summary>
    /// 実行後の項目値を、fixture や baseline と比較できる JSON へまとめます。
    /// 数値は小数点付きの文字列、表は添字順の配列とし、文字項目の末尾空白も残します。
    /// </summary>
    /// <returns>FILLER と独立領域を持たない項目を除いた、修飾名から値への対応表。</returns>
    public JsonObject Snapshot()
    {
        var result = new JsonObject();
        foreach (var s in Symbols.Values.Where(s => cells.ContainsKey(s.S("id")) && s.S("name") != "FILLER"))
        {
            string Format(object v) => v is Number n ? n.Decimal(s["pic"]!.I("scale")) : Machine.Text(v);
            var values = cells[s.S("id")];
            result[s.S("qualifiedName")] = Dimensions(s).Length == 0 ? JsonValue.Create(Format(values[0])) : new JsonArray(values.Select(v => (JsonNode?)JsonValue.Create(Format(v))).ToArray());
        }

        return result;
    }

    /// <summary>
    /// PIC の制約を格納直前に適用し、演算中の精密な値と保存済みの値を区別します。
    /// この境界に丸めを集めることで、計算途中に桁が失われることを防ぎます。
    /// </summary>
    /// <param name="s">受け取り側の PIC、符号、文字種などを含む宣言。</param>
    /// <param name="value">格納前の計算結果または文字列。</param>
    /// <param name="rounded">四捨五入が指定されている場合は true。</param>
    /// <param name="sizeError">桁あふれ時に元の値を保護する通知が必要な場合は true。</param>
    /// <returns>宣言された桁数・文字数へ揃えた値。</returns>
    private static object Convert(JsonNode s, object value, bool rounded, bool sizeError)
    {
        var pic = s["pic"] ?? throw new NotSupportedException("Group assignment");
        try
        {
            return pic.S("category") == "numeric"
                ? SourceSupport.NumberValue(value, pic.I("digits"), pic.I("scale"), pic.B("signed"), rounded, sizeError)
                : SourceSupport.TextValue(value, pic.I("digits"), pic.S("category") == "national", s.B("justified"));
        }
        catch (SizeError)
        {
            throw new SizeError(s.S("qualifiedName"));
        }
    }

    /// <summary>
    /// COBOL の 1 始まりの添字を、内部の平坦な配列の位置へ対応させます。
    /// 範囲外の指定を別項目へのアクセスと誤解しないよう、次元数と各添字を検査します。
    /// </summary>
    /// <param name="reference">シンボル ID と添字の式を含む参照。</param>
    /// <param name="eval">添字の式をプログラムの現在の値で評価する処理。</param>
    /// <returns>内部配列の 0 始まりの位置。</returns>
    /// <exception cref="IndexOutOfRangeException">添字の数や値が宣言された範囲と一致しない場合。</exception>
    private int Index(JsonNode reference, Func<JsonNode, object> eval)
    {
        var dims = Dimensions(Symbols[reference.S("symbolId")]);
        var indices = reference.A("subscripts").Select(e => Machine.Integer(eval(e))).ToArray();
        if (indices.Length != dims.Length)
        {
            throw new IndexOutOfRangeException("Subscript count: " + reference.S("symbolId"));
        }

        var index = 0;
        for (var i = 0; i < dims.Length; i++)
        {
            if (indices[i] < 1 || indices[i] > dims[i])
            {
                throw new IndexOutOfRangeException("COBOL subscript is outside 1.." + dims[i]);
            }

            index = checked((index * dims[i]) + indices[i] - 1);
        }

        return index;
    }
}
