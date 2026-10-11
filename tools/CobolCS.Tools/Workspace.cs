using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CobolCS.Cli;

namespace CobolCS.Tools;

/// <summary>開発ツールの作業ルート、JSON、ハッシュ、引数配列によるプロセス起動を共用します。</summary>
public static class Workspace
{
    /// <summary>Gets リポジトリルート。配布先から使用する場合はCOBOLCS_REPOSITORYを指定します。</summary>
    public static string Root { get; } = Locate();

    /// <summary>Gets 同じ構成でビルドした製品CLIへの引数。</summary>
    public static string[] Cli => [typeof(Generator).Assembly.Location];

    /// <summary>JSONをUTF-8で読み込みます。</summary>
    /// <param name="path">入力ファイル。</param>
    /// <returns>構造を保持したJSON。</returns>
    public static JsonNode Read(string path) => JsonNode.Parse(File.ReadAllText(path))!;

    /// <summary>JSONを改行付きUTF-8で保存します。</summary>
    /// <param name="path">保存先。</param>
    /// <param name="value">JSON値。</param>
    public static void Write(string path, JsonNode value) => File.WriteAllText(path, value.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");

    /// <summary>生バイトのSHA-256を小文字で返します。</summary>
    /// <param name="bytes">変換せずに検証するバイト。</param>
    /// <returns>16進数ハッシュ。</returns>
    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>ファイルの生バイトを検証します。</summary>
    /// <param name="path">対象ファイル。</param>
    /// <returns>SHA-256。</returns>
    public static string Digest(string path) => Hash(File.ReadAllBytes(path));

    /// <summary>シェルを使わずに実行し、終了・timeoutと両出力を区別して回収します。</summary>
    /// <param name="arguments">実行ファイルと個別の引数。</param>
    /// <param name="timeout">子プロセス群を終了するまでの秒数。</param>
    /// <returns>失敗も保持した測定結果。</returns>
    public static JsonObject Command(IEnumerable<string> arguments, double timeout = 150)
    {
        var args = arguments.ToArray();
        var start = new ProcessStartInfo(args[0])
        {
            WorkingDirectory = Root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in args.Skip(1))
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new IOException("Cannot start " + args[0]);
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
        var timedOut = false;
        try
        {
            process.WaitForExitAsync(cancel.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }

        return new JsonObject { ["exitCode"] = process.ExitCode, ["timedOut"] = timedOut, ["stdout"] = output.GetAwaiter().GetResult(), ["stderr"] = error.GetAwaiter().GetResult() };
    }

    /// <summary>入力パスの全成分を解決し、祖先のリンクによる脱出も検出可能にします。</summary>
    /// <param name="path">検証するパス。</param>
    /// <returns>実体の絶対パス。</returns>
    public static string Resolve(string path)
    {
        var full = Path.GetFullPath(path);
        var current = Path.GetPathRoot(full)!;
        foreach (var part in full[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget is not null)
            {
                current = info.ResolveLinkTarget(true)?.FullName ?? throw new IOException("Unresolved link: " + current);
            }
        }

        return current;
    }

    private static string Locate()
    {
        if (Environment.GetEnvironmentVariable("COBOLCS_REPOSITORY") is { } configured)
        {
            return Path.GetFullPath(configured);
        }

        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "CobolCS.sln")))
                {
                    return directory.FullName;
                }
            }
        }

        return Environment.CurrentDirectory;
    }
}
