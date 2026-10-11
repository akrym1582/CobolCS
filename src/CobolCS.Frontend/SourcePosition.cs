using System.Text.Json.Nodes;

namespace CobolCS.Frontend;

/// <summary>UTF-16 コード単位と混同しない、原文の Unicode 文字位置です。</summary>
/// <param name="Line">1 始まりの行。</param>
/// <param name="Column">1 始まりの Unicode スカラー列。</param>
/// <param name="ByteOffset">0 始まりの UTF-8 バイト位置。</param>
public sealed record SourcePosition(int Line, int Column, int ByteOffset)
{
    /// <summary>IR の位置オブジェクトへ変換します。</summary>
    /// <returns>項目名を維持した新しい JSON オブジェクト。</returns>
    public JsonObject ToJson() => new() { ["line"] = Line, ["column"] = Column, ["byteOffset"] = ByteOffset };
}
