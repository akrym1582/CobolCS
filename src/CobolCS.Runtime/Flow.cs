using System.Text.Json.Nodes;

namespace CobolCS.Runtime;

public sealed record Instruction(string Op, JsonNode? Node = null, int Next = -1, int Alternate = -1);

/// <summary>Lower nested blocks to a bounded, iterative program counter dispatcher.</summary>
public static class FlowBuilder
{
    public static Instruction[] Build(JsonNode program)
    {
        var code = new List<Instruction>();
        var paragraphs = program.A("paragraphs").ToArray();
        var starts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var ends = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int Emit(string op, JsonNode? node = null, int next = -1, int alternate = -1) { code.Add(new(op, node, next, alternate)); return code.Count - 1; }
        void Patch(int i, int next, int alternate = -1) => code[i] = code[i] with { Next = next, Alternate = alternate };
        void Block(IEnumerable<JsonNode> statements, List<int>? exits = null)
        {
            foreach (var s in statements)
            {
                switch (s.S("kind"))
                {
                    case "if":
                        var branch = Emit("branch", s["condition"]);
                        var yes = code.Count; Block(s.A("then"), exits); var jump = Emit("jump");
                        var no = code.Count; Block(s.A("else"), exits);
                        Patch(branch, yes, no); Patch(jump, code.Count); break;
                    case "evaluate":
                        var jumps = new List<int>();
                        foreach (var c in s.A("cases"))
                        {
                            JsonNode? test = null;
                            foreach (var value in c.A("values"))
                            {
                                var equal = new JsonObject { ["kind"] = "binary", ["op"] = "=", ["left"] = s["subject"]!.DeepClone(), ["right"] = value.DeepClone() };
                                test = test == null ? equal : new JsonObject { ["kind"] = "binary", ["op"] = "OR", ["left"] = test, ["right"] = equal };
                            }
                            var b = Emit("branch", test); var body = code.Count;
                            Block(c.A("body"), exits); jumps.Add(Emit("jump")); Patch(b, body, code.Count);
                        }
                        Block(s.A("other"), exits); foreach (var j in jumps) Patch(j, code.Count); break;
                    case "loop":
                        var init = Emit("loopInit", s); var check = Emit("loopCheck", s);
                        var begin = code.Count; var loopExits = new List<int>(); Block(s.A("body"), loopExits);
                        Emit("loopNext", s); var after = Emit("loopCheck", s); var end = code.Count;
                        Patch(init, check); Patch(check, begin, end); Patch(after, begin, end);
                        foreach (var j in loopExits) Patch(j, end); break;
                    case "sequence": Block(s.A("body"), exits); break;
                    case "arithmetic":
                        Emit("basic", s); var error = Emit("sizeBranch");
                        var on = code.Count; Block(s.A("onError"), exits); var skip = Emit("jump");
                        var not = code.Count; Block(s.A("notError"), exits); Patch(error, on, not); Patch(skip, code.Count); break;
                    case "exitPerform":
                        if (exits == null) throw new NotSupportedException("EXIT PERFORM outside inline loop");
                        exits.Add(Emit("jump")); break;
                    case "perform": Emit("perform", s); break;
                    case "goto": Emit("goto", s); break;
                    case "gotoDepending": Emit("gotoDepending", s); break;
                    case "halt": Emit("halt", s); break;
                    case "continue": case "sentenceBoundary": break;
                    default: Emit("basic", s); break;
                }
            }
        }
        foreach (var p in paragraphs)
        {
            starts[p.S("name")] = code.Count;
            if (p.S("section") is string section && section != "" && !starts.ContainsKey(section)) starts[section] = code.Count;
            Block(p.A("statements"));
            var boundary = Emit("boundary");
            ends[p.S("name")] = boundary;
            if (p.S("section") != "") ends[p.S("section")] = boundary;
        }
        Emit("end");
        for (var i = 0; i < code.Count; i++)
        {
            var ins = code[i]; var node = ins.Node;
            if (ins.Op == "perform") Patch(i, starts[node!.S("from")], ends[node!.S("thru")]);
            if (ins.Op == "goto") Patch(i, starts[node!.S("target")]);
            if (ins.Op == "gotoDepending")
            {
                var lowered = node!.DeepClone();
                lowered["targetPcs"] = new JsonArray(node.A("targets").Select(target => (JsonNode?)JsonValue.Create(starts[target.GetValue<string>()])).ToArray());
                code[i] = ins with { Node = lowered };
            }
        }
        return code.ToArray();
    }
}
