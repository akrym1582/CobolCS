using System.Diagnostics;

namespace CobolCS.Cli;

public static class ProcessRunner
{
    public static async Task<string> Run(string command, IEnumerable<string> arguments, string? directory = null, int timeoutSeconds = 120)
    {
        var start = new ProcessStartInfo(command) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = directory ?? Environment.CurrentDirectory };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Unable to start " + command);
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); throw new TimeoutException(command + " exceeded its time limit"); }
        var output = await stdout; var errors = await stderr;
        if (process.ExitCode != 0) throw new InvalidOperationException($"{command} exited {process.ExitCode}:\n{errors}\n{output}");
        return output;
    }
}
