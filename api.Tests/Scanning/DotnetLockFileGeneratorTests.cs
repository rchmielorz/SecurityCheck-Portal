using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using securitycheck_portal.Core.Processes;
using securitycheck_portal.Core.Scanning;

namespace securitycheck_portal.Tests.Scanning;

public sealed class DotnetLockFileGeneratorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lockgen-" + Guid.NewGuid().ToString("N"));
    private readonly FakeDotnet _dotnet = new();
    private readonly AdjustableTime _time = new();
    private readonly ScanOptions _options = new()
    {
        DotnetExecutablePath = "dotnet-under-test",
        CacheDirectory = "cache",
        WorkRoot = "work",
        RestoreTimeoutMinutes = 7,
        RestoreTotalTimeoutMinutes = 20,
    };

    public DotnetLockFileGeneratorTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private DotnetLockFileGenerator Generator => new(
        Options.Create(_options), _dotnet, new LockFileDetector(), _time, NullLogger<DotnetLockFileGenerator>.Instance);

    [Fact]
    public async Task Restore_runs_per_project_without_lock_with_the_expected_arguments()
    {
        Touch("src/App/App.csproj");
        Touch("src/Lib/Lib.csproj");
        Touch("src/Done/Done.csproj");
        Touch("src/Done/packages.lock.json");
        Touch("src/Legacy/Legacy.csproj");
        Touch("src/Legacy/packages.config");

        await Generator.GenerateAsync(_root, CancellationToken.None);

        Assert.Equal(2, _dotnet.Calls.Count);
        var first = _dotnet.Calls[0];
        var projectPath = Path.Combine(_root, "src", "App", "App.csproj");
        Assert.Equal("dotnet-under-test", first.StartInfo.FileName);
        Assert.Equal(["restore", projectPath, "--use-lock-file", "--nologo"], first.StartInfo.ArgumentList);
        Assert.Equal(Path.GetDirectoryName(projectPath), first.StartInfo.WorkingDirectory);
        Assert.Equal(TimeSpan.FromMinutes(7), first.Timeout);
        Assert.EndsWith("Lib.csproj", _dotnet.Calls[1].StartInfo.ArgumentList[1]);
    }

    [Fact]
    public async Task Checkout_without_projects_runs_nothing()
    {
        Touch("web/package.json");

        await Generator.GenerateAsync(_root, CancellationToken.None);

        Assert.Empty(_dotnet.Calls);
    }

    [Fact]
    public void Environment_is_a_whitelist_and_does_not_leak_other_variables()
    {
        const string secretName = "Git__Token";
        var previous = Environment.GetEnvironmentVariable(secretName);
        Environment.SetEnvironmentVariable(secretName, "must-not-leak");
        try
        {
            var startInfo = DotnetLockFileGenerator.CreateStartInfo(_options, Path.Combine(_root, "App.csproj"));

            Assert.DoesNotContain(secretName, startInfo.Environment.Keys);
            Assert.DoesNotContain(secretName, DotnetLockFileGenerator.InheritedEnvironment);
            var allowed = DotnetLockFileGenerator.InheritedEnvironment
                .Concat(["DOTNET_CLI_TELEMETRY_OPTOUT", "DOTNET_NOLOGO", "DOTNET_SKIP_FIRST_TIME_EXPERIENCE"])
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.All(startInfo.Environment.Keys, key => Assert.Contains(key, allowed));
            Assert.Equal("1", startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(secretName, previous);
        }
    }

    [Fact]
    public async Task A_failing_project_does_not_stop_the_next_ones()
    {
        Touch("a/A.csproj");
        Touch("b/B.csproj");
        Touch("c/C.csproj");
        _dotnet.Results.Enqueue(new ProcessRunResult(ProcessOutcome.Exited, 1, "", "error NU1101: Unable to find package"));
        _dotnet.Results.Enqueue(new ProcessRunResult(ProcessOutcome.TimedOut, null, "", ""));

        await Generator.GenerateAsync(_root, CancellationToken.None);

        Assert.Equal(3, _dotnet.Calls.Count);
    }

    [Fact]
    public async Task Dotnet_that_cannot_be_started_does_not_throw()
    {
        Touch("A.csproj");
        _dotnet.Results.Enqueue(new ProcessRunResult(ProcessOutcome.StartFailed, null, "", ""));

        await Generator.GenerateAsync(_root, CancellationToken.None);

        Assert.Single(_dotnet.Calls);
    }

    [Fact]
    public async Task Cancellation_propagates_and_stops_the_remaining_projects()
    {
        Touch("a/A.csproj");
        Touch("b/B.csproj");
        using var cts = new CancellationTokenSource();
        _dotnet.OnRun = _ => cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Generator.GenerateAsync(_root, cts.Token));

        Assert.Single(_dotnet.Calls);
    }

    [Fact]
    public async Task Projects_after_the_total_budget_is_used_up_are_not_restored()
    {
        Touch("a/A.csproj");
        Touch("b/B.csproj");
        Touch("c/C.csproj");
        _options.RestoreTotalTimeoutMinutes = 20;
        _options.RestoreTimeoutMinutes = 10;
        // Each restore "takes" 12 minutes: the first two fit the 20-minute budget (the second is cut to 8), the third does not.
        _dotnet.OnRun = _ => _time.Advance(TimeSpan.FromMinutes(12));

        await Generator.GenerateAsync(_root, CancellationToken.None);

        Assert.Equal(2, _dotnet.Calls.Count);
        Assert.Equal(TimeSpan.FromMinutes(10), _dotnet.Timeouts[0]);
        Assert.Equal(TimeSpan.FromMinutes(8), _dotnet.Timeouts[1]);
    }

    [Theory]
    [InlineData(ProcessOutcome.TimedOut, null)]
    [InlineData(ProcessOutcome.Exited, 1)]
    [InlineData(ProcessOutcome.StartFailed, null)]
    public async Task Partial_lock_file_left_by_a_failed_restore_is_removed(ProcessOutcome outcome, int? exitCode)
    {
        Touch("src/App.csproj");
        var lockPath = Path.Combine(_root, "src", "packages.lock.json");
        _dotnet.OnRun = _ => File.WriteAllText(lockPath, "{ \"version\": 1, \"depend");
        _dotnet.Results.Enqueue(new ProcessRunResult(outcome, exitCode, "", ""));

        await Generator.GenerateAsync(_root, CancellationToken.None);

        Assert.False(File.Exists(lockPath));
    }

    [Fact]
    public async Task Lock_file_of_a_successful_restore_is_kept()
    {
        Touch("src/App.csproj");
        var lockPath = Path.Combine(_root, "src", "packages.lock.json");
        _dotnet.OnRun = _ => File.WriteAllText(lockPath, "{}");

        await Generator.GenerateAsync(_root, CancellationToken.None);

        Assert.True(File.Exists(lockPath));
    }

    [Fact]
    public async Task Successful_restores_yield_no_unscanned_items()
    {
        Touch("a/A.csproj");
        Touch("B.csproj");

        var unscanned = await Generator.GenerateAsync(_root, CancellationToken.None);

        Assert.Empty(unscanned);
    }

    [Fact]
    public async Task Failed_restore_is_reported_with_the_first_line_of_stderr()
    {
        Touch("src/App/App.csproj");
        _dotnet.Results.Enqueue(new ProcessRunResult(
            ProcessOutcome.Exited, 1, "stdout line", "error NU1101: Unable to find package\r\nsecond line"));

        var unscanned = await Generator.GenerateAsync(_root, CancellationToken.None);

        var item = Assert.Single(unscanned);
        Assert.Equal("src/App/packages.lock.json", item.Path);
        Assert.Equal(UnscannedReason.RestoreFailed, item.Reason);
        Assert.Equal("error NU1101: Unable to find package", item.Detail);
    }

    [Fact]
    public async Task Failed_restore_without_stderr_uses_the_first_line_of_stdout()
    {
        Touch("App.csproj");
        _dotnet.Results.Enqueue(new ProcessRunResult(ProcessOutcome.Exited, 1, "build failed\nmore", ""));

        var unscanned = await Generator.GenerateAsync(_root, CancellationToken.None);

        var item = Assert.Single(unscanned);
        Assert.Equal("packages.lock.json", item.Path);
        Assert.Equal("build failed", item.Detail);
    }

    [Fact]
    public async Task Timed_out_restore_is_reported()
    {
        Touch("a/A.csproj");
        _dotnet.Results.Enqueue(new ProcessRunResult(ProcessOutcome.TimedOut, null, "", ""));

        var unscanned = await Generator.GenerateAsync(_root, CancellationToken.None);

        Assert.Equal([new UnscannedItem("a/packages.lock.json", UnscannedReason.RestoreTimedOut)], unscanned);
    }

    [Fact]
    public async Task Dotnet_that_cannot_be_started_is_reported()
    {
        Touch("a/A.csproj");
        _dotnet.Results.Enqueue(new ProcessRunResult(ProcessOutcome.StartFailed, null, "", ""));

        var unscanned = await Generator.GenerateAsync(_root, CancellationToken.None);

        Assert.Equal([new UnscannedItem("a/packages.lock.json", UnscannedReason.DotnetNotStarted)], unscanned);
    }

    [Fact]
    public async Task Projects_skipped_because_the_budget_ran_out_are_reported()
    {
        Touch("a/A.csproj");
        Touch("b/B.csproj");
        Touch("c/C.csproj");
        Touch("d/D.csproj");
        _options.RestoreTotalTimeoutMinutes = 20;
        _options.RestoreTimeoutMinutes = 10;
        _dotnet.OnRun = _ => _time.Advance(TimeSpan.FromMinutes(12));

        var unscanned = await Generator.GenerateAsync(_root, CancellationToken.None);

        Assert.Equal(2, _dotnet.Calls.Count);
        Assert.Equal(
            [
                new UnscannedItem("c/packages.lock.json", UnscannedReason.RestoreBudgetExceeded),
                new UnscannedItem("d/packages.lock.json", UnscannedReason.RestoreBudgetExceeded),
            ],
            unscanned);
    }

    [Fact]
    public async Task Directory_with_several_projects_is_reported_without_running_dotnet()
    {
        Touch("src/One.csproj");
        Touch("src/Two.csproj");
        Touch("ok/Ok.csproj");

        var unscanned = await Generator.GenerateAsync(_root, CancellationToken.None);

        Assert.Equal([new UnscannedItem("src/packages.lock.json", UnscannedReason.MultipleProjects)], unscanned);
        var call = Assert.Single(_dotnet.Calls);
        Assert.EndsWith("Ok.csproj", call.StartInfo.ArgumentList[1]);
    }

    [Fact]
    public async Task Unscanned_items_are_sorted_by_path_without_duplicates()
    {
        Touch("z/Z.csproj");
        Touch("m/One.csproj");
        Touch("m/Two.csproj");
        Touch("a/A.csproj");
        _dotnet.Results.Enqueue(new ProcessRunResult(ProcessOutcome.TimedOut, null, "", ""));
        _dotnet.Results.Enqueue(new ProcessRunResult(ProcessOutcome.TimedOut, null, "", ""));

        var unscanned = await Generator.GenerateAsync(_root, CancellationToken.None);

        Assert.Equal(
            ["a/packages.lock.json", "m/packages.lock.json", "z/packages.lock.json"],
            unscanned.Select(i => i.Path));
        Assert.Equal(UnscannedReason.MultipleProjects, unscanned[1].Reason);
    }

    private void Touch(string relativePath)
    {
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
    }

    private sealed class AdjustableTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class FakeDotnet : IProcessRunner
    {
        public List<(ProcessStartInfo StartInfo, TimeSpan Timeout)> Calls { get; } = [];

        public IReadOnlyList<TimeSpan> Timeouts => Calls.Select(c => c.Timeout).ToList();

        /// <summary>Results handed out in order; a call beyond the queue succeeds.</summary>
        public Queue<ProcessRunResult> Results { get; } = new();

        public Action<ProcessStartInfo>? OnRun { get; set; }

        public Task<ProcessRunResult> RunAsync(
            ProcessStartInfo startInfo, TimeSpan timeout, int maxCapturedChars, CancellationToken cancellationToken)
        {
            Calls.Add((startInfo, timeout));
            OnRun?.Invoke(startInfo);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Results.TryDequeue(out var result)
                ? result
                : new ProcessRunResult(ProcessOutcome.Exited, 0, "", ""));
        }
    }
}
