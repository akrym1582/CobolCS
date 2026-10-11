using System.Text;
using System.Text.Json.Nodes;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>グループ単位の型付き試験入力と独立スナップショットを構成します。</summary>
internal sealed partial class SourceBackend
{
    /// <summary>既知の宣言階層を持つ最上位グループです。</summary>
    /// <returns>子項目を公開するグループ。</returns>
    private IEnumerable<JsonNode> PublicGroups() => symbols.Values.Where(s => s.S("parentId") == string.Empty && s.S("name") != "FILLER" && s["pic"] == null && !s.B("stub") && s.I("level") != 88 && ArraySafe(s));

    /// <summary>グループ内の基本項目を型付き DTO として出力します。</summary>
    /// <returns>グループ入力と結果の宣言。</returns>
    private string GroupDtos()
    {
        var result = new StringBuilder();
        foreach (var group in PublicGroups())
        {
            var leaves = symbols.Values.Where(s => s.I("level") != 88 && s.S("name") != "FILLER" && (s["pic"] != null || s.B("stub")) && Ancestors(s).First().S("id") == group.S("id")).ToArray();
            var type = name + "Group" + group.S("id");
            result.AppendLine($"public sealed class {type}Setup\n{{");
            foreach (var symbol in leaves.Where(Known))
            {
                result.AppendLine(Dimensions(symbol).Length == 0 ? $"public Optional<{TypeName(symbol)}> {Member(symbol)} {{ get; init; }}" : $"public IReadOnlyList<TableEntry<{TypeName(symbol)}>> {Member(symbol)} {{ get; init; }} = Array.Empty<TableEntry<{TypeName(symbol)}>>();");
            }

            result.AppendLine($"}}\npublic sealed class {type}Snapshot\n{{");
            foreach (var symbol in leaves)
            {
                result.AppendLine($"public {(!Known(symbol) || Dimensions(symbol).Length == 0 ? TypeName(symbol) : $"IReadOnlyList<{TypeName(symbol)}>")} {Member(symbol)} {{ get; }}");
            }

            result.AppendLine($"internal {type}Snapshot({name}State state)\n{{");
            foreach (var symbol in leaves)
            {
                result.AppendLine($"{Member(symbol)} = {(!Known(symbol) ? $"new UnknownCobolValue({Quote(symbol.S("qualifiedName"))})" : Dimensions(symbol).Length == 0 ? Access(symbol, []) : $"Array.AsReadOnly({Elements(symbol, a => a)}.ToArray())")};");
            }

            result.AppendLine("}\n}");
        }

        return result.ToString();
    }
}
