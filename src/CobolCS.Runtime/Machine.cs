using System.Diagnostics;
using System.Text.Json.Nodes;

namespace CobolCS.Runtime;

/// <summary>
/// 一つの COBOL プログラムについて、項目値と実行順序を管理する実行器です。
/// 命令を一つずつ進める方式なので、GO TO の繰り返しによって C# の呼び出しが積み重なりません。
/// </summary>
public sealed partial class Machine
{
    private readonly Storage storage;
    private readonly JsonNode config;
    private readonly List<string> display = [];
    private readonly Dictionary<string, int> loopRemaining = [];
    private readonly HashSet<string> loopFirst = [];
    private bool sizeError;

    /// <summary>
    /// Initializes a new instance of the <see cref="Machine"/> class.
    /// COBOL の VALUE 句を反映した実行状態を作り、必要に応じて試験用の入力で初期値を置き換えます。
    /// </summary>
    /// <param name="program">実行する PROGRAM-ID の宣言を含んだ IR。</param>
    /// <param name="configuration">実行回数や時間の上限を含む設定。</param>
    /// <param name="fixture">省略可能な試験入力。fields でスカラー項目の初期値を指定できます。</param>
    /// <exception cref="NotSupportedException">未対応の外部接続や表の初期値が fixture に含まれる場合。</exception>
    public Machine(JsonNode program, JsonNode configuration, JsonNode? fixture = null)
    {
        config = configuration;
        storage = new Storage(program);
        storage.Initialize(Eval);
        if (fixture != null)
        {
            foreach (var property in fixture.AsObject())
            {
                if (property.Key != "fields" && property.Value is JsonNode value && (value is not JsonArray a || a.Count != 0) && (value is not JsonObject o || o.Count != 0))
                {
                    throw new NotSupportedException("Unsupported fixture property: " + property.Key);
                }
            }

            if (fixture["fields"] is JsonObject fields)
            {
                foreach (var (name, value) in fields)
                {
                    var candidates = storage.Symbols.Values.Where(s => s.S("qualifiedName") == name || s.S("name") == name).ToArray();
                    if (candidates.Length != 1)
                    {
                        throw new InvalidOperationException("Unknown or ambiguous fixture field " + name);
                    }

                    var symbol = candidates[0];
                    if (storage.Dimensions(symbol).Length != 0)
                    {
                        throw new NotSupportedException("Table fixture initialization");
                    }

                    storage.Set(new JsonObject { ["symbolId"] = symbol.S("id") }, value!.GetValue<string>(), Eval, sizeError: true);
                }
            }
        }
    }

