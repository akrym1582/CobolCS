using System.Diagnostics;
using System.Text;

namespace CobolCS.Cli;

/// <summary>
/// dotnet のビルド・実行を、CLI とは別のプロセスで呼び出す入口です。
/// 標準出力とエラー出力を分けて回収し、停止しない子プロセスを時間制限で終了させます。
/// </summary>
public static partial class ProcessRunner
{
    /// <summary>
    /// 外部コマンドを実行し、成功時の標準出力を呼び出し元へ返します。
    /// シェルで文字列を実行せず引数を個別に渡すため、空白を含むファイル名もコマンドとして誤解されません。
    /// </summary>
    /// <param name="command">PATH から探す実行ファイル名、またはそのパス。</param>
    /// <param name="arguments">順番を保持して渡す引数。シェルの引用符は付けません。</param>
    /// <param name="directory">子プロセスの作業ディレクトリ。省略時は CLI と同じ場所です。</param>
    /// <param name="timeoutSeconds">完了を待つ秒数の上限。超過時には子の子も含めて終了させます。</param>
    /// <param name="maxOutputCharacters">各出力の回収上限。超過時はプロセスを回収して拒否します。</param>
    /// <param name="environment">実行ごとの追加環境。親環境を書き換えません。</param>
    /// <returns>正常終了したプロセスの標準出力。JSON の解析やビルド結果の説明に使います。</returns>
    /// <exception cref="TimeoutException">指定した時間までに終了しない場合。</exception>
    /// <exception cref="InvalidOperationException">起動できない場合、または終了コードが 0 ではない場合。</exception>
    public static async Task<string> Run(string command, IEnumerable<string> arguments, string? directory = null, int timeoutSeconds = 120, int maxOutputCharacters = 16777216, IReadOnlyDictionary<string, string>? environment = null)
    {
        if (maxOutputCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxOutputCharacters));
        }

        var start = new ProcessStartInfo(command)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = directory ?? Environment.CurrentDirectory,
        };

        // 再利用ノードが出力パイプを保持すると、restore終了後もEOFを待ち続けるため無効にします。
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        if (environment != null)
        {
            foreach (var pair in environment)
            {
                start.Environment[pair.Key] = pair.Value;
            }
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Unable to start " + command);
        var stdout = ReadBounded(process.StandardOutput, process, maxOutputCharacters);
        var stderr = ReadBounded(process.StandardError, process, maxOutputCharacters);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            var error = new TimeoutException(command + " exceeded its time limit");
            error.Data["StandardOutput"] = await stdout;
            error.Data["StandardError"] = await stderr;
            throw error;
        }

        var streams = await Task.WhenAll(stdout, stderr);
        var output = streams[0];
        var errors = streams[1];
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{command} exited {process.ExitCode}:\n{errors}\n{output}");
        }

        return output;
    }

    /// <summary>無制限な表示や巨大状態で CLI の回収メモリを使い切らないようにします。</summary>
    /// <param name="reader">子の出力。</param>
    /// <param name="process">上限時に回収するプロセス。</param>
    /// <param name="maximum">文字上限。</param>
    /// <returns>有限の出力。</returns>
    private static async Task<string> ReadBounded(StreamReader reader, Process process, int maximum)
    {
        var buffer = new char[4096];
        var result = new StringBuilder();
        var exceeded = false;
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory())) != 0)
        {
            if (result.Length + count <= maximum && !exceeded)
            {
                result.Append(buffer, 0, count);
            }
            else if (!exceeded)
            {
                exceeded = true;
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // 読み取りと同時に終了した子も、出力上限超過として拒否します。
                }
            }
        }

        if (exceeded)
        {
            throw new InvalidOperationException("Process output limit exceeded");
        }

        return result.ToString();
    }
}
