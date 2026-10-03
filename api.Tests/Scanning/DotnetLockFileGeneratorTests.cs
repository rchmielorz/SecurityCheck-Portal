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
    private readonly ScanOptions _options = new()
    {
        DotnetExecutablePath = "dotnet-under-test",
        CacheDirectory = "cache",
        WorkRoot = "work",
        RestoreTimeoutMinutes = 7,
    };

    public DotnetLockFileGeneratorTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private DotnetLockFileGenerator Generator => new(
        Options.Create(_options), _dotnet, new LockFileDetector(), NullLogger<DotnetLockFileGenerator>.Instance);

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
        const string secretName = "SCW_TEST_SECRET";
        Environment.SetEnvironmentVariable(secretName, "must-not-leak");
        try
        {
            var startInfo = DotnetLockFileGenerator.CreateStartInfo(_options, Path.Combine(_root, "App.csproj"));

            Assert.DoesNotContain(secretName, startInfo.Environment.Keys);
            Assert.DoesNotContain("Git__Token", DotnetLockFileGenerator.InheritedEnvironment);
            var allowed = DotnetLockFileGenerator.InheritedEnvironment
                .Concat(["DOTNET_CLI_TELEMETRY_OPTOUT", "DOTNET_NOLOGO", "DOTNET_SKIP_FIRST_TIME_EXPERIENCE"])
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.All(startInfo.Environment.Keys, key => Assert.Contains(key, allowed));
            Assert.Equal("1", startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(secretName, null);
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
        _dotnet.OnRun = () => cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Generator.GenerateAsync(_root, cts.Token));

        Assert.Single(_dotnet.Calls);
    }

    private void Touch(string relativePath)
    {
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
    }

    private sealed class FakeDotnet : IProcessRunner
    {
        public List<(ProcessStartInfo StartInfo, TimeSpan Timeout)> Calls { get; } = [];

        /// <summary>Results handed out in order; a call beyond the queue succeeds.</summary>
        public Queue<ProcessRunResult> Results { get; } = new();

        public Action? OnRun { get; set; }

        public Task<ProcessRunResult> RunAsync(
            ProcessStartInfo startInfo, TimeSpan timeout, int maxCapturedChars, CancellationToken cancellationToken)
        {
            Calls.Add((startInfo, timeout));
            OnRun?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Results.TryDequeue(out var result)
                ? result
                : new ProcessRunResult(ProcessOutcome.Exited, 0, "", ""));
        }
    }
}
