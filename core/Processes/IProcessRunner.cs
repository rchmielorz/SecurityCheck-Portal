using System.Diagnostics;

namespace securitycheck_portal.Core.Processes;

/// <summary>How a launched process ended.</summary>
public enum ProcessOutcome
{
    /// <summary>The process ran to the end; <see cref="ProcessRunResult.ExitCode"/> is set.</summary>
    Exited,

    /// <summary>The time limit passed; the process tree was killed.</summary>
    TimedOut,

    /// <summary>The process could not be started (e.g. the executable does not exist).</summary>
    StartFailed,
}

/// <summary>Outcome of one process run. <c>Stdout</c> and <c>Stderr</c> are cut at the caller's limit.</summary>
public sealed record ProcessRunResult(ProcessOutcome Outcome, int? ExitCode, string Stdout, string Stderr);

/// <summary>Runs a ready <see cref="ProcessStartInfo"/> with a time limit, never waiting on input.</summary>
public interface IProcessRunner
{
    /// <param name="startInfo">Must redirect stdin, stdout and stderr.</param>
    /// <param name="timeout">When it passes, the process tree is killed and <see cref="ProcessOutcome.TimedOut"/> returned.</param>
    /// <param name="maxCapturedChars">Per stream; the rest of the output is read and dropped.</param>
    /// <exception cref="OperationCanceledException">The caller's token was cancelled (the process tree is killed first).</exception>
    Task<ProcessRunResult> RunAsync(
        ProcessStartInfo startInfo, TimeSpan timeout, int maxCapturedChars, CancellationToken cancellationToken);
}
