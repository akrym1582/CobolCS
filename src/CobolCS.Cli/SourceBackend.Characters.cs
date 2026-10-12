using System.Text;
using System.Text.Json.Nodes;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>INSPECT の対応判定と、捕捉済みの型付き値を結びます。</summary>
internal sealed partial class SourceBackend
{
    /// <summary>添字と全 operand を更新前に一度だけ評価します。</summary>
    /// <param name="statement">単一操作の INSPECT。</param>
    /// <returns>通常の C# 本文。</returns>
    private string InspectStatement(JsonNode statement)
    {
        var reason = CharacterCapabilities.Reason(statement, symbols);
        if (reason.Length != 0)
        {
            throw new NotSupportedException(reason);
        }

        var code = new StringBuilder();
        string Capture(JsonNode reference, string prefix)
        {
            var symbol = symbols[reference.S("symbolId")];
            var subscripts = reference.A("subscripts").ToArray();
            var dims = Dimensions(symbol);
            for (var i = 0; i < dims.Length; i++)
            {
                code.AppendLine($"var {prefix}{i} = SourceSupport.Index({Expression(subscripts[i])}, {dims[i]});");
            }

            return Access(symbol, dims.Select((_, i) => prefix + i).ToArray());
        }

        var operation = statement.A("operations").Single();
        var subject = Capture(statement["value"]!, "inspectIndex");
        var target = operation.S("verb") == "tally" ? Capture(operation["target"]!, "tallyIndex") : subject;
        code.AppendLine($"var inspected = {subject};");
        code.AppendLine($"string? pattern = {(operation["pattern"] == null ? "null" : Expression(operation["pattern"]!))};");
        if (operation.S("verb") == "tally")
        {
            code.AppendLine($"var tally = {target};\n{target} = {ConvertValue(symbols[operation["target"]!.S("symbolId")], $"tally + SourceCharacters.Tally(inspected, {Quote(operation.S("mode"))}, pattern)")};");
        }
        else
        {
            code.AppendLine($"var replacement = {Expression(operation["replacement"]!)};\n{target} = SourceCharacters.Replace(inspected, {Quote(operation.S("mode"))}, pattern, replacement);");
        }

        return code.ToString();
    }
}
