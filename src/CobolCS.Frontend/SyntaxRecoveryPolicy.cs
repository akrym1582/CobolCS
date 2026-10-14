namespace CobolCS.Frontend;

/// <summary>未知構文からの解析復旧の範囲を指定します。実行時の未知処理を正常処理へ置き換えません。</summary>
public enum SyntaxRecoveryPolicy
{
    /// <summary>固定IRとの互換性を保つ従来の復旧規則です。</summary>
    Standard,

    /// <summary>状態を復元して構造境界から再開し、未知部分を補完対象として保持します。</summary>
    Tolerant,
}
