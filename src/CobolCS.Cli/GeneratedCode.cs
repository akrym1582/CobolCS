using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>
/// 独立した生成プロジェクトに保存する C# とプロジェクト設定のひな形を作ります。
/// 生成したコードにも文書化と StyleCop 検査を適用できるよう、出力側の書式をまとめて管理します。
/// </summary>
internal static class GeneratedCode
{
    /// <summary>
    /// 実行に必要な IR と、開発時のコード検査を含む独立プロジェクトの設定を作ります。
    /// Analyzer は開発時だけに使い、生成された業務プログラムの実行時依存にはしません。
    /// </summary>
    /// <returns>.NET 10、XML 文書、StyleCop と許可された五つの除外規則を含む csproj の内容。</returns>
    internal static string CreateProject() => """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <OutputType>Exe</OutputType>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
            <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
            <GenerateDocumentationFile>true</GenerateDocumentationFile>
            <NoWarn>$(NoWarn);SA1101;SA1200;SA1309;SA1629;SA1633</NoWarn>
          </PropertyGroup>
          <ItemGroup>
            <EmbeddedResource Include="reports/ir.json" LogicalName="CobolCS.IR" />
            <PackageReference Include="StyleCop.Analyzers" Version="1.2.0-beta.556" PrivateAssets="all" />
          </ItemGroup>
        </Project>
        """ + "\n";

    /// <summary>
    /// 一つの PROGRAM-ID の実行入口と、元位置を残した命令列を C# として組み立てます。
    /// 生成時に対応できなかったプログラムでは、成功したように見える空処理の代わりに実行拒否を出力します。
    /// </summary>
    /// <param name="index">出力ファイル同士を区別する番号。COBOL の名前を C# の識別子として直接使う必要をなくします。</param>
    /// <param name="program">対象 PROGRAM-ID の段落、宣言、元位置を含む IR。</param>
    /// <param name="errors">未対応などのエラー診断がある場合は true。</param>
    /// <returns>独立してビルドするための名前空間と、日本語 XML コメントを持つプログラムのソース。</returns>
    internal static string CreateProgram(int index, JsonNode program, bool errors)
    {
        var programName = SecurityElement.Escape(program.S("id"));
        var source = new StringBuilder($$"""
            using System.Text.Json.Nodes;
            using CobolCS.Runtime;

            namespace CobolCS.Generated;

            /// <summary>
            /// PROGRAM-ID <c>{{programName}}</c> の実行入口です。
            /// COBOL の元位置を命令とともに保持し、実行失敗を原文に対応させて調べられるようにします。
            /// </summary>
            internal static class Program{{index:D4}}
            {
                /// <summary>
                /// VALUE 句と試験入力から状態を用意し、解析済みの命令列を実行します。
                /// 戻り値は表示内容と最後の項目値を含み、期待値との比較に使用できます。
                /// </summary>
                /// <param name="program">この PROGRAM-ID の宣言と元位置を含む IR。</param>
                /// <param name="config">実行回数や時間の制限を含む設定。</param>
                /// <param name="fixture">省略可能な試験入力。指定しない場合は COBOL の初期値を使います。</param>
                /// <returns>表示、項目値、終了理由を持つ JSON の実行結果。</returns>
                public static JsonObject Run(JsonNode program, JsonNode config, JsonNode? fixture)
                {
            """);
        source.AppendLine();
        if (errors)
        {
            source.AppendLine("        throw new NotSupportedException(\"Scaffold requires review. See reports/diagnostics.json.\");");
        }
        else
        {
            source.AppendLine("        Instruction[] flow =");
            source.AppendLine("        [");
            foreach (var instruction in FlowBuilder.Build(program))
            {
                var node = instruction.Node;
                var raw = node?["source"]?.S("raw").Replace('\r', ' ').Replace('\n', ' ');
                if (!string.IsNullOrEmpty(raw))
                {
                    source.AppendLine();
                    source.AppendLine("            // 対応する元 COBOL 文: " + raw);
                }

                var operand = node == null ? "null" : "J.Parse(" + JsonSerializer.Serialize(node.ToJsonString()) + ")";
                source.AppendLine($"            new({JsonSerializer.Serialize(instruction.Op)}, {operand}, {instruction.Next}, {instruction.Alternate}),");
            }

            source.AppendLine("        ];");
            source.AppendLine();
            source.AppendLine("        return new Machine(program, config, fixture).Run(flow, program);");
        }

        source.AppendLine("    }");
        source.AppendLine("}");
        return source.ToString();
    }

    /// <summary>
    /// 利用者が指定した PROGRAM-ID に実行を振り分ける、生成プロジェクトの CLI を作ります。
    /// 対応表を固定したコードにすることで、元リポジトリや Python に問い合わせず実行できます。
    /// </summary>
    /// <param name="ir">生成対象の PROGRAM-ID の一覧を含む解析結果。</param>
    /// <returns>埋め込み IR と任意の fixture を読み、実行結果を標準出力へ返すソース。</returns>
    internal static string CreateRunner(JsonNode ir)
    {
        var runner = new StringBuilder("""
            using System.Text.Json;
            using System.Text.Json.Nodes;
            using CobolCS.Generated;
            using CobolCS.Runtime;

            try
            {
                if (args.Length is < 1 or > 2)
                {
                    throw new ArgumentException("Usage: <PROGRAM-ID> [fixture.json]");
                }

                using var stream = typeof(Program).Assembly.GetManifestResourceStream("CobolCS.IR")!;
                var ir = JsonNode.Parse(stream)!;
                var fixture = args.Length == 2 ? J.Parse(File.ReadAllText(args[1])) : null;
                var programs = ir.A("programs").ToArray();
                JsonObject result = args[0] switch
                {
            """);
        runner.AppendLine();
        var index = 0;
        foreach (var program in ir.A("programs"))
        {
            runner.AppendLine($"        {JsonSerializer.Serialize(program.S("id"))} => Program{index:D4}.Run(programs[{index}], ir[\"config\"]!, fixture),");
            index++;
        }

        runner.AppendLine("""
                    _ => throw new ArgumentException("Unknown PROGRAM-ID " + args[0]),
                };
                Console.WriteLine(result.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
                return 2;
            }
            """);
        return runner.ToString();
    }
}
