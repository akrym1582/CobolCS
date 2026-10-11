using System.Diagnostics;
using System.Text;

namespace CobolCS.Frontend.Tests;

/// <summary>テスト専用の参照プロセスです。製品からは参照しません。</summary>
internal static class ReferenceProcess
{
    /// <summary>引数配列と UTF-8 を使用し、終了コードと両出力を保持します。</summary>
    /// <param name="command">実行ファイル。</param>
    /// <param name="arguments">個別の引数。</param>
    /// <param name="directory">作業ディレクトリ。</param>
    /// <param name="timeout">制限時間。</param>
    /// <returns>終了コード、標準出力、標準エラー。</returns>
    internal static async Task<(int ExitCode, string Output, string Error)> Run(string command, IEnumerable<string> arguments, string directory, TimeSpan timeout)
    {
        var info = new ProcessStartInfo(command)
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        info.Environment["PYTHONUTF8"] = "1";
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("Unable to start " + command);
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync();
            await Task.WhenAll(output, error);
            throw new TimeoutException(command + " exceeded " + timeout);
        }

        return (process.ExitCode, await output, await error);
    }
}
