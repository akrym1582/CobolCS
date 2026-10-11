using System.Text;

namespace CobolCS.Cli;

/// <summary>生成済み C# の括弧を文字列やコメントと区別してインデントします。</summary>
internal static class SourceFormatting
{
    /// <summary>生成テンプレートの改行を保ちながら、ブロック構造を読みやすくします。</summary>
    /// <param name="source">この backend のテンプレートが生成した C#。</param>
    /// <returns>決定的な四空白インデント。</returns>
    internal static string Indent(string source)
    {
        var result = new StringBuilder();
        var depth = 0;
        foreach (var raw in source.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                result.AppendLine();
                continue;
            }

            var indentation = Math.Max(0, depth - (line.StartsWith('}') ? 1 : 0));
            if (!line.StartsWith('#'))
            {
                result.Append(' ', indentation * 4);
            }

            result.AppendLine(line);
            if (line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            var quoted = false;
            for (var i = 0; i < line.Length; i++)
            {
                if (quoted && line[i] == '\\')
                {
                    i++;
                }
                else if (line[i] == '"')
                {
                    quoted = !quoted;
                }
                else if (!quoted)
                {
                    depth += line[i] == '{' ? 1 : line[i] == '}' ? -1 : 0;
                }
            }
        }

        return result.ToString().TrimEnd() + "\n";
    }
}
