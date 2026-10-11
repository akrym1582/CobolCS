namespace CobolCS.Frontend.Tests;

/// <summary>入力と JSON Pointer によって特定した互換性の差異です。</summary>
/// <param name="InputId">比較対象の固定入力 ID。</param>
/// <param name="Path">RFC 6901 の JSON Pointer。</param>
/// <param name="Expected">参照値。欠落は JSON と区別できる表記です。</param>
/// <param name="Actual">実際の値。</param>
public sealed record JsonDifference(string InputId, string Path, string Expected, string Actual);
