using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace CobolCS.Runtime;

/// <summary>公開業務入力と固定外部応答を分離します。InitialState はアダプターが明示的に選ぶ試験補助です。</summary>
/// <param name="Values">生成メンバー名を使わない業務入力。</param>
/// <param name="External">ケース専用の固定応答列。</param>
public sealed record BusinessCaseInput(JsonObject Values, BusinessExternalScript External);
