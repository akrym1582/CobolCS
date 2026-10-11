using System.Text.Json.Nodes;

namespace CobolCS.Frontend;

/// <summary>通常 generate の有限探索設定を検査します。既定設定の正準 hash は変更しません。</summary>
internal static class BranchGenerationConfiguration
{
    /// <summary>追加設定の未知キーと、無制限な探索指定を拒否します。</summary>
    /// <param name="value">省略可能な testGeneration オブジェクト。</param>
    internal static void Validate(JsonNode? value)
    {
        if (value is not JsonObject settings || settings.Any(p => p.Key is not ("maxAttempts" or "seed" or "timeoutMs" or "maxTraceEvents" or "candidates")))
        {
            throw new ArgumentException("Invalid testGeneration settings");
        }

        foreach (var (key, minimum, maximum) in new[] { ("maxAttempts", 1, 200), ("seed", 0, int.MaxValue), ("timeoutMs", 1000, 600000), ("maxTraceEvents", 1, 100000) })
        {
            if (settings[key] != null && (settings[key] is not JsonValue number || !number.TryGetValue<int>(out var n) || n < minimum || n > maximum))
            {
                throw new ArgumentException("Invalid testGeneration " + key);
            }
        }

        if (settings["candidates"] != null && settings["candidates"] is not JsonArray { Count: <= 200 })
        {
            throw new ArgumentException("testGeneration candidates must be an array of at most 200 entries");
        }

        foreach (var candidate in settings["candidates"]?.AsArray() ?? [])
        {
            if (candidate is not JsonObject item || item.Any(p => p.Key is not ("programId" or "input" or "responseMode" or "responseModes" or "responseValues")) || item["programId"] is not JsonValue program || !program.TryGetValue<string>(out _) || item["input"] is not JsonObject)
            {
                throw new ArgumentException("Invalid manual testGeneration candidate");
            }
        }
    }
}
