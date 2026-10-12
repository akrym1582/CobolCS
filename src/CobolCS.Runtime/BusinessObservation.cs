using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace CobolCS.Runtime;

/// <summary>内部 snapshot を期待値へ自動追加せず、業務結果・順序付き効果・終了だけを固定します。</summary>
/// <param name="Result">選択した業務名の結果。</param>
/// <param name="Effects">安定した操作名と業務 payload の列。</param>
/// <param name="Termination">契約で選んだ業務終了状態。</param>
/// <param name="HasNoOpCalls">探索 NoOp を正常な業務ケースへ昇格させません。</param>
/// <param name="Completed">不完全な実行を拒否します。</param>
public sealed record BusinessObservation(JsonObject Result, JsonArray Effects, string Termination, bool HasNoOpCalls = false, bool Completed = true);
