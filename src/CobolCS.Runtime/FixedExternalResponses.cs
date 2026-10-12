using System.Text.Json.Nodes;

namespace CobolCS.Runtime;

/// <summary>明示された外部依頼と応答を順番に照合します。実サービスや成功 fallback は使用しません。</summary>
public sealed class FixedExternalResponses
{
    private readonly JsonArray responses;
    private int position;

    /// <summary>Initializes a new instance of the <see cref="FixedExternalResponses"/> class. 固定列を検査して保持します。</summary>
    /// <param name="responses">request、response の順序列。未設定は空列です。</param>
    public FixedExternalResponses(JsonArray? responses)
    {
        this.responses = responses?.DeepClone().AsArray() ?? [];
        if (this.responses.Count > 10000)
        {
            throw new InvalidDataException("External response observation limit exceeded");
        }

        foreach (var item in this.responses)
        {
            RequireKeys(item!, "request response");
            var request = item!["request"] ?? throw new InvalidDataException("Missing external request");
            RequireKeys(request, "statementId kind target operation arguments query");
            foreach (var key in new[] { "statementId", "kind", "target", "operation", "query" })
            {
                _ = request[key]?.GetValue<string>() ?? throw new InvalidDataException("Missing request." + key);
            }

            if (request.S("kind") is not ("call" or "file" or "sql") || string.IsNullOrWhiteSpace(request.S("target")) || string.IsNullOrWhiteSpace(request.S("operation")) || string.IsNullOrWhiteSpace(request.S("statementId")))
            {
                throw new InvalidDataException("Invalid external request identity");
            }

            if (request["arguments"] is not JsonArray arguments)
            {
                throw new InvalidDataException("Missing request.arguments");
            }

            foreach (var argument in arguments)
            {
                RequireKeys(argument!, "mode value");
                _ = argument!["mode"]?.GetValue<string>() ?? throw new InvalidDataException("Missing passing mode");
                ValidateLogical(argument["value"]);
            }

            var response = item["response"] ?? throw new InvalidDataException("Missing external response");
            RequireKeys(response, "outcome status arguments returning record outputs sqlstate bytes");
            if (response.S("outcome") is not ("success" or "eof" or "no-data" or "error" or "invalid"))
            {
                throw new InvalidDataException("Unknown external outcome");
            }

            _ = response["status"]?.GetValue<string>() ?? throw new InvalidDataException("Missing response.status");
            if ((request.S("kind") == "call" && response["arguments"] is not JsonArray) || (request.S("kind") == "sql" && response["outputs"] is not JsonArray))
            {
                throw new InvalidDataException("Missing kind-specific fixed response arguments/outputs");
            }

            foreach (var key in new[] { "arguments", "outputs" })
            {
                if (response[key] is JsonArray array)
                {
                    foreach (var value in array)
                    {
                        ValidateLogical(value);
                    }
                }
                else if (response.AsObject().ContainsKey(key))
                {
                    throw new InvalidDataException("Invalid response." + key);
                }
            }

            foreach (var key in new[] { "record", "returning" })
            {
                if (response.AsObject().ContainsKey(key))
                {
                    ValidateLogical(response[key]);
                }
            }

            if (response["sqlstate"] != null && response.S("sqlstate").Length != 5)
            {
                throw new InvalidDataException("Invalid SQLSTATE");
            }

            if (response["bytes"] is JsonArray bytes)
            {
                foreach (var record in bytes)
                {
                    BusinessObservation.ValidateBytes(record!);
                }
            }
            else if (response.AsObject().ContainsKey("bytes"))
            {
                throw new InvalidDataException("Invalid response.bytes");
            }
        }
    }

    /// <summary>Gets 実際に照合した依頼の順序列です。同じ文の反復も別の要素に残します。</summary>
    public JsonArray Requests { get; } = [];

    /// <summary>Gets アダプターが渡した物理バイト列です。PIC の新しいデコーダは使用しません。</summary>
    public JsonArray Bytes { get; } = [];

    /// <summary>許可されていない契約フィールドやオブジェクトでない値を拒否します。</summary>
    /// <param name="node">検査対象。</param>
    /// <param name="allowed">空白区切りの許可フィールド。</param>
    public static void RequireKeys(JsonNode node, string allowed)
    {
        if (node is not JsonObject obj || obj.Any(p => !allowed.Split(' ').Contains(p.Key)))
        {
            throw new InvalidDataException("Unknown field or invalid object: " + allowed);
        }
    }

    /// <summary>精密数値も文字列として受け渡す論理値を検査します。</summary>
    /// <param name="value">文字列、配列、集団項目のオブジェクト。</param>
    public static void ValidateLogical(JsonNode? value)
    {
        if (value is JsonObject obj)
        {
            foreach (var pair in obj)
            {
                ValidateLogical(pair.Value);
            }
        }
        else if (value is JsonArray array)
        {
            foreach (var item in array)
            {
                ValidateLogical(item);
            }
        }
        else
        {
            _ = value?.GetValue<string>() ?? throw new InvalidDataException("Missing/null logical value");
        }
    }

    /// <summary>依頼を完全照合し、その一件に設定された応答だけを返します。</summary>
    /// <param name="request">一度だけ評価した引数と元文 ID を持つ依頼。</param>
    /// <returns>設定済み応答のコピー。</returns>
    public JsonNode Invoke(JsonNode request)
    {
        if (position >= responses.Count)
        {
            throw new InvalidOperationException("Unconfigured external response at " + request.S("statementId") + ", invocation " + position);
        }

        var item = responses[position]!;
        var comparison = ReferenceCase.Compare(item["request"], request);
        if (!comparison.B("matched"))
        {
            throw new InvalidOperationException("External request mismatch at " + request.S("statementId") + ", invocation " + position + ": " + comparison.ToJsonString());
        }

        position++;
        Requests.Add(request.DeepClone());
        foreach (var bytes in item["response"]!.A("bytes"))
        {
            Bytes.Add(bytes.DeepClone());
        }

        return item["response"]!.DeepClone();
    }

    /// <summary>未使用の応答を拒否します。未到達のスタブを安全とみなす処理ではありません。</summary>
    public void AssertComplete()
    {
        if (position != responses.Count)
        {
            throw new InvalidOperationException("Surplus external responses: " + (responses.Count - position));
        }
    }
}
