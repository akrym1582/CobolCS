using System.Diagnostics;

namespace CobolCS.Cli;

/// <summary>
/// dotnet のビルド・実行を、CLI とは別のプロセスで呼び出す入口です。
/// 標準出力とエラー出力を分けて回収し、停止しない子プロセスを時間制限で終了させます。
/// </summary>
public static class ProcessRunner
{
    /// <summary>
    /// 外部コマンドを実行し、成功時の標準出力を呼び出し元へ返します。
    /// シェルで文字列を実行せず引数を個別に渡すため、空白を含むファイル名もコマンドとして誤解されません。
    /// </summary>
    /// <param name="command">PATH から探す実行ファイル名、またはそのパス。</param>
    /// <param name="arguments">順番を保持して渡す引数。シェルの引用符は付けません。</param>
    /// <param name="directory">子プロセスの作業ディレクトリ。省略時は CLI と同じ場所です。</param>
    /// <param name="timeoutSeconds">完了を待つ秒数の上限。超過時には子の子も含めて終了させます。</param>
    /// <returns>正常終了したプロセスの標準出力。JSON の解析やビルド結果の説明に使います。</returns>
    /// <exception cref="TimeoutException">指定した時間までに終了しない場合。</exception>
    /// <exception cref="InvalidOperationException">起動できない場合、または終了コードが 0 ではない場合。</exception>
    public static async Task<string> Run(string command, IEnumerable<string> arguments, string? directory = null, int timeoutSeconds = 120)
    {
        var start = new ProcessStartInfo(command)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = directory ?? Environment.CurrentDirectory,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Unable to start " + command);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException(command + " exceeded its time limit");
        }

        var output = await stdout;
        var errors = await stderr;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{command} exited {process.ExitCode}:\n{errors}\n{output}");
        }

        return output;
    }
}
