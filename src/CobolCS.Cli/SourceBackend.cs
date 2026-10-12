using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>解析時だけ IR を使い、通常の C# 本文・状態・スタブを生成します。</summary>
internal sealed partial class SourceBackend
{
    private readonly JsonNode program;
    private readonly JsonNode config;
    private readonly Dictionary<string, JsonNode> symbols;
    private readonly SortedDictionary<string, string> files;
    private readonly JsonArray stubs;
    private readonly JsonArray sourceMap;
    private readonly Dictionary<string, List<string>> contracts = new()
    {
        ["Calls"] = [],
        ["Files"] = [],
        ["Sql"] = [],
        ["Unsupported"] = [],
    };

    private readonly HashSet<string> emittedStubs = [];
    private readonly string name;
    private readonly Dictionary<JsonNode, JsonNode[]> ancestorsBySymbol = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<JsonNode, string> membersBySymbol = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<JsonNode, int[]> dimensionsBySymbol = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<JsonNode, string> scalarAccessBySymbol = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<JsonNode, bool> knownBySymbol = new(ReferenceEqualityComparer.Instance);
    private JsonNode? currentStatement;
    private int temporary;
    private int loopDepth;

    /// <summary>Initializes a new instance of the <see cref="SourceBackend"/> class. 生成単位のシンボルと成果物を関連付けます。</summary>
    /// <param name="program">一つの解析済みプログラム。</param>
    /// <param name="config">実行制限。</param>
    /// <param name="files">生成先ファイルの内容。</param>
    /// <param name="stubs">未解決箇所レポート。</param>
    /// <param name="sourceMap">元名と生成名の対応。</param>
    internal SourceBackend(JsonNode program, JsonNode config, SortedDictionary<string, string> files, JsonArray stubs, JsonArray sourceMap)
    {
        this.program = program;
        this.config = config;
        this.files = files;
        this.stubs = stubs;
        this.sourceMap = sourceMap;
        symbols = program.A("symbols").ToDictionary(s => s.S("id"));
        name = Identifier(program.S("id"));
    }

