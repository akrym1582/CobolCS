using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>既存 IR の宣言位置と参照先から RENAMES の影響する 01 領域を保守的に特定します。</summary>
internal static class RenamedRegions
{
    /// <summary>範囲を証明できない RENAMES が一件でもあれば全領域を拒否します。</summary>
    /// <param name="operand">利用する基本項目またはグループ。</param>
    /// <param name="symbols">同じプログラムの宣言集合。</param>
    /// <returns>共有または不明の領域なら true。</returns>
    internal static bool Affects(JsonNode operand, IReadOnlyDictionary<string, JsonNode> symbols)
    {
        JsonNode? Root(JsonNode symbol)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (symbol.S("parentId") != string.Empty)
            {
                if (!visited.Add(symbol.S("id")) || !symbols.TryGetValue(symbol.S("parentId"), out var parent))
                {
                    return null;
                }

                symbol = parent;
            }

            return symbol;
        }

        var operandRoot = Root(operand);
        if (operandRoot == null)
        {
            return true;
        }

        foreach (var rename in symbols.Values.Where(s => s.I("level") == 66))
        {
            // The frontend retains unsupported level-66 declarations in source.raw.
            // Only the complete simple RENAMES grammar can prove an independent region.
            var match = Regex.Match(rename["source"]?.S("raw") ?? string.Empty, @"\A\s*66\s+[A-Z0-9_-]+\s+RENAMES\s+(?<from>[A-Z0-9_-]+)(?:\s+(?:THRU|THROUGH)\s+(?<to>[A-Z0-9_-]+))?\s*\.\s*\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                return true;
            }

            JsonNode? Resolve(string name)
            {
                var selected = symbols.Values.Where(s => s.I("level") is not (66 or 88) && string.Equals(s.S("name"), name, StringComparison.OrdinalIgnoreCase) && s.S("scope") == rename.S("scope")).ToArray();
                return selected.Length == 1 ? selected[0] : null;
            }

            var from = Resolve(match.Groups["from"].Value);
            var to = match.Groups["to"].Success ? Resolve(match.Groups["to"].Value) : from;
            var fromRoot = from == null ? null : Root(from);
            var toRoot = to == null ? null : Root(to);
            var parentRoot = Root(rename);
            if (fromRoot == null || toRoot == null || parentRoot == null || fromRoot.I("level") != 1 || fromRoot.S("id") != toRoot.S("id") || fromRoot.S("id") != parentRoot.S("id") || fromRoot.S("scope") != rename.S("scope"))
            {
                return true;
            }

            var ordered = symbols.Values.Where(s => Root(s)?.S("id") == fromRoot.S("id") && s.I("level") is not (66 or 88)).ToArray();
            var first = Array.IndexOf(ordered, from);
            var last = Array.IndexOf(ordered, to);
            if (first < 0 || last < first || ordered.Skip(first).Take(last - first + 1).Any(s => s.B("stub") || s.S("layoutStatus").Contains("unknown", StringComparison.Ordinal) || s["redefines"] != null || s["dependingOn"] != null))
            {
                return true;
            }

            if (operandRoot.S("id") == fromRoot.S("id"))
            {
                return true;
            }
        }

        return false;
    }
}
