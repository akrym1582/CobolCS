using System.IO.Enumeration;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CobolCS.Frontend;
using CobolCS.Frontend.Parsing;
using CobolCS.Tools;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CobolCS.Frontend.Parsing.Values;

namespace CobolCS.IntegrationTests;

/// <summary>移植した回帰条件の値操作を共用し、ファイルとプロセスは.NETで直接扱います。</summary>
internal static class RegressionSupport
{
    /// <summary>Gets 生成テンプレートや合成入力を参照するリポジトリルート。</summary>
    internal static V Root => PathValue(Workspace.Root);

    /// <summary>Gets 同じ構成でビルドした製品CLIの引数配列。</summary>
    internal static V CliArguments => ListOf(new[] { "dotnet" }.Concat(Workspace.Cli).Select(S));

    /// <summary>Gets C#補助ツールの実行引数。</summary>
    internal static V ToolCli => ListOf(S("dotnet"), S(typeof(Corpus).Assembly.Location));

    /// <summary>配列の反復を要素順のまま保持し、数値・文字列は共通演算へ渡します。</summary>
    /// <param name="value">反復または乗算する値。</param>
    /// <param name="count">回数または乗数。</param>
    /// <returns>元の回帰と同じ要素列または積。</returns>
    internal static V TestMultiply(V value, V count) => value.Data is List<V> items ? ListOf(Enumerable.Range(0, Number(count)).SelectMany(_ => items)) : Multiply(value, count);

    /// <summary>構造化値をJSONへ直列化し、設定・fixture・レビュー記録へ渡します。</summary>
    /// <param name="value">検査対象。</param>
    /// <returns>内容を失わない表現。</returns>
    internal static V TestStringValue(V value) => value.Data is Dictionary<V, V> or List<V> or HashSet<V> ? WriteJson(value) : StringValue(value);

