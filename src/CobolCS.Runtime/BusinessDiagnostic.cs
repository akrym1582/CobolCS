namespace CobolCS.Runtime;

/// <summary>修正先と因果関係を保持する有限長の静的診断。</summary>
/// <param name="Path">生成先からの相対ファイル。</param>
/// <param name="CaseId">読めた場合の元 ID。</param>
/// <param name="Category">ケース・契約・採取データの区分。</param>
/// <param name="Code">理由コード。</param>
/// <param name="Reason">不足と修正理由。</param>
/// <param name="Pointer">判明した JSON Pointer。</param>
public sealed record BusinessDiagnostic(string Path, string? CaseId, string Category, string Code, string Reason, string Pointer);
