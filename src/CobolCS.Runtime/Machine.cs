using System.Diagnostics;
using System.Text.Json.Nodes;

namespace CobolCS.Runtime;

public sealed partial class Machine
{
    private readonly Storage storage;
    private readonly JsonNode config;
    private readonly List<string> display = [];
    private bool sizeError;
    private readonly Dictionary<string, int> loopRemaining = [];
    private readonly HashSet<string> loopFirst = [];
    public Machine(JsonNode program, JsonNode configuration, JsonNode? fixture = null)
    {
        config = configuration; storage = new Storage(program); storage.Initialize(Eval);
        if (fixture != null)
        {
            foreach (var property in fixture.AsObject())
                if (property.Key != "fields" && property.Value is JsonNode value && (value is not JsonArray a || a.Count != 0) && (value is not JsonObject o || o.Count != 0))
                    throw new NotSupportedException("Unsupported fixture property: " + property.Key);
            if (fixture["fields"] is JsonObject fields)
                foreach (var (name, value) in fields)
                {
                    var candidates = storage.Symbols.Values.Where(s => s.S("qualifiedName") == name || s.S("name") == name).ToArray();
                    if (candidates.Length != 1) throw new InvalidOperationException("Unknown or ambiguous fixture field " + name);
                    var symbol = candidates[0];
                    if (storage.Dimensions(symbol).Length != 0) throw new NotSupportedException("Table fixture initialization");
                    storage.Set(new JsonObject { ["symbolId"] = symbol.S("id") }, value!.GetValue<string>(), Eval, sizeError: true);
                }
        }
    }
    public JsonObject Run(Instruction[] flow, JsonNode program)
    {
        var timer = Stopwatch.StartNew(); var steps = 0; var pc = 0; var signal = "fallthrough";
        var returns = new Stack<(int End, int Return)>();
        try
        {
            while (pc >= 0 && pc < flow.Length)
            {
                if (++steps > config.I("maxSteps", 1000000) || timer.ElapsedMilliseconds > config.I("timeoutMs", 10000)) throw new InvalidOperationException("Execution limit exceeded");
                var instruction = flow[pc]; var s = instruction.Node; var next = pc + 1;
                switch (instruction.Op)
                {
                    case "branch": next = Truth(Eval(s!)) ? instruction.Next : instruction.Alternate; break;
                    case "sizeBranch": next = sizeError ? instruction.Next : instruction.Alternate; break;
                    case "jump": case "goto": next = instruction.Next; break;
                    case "gotoDepending":
                        var index = Integer(Eval(s!["depending"]!)); var targets = s.A("targetPcs").ToArray();
                        if (index > 0 && index <= targets.Length) next = targets[index - 1].GetValue<int>(); break;
                    case "perform":
                        if (returns.Count > 10000) throw new InvalidOperationException("PERFORM nesting limit");
                        returns.Push((instruction.Alternate, pc + 1)); next = instruction.Next; break;
                    case "boundary":
                        if (returns.TryPeek(out var frame) && frame.End == pc) { returns.Pop(); next = frame.Return; } break;
                    case "loopInit":
                        loopFirst.Add(s!.S("id"));
                        if (s!["times"] is JsonNode times) loopRemaining[s.S("id")] = Math.Max(0, Integer(Eval(times)));
                        if (s["varying"] is JsonNode v) storage.Set(v["reference"]!, Eval(v["from"]!), Eval);
                        break;
                    case "loopCheck":
                        var first = loopFirst.Remove(s!.S("id"));
                        var stop = s!["times"] != null ? loopRemaining[s.S("id")] <= 0 : s["until"] != null && !(first && s.B("testAfter")) && Truth(Eval(s["until"]!));
                        next = stop ? instruction.Alternate : instruction.Next; break;
                    case "loopNext":
                        if (s!["times"] != null) loopRemaining[s.S("id")]--;
                        if (s["varying"] is JsonNode varying) storage.Set(varying["reference"]!, Numeric(Eval(varying["reference"]!)) + Numeric(Eval(varying["by"]!)), Eval);
                        break;
                    case "halt": signal = s!.S("signal"); pc = flow.Length; continue;
                    case "end": pc = flow.Length; continue;
                    case "basic": Execute(s!); break;
                    default: throw new NotSupportedException("Instruction " + instruction.Op);
                }
                pc = next;
            }
        }
        catch (Exception e) { throw new InvalidOperationException($"{program.S("id")} at instruction {pc}: {flow[Math.Min(pc, flow.Length - 1)].Node?["source"]?.ToJsonString()}: {e.Message}", e); }
        return new JsonObject { ["display"] = new JsonArray(display.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()), ["fields"] = storage.Snapshot(), ["signal"] = signal };
    }
    private void Execute(JsonNode s)
    {
        switch (s.S("kind"))
        {
            case "move":
                var value = Eval(s["value"]!); foreach (var target in s.A("targets")) storage.Set(target, value, Eval); break;
            case "display":
                display.Add(string.Concat(s.A("values").Select(e => e.S("kind") == "reference" ? storage.Display(e, Eval) : Text(Eval(e))))); break;
            case "set88":
                var condition = storage.Symbols[s.S("symbolId")];
                storage.Set(new JsonObject { ["symbolId"] = condition.S("parentId"), ["subscripts"] = s["subscripts"]!.DeepClone() }, Eval(condition.A("conditions").First()["low"]!), Eval); break;
            case "setIndex":
                var operand = Eval(s["value"]!);
                foreach (var target in s.A("targets")) storage.Set(target, s.S("operation") switch { "up" => Numeric(Eval(target)) + Numeric(operand), "down" => Numeric(Eval(target)) - Numeric(operand), _ => operand }, Eval);
                break;
            case "arithmetic": Arithmetic(s); break;
            default: throw new NotSupportedException("Unsupported COBOL statement: " + s.S("kind") + ": " + s["source"]?.S("raw"));
        }
    }
    private void Arithmetic(JsonNode s)
    {
        sizeError = false;
        try
        {
            var value = Numeric(Eval(s["value"]!));
            Number? quotient = null;
            var prepared = new List<Action>();
            foreach (var target in s.A("targets"))
            {
                var reference = target["reference"]!; var result = value;
                if (s.B("update")) result = s.S("verb") switch
                {
                    "ADD" => Numeric(Eval(reference)) + value, "SUBTRACT" => Numeric(Eval(reference)) - value,
                    "MULTIPLY" => Numeric(Eval(reference)) * value, "DIVIDE" => Numeric(Eval(reference)) / value,
                    _ => throw new NotSupportedException("Arithmetic update")
                };
                try
                {
                    prepared.Add(storage.Prepare(reference, result, Eval, target.B("rounded"), s.B("hasSizeError")));
                    var scale = storage.Symbols[reference.S("symbolId")]["pic"]!.I("scale");
                    quotient = new Number(result.Units(scale), System.Numerics.BigInteger.Pow(10, scale));
                }
                catch (SizeError) { sizeError = true; }
            }
            if (s["remainder"] is JsonNode remainder && quotient is Number q)
            {
                var expression = s["value"]!;
                if (expression.S("kind") != "binary" || expression.S("op") != "/") throw new NotSupportedException("Remainder form");
                prepared.Add(storage.Prepare(remainder, Numeric(Eval(expression["left"]!)) - q * Numeric(Eval(expression["right"]!)), Eval, sizeError: s.B("hasSizeError")));
            }
            foreach (var commit in prepared) commit();
        }
        catch (DivideByZeroException) when (s.B("hasSizeError")) { sizeError = true; }
        catch (SizeError) when (s.B("hasSizeError")) { sizeError = true; }
    }
}