    /// <summary>参照共有に依存せず、辞書・集合・配列・バイト列を内容で比較します。</summary>
    /// <param name="actual">実際の値。</param>
    /// <param name="expected">元の回帰の期待値。</param>
    /// <returns>入れ子も含めた一致。</returns>
    internal static bool TestEqual(V actual, V expected) => (actual.Data, expected.Data) switch
    {
        (byte[] a, byte[] b) => a.SequenceEqual(b),
        (Dictionary<V, V> a, Dictionary<V, V> b) => a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var value) && TestEqual(p.Value, value)),
        (HashSet<V> a, HashSet<V> b) => a.Count == b.Count && a.All(item => b.Any(other => TestEqual(item, other))),
        (List<V> a, List<V> b) => a.Count == b.Count && a.Zip(b).All(pair => TestEqual(pair.First, pair.Second)),
        _ => Equal(actual, expected),
    };

    /// <summary>時間制限付きでプロセスを実行し、両出力と終了コードを保持します。</summary>
    /// <param name="args">検証する args。</param>
    /// <param name="timeout">検証する timeout。</param>
    /// <returns>元の回帰条件で比較する値。</returns>
    internal static V RunProcess(V args, V timeout)
    {
        var result = Workspace.Command(Items(args).Select(Text), Number(timeout));
        Assert.IsFalse(result["timedOut"]!.GetValue<bool>(), result.ToJsonString());
        var value = new V();
        value.Fields["stdout"] = S(result["stdout"]!.GetValue<string>());
        value.Fields["stderr"] = S(result["stderr"]!.GetValue<string>());
        value.Fields["returncode"] = I(result["exitCode"]!.GetValue<int>());
        return value;
    }

    /// <summary>引数をシェル展開せずに実行し、期待する終了コードと比較します。</summary>
    /// <param name="args">検証する args。</param>
    /// <param name="code">検証する code。</param>
    /// <returns>元の回帰条件で比較する値。</returns>
    internal static V RunChecked(V args, V code)
    {
        var result = RunProcess(args, I(300));
        Assert.AreEqual(Number(code), Number(result.Fields["returncode"]), string.Join(" ", Items(args).Select(Text)) + "\n" + Text(result.Fields["stdout"]) + "\n" + Text(result.Fields["stderr"]));
        return result;
    }

    /// <summary>JSONの配列・辞書・値を回帰検査の構造化値に変換します。</summary>
    /// <param name="text">検証する text。</param>
    /// <returns>元の回帰条件で比較する値。</returns>
    internal static V ReadJson(V text) => FromJson(JsonNode.Parse(Text(text)));

    /// <summary>構造化値をJSONへ直列化し、設定・fixture・レビュー記録へ渡します。</summary>
    /// <param name="value">検証する value。</param>
    /// <returns>元の回帰条件で比較する値。</returns>
    internal static V WriteJson(V value) => S(value.ToJson()?.ToJsonString() ?? "null");

    /// <summary>キーと値のペアを、元の行列と同じ辞書へ格納します。</summary>
    /// <param name="pairs">検証する pairs。</param>
    /// <returns>元の回帰条件で比較する値。</returns>
    internal static V MakeDictionary(V pairs)
    {
        var result = Dict();
        foreach (var pair in Items(pairs))
        {
            Put(result, pair[I(0)], pair[I(1)]);
        }

        return result;
    }

    /// <summary>パスの成分とプロセス結果を、元の回帰条件へ渡します。</summary>
    /// <param name="value">検証する value。</param>
    /// <param name="name">検証する name。</param>
    /// <returns>元の回帰条件で比較する値。</returns>
    internal static V TestMember(V value, string name) => name == "parts" ? ListOf(Text(value).Split(Path.DirectorySeparatorChar).Select(S)) : Member(value, name);

    /// <summary>バイト列の連結を保持し、その他は文字列・数値・列挙の共通演算へ渡します。</summary>
    /// <param name="a">検証する a。</param>
    /// <param name="b">検証する b。</param>
    /// <returns>元の回帰条件で比較する値。</returns>
    internal static V TestAdd(V a, V b) => a.Data is byte[] first && b.Data is byte[] second ? new V(first.Concat(second).ToArray()) : Add(a, b);

    /// <summary>テストのファイル操作と文字列処理を.NETで直接実行します。</summary>
    /// <param name="receiver">検証する receiver。</param>
    /// <param name="method">検証する method。</param>
    /// <param name="args">検証する args。</param>
    /// <returns>元の回帰条件で比較する値。</returns>
    internal static V TestCall(V receiver, string method, params V[] args)
    {
        if (receiver.Data is Match match)
        {
            var group = args.Length == 0 ? 0 : Number(args[0]);
            return method switch
            {
                "group" => S(match.Groups[group].Value),
                "start" => I(match.Groups[group].Index),
                "end" => I(match.Groups[group].Index + match.Groups[group].Length),
                _ => throw new InvalidOperationException(method),
            };
        }

        var path = Text(receiver);
        switch (method)
        {
            case "removesuffix":
                return S(path.EndsWith(Text(args[0]), StringComparison.Ordinal) && Text(args[0]).Length > 0 ? path[..^Text(args[0]).Length] : path);
            case "removeprefix":
                return S(path.StartsWith(Text(args[0]), StringComparison.Ordinal) ? path[Text(args[0]).Length..] : path);
            case "read_text":
                return S(File.ReadAllText(path));
            case "write_text":
                File.WriteAllText(path, Text(args[0]));
                return Nil;
            case "write_bytes":
                File.WriteAllBytes(path, (byte[])args[0].Data!);
                return Nil;
            case "mkdir":
                Directory.CreateDirectory(path);
                return Nil;
            case "unlink":
                File.Delete(path);
                return Nil;
            case "exists":
                return B(Path.Exists(path));
            case "rename":
                if (Directory.Exists(path))
                {
                    Directory.Move(path, Text(args[0]));
                }
                else
                {
                    File.Move(path, Text(args[0]));
                }

                return Nil;
            case "symlink_to":
                if (Directory.Exists(Text(args[0])))
                {
                    Directory.CreateSymbolicLink(path, Text(args[0]));
                }
                else
                {
                    File.CreateSymbolicLink(path, Text(args[0]));
                }

                return Nil;
            case "glob":
            case "rglob":
                return ListOf(Directory.Exists(path) ? Directory.EnumerateFileSystemEntries(path, "*", method == "rglob" ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly).Where(p => FileSystemName.MatchesSimpleExpression(Text(args[0]), Path.GetFileName(p))).Select(PathValue) : []);
            case "index" when receiver.Data is string:
                var index = path.IndexOf(Text(args[0]), args.Length > 1 ? Number(args[1]) : 0, StringComparison.Ordinal);
                return index < 0 ? throw new ArgumentException("Substring not found") : I(index);
            case "rfind":
                return I(path.LastIndexOf(Text(args[0]), StringComparison.Ordinal));
            case "encode":
                return new V(Encoding.UTF8.GetBytes(path));
            default:
                return Call(receiver, method, args);
        }
    }

    /// <summary>独立実行試験のために生成ディレクトリを複製します。</summary>
    /// <param name="source">検証する source。</param>
    /// <param name="destination">検証する destination。</param>
    /// <returns>元の回帰条件で比較する値。</returns>
    internal static V CopyTree(V source, V destination)
    {
        Directory.CreateDirectory(Text(destination));
        foreach (var directory in Directory.EnumerateDirectories(Text(source), "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(Text(destination), Path.GetRelativePath(Text(source), directory)));
        }

        foreach (var file in Directory.EnumerateFiles(Text(source), "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(Text(destination), Path.GetRelativePath(Text(source), file)));
        }

        return Nil;
    }

    /// <summary>独立実行試験で元の生成先やreportsを取り除きます。</summary>
    /// <param name="directory">検証する directory。</param>
    /// <returns>元の回帰条件で比較する値。</returns>
    internal static V RemoveTree(V directory)
    {
        Directory.Delete(Text(directory), true);
        return Nil;
    }

    /// <summary>生成コード中の抽出対象を.NETの正規表現で検索します。</summary>
    /// <param name="pattern">検証する pattern。</param>
    /// <param name="input">検証する input。</param>
    /// <returns>元の回帰条件で比較する値。</returns>
    internal static V RegexSearch(V pattern, V input)
    {
        var match = Regex.Match(Text(input), Text(pattern));
        return match.Success ? new V(match) : Nil;
    }

    /// <summary>生成コードの置換結果と置換件数を返し、誤った抽出を検出できるようにします。</summary>
    /// <param name="pattern">検証する pattern。</param>
    /// <param name="replacement">検証する replacement。</param>
    /// <param name="input">検証する input。</param>
    /// <returns>元の回帰条件で比較する値。</returns>
    internal static V TestRegexSubn(V pattern, V replacement, V input)
    {
        var expression = new Regex(Text(pattern), RegexOptions.CultureInvariant);
        var converted = Regex.Replace(Text(replacement), @"\\([0-9]+)", "$$$1");
        return ListOf(S(expression.Replace(Text(input), converted)), I(expression.Matches(Text(input)).Count));
    }

    /// <summary>生成コードの置換結果と置換件数を返し、誤った抽出を検出できるようにします。</summary>
    /// <param name="pattern">検証する pattern。</param>
    /// <param name="replacement">検証する replacement。</param>
    /// <param name="input">検証する input。</param>
    /// <returns>元の回帰条件で比較する値。</returns>
    internal static V TestRegexSubn(V pattern, Func<V, V> replacement, V input)
    {
        var count = 0;
        var text = Regex.Replace(Text(input), Text(pattern), _ =>
        {
            count++;
            return Text(replacement(Nil));
        });
        return ListOf(S(text), I(count));
    }

    /// <summary>生成コードの抽出・改変試験へ置換結果を返します。</summary>
    /// <param name="pattern">検証する pattern。</param>
    /// <param name="replacement">検証する replacement。</param>
    /// <param name="input">検証する input。</param>
    /// <returns>元の回帰条件で比較する値。</returns>
    internal static V TestRegexSub(V pattern, V replacement, V input) => TestRegexSubn(pattern, replacement, input)[I(0)];

    /// <summary>入れ子の値とバイト列を内容で比較し、不一致時に両方の値を表示します。</summary>
    /// <param name="actual">検証する actual。</param>
    /// <param name="expected">検証する expected。</param>
    /// <param name="message">検証する message。</param>
    /// <returns>元の回帰条件で比較する値。</returns>
    internal static V CheckEqual(V actual, V expected, V? message = null)
    {
        Assert.IsTrue(TestEqual(actual, expected), Text(message ?? Nil) + "\nExpected: " + Describe(expected) + "\nActual: " + Describe(actual));
        return Nil;
    }

    /// <summary>異なるべき値が同一内容になっていないことを確認します。</summary>
    /// <param name="actual">検証する actual。</param>
    /// <param name="expected">検証する expected。</param>
    /// <returns>元の回帰条件で比較する値。</returns>
    internal static V CheckNotEqual(V actual, V expected)
    {
        Assert.IsFalse(TestEqual(actual, expected));
        return Nil;
    }

    /// <summary>元の回帰が成功条件としている真偽を検査します。</summary>
    /// <param name="value">検証する value。</param>
    /// <param name="message">検証する message。</param>
    /// <returns>元の回帰条件で比較する値。</returns>
    internal static V CheckTrue(V value, V? message = null)
    {
        Assert.IsTrue(Truth(value), Text(message ?? Nil));
        return Nil;
    }

    /// <summary>元の回帰が拒否条件としている真偽を検査します。</summary>
    /// <param name="value">検証する value。</param>
    /// <returns>元の回帰条件で比較する値。</returns>
    internal static V CheckFalse(V value)
    {
        Assert.IsFalse(Truth(value));
        return Nil;
    }

    /// <summary>診断や出力に期待する内容が含まれることを確認します。</summary>
    /// <param name="value">検証する value。</param>
    /// <param name="container">検証する container。</param>
    /// <returns>元の回帰条件で比較する値。</returns>
    internal static V CheckIn(V value, V container)
    {
        Assert.IsTrue(Contains(container, value), Text(value) + " was absent from " + Text(container));
        return Nil;
    }

    /// <summary>出力に禁止した内容が混入していないことを確認します。</summary>
    /// <param name="value">検証する value。</param>
    /// <param name="container">検証する container。</param>
    /// <returns>元の回帰条件で比較する値。</returns>
    internal static V CheckNotIn(V value, V container)
    {
        Assert.IsFalse(Contains(container, value), Text(value) + " was present in " + Text(container));
        return Nil;
    }

    /// <summary>不明な解析値がnullのまま保持されていることを確認します。</summary>
    /// <param name="value">検証する value。</param>
    /// <returns>元の回帰条件で比較する値。</returns>
    internal static V CheckIsNone(V value)
    {
        Assert.AreSame(Nil, value);
        return Nil;
    }

    /// <summary>解析値や抽出位置が見つかったことを確認します。</summary>
    /// <param name="value">検証する value。</param>
    /// <returns>元の回帰条件で比較する値。</returns>
    internal static V CheckIsNotNone(V value)
    {
        Assert.AreNotSame(Nil, value);
        return Nil;
    }

    /// <summary>合成COBOL・COPY・設定を同じ低水準解析APIへ渡し、解析エラーを呼出し元で検査可能にします。</summary>
    /// <param name="source">検証する source。</param>
    /// <param name="scaffold">検証する scaffold。</param>
    /// <param name="fixedFormat">検証する fixedFormat。</param>
    /// <param name="copies">検証する copies。</param>
    /// <param name="p64">検証する p64。</param>
    /// <returns>元の回帰条件で比較する値。</returns>
    internal static V ParseSource(V source, V? scaffold, V? fixedFormat, V? copies, V? p64)
    {
        using var directory = new TestDirectory();
        var input = Path.Combine(directory.Path, "sample.cbl");
        File.WriteAllText(input, Text(source));
        foreach (var pair in Items(Call(copies ?? Dict(), "items")))
        {
            File.WriteAllText(Path.Combine(directory.Path, Text(pair[I(0)])), Text(pair[I(1)]));
        }

        var config = FrontendConfiguration.Parse(new JsonObject { ["sourceFormat"] = Truth(fixedFormat ?? Nil) ? "fixed" : "free", ["p64"] = (p64 ?? Nil).ToJson() }.ToJsonString());
        var frontend = new SourceFrontend(FromJson(config.ToJson()), ListOf(), PathValue(directory.Path), scaffold);
        var program = new Parser(frontend, frontend.M_load(PathValue(input))).M_parse();
        return ListOf(program, frontend);
    }

    private static string Describe(V value)
    {
        try
        {
            return value.Data is byte[] bytes ? Convert.ToHexStringLower(bytes) : value.ToJson()?.ToJsonString() ?? "null";
        }
        catch (InvalidOperationException)
        {
            return Text(value);
        }
    }
}
