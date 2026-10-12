using System.Text.Json.Nodes;

namespace CobolCS.Runtime;

/// <summary>レビューされた観測境界の結果と、別の full-state スナップショットを保持します。</summary>
/// <param name="Observation">業務キー、終了種別と外部依頼列の観測。</param>
/// <param name="Snapshot">内部整理では比較対象にしない全状態。</param>
public sealed record BusinessResult(JsonObject Observation, JsonObject Snapshot);