    /// <summary>文字を可逆なコードポイントへ変換し、キーワードと大小文字の衝突も避けます。</summary>
    /// <param name="value">COBOL 名。</param>
    /// <returns>決定的な C# 識別子。</returns>
    internal static string Identifier(string value) => "C" + string.Concat(value.Select(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' ? c.ToString() : "_" + ((int)c).ToString("X4"))) + "_" + Generator.Hash(Encoding.UTF8.GetBytes(value))[..8];

    /// <summary>一つのプログラムの直接生成を実行します。</summary>
    internal void Generate()
    {
        Models();
        var methods = StateMethods();
        var body = ProgramBody();
        var result = new StringBuilder(Header());
        result.AppendLine($"/// <summary>{SecurityElement.Escape(program.S("id"))} の業務処理。期待値との比較には fixture を使います。</summary>\ninternal sealed class {name}Program\n{{");
        result.AppendLine($"    private readonly {name}State state = new();\n    private readonly I{name}Dependencies dependencies;\n    private readonly List<string> display = [];\n    private readonly Queue<string> input = [];\n    private readonly List<DependencyTrace> dependencyTrace = [];\n    private readonly System.Diagnostics.Stopwatch timer = new();\n    private int steps;\n    private string location = {Quote(program.S("id"))};\n    private SourceLocation? currentSource;\n    private string signal = \"fallthrough\";");
        result.AppendLine($"    private readonly BranchRecorder branches;\n    private readonly ExecutionOptions options;\n    private readonly CancellationToken cancellationToken;\n    internal {name}Program(I{name}Dependencies dependencies, ExecutionOptions options, CancellationToken cancellationToken)\n    {{\n        this.dependencies = dependencies;\n        this.options = options;\n        branches = new(options);\n        this.cancellationToken = cancellationToken;\n    }}");
        result.AppendLine("private BranchSnapshot TraceSnapshot()\n{\nvar trace = branches.Snapshot();\nreturn new BranchSnapshot(trace.Events, trace.Truncated || (dependencies as IInternalStubTrace)?.InternalTraceTruncated == true);\n}");
        result.AppendLine($"    private void Tick()\n    {{\n        cancellationToken.ThrowIfCancellationRequested();\n        if (++steps > (options.MaxSteps ?? {config.I("maxSteps", 1000000)})) throw new StepLimitException(location);\n        if (timer.ElapsedMilliseconds > (options.TimeoutMs ?? {config.I("timeoutMs", 10000)})) throw new ExecutionTimeoutException(location);\n    }}");
        result.AppendLine($"    internal {name}Result Run({name}Input value)\n    {{\n        timer.Restart();\n        try\n        {{\n            Initialize();\n            ApplyInput(value);\n            Execute();\n            branches.FinishOpenLoops();\n            cancellationToken.ThrowIfCancellationRequested();\n            if (dependencies is IDependencyCompletion completion) completion.VerifyComplete();\n        }}\n        catch (Exception error)\n        {{\n            error.Data[\"InternalTrace\"] = (dependencies as IInternalStubTrace)?.InternalTrace;\n            error.Data[\"BranchTrace\"] = TraceSnapshot();\n            error.Data[\"DependencyTrace\"] = Array.AsReadOnly(dependencyTrace.ToArray());\n            error.Data[\"SourceLocation\"] = currentSource;\n            if (error is OperationCanceledException or StepLimitException or ExecutionTimeoutException) throw;\n            var wrapped = new ExecutionException({Quote(program.S("id"))}, location, error, dependencyTrace, currentSource);\n            wrapped.Data[\"InternalTrace\"] = (dependencies as IInternalStubTrace)?.InternalTrace;\n            wrapped.Data[\"BranchTrace\"] = TraceSnapshot();\n            wrapped.Data[\"DependencyTrace\"] = Array.AsReadOnly(dependencyTrace.ToArray());\n            wrapped.Data[\"SourceLocation\"] = currentSource;\n            throw wrapped;\n        }}\n        return new(new {name}Snapshot(state), Array.AsReadOnly(display.ToArray()), Array.AsReadOnly(dependencyTrace.ToArray()), signal switch {{ \"goback\" => TerminationKind.Goback, \"stopRun\" => TerminationKind.StopRun, \"exitProgram\" => TerminationKind.ExitProgram, _ => TerminationKind.Fallthrough }}, options.CallStubMode) {{ Arguments = new {name}ArgumentsSnapshot(state), BranchTrace = TraceSnapshot(), InternalTrace = (dependencies as IInternalStubTrace)?.InternalTrace ?? Array.Empty<InternalStubEvent>() }};\n    }}");
        result.Append(methods);
        result.Append(body);
        result.AppendLine("}");
        files[$"Programs/{name}Program.g.cs"] = result.ToString();
        var mappedStatements = sourceMap.OfType<JsonObject>().Where(m => m.S("programId") == program.S("id")).Select(m => m.S("statementId")).ToHashSet(StringComparer.Ordinal);
        foreach (var statement in Validation.Walk(program["paragraphs"]).Where(s => s.S("id").StartsWith('s') && s["source"] != null))
        {
            if (mappedStatements.Add(statement.S("id")))
            {
                var enclosingStub = stubs.OfType<JsonObject>().FirstOrDefault(stub => stub.S("programId") == program.S("id") && Validation.Walk(stub["statement"]).Any(n => n.S("id") == statement.S("id")));
                sourceMap.Add(new JsonObject { ["programId"] = program.S("id"), ["statementId"] = statement.S("id"), ["capability"] = enclosingStub == null ? "native" : "unsupported", ["enclosingStub"] = enclosingStub?.S("statementId"), ["source"] = statement["source"]?.DeepClone(), ["generatedFile"] = $"Programs/{name}Program.g.cs" });
            }
        }

        Contracts();
        Api();
        Readiness();
        BusinessCandidates();
        BranchPreparation();
    }

    /// <summary>文字列を C# のリテラルとして表します。</summary>
    /// <param name="value">原文。</param>
    /// <returns>引用済みの文字列。</returns>
    private static string Quote(string value) => JsonSerializer.Serialize(value);

    /// <summary>真偽値を C# で表します。</summary>
    /// <param name="value">真偽値。</param>
    /// <returns>小文字のキーワード。</returns>
    private static string Bool(bool value) => value ? "true" : "false";

    /// <summary>生成ソースの共通ヘッダです。</summary>
    /// <returns>IR を実行時に参照しない using と名前空間。</returns>
    private static string Header() => "// <auto-generated/>\n#nullable enable\nusing System.Text.Json.Nodes;\nusing CobolCS.Runtime;\n\nnamespace CobolCS.Generated;\n\n";

    /// <summary>シンボルのプロパティ名です。</summary>
    /// <param name="symbol">宣言。</param>
    /// <returns>同名宣言とも区別できる名前。</returns>
    private string Member(JsonNode symbol)
    {
        if (!membersBySymbol.TryGetValue(symbol, out var member))
        {
            member = Identifier(symbol.S("name")) + "_" + symbol.S("id");
            membersBySymbol.Add(symbol, member);
        }

        return member;
    }

    /// <summary>祖先から対象までの宣言列を返します。</summary>
    /// <param name="symbol">対象宣言。</param>
    /// <returns>外側から内側への宣言。</returns>
    private IEnumerable<JsonNode> Ancestors(JsonNode symbol)
    {
        if (!ancestorsBySymbol.TryGetValue(symbol, out var ancestors))
        {
            ancestors = symbols.TryGetValue(symbol.S("parentId"), out var parent) ? [.. Ancestors(parent), symbol] : [symbol];
            ancestorsBySymbol.Add(symbol, ancestors);
        }

        return ancestors;
    }

    /// <summary>固定表の次元を求めます。</summary>
    /// <param name="symbol">対象宣言。</param>
    /// <returns>外側から内側の要素数。</returns>
    private int[] Dimensions(JsonNode symbol)
    {
        if (!dimensionsBySymbol.TryGetValue(symbol, out var dimensions))
        {
            dimensions = Ancestors(symbol).Where(s => s.I("occurs", 1) > 1 || s["occursClause"] != null).Select(s => s.I("occurs", 1)).ToArray();
            dimensionsBySymbol.Add(symbol, dimensions);
        }

        return dimensions;
    }

    /// <summary>未対応格納属性を継承する項目を検出します。</summary>
    /// <param name="symbol">対象宣言。</param>
    /// <returns>通常の値格納が可能かどうか。</returns>
    private bool Known(JsonNode symbol)
    {
        if (!knownBySymbol.TryGetValue(symbol, out var known))
        {
            known = Ancestors(symbol).All(s => !s.B("stub") && s["redefines"] == null && s["dependingOn"] == null && s["occursClause"]?["dependingOn"] == null && !s.B("synchronized") && !s.B("blankWhenZero") && s.S("usage") is not ("COMP-1" or "COMP-2") && (s["signClause"] == null || (s["signClause"]!.S("position") == "leading" && s["signClause"]!.B("separate")))) && symbol["pic"] is JsonNode pic && pic.S("category") is "numeric" or "text" or "alphabetic" or "national" && pic.I("digits") is > 0 and <= 1000 && pic.I("scale") is >= 0 and <= 1000 && ArraySafe(symbol);
            knownBySymbol.Add(symbol, known);
        }

        return known;
    }

    /// <summary>未知の大きな表をモデルの構築時に確保しないための検査です。</summary>
    /// <param name="symbol">対象の宣言。</param>
    /// <returns>現行の基本項目ごとの上限内であれば true。</returns>
    private bool ArraySafe(JsonNode symbol) => Dimensions(symbol).Aggregate(1L, (a, b) => Math.Min(1000001, a * b)) <= 1000000;

    /// <summary>モデルの型を決めます。</summary>
    /// <param name="symbol">宣言。</param>
    /// <returns>未確定型を含む明示的な C# 型。</returns>
    private string TypeName(JsonNode symbol) => Known(symbol) ? symbol["pic"]!.S("category") == "numeric" ? "Number" : "string" : "UnknownCobolValue";

    /// <summary>状態のプロパティへ、宣言階層と添字を使ってアクセスします。</summary>
    /// <param name="symbol">宣言。</param>
    /// <param name="indices">検査済みの 0 始まり添字。</param>
    /// <returns>C# アクセス式。</returns>
    private string Access(JsonNode symbol, string[] indices)
    {
        if (indices.Length == 0 && scalarAccessBySymbol.TryGetValue(symbol, out var cached))
        {
            return cached;
        }

        var result = new StringBuilder("state");
        var index = 0;
        foreach (var part in Ancestors(symbol))
        {
            result.Append('.').Append(Member(part));
            if (part.I("occurs", 1) > 1 || part["occursClause"] != null)
            {
                result.Append('[').Append(indices[index++]).Append(']');
            }
        }

        var access = result.ToString();
        if (indices.Length == 0)
        {
            scalarAccessBySymbol.Add(symbol, access);
        }

        return access;
    }

    /// <summary>格納前の PIC 変換を生成します。</summary>
    /// <param name="symbol">受信宣言。</param>
    /// <param name="value">送信式。</param>
    /// <param name="rounded">丸め指定。</param>
    /// <param name="sizeError">桁あふれ通知。</param>
    /// <returns>型付き変換式。</returns>
    private string ConvertValue(JsonNode symbol, string value, bool rounded = false, bool sizeError = false)
    {
        var pic = symbol["pic"]!;
        return pic.S("category") == "numeric" ? $"SourceSupport.NumberValue({value}, {pic.I("digits")}, {pic.I("scale")}, {Bool(pic.B("signed"))}, {Bool(rounded)}, {Bool(sizeError)})" : $"SourceSupport.TextValue({value}, {pic.I("digits")}, {Bool(pic.S("category") == "national")}, {Bool(symbol.B("justified"))})";
    }

    /// <summary>全表要素の列挙式を作ります。</summary>
    /// <param name="symbol">宣言。</param>
    /// <param name="value">各要素を変換する関数。</param>
    /// <returns>LINQ の列挙式。</returns>
    private string Elements(JsonNode symbol, Func<string, string> value)
    {
        var dims = Dimensions(symbol);
        var indices = dims.Select((_, i) => "i" + i).ToArray();
        return dims.Length == 0 ? value(Access(symbol, [])) : "(" + string.Join(" ", dims.Select((d, i) => $"from i{i} in Enumerable.Range(0, {d})")) + " select " + value(Access(symbol, indices)) + ")";
    }

    /// <summary>型付きモデルを生成します。</summary>
    private void Models()
    {
        var childrenByParent = symbols.Values.Where(s => s.I("level") != 88).ToLookup(s => s.S("parentId"), StringComparer.Ordinal);
        var result = new StringBuilder(Header());
        void Group(string className, string parentId)
        {
            result.AppendLine($"/// <summary>COBOL 宣言の型付き状態です。</summary>\ninternal sealed class {className}\n{{");
            foreach (var symbol in childrenByParent[parentId])
            {
                var group = symbol["pic"] == null && !symbol.B("stub") && ArraySafe(symbol);
                var type = group ? name + "Group" + symbol.S("id") : TypeName(symbol);
                var array = ArraySafe(symbol) && (symbol.I("occurs", 1) > 1 || symbol["occursClause"] != null);
                var initial = group ? $"new {type}()" : type == "Number" ? "new Number(0)" : type == "string" ? ConvertValue(symbol, "string.Empty") : $"new UnknownCobolValue({Quote(symbol.S("qualifiedName"))})";
                result.AppendLine($"    /// <summary>{SecurityElement.Escape(symbol.S("qualifiedName"))} の状態。PIC {SecurityElement.Escape(symbol["pic"]?.S("raw") ?? "group/unknown")}、{SecurityElement.Escape(symbol.S("usage"))}。</summary>");
                result.AppendLine($"    internal {type}{(array ? "[]" : string.Empty)} {Member(symbol)} {{ get; {(array || group ? string.Empty : "set; ")}}} = {(array ? $"Enumerable.Range(0, {Math.Max(1, symbol.I("occurs", 1))}).Select(_ => {initial}).ToArray()" : initial)};");
                sourceMap.Add(new JsonObject { ["programId"] = program.S("id"), ["symbolId"] = symbol.S("id"), ["originalName"] = symbol.S("qualifiedName"), ["generatedName"] = className + "." + Member(symbol), ["source"] = symbol["source"]?.DeepClone() });
            }

            if (parentId == string.Empty)
            {
                foreach (var condition in symbols.Values.Where(s => s.I("level") == 88 && Known(symbols[s.S("parentId")]) && Dimensions(symbols[s.S("parentId")]).Length == 0))
                {
                    var expression = new JsonObject { ["kind"] = "condition88", ["symbolId"] = condition.S("id"), ["subscripts"] = new JsonArray() };
                    try
                    {
                        var getter = Expression(expression).Replace("state.", string.Empty);
                        result.AppendLine($"    /// <summary>{SecurityElement.Escape(condition.S("qualifiedName"))} の 88 条件です。</summary>\n    internal bool {Member(condition)} => {getter};");
                    }
                    catch (NotSupportedException)
                    {
                        // The actual condition use is isolated by statement generation.
                    }
                }
            }

            result.AppendLine("}");
        }

        Group(name + "State", string.Empty);
        foreach (var symbol in symbols.Values.Where(s => s["pic"] == null && !s.B("stub") && s.I("level") != 88))
        {
            Group(name + "Group" + symbol.S("id"), symbol.S("id"));
        }

        files[$"Models/{name}State.g.cs"] = result.ToString();
    }

    /// <summary>初期値と fixture とスナップショットを生成します。</summary>
    /// <returns>プログラム内の補助メソッド。</returns>
    private string StateMethods()
    {
        var result = new StringBuilder("    private void Initialize()\n    {\n");
        foreach (var symbol in symbols.Values.Where(s => s.I("level") != 88 && Known(s)))
        {
            var dims = Dimensions(symbol);
            for (var i = 0; i < dims.Length; i++)
            {
                result.AppendLine($"        for (var i{i} = 0; i{i} < {dims[i]}; i{i}++)\n        {{");
            }

            var initialization = new JsonObject { ["kind"] = "initialValue", ["id"] = "initial_" + symbol.S("id"), ["source"] = symbol["source"]?.DeepClone(), ["symbol"] = symbol.DeepClone() };
            currentStatement = initialization;
            string value;
            try
            {
                value = symbol["initial"] is JsonNode initial ? Expression(initial) : symbol["pic"]!.S("category") == "numeric" ? "new Number(0)" : "string.Empty";
            }
            catch (NotSupportedException error)
            {
                var method = "Initialize_" + symbol.S("id");
                AddStub(initialization, method, "Unsupported", TypeName(symbol), [(name + "State", "state"), ("SourceLocation", "source")], error.Message);
                value = $"dependencies.{method}(state, {Metadata(initialization)})";
            }

            currentStatement = null;
            result.AppendLine($"        location = {Quote(Location(initialization))};\n        currentSource = {Metadata(initialization)};");
            result.AppendLine($"        {Access(symbol, dims.Select((_, i) => "i" + i).ToArray())} = {ConvertValue(symbol, value)};");
            result.AppendLine(string.Concat(dims.Select(_ => "        }\n")));
        }

        result.AppendLine("    }");
        result.Append(ApplyInput());
        return result.ToString();
    }
}
