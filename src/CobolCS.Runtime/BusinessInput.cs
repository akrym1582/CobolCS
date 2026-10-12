using System.Text.Json.Nodes;

namespace CobolCS.Runtime;

/// <summary>業務入力、順序付き引数、内部状態注入と固定外部応答を分離した入力です。</summary>
/// <param name="Values">契約の業務入力キー。数値は精密な文字列です。</param>
/// <param name="Arguments">契約順序に対応する引数。</param>
/// <param name="InitialState">明示した内部状態注入。</param>
/// <param name="ExternalResponses">設定済み依頼と応答の順序列。</param>
public sealed record BusinessInput(JsonObject Values, JsonArray Arguments, JsonObject InitialState, JsonArray ExternalResponses)
{
    /// <summary>検証済み API の入力 JSON を作ります。</summary>
    /// <returns>境界を区別した入力。</returns>
    public JsonNode ToJson() => new JsonObject { ["values"] = Values.DeepClone(), ["arguments"] = Arguments.DeepClone(), ["fields"] = InitialState.DeepClone(), ["externalResponses"] = ExternalResponses.DeepClone() };
}
