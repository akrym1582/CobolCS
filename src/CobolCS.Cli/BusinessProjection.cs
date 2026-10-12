using System.Text.Json;
using System.Text.Json.Nodes;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>検査済み metadata から、反射や任意式を使わず型付き変換を生成します。</summary>
internal static class BusinessProjection
{
    /// <summary>文字列を C# のデータリテラルにします。</summary>
    /// <param name="value">業務キー。</param>
    /// <returns>引用済み文字列。</returns>
    internal static string Quote(string value) => JsonSerializer.Serialize(value);

    /// <summary>既存比較器と同じ型契約を作ります。</summary>
    /// <param name="wire">既知型の metadata。</param>
    /// <returns>型記述。</returns>
    internal static JsonObject Descriptor(JsonNode wire)
    {
        var result = new JsonObject { ["type"] = wire.S("kind") };
        if (wire["pic"] != null)
        {
            result["pic"] = wire["pic"]!.DeepClone();
        }

        if (wire.S("kind") == "array")
        {
            result["items"] = Descriptor(wire["items"]!);
        }

        if (wire.S("kind") == "object")
        {
            result["fields"] = new JsonObject(wire.A("fields").Select(f => KeyValuePair.Create<string, JsonNode?>(f.S("key"), Descriptor(f))));
        }

        BusinessComparison.ValidateType(result);
        return result;
    }

    /// <summary>固定 JSON から既存の Number/string/DTO を構築します。</summary>
    /// <param name="wire">既知型。</param>
    /// <param name="expression">内部で構築した JSON アクセス式。</param>
    /// <param name="depth">配列ラムダの識別番号。</param>
    /// <returns>コンパイル時に型を決めた式。</returns>
    internal static string Read(JsonNode wire, string expression, int depth = 0) => wire.S("kind") switch
    {
        "number" => $"CobolValue.Numeric({expression}!.GetValue<string>())",
        "string" => $"{expression}!.GetValue<string>()",
        "boolean" => $"{expression}!.GetValue<bool>()",
        "array" => $"BusinessValues.Array({expression}, {wire.I("count")}).Select(v{depth} => {Read(wire["items"]!, "v" + depth, depth + 1)}).ToArray()",
        "object" => $"new {wire.S("csharpType")}({string.Join(", ", wire.A("fields").Select(f => f.S("member") + ": " + Read(f, expression + "![" + Quote(f.S("key")) + "]", depth + 1)))})",
        _ => throw new ArgumentException("Unknown business wire type"),
    };

    /// <summary>値を丸めず、宣言済みの固定小数桁と完全な文字列を投影します。</summary>
    /// <param name="wire">既知型。</param>
    /// <param name="expression">内部で構築した型付きアクセス式。</param>
    /// <param name="depth">配列ラムダの識別番号。</param>
    /// <returns>業務 JSON 値を作る式。</returns>
    internal static string Write(JsonNode wire, string expression, int depth = 0) => wire.S("kind") switch
    {
        "number" => $"JsonValue.Create(BusinessValues.Decimal({expression}, {wire.I("scale")}))",
        "string" or "boolean" => $"JsonValue.Create({expression})",
        "array" => $"new JsonArray({expression}.Select(v{depth} => (JsonNode?){Write(wire["items"]!, "v" + depth, depth + 1)}).ToArray())",
        "object" => $"new JsonObject {{ {string.Join(", ", wire.A("fields").Select(f => "[" + Quote(f.S("key")) + "] = " + Write(f, expression + "." + f.S("member"), depth + 1)))} }}",
        _ => throw new ArgumentException("Unknown business wire type"),
    };
}
