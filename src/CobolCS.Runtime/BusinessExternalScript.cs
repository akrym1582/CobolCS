using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace CobolCS.Runtime;

/// <summary>生成文メソッドを JSON に露出せず、型付き fake と安定した業務操作を橋渡しします。</summary>
public sealed class BusinessExternalScript
{
    private readonly string caseId;
    private readonly JsonObject operations;
    private readonly Queue<JsonNode> steps;
    private readonly JsonArray effects = new();
    private readonly JsonNode[] bindings;
    private int bound;

    /// <summary>Initializes a new instance of the <see cref="BusinessExternalScript"/> class.</summary>
    /// <param name="caseId">差分で識別するケース。</param>
    /// <param name="operations">操作別の業務型契約。</param>
    /// <param name="responses">呼出し順に並べた固定要求・応答。</param>
    public BusinessExternalScript(string caseId, JsonObject operations, JsonArray responses)
    {
        this.caseId = caseId;
        this.operations = (JsonObject)operations.DeepClone();
        steps = new Queue<JsonNode>(responses.Select(s => s!.DeepClone()));
        bindings = responses.Select(s => s!.DeepClone()).ToArray();
    }

    /// <summary>Gets 呼出し時点の要求を複製した順序付き業務効果。</summary>
    public JsonArray Effects => (JsonArray)effects.DeepClone();

    /// <summary>次の固定応答を既存 ScriptedDependencies の Expect メソッドへ接続します。</summary>
    /// <typeparam name="TRequest">型付き要求。</typeparam>
    /// <typeparam name="TResponse">型付き応答。</typeparam>
    /// <param name="operation">固定 JSON の業務操作名。</param>
    /// <param name="register">既存 fake の Expect メソッド。</param>
    /// <param name="mapRequest">生成 request から業務要求への投影。</param>
    /// <param name="mapResponse">固定業務応答から生成 response への純粋な変換。</param>
    public void BindNext<TRequest, TResponse>(string operation, Action<Func<TRequest, bool>, TResponse> register, Func<TRequest, JsonObject> mapRequest, Func<JsonObject, TResponse> mapResponse)
    {
        if (bound >= bindings.Length || bindings[bound]["operation"]!.GetValue<string>() != operation)
        {
            throw new InvalidOperationException(caseId + " /responses: Missing or out-of-order binding " + operation);
        }

        var response = mapResponse((JsonObject)bindings[bound++]["response"]!.DeepClone());
        Func<TRequest, bool> matcher = request =>
        {
            _ = Invoke(operation, request, mapRequest, mapResponse);
            return true;
        };
        register(matcher, response);
    }

    /// <summary>要求値と操作順を比較し、固定応答を既存の型付き応答へ変換します。</summary>
    /// <typeparam name="TRequest">既存の型付き request。</typeparam>
    /// <typeparam name="TResponse">既存の型付き response。</typeparam>
    /// <param name="operation">customer.lookup 等の安定した操作名。</param>
    /// <param name="request">今回の実際の要求。</param>
    /// <param name="mapRequest">業務 payload への変換。</param>
    /// <param name="mapResponse">固定業務応答から型付き応答への変換。</param>
    /// <returns>ケース専用の応答。不足・順序・要求不一致は例外です。</returns>
    public TResponse Invoke<TRequest, TResponse>(string operation, TRequest request, Func<TRequest, JsonObject> mapRequest, Func<JsonObject, TResponse> mapResponse)
    {
        if (steps.Count == 0 || steps.Peek()["operation"]!.GetValue<string>() != operation || operations[operation] == null)
        {
            throw new InvalidOperationException(caseId + " /effects: Missing or out-of-order business response " + operation);
        }

        var step = steps.Peek();
        var payload = mapRequest(request);
        BusinessComparison.Equal(caseId, operations[operation]!["request"]!, step["request"], payload, "/effects/" + effects.Count + "/request");
        var response = mapResponse((JsonObject)step["response"]!.DeepClone());
        steps.Dequeue();
        effects.Add(new JsonObject { ["operation"] = operation, ["request"] = payload.DeepClone() });
        return response;
    }

    /// <summary>余剰応答を見逃さず、必須外部呼出しの欠落を失敗にします。</summary>
    public void VerifyComplete()
    {
        if (steps.Count != 0)
        {
            throw new InvalidOperationException(caseId + " /responses: Unused business responses");
        }
    }
}
