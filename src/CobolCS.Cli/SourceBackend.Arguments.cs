using System.Text;
using System.Text.Json.Nodes;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>固定レイアウトの正式引数を独立した値として受け渡します。</summary>
internal sealed partial class SourceBackend
{
    private Dictionary<string, List<JsonNode>>? layoutLeavesByRoot;
    private HashSet<string>? unknownLayoutRoots;
    private HashSet<string>? renamedLayoutRoots;

    /// <summary>生成中に不変の宣言を一度索引化し、元のシンボル順で対象領域の基本項目を返します。</summary>
    /// <param name="root">領域。</param>
    /// <returns>条件名以外の基本項目。</returns>
    private IEnumerable<JsonNode> LayoutLeaves(JsonNode root)
    {
        if (layoutLeavesByRoot == null)
        {
            layoutLeavesByRoot = new(StringComparer.Ordinal);
            foreach (var symbol in symbols.Values.Where(s => s.I("level") != 88 && (s["pic"] != null || s.B("stub"))))
            {
                foreach (var ancestor in Ancestors(symbol))
                {
                    var id = ancestor.S("id");
                    if (!layoutLeavesByRoot.TryGetValue(id, out var leaves))
                    {
                        leaves = [];
                        layoutLeavesByRoot.Add(id, leaves);
                    }

                    leaves.Add(symbol);
                }
            }
        }

        return layoutLeavesByRoot.GetValueOrDefault(root.S("id")) ?? [];
    }

    /// <summary>領域全体を値として扱えるか確認します。</summary>
    /// <param name="root">対象領域。</param>
    /// <returns>別名や未知宣言がない場合は true。</returns>
    private bool FixedLayout(JsonNode root)
    {
        if (unknownLayoutRoots == null)
        {
            unknownLayoutRoots = symbols.Values.Where(s => s.S("layoutStatus").Contains("unknown", StringComparison.Ordinal)).SelectMany(Ancestors).Select(s => s.S("id")).ToHashSet(StringComparer.Ordinal);
            renamedLayoutRoots = symbols.Values.Where(s => s.I("level") == 66).Select(s => Ancestors(s).First().S("id")).ToHashSet(StringComparer.Ordinal);
        }

        return !root.B("stub") && root["redefines"] == null && !unknownLayoutRoots.Contains(root.S("id")) && LayoutLeaves(root).Any() && LayoutLeaves(root).All(Known) && !renamedLayoutRoots!.Contains(Ancestors(root).First().S("id"));
    }

    /// <summary>値境界では切り捨てず PIC と文字数を検査します。</summary>
    /// <param name="symbol">基本項目。</param>
    /// <param name="value">入力式。</param>
    /// <returns>検証後の値。</returns>
    private string ValidateBoundary(JsonNode symbol, string value) => TypeName(symbol) == "Number" ? ConvertValue(symbol, value, sizeError: true) : $"SourceSupport.BoundaryText({value}, {symbol["pic"]!.I("digits")}, {Bool(symbol["pic"]!.S("category") == "national")})";

    /// <summary>引数全体が今回の宣言側の範囲に収まるか確認します。</summary>
    /// <returns>参照渡しと数値スカラー値渡しを許可します。</returns>
    private bool SupportedUsing() => program.A("using").All(a => symbols.TryGetValue(a.S("symbolId"), out var s) && s.S("scope") == "LINKAGE" && FixedLayout(s) && (a.S("mode") == "reference" || (a.S("mode") == "value" && Known(s) && TypeName(s) == "Number" && Dimensions(s).Length == 0))) && program.A("using").Select(a => a.S("symbolId")).Distinct().Count() == program.A("using").Count();

    /// <summary>同一または親子領域を検出します。</summary>
    /// <param name="left">一方の宣言。</param>
    /// <param name="right">他方の宣言。</param>
    /// <returns>領域が重なる場合は true。</returns>
    private bool Overlaps(JsonNode left, JsonNode right) => Ancestors(left).Any(a => a.S("id") == right.S("id")) || Ancestors(right).Any(a => a.S("id") == left.S("id"));

    /// <summary>公開引数の型名です。</summary>
    /// <param name="symbol">引数宣言。</param>
    /// <returns>スカラー、表または既存グループ DTO。</returns>
    private string ArgumentType(JsonNode symbol) => symbol["pic"] == null ? name + "Group" + symbol.S("id") + "Snapshot" : Dimensions(symbol).Length == 0 ? TypeName(symbol) : $"IReadOnlyList<{TypeName(symbol)}>";

