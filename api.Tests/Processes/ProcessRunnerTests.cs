using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using securitycheck_portal.Core.Processes;

namespace securitycheck_portal.Tests.Processes;

public sealed class ProcessRunnerTests
{
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(30);

    private readonly ProcessRunner _runner = new(NullLogger<ProcessRunner>.Instance);

    [SkippableFact]
    public async Task Exit_code_zero_is_reported_with_the_output()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows shell used");

        var result = await _runner.RunAsync(Cmd("echo hello"), Generous, 1000, CancellationToken.None);

        Assert.Equal(ProcessOutcome.Exited, result.Outcome);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("hello", result.Stdout.Trim());
    }

    [SkippableFact]
    public async Task Non_zero_exit_code_is_reported_with_stderr()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows shell used");

        var result = await _runner.RunAsync(Cmd("echo oops 1>&2 & exit 3"), Generous, 1000, CancellationToken.None);

        Assert.Equal(ProcessOutcome.Exited, result.Outcome);
        Assert.Equal(3, result.ExitCode);
        Assert.Equal("oops", result.Stderr.Trim());
    }

    [Fact]
    public async Task Missing_executable_is_a_start_failure()
    {
        var startInfo = Redirected(Path.Combine(Path.GetTempPath(), "no-such-executable-" + Guid.NewGuid().ToString("N")));

        var result = await _runner.RunAsync(startInfo, Generous, 1000, CancellationToken.None);

        Assert.Equal(ProcessOutcome.StartFailed, result.Outcome);
        Assert.Null(result.ExitCode);
    }

    [SkippableFact]
    public async Task Output_is_truncated_to_the_limit()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows shell used");

        var result = await _runner.RunAsync(Cmd("echo 0123456789"), Generous, 4, CancellationToken.None);

        Assert.Equal(ProcessOutcome.Exited, result.Outcome);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("0123", result.Stdout);
    }

    [SkippableFact]
    public async Task Timeout_kills_the_whole_process_tree()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows shell used");

        // powershell prints the PID of the ping it started, then waits for it: a grandchild of the runner.
        var startInfo = Redirected("powershell.exe");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(
            "$p = Start-Process ping.exe -ArgumentList '-n','120','127.0.0.1' -PassThru -WindowStyle Hidden; " +
            "[Console]::Out.WriteLine($p.Id); [Console]::Out.Flush(); $p.WaitForExit()");

        var result = await _runner.RunAsync(startInfo, TimeSpan.FromSeconds(8), 1000, CancellationToken.None);

        Assert.Equal(ProcessOutcome.TimedOut, result.Outcome);
        var childId = int.Parse(result.Stdout.Trim());
        Assert.True(WaitUntilGone(childId), $"child process {childId} survived the timeout");
    }

    [SkippableFact]
    public async Task Caller_cancellation_throws_and_does_not_look_like_a_timeout()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows shell used");

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _runner.RunAsync(Sleep(60), Generous, 1000, cts.Token));
    }

    private static ProcessStartInfo Cmd(string command)
    {
        var startInfo = Redirected("cmd.exe");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add(command);
        return startInfo;
    }

    private static ProcessStartInfo Sleep(int seconds)
    {
        var startInfo = Redirected("ping.exe");
        startInfo.ArgumentList.Add("-n");
        startInfo.ArgumentList.Add((seconds + 1).ToString());
        startInfo.ArgumentList.Add("127.0.0.1");
        return startInfo;
    }

    private static ProcessStartInfo Redirected(string fileName) => new(fileName)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };

    private static bool WaitUntilGone(int processId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                return true;
            }

            Thread.Sleep(100);
        }

        return false;
    }
}
