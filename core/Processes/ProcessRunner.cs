using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace securitycheck_portal.Core.Processes;

/// <inheritdoc />
public sealed class ProcessRunner(ILogger<ProcessRunner> logger) : IProcessRunner
{
    public async Task<ProcessRunResult> RunAsync(
        ProcessStartInfo startInfo, TimeSpan timeout, int maxCapturedChars, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            logger.LogError(ex, "Could not start process {FileName}", startInfo.FileName);
            return new ProcessRunResult(ProcessOutcome.StartFailed, null, "", "");
        }

        // Nothing is ever written to the child; a closed stdin makes any prompt fail instead of waiting.
        process.StandardInput.Close();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        var token = timeoutCts.Token;

        // Both pipes are drained concurrently, so a full stderr buffer cannot block the child.
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var stdoutTask = DrainAsync(process.StandardOutput, stdout, maxCapturedChars, token);
        var stderrTask = DrainAsync(process.StandardError, stderr, maxCapturedChars, token);

        try
        {
            await process.WaitForExitAsync(token);
            await stdoutTask;
            await stderrTask;

            return new ProcessRunResult(ProcessOutcome.Exited, process.ExitCode, stdout.ToString(), stderr.ToString());
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            // Observe the (cancelled) reads; their results no longer matter.
            await ((Task)Task.WhenAll(stdoutTask, stderrTask)).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            logger.LogWarning("Process {FileName} timed out after {Timeout}", startInfo.FileName, timeout);
            return new ProcessRunResult(ProcessOutcome.TimedOut, null, stdout.ToString(), stderr.ToString());
        }
    }

    // Reads to the end so the child never blocks on a full pipe, but keeps only the first maxChars.
    private static async Task DrainAsync(StreamReader reader, StringBuilder sink, int maxChars, CancellationToken token)
    {
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), token)) > 0)
        {
            var room = maxChars - sink.Length;
            if (room > 0)
            {
                sink.Append(buffer, 0, Math.Min(read, room));
            }
        }
    }

    private void KillTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Already exited between the timeout and the kill.
            logger.LogDebug(ex, "Process could not be killed");
        }
    }
}
