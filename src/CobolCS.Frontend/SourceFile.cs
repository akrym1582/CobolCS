using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace CobolCS.Frontend;

/// <summary>UTF-8 の原文と、UTF-16 添字から独立した位置を保持します。</summary>
public sealed class SourceFile
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly byte[] bytes;
    private readonly SourcePosition?[] positions;

    /// <summary>Initializes a new instance of the <see cref="SourceFile"/> class.</summary>
    /// <param name="path">IR に保存する相対パス。パス解決は呼び出し元の責務です。</param>
    /// <param name="bytes">元の UTF-8 バイト列。内部でコピーします。</param>
    public SourceFile(string path, ReadOnlySpan<byte> bytes)
    {
        Path = path;
        this.bytes = bytes.ToArray();
        Text = StrictUtf8.GetString(bytes);
        Hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        positions = new SourcePosition?[Text.Length + 1];
        var line = 1;
        var column = 1;
        var offset = 0;
        var index = 0;
        foreach (var rune in Text.EnumerateRunes())
        {
            positions[index] = new(line, column, offset);
            index += rune.Utf16SequenceLength;
            offset += rune.Utf8SequenceLength;
            column++;
            if (rune.Value == '\n' || (rune.Value == '\r' && (index == Text.Length || Text[index] != '\n')) || rune.Value is '\v' or '\f' or '\u001c' or '\u001d' or '\u001e' or '\u0085' or '\u2028' or '\u2029')
            {
                line++;
                column = 1;
            }
        }

        positions[Text.Length] = new(line, column, offset);
    }

    /// <summary>Gets IR に保存するファイル名。</summary>
    public string Path { get; }

    /// <summary>Gets 改行・BOM・文字を置換しない原文。</summary>
    public string Text { get; }

    /// <summary>Gets 原バイト列の SHA-256。</summary>
    public string Hash { get; }

    /// <summary>UTF-16 の境界添字を Unicode 文字単位の列と UTF-8 バイト位置へ変換します。</summary>
    /// <param name="utf16Index">文字列の境界。サロゲートペアの中間は指定できません。</param>
    /// <returns>1 始まりの行・列と 0 始まりのバイト位置。</returns>
    public SourcePosition Position(int utf16Index)
    {
        if (utf16Index < 0 || utf16Index >= positions.Length || positions[utf16Index] is not { } position)
        {
            throw new ArgumentOutOfRangeException(nameof(utf16Index), "Position must be a Unicode scalar boundary");
        }

        return position;
    }

    /// <summary>元のバイト列を維持した IR の source エントリを返します。</summary>
    /// <returns>新しい JSON オブジェクト。</returns>
    public JsonObject ToJson() => new()
    {
        ["path"] = Path,
        ["sha256"] = Hash,
        ["encoding"] = "utf-8",
        ["text"] = Text,
        ["bytesHex"] = Convert.ToHexStringLower(bytes),
    };

    /// <summary>トークンの範囲を IR と同じ項目で作成します。</summary>
    /// <param name="start">開始 UTF-16 添字。</param>
    /// <param name="end">終了直後の UTF-16 添字。</param>
    /// <param name="copyStack">COPY の展開元。順番を保持します。</param>
    /// <returns>原文と元位置を保持する範囲。</returns>
    public JsonObject Span(int start, int end, IEnumerable<string>? copyStack = null)
    {
        var first = Position(start);
        var last = Position(end);
        ArgumentOutOfRangeException.ThrowIfLessThan(end, start);
        return new()
        {
            ["file"] = Path,
            ["start"] = first.ToJson(),
            ["end"] = last.ToJson(),
            ["copyStack"] = new JsonArray((copyStack ?? []).Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()),
            ["raw"] = Text[start..end],
        };
    }
}
