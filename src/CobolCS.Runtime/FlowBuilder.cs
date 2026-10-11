using System.Text.Json.Nodes;

namespace CobolCS.Runtime;

/// <summary>
/// 入れ子の COBOL 制御構造を、実行器が一命令ずつ進められる形に組み替えます。
/// 元の命令と位置を残しながら、条件成立・不成立や段落から戻る位置を明示します。
/// </summary>
public static class FlowBuilder
{
    /// <summary>
    /// COBOL の段落と入れ子の文を、移動先が番号で決まる命令列へ変換します。
    /// IF の両側や PERFORM の終端を明示することで、実行器は C# の再帰を使わず次の命令を選べます。
    /// </summary>
    /// <param name="program">宣言順の段落と、その中の文を含む一つの PROGRAM-ID の IR。</param>
    /// <param name="preserveSentences">制御スタブの移動先として文末境界を保持する場合は true。</param>
    /// <returns>条件分岐、ループ、段落の戻り先を解決した命令列。</returns>
    /// <exception cref="KeyNotFoundException">GO TO や PERFORM が指す段落・SECTION が見つからない場合。</exception>
    /// <exception cref="NotSupportedException">ループ外の EXIT PERFORM など、実行順序を構成できない場合。</exception>
    public static Instruction[] Build(JsonNode program, bool preserveSentences = false)
    {
        var code = new List<Instruction>();
        var paragraphs = program.A("paragraphs").ToArray();
        var starts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var ends = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int Emit(string op, JsonNode? node = null, int next = -1, int alternate = -1)
        {
            code.Add(new(op, node, next, alternate));
            return code.Count - 1;
        }

        void Patch(int i, int next, int alternate = -1) => code[i] = code[i] with
        {
            Next = next,
            Alternate = alternate,
        };
        void Block(IEnumerable<JsonNode> statements, List<int>? exits = null)
        {
            foreach (var s in statements)
            {
                switch (s.S("kind"))
                {
                    case "if":
                        var branch = Emit("branch", s["condition"]);
                        var yes = code.Count;
                        Block(s.A("then"), exits);
                        var jump = Emit("jump");
                        var no = code.Count;
                        Block(s.A("else"), exits);
                        Patch(branch, yes, no);
                        Patch(jump, code.Count);
                        break;
                    case "evaluate":
                        var jumps = new List<int>();
                        foreach (var c in s.A("cases"))
                        {
                            JsonNode? test = null;
                            foreach (var value in c.A("values"))
                            {
                                var equal = new JsonObject
                                {
                                    ["kind"] = "binary",
                                    ["op"] = "=",
                                    ["left"] = s["subject"]!.DeepClone(),
                                    ["right"] = value.DeepClone(),
                                };
                                test = test == null ? equal : new JsonObject
                                {
                                    ["kind"] = "binary",
                                    ["op"] = "OR",
                                    ["left"] = test,
                                    ["right"] = equal,
                                };
                            }

                            if (test != null)
                            {
                                test["source"] = s["source"]?.DeepClone();
                                test["id"] = s.S("id");
                            }

                            var b = Emit("branch", test);
                            var body = code.Count;
                            Block(c.A("body"), exits);
                            jumps.Add(Emit("jump"));
                            Patch(b, body, code.Count);
                        }

                        Block(s.A("other"), exits);
                        foreach (var j in jumps)
                        {
                            Patch(j, code.Count);
                        }

                        break;
                    case "loop":
                        var init = Emit("loopInit", s);
                        var check = Emit("loopCheck", s);
                        var begin = code.Count;
                        var loopExits = new List<int>();
                        Block(s.A("body"), loopExits);
                        Emit("loopNext", s);
                        var after = Emit("loopCheck", s);
                        var end = code.Count;
                        Patch(init, check);
                        Patch(check, begin, end);
                        Patch(after, begin, end);
                        foreach (var j in loopExits)
                        {
                            Patch(j, end);
                        }

                        break;
                    case "sequence":
                        Block(s.A("body"), exits);
                        break;
                    case "arithmetic":
                    case "stringTransfer" when preserveSentences:
                        Emit("basic", s);
                        var error = Emit("sizeBranch");
                        var on = code.Count;
                        Block(s.A(s.S("kind") == "stringTransfer" ? "onOverflow" : "onError"), exits);
                        var skip = Emit("jump");
                        var not = code.Count;
                        Block(s.A(s.S("kind") == "stringTransfer" ? "notOverflow" : "notError"), exits);
                        Patch(error, on, not);
                        Patch(skip, code.Count);
                        break;
                    case "exitPerform":
                        if (exits == null)
                        {
                            throw new NotSupportedException("EXIT PERFORM outside inline loop");
                        }

                        exits.Add(Emit("jump"));
                        break;
                    case "perform":
                        Emit("perform", s);
                        break;
                    case "goto":
                        Emit("goto", s);
                        break;
                    case "gotoDepending":
                        Emit("gotoDepending", s);
                        break;
                    case "halt":
                        Emit("halt", s);
                        break;
                    case "sentenceBoundary":
                        if (preserveSentences)
                        {
                            Emit("sentenceBoundary", s);
                        }

                        break;
                    case "continue":
                        break;
                    default:
                        Emit("basic", s);
                        break;
                }
            }
        }

        foreach (var p in paragraphs)
        {
            starts[p.S("name")] = code.Count;
            if (p.S("section") is string section && section != string.Empty && !starts.ContainsKey(section))
            {
                starts[section] = code.Count;
            }

            Block(p.A("statements"));
            var boundary = Emit("boundary");
            ends[p.S("name")] = boundary;
            if (p.S("section") != string.Empty)
            {
                ends[p.S("section")] = boundary;
            }
        }

        Emit("end");
        for (var i = 0; i < code.Count; i++)
        {
            var ins = code[i];
            var node = ins.Node;
            if (ins.Op == "perform")
            {
                Patch(i, starts[node!.S("from")], ends[node!.S("thru")]);
            }

            if (ins.Op == "goto")
            {
                Patch(i, starts[node!.S("target")]);
            }

            if (ins.Op == "gotoDepending")
            {
                var lowered = node!.DeepClone();
                lowered["targetPcs"] = new JsonArray(node.A("targets").Select(target => (JsonNode?)JsonValue.Create(starts[target.GetValue<string>()])).ToArray());
                code[i] = ins with
                {
                    Node = lowered,
                };
            }
        }

        return code.ToArray();
    }
}
