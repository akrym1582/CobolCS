using System.IO.Enumeration;
using System.Text;
using System.Text.Json.Nodes;
using CobolCS.Frontend.Parsing;
using static CobolCS.Frontend.Parsing.Values;

namespace CobolCS.Frontend;

/// <summary>COBOLの位置付き解析とIR構築を.NET内で実行します。外部プロセスは使用しません。</summary>
public static class FrontendAnalyzer
{
    /// <summary>C#実装のバージョン。</summary>
    public const string Version = "0.3.0-csharp";

    /// <summary>入力を列挙し、前処理、宣言、手続き、依存情報を解析します。</summary>
    /// <param name="input">COBOLファイルまたはディレクトリ。</param>
    /// <param name="configuration">検証済み設定。省略時はportable-test。</param>
    /// <param name="copybookPaths">探索順を保持したCOPYディレクトリ。</param>
    /// <param name="allowMissingCopy">欠落COPYをレビュー対象の未知宣言として保持するかどうか。</param>
    /// <param name="syntaxRecovery">復旧規則。Standardは従来IRを維持し、Tolerantは未知範囲を保持して解析を続けます。</param>
    /// <returns>schemaVersion 2.2の完全IR。エラー診断も返却値に保持します。</returns>
    public static JsonObject Analyze(string input, FrontendConfiguration? configuration = null, IEnumerable<string>? copybookPaths = null, bool allowMissingCopy = false, SyntaxRecoveryPolicy syntaxRecovery = SyntaxRecoveryPolicy.Standard)
    {
        if (!Enum.IsDefined(syntaxRecovery))
        {
            throw new ArgumentOutOfRangeException(nameof(syntaxRecovery));
        }

        var config = FromJson((configuration ?? FrontendConfiguration.Parse()).ToJson());
        var path = RealPath(input);
        var directory = Directory.Exists(path) ? path : System.IO.Path.GetDirectoryName(path)!;
        var front = new SourceFrontend(config, ListOf((copybookPaths ?? []).Select(p => PathValue(RealPath(p)))), PathValue(directory), B(allowMissingCopy));
        string[] extensions = [".cbl", ".cob", ".cobol", ".sqb"];
        var files = Directory.Exists(path) ? EnumerateFiles(path).Where(p => extensions.Contains(System.IO.Path.GetExtension(p).ToLowerInvariant())).OrderBy(p => S(p), Comparer<V>.Create(Compare)).ToArray() : [path];
        if (files.Length == 0)
        {
            throw new ArgumentException("No CBL inputs");
        }

        var programs = ListOf();
        V tokens = Nil;
        foreach (var file in files)
        {
            tokens = front.M_load(PathValue(file));
            if (!Truth(tokens))
            {
                var name = System.IO.Path.GetRelativePath(directory, file).Replace('\\', '/');
                if (!Items(Member(front, "diagnostics")).Any(d => Text(d[S("code")]) == "INPUT_DECODE" && Text(d[S("source")][S("file")]) == name))
                {
                    var position = new SourcePosition(1, 1, 0).ToJson();
                    front.M_diagnostic(S("EMPTY_SOURCE"), S("No COBOL tokens in input"), FromJson(new JsonObject { ["file"] = name, ["start"] = position, ["end"] = position.DeepClone(), ["copyStack"] = new JsonArray(), ["raw"] = string.Empty }));
                }

                continue;
            }

            var parser = new Parser(front, tokens, syntaxRecovery);
            try
            {
                Call(programs, "append", parser.M_parse());
            }
            catch (ParseException error)
            {
                front.M_diagnostic(S("PARSE_ERROR"), S(error.Message), Member(tokens[I(Math.Min(Number(Member(parser, "i")), Number(Length(tokens)) - 1))], "source"));
                foreach (var token in Items(tokens))
                {
                    Call(Member(front, "rejected"), "append", FromToken(token));
                }
            }
        }

        if (Text(config[S("profile")]) == "fujitsu-review")
        {
            front.M_diagnostic(S("FUJITSU_UNVERIFIED"), S("Host product, options, encoding and baseline are unverified"), Truth(tokens) ? Member(tokens[I(0)], "source") : Nil, S("review"));
        }

        var finalConfig = config.ToJson()!;
        var result = new JsonObject
        {
            ["schemaVersion"] = "2.2",
            ["frontendVersion"] = Version,
            ["upstreamCommit"] = "2b53472b9d884760ff09be2753472442b099d85a",
            ["configHash"] = FrontendConfiguration.Parse(finalConfig.ToJsonString()).Hash,
            ["config"] = finalConfig,
            ["sources"] = Call(Member(front, "sources"), "values").ToJson(),
            ["rejectedTokens"] = Member(front, "rejected").ToJson(),
            ["programs"] = programs.ToJson(),
            ["diagnostics"] = Member(front, "diagnostics").ToJson(),
            ["excluded"] = Member(front, "excluded").ToJson(),
        };
        if (syntaxRecovery == SyntaxRecoveryPolicy.Tolerant)
        {
            result["syntaxRecovery"] = "tolerant";
        }

        return result;
    }

    private static IEnumerable<string> EnumerateFiles(string path)
    {
        // Match pathlib.rglob: include file links, but do not traverse directory links.
        return new FileSystemEnumerable<string>(path, (ref FileSystemEntry entry) => entry.ToFullPath(), new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = 0,
            IgnoreInaccessible = true,
        })
        {
            ShouldRecursePredicate = (ref FileSystemEntry entry) => (entry.Attributes & FileAttributes.ReparsePoint) == 0,
            ShouldIncludePredicate = (ref FileSystemEntry entry) => !entry.IsDirectory && File.Exists(entry.ToFullPath()),
        };
    }

    private static V FromToken(V token) => Put(Put(Put(Put(Dict(), S("text"), Member(token, "text")), S("ownerKind"), S("unknown")), S("ownerId"), S("$parseRejected")), S("source"), Member(token, "source"));
}
