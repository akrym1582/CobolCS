using System.Text;
using System.Text.Json.Nodes;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>初期化と文字転送の限定した構文を直接生成します。</summary>
internal sealed partial class SourceBackend
{
    /// <summary>基本項目と固定表へ既定値を代入します。</summary>
    /// <param name="statement">INITIALIZE 文。</param>
    /// <returns>型付き代入とループ。</returns>
    private string InitializeStatement(JsonNode statement)
    {
        if (statement.B("withFiller") || statement.A("replacing").Any())
        {
            throw new NotSupportedException("INITIALIZE additional clauses");
        }

        var code = new StringBuilder();
        foreach (var target in statement.A("targets"))
        {
            if (!symbols.TryGetValue(target.S("symbolId"), out var root) || !FixedLayout(root) || target["modification"] != null)
            {
                throw new NotSupportedException("INITIALIZE requires known fixed layout");
            }

            var selected = target.A("subscripts").ToArray();
            var rootDims = Dimensions(root);
            if (selected.Length != 0 && selected.Length != rootDims.Length)
            {
                throw new NotSupportedException("INITIALIZE subscript count");
            }

            code.AppendLine("{");
            for (var i = 0; i < selected.Length; i++)
            {
                code.AppendLine($"var selected{i} = SourceSupport.Index({Expression(selected[i])}, {rootDims[i]});");
            }

            foreach (var leaf in LayoutLeaves(root).Where(s => s.S("name") != "FILLER"))
            {
                var dims = Dimensions(leaf);
                code.AppendLine("{");
                for (var i = selected.Length; i < dims.Length; i++)
                {
                    code.AppendLine($"for (var i{i} = 0; i{i} < {dims[i]}; i{i}++)\n{{");
                }

                var indices = dims.Select((_, i) => (i < selected.Length ? "selected" : "i") + i).ToArray();
                code.AppendLine($"{Access(leaf, indices)} = {ConvertValue(leaf, TypeName(leaf) == "Number" ? "new Number(0)" : "string.Empty")};");
                code.AppendLine(new string('}', dims.Length - selected.Length + 1));
            }

            code.AppendLine("}");
        }

        return code.ToString();
    }

    /// <summary>ASCII の SIZE・値区切り転送を受信領域保持付きで生成します。</summary>
    /// <param name="statement">STRING 文。</param>
    /// <param name="includeBranches">構造化分岐を含めるかどうか。</param>
    /// <returns>転送と overflow 分岐。</returns>
    private string StringStatement(JsonNode statement, bool includeBranches = true)
    {
        var reason = CharacterCapabilities.Reason(statement, symbols);
        if (reason.Length != 0)
        {
            throw new NotSupportedException(reason);
        }

        var target = statement["target"]!;
        var sendings = statement.A("sendings").ToArray();
        var pointer = statement["pointer"];

        var receiver = Reference(target);
        var pointerAccess = pointer == null ? null : Reference(pointer);

        // Capture all subscript expressions before updating either receiver or pointer.
        var code = new StringBuilder();
        string Capture(JsonNode reference, string prefix)
        {
            var symbol = symbols[reference.S("symbolId")];
            var dims = Dimensions(symbol);
            var subscripts = reference.A("subscripts").ToArray();
            for (var i = 0; i < dims.Length; i++)
            {
                code.AppendLine($"var {prefix}{i} = SourceSupport.Index({Expression(subscripts[i])}, {dims[i]});");
            }

            return Access(symbol, dims.Select((_, i) => prefix + i).ToArray());
        }

        receiver = Capture(target, "receiverIndex");
        pointerAccess = pointer == null ? null : Capture(pointer, "pointerIndex");
        code.AppendLine($"var transfer = SourceSupport.StringSize({receiver}, {pointerAccess ?? "new Number(1)"}, new string[] {{ {string.Join(", ", sendings.Select(s => s["delimiter"] == null ? Expression(s["value"]!) : $"SourceCharacters.Prefix({Expression(s["value"]!)}, {Expression(s["delimiter"]!)})"))} }});\n{receiver} = transfer.Value;");
        if (pointer != null)
        {
            code.AppendLine($"{pointerAccess} = {ConvertValue(symbols[pointer.S("symbolId")], "transfer.Pointer", sizeError: true)};");
        }

        if (!includeBranches)
        {
            return code.AppendLine("lastSizeError = transfer.Overflow;").ToString();
        }

        code.AppendLine($"if (branches.Test({Quote(BranchId(statement))}, transfer.Overflow, \"overflow\", \"normal\"))\n{{");
        code.Append(Block(statement.A("onOverflow")));
        code.AppendLine("}\nelse\n{");
        code.Append(Block(statement.A("notOverflow")));
        return code.AppendLine("}").ToString();
    }
}
