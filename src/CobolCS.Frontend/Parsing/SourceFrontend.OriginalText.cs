using static CobolCS.Frontend.Parsing.Values;

namespace CobolCS.Frontend.Parsing;

/// <summary>原文字コードのバイト位置を使ってCOPYと文の原文を復元します。</summary>
internal sealed partial class SourceFrontend
{
    /// <summary>UTF-8へ再符号化せず、対象ファイルの元バイト範囲を復元します。</summary>
    /// <param name="source">ファイル、開始・終了バイト位置を持つ原文範囲。</param>
    /// <returns>元の文字コードで厳密にデコードした原文。</returns>
    internal V OriginalText(V source)
    {
        var record = Member(this, "sources")[source[S("file")]];
        var bytes = Convert.FromHexString(Text(record[S("bytesHex")]));
        var start = Number(source[S("start")][S("byteOffset")]);
        var end = Number(source[S("end")][S("byteOffset")]);
        return S(SourceCodec.Decode(bytes.AsSpan(start, end - start), Text(record[S("encoding")])));
    }
}
