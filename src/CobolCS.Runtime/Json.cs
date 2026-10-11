using System.Text.Json.Nodes;

namespace CobolCS.Runtime;

public static class J
{
    public static string S(this JsonNode n, string key) => n[key]?.GetValue<string>() ?? "";
    public static int I(this JsonNode n, string key, int fallback = 0) => n[key]?.GetValue<int>() ?? fallback;
    public static bool B(this JsonNode n, string key) => n[key]?.GetValue<bool>() ?? false;
    public static IEnumerable<JsonNode> A(this JsonNode n, string key) => (n[key] as JsonArray ?? []).Select(x => x!);
    public static JsonNode Parse(string json) => JsonNode.Parse(json) ?? throw new InvalidDataException("Empty JSON");
}
