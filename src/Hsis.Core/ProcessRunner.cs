using System.Diagnostics;

namespace Hsis.Core;

public static class ProcessRunner
{
    public static async Task RunAsync(string executable, IEnumerable<string> arguments, string directory, Action<string> log, CancellationToken cancellationToken = default)
    {
        ProcessStartInfo start = new(executable)
        {
            WorkingDirectory = directory, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = new() { StartInfo = start };
        process.Start();
        async Task ReadAsync(StreamReader reader)
        {
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is string line) log(line);
        }
        try
        {
            await Task.WhenAll(ReadAsync(process.StandardOutput), ReadAsync(process.StandardError), process.WaitForExitAsync(cancellationToken)).ConfigureAwait(false);
            if (process.ExitCode != 0) throw new InvalidOperationException($"{Path.GetFileName(executable)} が終了コード {process.ExitCode} を返しました。ログを確認してください。");
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }
}
