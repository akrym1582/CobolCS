namespace CobolCS.Runtime;

/// <summary>IR に依存しない精密数値・文字・比較の共通処理です。</summary>
public static class CobolValue
{
    /// <summary>算術が必要な場面で、数値または十進表記の文字列を精密な数値として受け取ります。</summary>
    /// <param name="value">すでに評価された数値、または数値として解釈する文字列。</param>
    /// <returns>浮動小数点への変換を挟まない数値。</returns>
    /// <exception cref="FormatException">文字列が対応する十進表記ではない場合。</exception>
    public static Number Numeric(object value) => value is Number n ? n : Number.Parse(Text(value));

    /// <summary>
    /// 添字や繰り返し回数を、小数部分のない整数として検査します。
    /// 小数を黙って切り捨てると別の表要素を選んでしまうため、整数でない値は拒否します。
    /// </summary>
    /// <param name="value">整数として利用する評価済みの値。</param>
    /// <returns>C# の配列位置や回数の管理に利用できる整数。</returns>
    /// <exception cref="InvalidOperationException">値が整数ではない場合。</exception>
    /// <exception cref="OverflowException">C# の int 型で表せる範囲を超える場合。</exception>
    public static int Integer(object value)
    {
        var n = Numeric(value);
        if (n.Denominator != 1)
        {
            throw new InvalidOperationException("Expected an integer");
        }

        return checked((int)n.Numerator);
    }

    /// <summary>
    /// 式の値を文字列として扱うため、型に応じた表記を用意します。
    /// 数値のゼロ埋めは行わないため、PIC の DISPLAY 表示が必要な場合には別の処理を使用します。
    /// </summary>
    /// <param name="value">文字列、精密な数値、または条件の真偽値。</param>
    /// <returns>文字列はそのまま、数値は正確な十進表記、真偽値は true または false。</returns>
    /// <exception cref="InvalidOperationException">未対応の型、または十進小数が無限に続く数値の場合。</exception>
    public static string Text(object value) => value switch
    {
        string s => s,
        Number n => n.ExactDecimal(),
        bool b => b ? "true" : "false",
        _ => throw new InvalidOperationException("Invalid value"),
    };

    /// <summary>IF などの条件に使う値を受け取り、数値や文字列を暗黙に真偽値へ変換する誤解を防ぎます。</summary>
    /// <param name="value">条件式の評価結果。</param>
    /// <returns>条件が成立する場合は true、不成立の場合は false。</returns>
    /// <exception cref="InvalidOperationException">比較などを経ていない数値や文字列が渡された場合。</exception>
    public static bool Truth(object value) => value is bool b ? b : throw new InvalidOperationException("Expected a condition");

    /// <summary>
    /// COBOL の比較に合わせ、数値は正確な値で、文字列は不足する末尾を空白で埋めて比較します。
    /// このため「A」と「A 」は文字列の長さが異なっていても等しいものとして扱います。
    /// </summary>
    /// <param name="a">比較する左側の値。</param>
    /// <param name="b">比較する右側の値。</param>
    /// <returns>左側が小さい場合は負数、等しい場合は 0、大きい場合は正数。</returns>
    public static int Compare(object a, object b) => a is Number || b is Number ? Numeric(a).CompareTo(Numeric(b)) : string.CompareOrdinal(Text(a).PadRight(Math.Max(Text(a).Length, Text(b).Length)), Text(b).PadRight(Math.Max(Text(a).Length, Text(b).Length)));

    /// <summary>式のべき乗を評価する前に指数を制限し、極端に大きな整数の作成で実行が停止することを防ぎます。</summary>
    /// <param name="value">べき乗の基数。</param>
    /// <param name="exponent">整数として検査済みの指数。</param>
    /// <returns>許容範囲内で計算した正確なべき乗。</returns>
    /// <exception cref="InvalidOperationException">指数の絶対値が 1,000 を超える場合。</exception>
    public static Number Pow(Number value, int exponent)
    {
        if (Math.Abs((long)exponent) > 1000)
        {
            throw new InvalidOperationException("Exponent limit");
        }

        return value.Pow(exponent);
    }

    /// <summary>
    /// 対応済みの COBOL 組み込み関数を、プログラムの表示言語に依存しない規則で評価します。
    /// 引数の個数や対応範囲は生成前にも検査し、未知の関数を仮の成功値で置き換えません。
    /// </summary>
    /// <param name="name">解析器が正規化した関数名。</param>
    /// <param name="args">呼び出し元の式から評価した引数。</param>
    /// <returns>関数の種類に応じた数値または文字列。</returns>
    public static object Function(string name, object[] args) => name switch
    {
        "ABS" => Numeric(args[0]).Abs(),
        "LENGTH" => new Number(Text(args[0]).Length),
        "UPPER-CASE" => Text(args[0]).ToUpperInvariant(),
        "LOWER-CASE" => Text(args[0]).ToLowerInvariant(),
        "TRIM" => Text(args[0]).Trim(),
        "REVERSE" => new string(Text(args[0]).Reverse().ToArray()),
        "NUMVAL" => Number.Parse(Text(args[0])),
        "MAX" => args.Select(Numeric).Max(),
        "MIN" => args.Select(Numeric).Min(),
        "INTEGER-PART" => new Number(Numeric(args[0]).Units(0), 1),
        "INTEGER" => Floor(Numeric(args[0])),
        "MOD" => Numeric(args[0]) - (Floor(Numeric(args[0]) / Numeric(args[1])) * Numeric(args[1])),
        "REM" => Numeric(args[0]) - (new Number((Numeric(args[0]) / Numeric(args[1])).Units(0), 1) * Numeric(args[1])),
        _ => throw new NotSupportedException("FUNCTION " + name),
    };

    /// <summary>
    /// INTEGER 関数などで使う、元の値を超えない最大の整数を求めます。
    /// ゼロ方向への切り捨てとは異なり、-1.2 では -1 ではなく -2 になります。
    /// </summary>
    /// <param name="n">整数への切り下げが必要な値。</param>
    /// <returns>負数にも数学的な床関数の規則を適用した整数。</returns>
    public static Number Floor(Number n) => new(n.Units(0) - (n.Numerator.Sign < 0 && n.Denominator != 1 ? 1 : 0), 1);
}
