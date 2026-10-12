using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>探索プロセスの起動費用を共有し、各試行の時間・出力上限を維持します。</summary>
public static partial class ProcessRunner
{
    /// <summary>生成された専用workerだけを実行し、異常時はプロセス全体を捨てます。</summary>
    internal sealed class ProbeSession
    {
        private const int OutputLimit = 1048576;
        private readonly string dll;
        private readonly string directory;
        private Process? process;
        private Task<string>? stderr;
        private int requests;

        /// <summary>Initializes a new instance of the <see cref="ProbeSession"/> class. workerの所在を固定します。</summary>
        /// <param name="dll">探索専用アセンブリ。</param>
        /// <param name="directory">隔離された作業先。</param>
        internal ProbeSession(string dll, string directory)
        {
            this.dll = dll;
            this.directory = directory;
        }

        /// <summary>Gets the number of actual worker starts. 起動削減を測定するための件数です。</summary>
        /// <value>正常・異常を問わない起動回数。</value>
        internal int Starts { get; private set; }

        /// <summary>一要求だけ送信し、有限のJSON応答を時間内に取得します。</summary>
        /// <param name="id">対象プログラム。</param>
        /// <param name="candidate">固定入力と応答。</param>
        /// <param name="timeoutMs">起動を含む試行の上限。</param>
        /// <returns>完了または途中trace。</returns>
        internal async Task<JsonNode> Trial(string id, JsonNode candidate, int timeoutMs)
        {
            if (timeoutMs <= 0)
            {
                throw new TimeoutException("Overall exploration timeout");
            }

            using var timeout = new CancellationTokenSource(timeoutMs);
            try
            {
                if (process == null || process.HasExited || requests >= 32)
                {
                    await DisposeAsync();
                    var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = directory };
                    start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
                    start.ArgumentList.Add(dll);
                    start.ArgumentList.Add("--worker");
                    process = Process.Start(start) ?? throw new InvalidOperationException("Unable to start branch worker");
                    stderr = ReadBounded(process.StandardError, process, OutputLimit);
                    requests = 0;
                    Starts++;
                }

                requests++;
                var request = new JsonObject { ["programId"] = id, ["candidate"] = candidate.DeepClone() }.ToJsonString();
                await process.StandardInput.WriteLineAsync(request.AsMemory(), timeout.Token);
                await process.StandardInput.FlushAsync(timeout.Token);
                var response = await ReadResponse(process.StandardOutput, timeout.Token);
                if (Encoding.UTF8.GetByteCount(response) > OutputLimit)
                {
                    throw new NotSupportedException("observation-output-limit");
                }

                return J.Parse(response);
            }
            catch (OperationCanceledException)
            {
                await DisposeAsync();
                throw new TimeoutException("Branch worker exceeded its time limit");
            }
            catch (Exception error) when (error is IOException or InvalidOperationException or JsonException or NotSupportedException)
            {
                await DisposeAsync();
                throw new InvalidOperationException("Branch worker failed: " + error.Message, error);
            }
        }

        /// <summary>子の子も含めて終了し、両パイプとプロセスハンドルを回収します。</summary>
        /// <returns>終了待機。</returns>
        internal async ValueTask DisposeAsync()
        {
            var previous = process;
            process = null;
            if (previous == null)
            {
                return;
            }

            try
            {
                try
                {
                    previous.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // 正常なEOFや異常終了と同時でも、残りのパイプは回収します。
                }

                await previous.WaitForExitAsync();
                var discard = new char[4096];
                while (await previous.StandardOutput.ReadAsync(discard.AsMemory()) != 0)
                {
                    // 終了後の残りも蓄積せずに排出します。
                }

                if (stderr != null)
                {
                    try
                    {
                        await stderr;
                    }
                    catch (InvalidOperationException)
                    {
                        // 出力上限による強制終了は、元の試行エラーとして保持します。
                    }
                }
            }
            finally
            {
                previous.Dispose();
                stderr = null;
            }
        }

        /// <summary>改行までの出力を有限のバッファへ回収します。</summary>
        /// <param name="reader">workerの標準出力。</param>
        /// <param name="token">試行の時間上限。</param>
        /// <returns>一行だけの応答。</returns>
        private static async Task<string> ReadResponse(StreamReader reader, CancellationToken token)
        {
            var result = new StringBuilder();
            var buffer = new char[4096];
            while (true)
            {
                var count = await reader.ReadAsync(buffer.AsMemory(), token);
                if (count == 0)
                {
                    throw new InvalidOperationException("Branch worker exited before response");
                }

                var newline = Array.IndexOf(buffer, '\n', 0, count);
                var length = newline < 0 ? count : newline;
                if (result.Length + length > OutputLimit)
                {
                    throw new InvalidOperationException("Process output limit exceeded");
                }

                result.Append(buffer, 0, length);
                if (newline >= 0)
                {
                    if (newline != count - 1)
                    {
                        throw new InvalidOperationException("Unexpected branch worker output");
                    }

                    return result.ToString().TrimEnd('\r');
                }
            }
        }
    }
}
