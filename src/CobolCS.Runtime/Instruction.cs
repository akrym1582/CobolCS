namespace CobolCS.Runtime;
/// <summary>
/// 元の COBOL 文を、実行順序を管理できる一段階の操作に分けた命令です。
/// 分岐先を番号で保持するため、GO TO を繰り返しても C# の再帰呼び出しは増えません。
/// </summary>
/// <param name="Op">実行器が識別する操作名。通常の文、条件分岐、段落呼び出しなどを区別します。</param>
/// <param name="Node">操作に必要な解析済みの式や元位置。移動だけの命令では省略できます。</param>
/// <param name="Next">条件成立時や段落呼び出し時の移動先。不要な命令では -1 です。</param>
/// <param name="Alternate">条件不成立時の移動先、または呼び出した段落の終端。不要な命令では -1 です。</param>
/// <param name="StatementId">元文の識別子。条件や合成移動も親文に対応させます。</param>
public sealed record Instruction(string Op, System.Text.Json.Nodes.JsonNode? Node = null, int Next = -1, int Alternate = -1, string StatementId = "");
