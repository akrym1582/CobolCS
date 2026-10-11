namespace CobolCS.Frontend;

/// <summary>上流 PIC 解析器と互換な解析結果です。実行可能性の判定とは区別します。</summary>
/// <param name="Raw">外側の空白を除いた元の PIC。</param>
/// <param name="Expanded">繰り返し記法を展開した PIC。</param>
/// <param name="Category">上流と同じカテゴリ名。</param>
/// <param name="Size">表示位置の数。</param>
/// <param name="Decimals">V より後にある 9 の数。</param>
/// <param name="Signed">S を含むかどうか。</param>
public sealed record PictureClause(string Raw, string Expanded, string Category, int Size, int Decimals, bool Signed);