    /// <summary>引数入力と実行後の読み取り専用結果を生成します。</summary>
    /// <returns>公開宣言。</returns>
    private string ArgumentsApi()
    {
        var arguments = program.A("using").Where(a => symbols.ContainsKey(a.S("symbolId"))).Select(a => symbols[a.S("symbolId")]).Distinct().Where(FixedLayout).ToArray();
        var code = new StringBuilder($"public sealed class {name}Arguments\n{{\n");
        foreach (var symbol in arguments)
        {
            code.AppendLine($"public Optional<{ArgumentType(symbol)}> {Member(symbol)} {{ get; init; }}");
        }

        code.AppendLine($"}}\npublic sealed class {name}ArgumentsSnapshot\n{{");
        foreach (var symbol in arguments)
        {
            code.AppendLine($"public {ArgumentType(symbol)} {Member(symbol)} {{ get; }}");
        }

        code.AppendLine($"internal {name}ArgumentsSnapshot({name}State state)\n{{");
        foreach (var symbol in arguments)
        {
            code.AppendLine($"{Member(symbol)} = {(symbol["pic"] == null ? "new(state)" : Dimensions(symbol).Length == 0 ? Access(symbol, []) : $"Array.AsReadOnly({Elements(symbol, v => v)}.ToArray())")};");
        }

        return code.AppendLine("}\n}").ToString();
    }

    /// <summary>完全な値を検証してから LINKAGE 領域へ格納します。</summary>
    /// <returns>入力反映コード。</returns>
    private string ApplyArguments()
    {
        if (!program.A("using").Any() || !SupportedUsing())
        {
            return string.Empty;
        }

        var code = new StringBuilder("ArgumentNullException.ThrowIfNull(value.Arguments);\n");
        foreach (var argument in program.A("using"))
        {
            var symbol = symbols[argument.S("symbolId")];
            var member = "value.Arguments." + Member(symbol);
            code.AppendLine($"if (!{member}.HasValue) throw new ArgumentException({Quote("Missing LINKAGE argument " + symbol.S("qualifiedName"))});");
            if (symbol["pic"] == null)
            {
                code.AppendLine($"var argument_{symbol.S("id")} = {member}.Value?.Copy() ?? throw new ArgumentException(\"Null LINKAGE group\");");
                foreach (var leaf in LayoutLeaves(symbol))
                {
                    code.Append(StoreLayoutLeaf(leaf, $"argument_{symbol.S("id")}.{Member(leaf)}"));
                }
            }
            else if (Dimensions(symbol).Length == 0)
            {
                code.AppendLine($"{Access(symbol, [])} = {ValidateBoundary(symbol, member + ".Value")};");
            }
            else
            {
                var count = Dimensions(symbol).Aggregate(1, (a, b) => a * b);
                code.AppendLine($"if ({member}.Value == null || {member}.Value.Count != {count}) throw new ArgumentException(\"Incomplete LINKAGE table\");\nvar argument_{symbol.S("id")} = {member}.Value.Select(v => {ValidateBoundary(symbol, "v")}).ToArray();");
                code.Append(StoreLayoutLeaf(symbol, "argument_" + symbol.S("id")));
            }
        }

        return code.ToString();
    }

    /// <summary>平坦化した固定表を生成時に決めたループで反映します。</summary>
    /// <param name="symbol">基本項目。</param>
    /// <param name="value">検証済みの値。</param>
    /// <returns>代入と固定ループ。</returns>
    private string StoreLayoutLeaf(JsonNode symbol, string value)
    {
        var dims = Dimensions(symbol);
        if (dims.Length == 0)
        {
            return $"{Access(symbol, [])} = {value};\n";
        }

        var code = new StringBuilder("{\nvar offset = 0;\n");
        for (var i = 0; i < dims.Length; i++)
        {
            code.AppendLine($"for (var i{i} = 0; i{i} < {dims[i]}; i{i}++)\n{{");
        }

        code.AppendLine($"{Access(symbol, dims.Select((_, i) => "i" + i).ToArray())} = {value}[offset++];");
        return code.AppendLine(new string('}', dims.Length + 1)).ToString();
    }
}