    /// <summary>
    /// 平坦化した命令列を実行し、表示内容・終了理由・最後の項目値を返します。
    /// PERFORM の戻り先は C# の再帰呼び出しとは別に管理し、実行回数と時間にも上限を設けます。
    /// </summary>
    /// <param name="flow">生成器が COBOL の入れ子構造から作成した、実行順序を持つ命令列。</param>
    /// <param name="program">エラー時の PROGRAM-ID と原文位置を説明するための IR。</param>
    /// <returns>display、fields、signal を含む、期待値と比較可能な実行結果。</returns>
    /// <exception cref="InvalidOperationException">実行制限を超えた場合や、命令・式の評価に失敗した場合。可能な範囲で元位置を付加します。</exception>
    public JsonObject Run(Instruction[] flow, JsonNode program)
    {
        var timer = Stopwatch.StartNew();
        var steps = 0;
        var pc = 0;
        var signal = "fallthrough";
        var returns = new Stack<(int End, int Return)>();
        try
        {
            while (pc >= 0 && pc < flow.Length)
            {
                if (++steps > config.I("maxSteps", 1000000) || timer.ElapsedMilliseconds > config.I("timeoutMs", 10000))
                {
                    throw new InvalidOperationException("Execution limit exceeded");
                }

                var instruction = flow[pc];
                var s = instruction.Node;
                var next = pc + 1;
                switch (instruction.Op)
                {
                    case "branch":
                        next = Truth(Eval(s!)) ? instruction.Next : instruction.Alternate;
                        break;
                    case "sizeBranch":
                        next = sizeError ? instruction.Next : instruction.Alternate;
                        break;
                    case "jump":
                    case "goto":
                        next = instruction.Next;
                        break;
                    case "gotoDepending":
                        var index = Integer(Eval(s!["depending"]!));
                        var targets = s.A("targetPcs").ToArray();
                        if (index > 0 && index <= targets.Length)
                        {
                            next = targets[index - 1].GetValue<int>();
                        }

                        break;
                    case "perform":
                        if (returns.Count > 10000)
                        {
                            throw new InvalidOperationException("PERFORM nesting limit");
                        }

                        returns.Push((instruction.Alternate, pc + 1));
                        next = instruction.Next;
                        break;
                    case "boundary":
                        if (returns.TryPeek(out var frame) && frame.End == pc)
                        {
                            returns.Pop();
                            next = frame.Return;
                        }

                        break;
                    case "loopInit":
                        loopFirst.Add(s!.S("id"));
                        if (s!["times"] is JsonNode times)
                        {
                            loopRemaining[s.S("id")] = Math.Max(0, Integer(Eval(times)));
                        }

                        if (s["varying"] is JsonNode v)
                        {
                            storage.Set(v["reference"]!, Eval(v["from"]!), Eval);
                        }

                        break;
                    case "loopCheck":
                        var first = loopFirst.Remove(s!.S("id"));
                        var stop = s!["times"] != null ? loopRemaining[s.S("id")] <= 0 : s["until"] != null && !(first && s.B("testAfter")) && Truth(Eval(s["until"]!));
                        next = stop ? instruction.Alternate : instruction.Next;
                        break;
                    case "loopNext":
                        if (s!["times"] != null)
                        {
                            loopRemaining[s.S("id")]--;
                        }

                        if (s["varying"] is JsonNode varying)
                        {
                            storage.Set(varying["reference"]!, Numeric(Eval(varying["reference"]!)) + Numeric(Eval(varying["by"]!)), Eval);
                        }

                        break;
                    case "halt":
                        signal = s!.S("signal");
                        pc = flow.Length;
                        continue;
                    case "end":
                        pc = flow.Length;
                        continue;
                    case "basic":
                        Execute(s!);
                        break;
                    default:
                        throw new NotSupportedException("Instruction " + instruction.Op);
                }

                pc = next;
            }
        }
        catch (Exception e)
        {
            throw new InvalidOperationException($"{program.S("id")} at instruction {pc}: {flow[Math.Min(pc, flow.Length - 1)].Node?["source"]?.ToJsonString()}: {e.Message}", e);
        }

        return new JsonObject
        {
            ["display"] = new JsonArray(display.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
            ["fields"] = storage.Snapshot(),
            ["signal"] = signal,
        };
    }

    /// <summary>
    /// 分岐や段落呼び出し以外の、一つの COBOL 文が項目値や表示内容へ与える効果を実行します。
    /// 未対応文を空処理にしないため、実装のない種類は明示的な失敗として扱います。
    /// </summary>
    /// <param name="s">MOVE や DISPLAY などの解析済みの文。演算対象は解決済みのシンボル ID を使います。</param>
    private void Execute(JsonNode s)
    {
        switch (s.S("kind"))
        {
            case "move":
                var value = Eval(s["value"]!);
                foreach (var target in s.A("targets"))
                {
                    storage.Set(target, value, Eval);
                }

                break;
            case "display":
                display.Add(string.Concat(s.A("values").Select(e => e.S("kind") == "reference" ? storage.Display(e, Eval) : Text(Eval(e)))));
                break;
            case "set88":
                var condition = storage.Symbols[s.S("symbolId")];
                storage.Set(new JsonObject { ["symbolId"] = condition.S("parentId"), ["subscripts"] = s["subscripts"]!.DeepClone() }, Eval(condition.A("conditions").First()["low"]!), Eval);
                break;
            case "setIndex":
                var operand = Eval(s["value"]!);
                foreach (var target in s.A("targets"))
                {
                    var updatedValue = s.S("operation") switch
                    {
                        "up" => Numeric(Eval(target)) + Numeric(operand),
                        "down" => Numeric(Eval(target)) - Numeric(operand),
                        _ => operand,
                    };
                    storage.Set(target, updatedValue, Eval);
                }

                break;
            case "arithmetic":
                Arithmetic(s);
                break;
            default:
                throw new NotSupportedException("Unsupported COBOL statement: " + s.S("kind") + ": " + s["source"]?.S("raw"));
        }
    }

    /// <summary>
    /// 算術文の計算と格納を区別し、計算材料を変更する前に商・余りなどを求めます。
    /// 桁あふれの対象を勝手に書き換えず、ON SIZE ERROR の分岐が必要かを実行順序の処理へ伝えます。
    /// </summary>
    /// <param name="s">計算式、受け取り項目、丸め、桁あふれ時の処理を含む算術文。</param>
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
                var reference = target["reference"]!;
                var result = value;
                if (s.B("update"))
                {
                    result = s.S("verb") switch
                    {
                        "ADD" => Numeric(Eval(reference)) + value,
                        "SUBTRACT" => Numeric(Eval(reference)) - value,
                        "MULTIPLY" => Numeric(Eval(reference)) * value,
                        "DIVIDE" => Numeric(Eval(reference)) / value,
                        _ => throw new NotSupportedException("Arithmetic update"),
                    };
                }

                try
                {
                    prepared.Add(storage.Prepare(reference, result, Eval, target.B("rounded"), s.B("hasSizeError")));
                    var scale = storage.Symbols[reference.S("symbolId")]["pic"]!.I("scale");
                    quotient = new Number(result.Units(scale), System.Numerics.BigInteger.Pow(10, scale));
                }
                catch (SizeError)
                {
                    sizeError = true;
                }
            }

            if (s["remainder"] is JsonNode remainder && quotient is Number q)
            {
                var expression = s["value"]!;
                if (expression.S("kind") != "binary" || expression.S("op") != "/")
                {
                    throw new NotSupportedException("Remainder form");
                }

                prepared.Add(storage.Prepare(remainder, Numeric(Eval(expression["left"]!)) - (q * Numeric(Eval(expression["right"]!))), Eval, sizeError: s.B("hasSizeError")));
            }

            foreach (var commit in prepared)
            {
                commit();
            }
        }
        catch (DivideByZeroException) when (s.B("hasSizeError"))
        {
            sizeError = true;
        }
        catch (SizeError) when (s.B("hasSizeError"))
        {
            sizeError = true;
        }
    }
}
