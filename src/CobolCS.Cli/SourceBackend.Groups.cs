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
        foreach (var group in symbols.Values.Where(s => s["pic"] == null && s.I("level") != 88 && !s.B("stub") && ArraySafe(s)))
        {
            var leaves = symbols.Values.Where(s => s.I("level") != 88 && (s["pic"] != null || s.B("stub")) && Ancestors(s).Any(a => a.S("id") == group.S("id"))).ToArray();
            var type = name + "Group" + group.S("id");
            result.AppendLine($"public sealed class {type}Setup\n{{");
            foreach (var symbol in leaves.Where(s => Known(s) && s.S("name") != "FILLER"))
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

            result.AppendLine("}");
            if (FixedLayout(group))
            {
                var parameters = leaves.Select(s => Dimensions(s).Length == 0 ? $"Optional<{TypeName(s)}> {Member(s)} = default" : $"IReadOnlyList<{TypeName(s)}>? {Member(s)} = null");
                result.AppendLine($"public {type}Snapshot({string.Join(", ", parameters)})\n{{");
                foreach (var symbol in leaves)
                {
                    var member = Member(symbol);
                    var count = Dimensions(symbol).Aggregate(1, (a, b) => a * b);
                    if (Dimensions(symbol).Length == 0)
                    {
                        result.AppendLine($"if (!{member}.HasValue) throw new ArgumentException({Quote("Missing group field " + symbol.S("qualifiedName"))});\nthis.{member} = {ValidateBoundary(symbol, member + ".Value")};");
                    }
                    else
                    {
                        result.AppendLine($"if ({member} == null || {member}.Count != {count}) throw new ArgumentException({Quote("Incomplete group table " + symbol.S("qualifiedName"))});\nthis.{member} = Array.AsReadOnly({member}.Select(v => {ValidateBoundary(symbol, "v")}).ToArray());");
                    }
                }

                var comparison = string.Join(" && ", leaves.Select(s => Dimensions(s).Length == 0 ? $"{Member(s)} == other.{Member(s)}" : $"{Member(s)}.SequenceEqual(other.{Member(s)})"));
                result.AppendLine($"}}\npublic bool ContentEquals({type}Snapshot? other) => other != null && {comparison};\npublic {type}Snapshot Copy() => new({string.Join(", ", leaves.Select(s => Member(s)))});");
            }

            result.AppendLine("}");
        }

        return result.ToString();
    }
}
