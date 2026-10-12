using System.Text.Json.Nodes;

namespace CobolCS.Runtime;

/// <summary>ケース専用の業務キーと値列を消費します。生成型や COBOL の文 ID に依存しません。</summary>
public sealed class BusinessAcceptScript
{
    private readonly Dictionary<string, JsonArray> values;
    private readonly Dictionary<string, int> offsets;
    private readonly string[] sequence;
    private readonly string sequenceField;
    private int position;

    /// <summary>Initializes a new instance of the <see cref="BusinessAcceptScript"/> class. 入力を複製し、回数とキーを実行前に照合します。</summary>
    /// <param name="input">ケースの正式入力。各値は共通型検証を通した scalar 配列です。</param>
    /// <param name="valueFields">明示契約で選んだ業務キー。</param>
    /// <param name="sequenceField">順序を保持する入力フィールド。</param>
    public BusinessAcceptScript(JsonObject input, IEnumerable<string> valueFields, string sequenceField = "acceptSequence")
    {
        this.sequenceField = sequenceField;
        var keys = valueFields.ToArray();
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Length || keys.Any(k => k == sequenceField))
        {
            throw BusinessComparison.Failure("Invalid ACCEPT keys", "/input", "accept-input-unmapped");
        }

        if (input[sequenceField] is not JsonArray order || order.Any(n => n is not JsonValue v || !v.TryGetValue<string>(out _)))
        {
            throw BusinessComparison.Failure("Missing ACCEPT sequence", "/input/" + Pointer(sequenceField), "accept-input-missing");
        }

        sequence = order.Select(n => n!.GetValue<string>()).ToArray();
        values = new(StringComparer.Ordinal);
        offsets = new(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            if (input[key] is not JsonArray array || array.Any(n => n == null))
            {
                throw BusinessComparison.Failure("Missing ACCEPT values for " + key, "/input/" + Pointer(key), "accept-input-missing");
            }

            var count = sequence.Count(k => k == key);
            if (array.Count != count)
            {
                throw BusinessComparison.Failure("ACCEPT sequence/value count mismatch for " + key, "/input/" + Pointer(key), array.Count < count ? "accept-input-missing" : "accept-input-unused");
            }

            values.Add(key, (JsonArray)array.DeepClone());
            offsets.Add(key, 0);
        }

        var unknown = Array.FindIndex(sequence, k => !values.ContainsKey(k));
        if (unknown >= 0)
        {
            throw BusinessComparison.Failure("Unknown ACCEPT sequence key", "/input/" + Pointer(sequenceField) + "/" + unknown, "accept-input-unmapped");
        }
    }

    /// <summary>未配線の生成文にも元位置を保持した明示失敗を渡します。</summary>
    /// <param name="location">文の元位置または未選択キー。</param>
    /// <returns>fallback を許さない業務例外。</returns>
    public static InvalidOperationException Unmapped(string location) => BusinessComparison.Failure("Unmapped ACCEPT input: " + location, "/input/acceptSequence", "accept-input-unmapped");

    /// <summary>次の業務キーと一致する値だけを返します。型や値が同じでも順序違いを拒否します。</summary>
    /// <param name="key">実際に要求された安定業務キー。</param>
    /// <returns>当該キーの次の値の複製。</returns>
    public JsonNode Read(string key)
    {
        var pointer = "/input/" + Pointer(sequenceField) + "/" + position;
        if (!values.ContainsKey(key))
        {
            throw Unmapped("Unselected ACCEPT key: " + key);
        }

        if (position >= sequence.Length)
        {
            throw BusinessComparison.Failure("ACCEPT input exhausted", pointer, "accept-input-missing");
        }

        if (sequence[position] != key)
        {
            throw BusinessComparison.Failure("ACCEPT input order mismatch: requested " + key, pointer, "accept-input-order-mismatch");
        }

        position++;
        return values[key][offsets[key]++]!.DeepClone();
    }

    /// <summary>正常終了後だけ呼び、未消費の順序と値を拒否します。</summary>
    public void VerifyComplete()
    {
        if (position != sequence.Length || values.Any(p => offsets[p.Key] != p.Value.Count))
        {
            throw BusinessComparison.Failure("Unused ACCEPT input", "/input/" + Pointer(sequenceField) + "/" + position, "accept-input-unused");
        }
    }

    /// <summary>Strict と Inspect が共用する任意 metadata の型検査です。</summary>
    /// <param name="contract">業務型契約。</param>
    internal static void ValidateContract(JsonNode contract)
    {
        if (contract["acceptInputs"] == null)
        {
            return;
        }

        var metadata = contract["acceptInputs"]!;
        BusinessComparison.Keys(metadata, ["sequenceField", "valueFields"], ["sequenceField", "valueFields"]);
        var field = metadata["sequenceField"]!.GetValue<string>();
        var fields = contract["input"]!["fields"]!.AsObject();
        var keys = metadata["valueFields"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
        bool ArrayOf(string key, params string[] kinds) => fields[key] is JsonNode type && type["type"]?.GetValue<string>() == "array" && type["nullable"]?.GetValue<bool>() != true && type["items"]?["nullable"]?.GetValue<bool>() != true && kinds.Contains(type["items"]?["type"]?.GetValue<string>(), StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(field) || field == "testState" || !ArrayOf(field, "string") || keys.Distinct(StringComparer.Ordinal).Count() != keys.Length || keys.Any(k => string.IsNullOrWhiteSpace(k) || k == field || k == "testState" || !ArrayOf(k, "number", "string")))
        {
            throw new ArgumentException("Invalid acceptInputs metadata: unknown, duplicate or incompatible input fields");
        }
    }

    /// <summary>実行しない静的検査でも同じ回数・順序キー検査を使用します。</summary>
    /// <param name="input">型検査前にも回数不足を明示できる入力。</param>
    /// <param name="contract">検証済み契約。</param>
    internal static void ValidateInput(JsonNode input, JsonNode contract)
    {
        if (contract["acceptInputs"] is JsonNode metadata)
        {
            _ = new BusinessAcceptScript(input.AsObject(), metadata["valueFields"]!.AsArray().Select(n => n!.GetValue<string>()), metadata["sequenceField"]!.GetValue<string>());
        }
    }

    /// <summary>キーの区切り文字を JSON Pointer としてエスケープします。</summary>
    private static string Pointer(string key) => key.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}
