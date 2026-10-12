using System.Text.Json.Nodes;

namespace CobolCS.Runtime;

/// <summary>静的検査の有効な登録と診断。実行境界や fake は生成しません。</summary>
/// <param name="Cases">有効で一意なケース。</param>
/// <param name="Contracts">有効な契約。</param>
/// <param name="Diagnostics">不正ファイルと必須欠落。</param>
public sealed record BusinessInspection(
    IReadOnlyList<(string Path, JsonNode Case, JsonNode Contract)> Cases,
    IReadOnlyDictionary<string, JsonNode> Contracts,
    IReadOnlyList<BusinessDiagnostic> Diagnostics);
