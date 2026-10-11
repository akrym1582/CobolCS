using System.Text.Json.Nodes;

namespace CobolCS.Runtime;

/// <summary>
/// COBOL の解析結果に含まれる JSON 項目を読む共通入口です。
/// 省略可能な項目の扱いを揃え、各処理で同じ null 判定を繰り返す必要を減らします。
/// </summary>
public static class J
{
    /// <summary>名前や操作種別など、文字列として保存された項目を読み取ります。</summary>
    /// <param name="n">項目を含む JSON オブジェクト。</param>
    /// <param name="key">読み取る項目名。たとえば PROGRAM-ID を表す id などです。</param>
    /// <returns>保存された文字列。項目がない場合や null の場合は空文字列です。</returns>
    public static string S(this JsonNode n, string key) => n[key]?.GetValue<string>() ?? string.Empty;

    /// <summary>桁数や実行上限など、JSON に整数として保存された設定値を読み取ります。</summary>
    /// <param name="n">設定値を含む JSON オブジェクト。</param>
    /// <param name="key">読み取る項目名。</param>
    /// <param name="fallback">省略時に使う値。たとえば OCCURS の既定回数には 1 を指定します。</param>
    /// <returns>保存された整数、または省略時の既定値。</returns>
    public static int I(this JsonNode n, string key, int fallback = 0) => n[key]?.GetValue<int>() ?? fallback;

    /// <summary>符号付き PIC や丸め指定のような、有効・無効を表す項目を読み取ります。</summary>
    /// <param name="n">指定を含む JSON オブジェクト。</param>
    /// <param name="key">読み取る真偽値の項目名。</param>
    /// <returns>保存された真偽値。指定を省略している場合は無効として false を返します。</returns>
    public static bool B(this JsonNode n, string key) => n[key]?.GetValue<bool>() ?? false;

    /// <summary>命令や宣言の配列を順番に調べるための列挙を用意します。</summary>
    /// <param name="n">配列の項目を含む JSON オブジェクト。</param>
    /// <param name="key">読み取る配列名。要素が null でない解析済み IR を対象とします。</param>
    /// <returns>配列内の要素。配列を省略している場合は空の列挙です。</returns>
    public static IEnumerable<JsonNode> A(this JsonNode n, string key) => (n[key] as JsonArray ?? []).Select(x => x!);

    /// <summary>解析器や実行器との受け渡しに使う JSON を読み取り、null だけの入力を拒否します。</summary>
    /// <param name="json">一つの JSON 値を表す文字列。</param>
    /// <returns>以降の項目参照に使える JSON ツリー。</returns>
    /// <exception cref="InvalidDataException">入力が JSON の null であり、解析結果として利用できない場合。</exception>
    public static JsonNode Parse(string json) => JsonNode.Parse(json) ?? throw new InvalidDataException("Empty JSON");
}
