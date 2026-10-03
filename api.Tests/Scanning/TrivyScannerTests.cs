using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using securitycheck_portal.Core.Data;
using securitycheck_portal.Core.Processes;
using securitycheck_portal.Core.Scanning;

namespace securitycheck_portal.Tests.Scanning;

public sealed class TrivyScannerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private const string EmptyReport = """{"Results":[{"Target":"packages.lock.json","Type":"nuget"}]}""";

    private const string OneVulnerability = """
        {"Results":[{"Target":"packages.lock.json","Vulnerabilities":[
          {"VulnerabilityID":"CVE-1","PkgName":"lib","InstalledVersion":"1.0","Severity":"HIGH"}]}]}
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "trivyscan-" + Guid.NewGuid().ToString("N"));
    private readonly string _checkout;
    private readonly FakeTrivy _trivy = new();
    private readonly FakeDotnet _dotnet = new();
    private readonly ScanOptions _options;

    public TrivyScannerTests()
    {
        _checkout = Path.Combine(_root, "checkout");
        Directory.CreateDirectory(_checkout);
        _options = new ScanOptions
        {
            TrivyExecutablePath = "trivy-under-test",
            CacheDirectory = Path.Combine(_root, "cache"),
            WorkRoot = Path.Combine(_root, "work"),
            MaxDbAgeDays = 7,
        };
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private TrivyScanner Scanner => new(
        Options.Create(_options), _trivy, new LockFileDetector(),
        new DotnetLockFileGenerator(
            Options.Create(_options), _dotnet, new LockFileDetector(), new FixedTime(Now), NullLogger<DotnetLockFileGenerator>.Instance),
        new FixedTime(Now), NullLogger<TrivyScanner>.Instance);

    private void WithLockFile() => File.WriteAllText(Path.Combine(_checkout, "packages.lock.json"), "{}");

    [Fact]
    public async Task Clean_scan_with_a_target_and_lock_files_is_completed()
    {
        _trivy.Version = VersionJson("0.58.0", Now.AddDays(-1));
        _trivy.Report = EmptyReport;
        WithLockFile();

        var outcome = await Scanner.ScanAsync(_checkout, CancellationToken.None);

        var completed = Assert.IsType<ScanOutcome.Completed>(outcome);
        Assert.Empty(completed.Findings);
        Assert.Equal("0.58.0", completed.TrivyVersion);
        Assert.Equal(Now.AddDays(-1), completed.DbUpdatedAt);
    }

    [Fact]
    public async Task Findings_are_returned_and_the_commands_have_the_expected_flags()
    {
        _trivy.Version = VersionJson("0.58.0", Now.AddDays(-1));
        _trivy.Report = OneVulnerability;
        WithLockFile();
        _options.DbRepository = "registry.internal/trivy-db";

        var outcome = await Scanner.ScanAsync(_checkout, CancellationToken.None);

        var completed = Assert.IsType<ScanOutcome.Completed>(outcome);
        Assert.Equal("CVE-1", completed.Findings.Single().VulnerabilityId);

        var update = _trivy.Calls.Single(c => c.Subcommand == "image").StartInfo.ArgumentList;
        Assert.Contains("--download-db-only", update);
        Assert.Contains("--skip-java-db-update", update);
        Assert.Equal("registry.internal/trivy-db", update[update.IndexOf("--db-repository") + 1]);

        var scan = _trivy.Calls.Single(c => c.Subcommand == "fs");
        var args = scan.StartInfo.ArgumentList;
        Assert.Equal(["fs", "--scanners", "vuln", "--format", "json"], args.Take(5));
        Assert.Contains("--skip-db-update", args);
        Assert.Contains("--skip-java-db-update", args);
        Assert.Equal(_options.CacheDirectory, args[args.IndexOf("--cache-dir") + 1]);
        Assert.Equal(_checkout, args[^1]);
        Assert.Equal(TimeSpan.FromMinutes(_options.ScanTimeoutMinutes), scan.Timeout);
        Assert.False(File.Exists(args[args.IndexOf("--output") + 1]), "report file is removed");
    }

    [Fact]
    public async Task Missing_lock_file_is_incomplete_with_the_findings_kept()
    {
        _trivy.Version = VersionJson("0.58.0", Now.AddDays(-1));
        _trivy.Report = OneVulnerability;
        File.WriteAllText(Path.Combine(_checkout, "App.csproj"), "");

        var outcome = await Scanner.ScanAsync(_checkout, CancellationToken.None);

        var incomplete = Assert.IsType<ScanOutcome.Incomplete>(outcome);
        Assert.Equal(["packages.lock.json"], incomplete.MissingLockFiles);
        Assert.Single(incomplete.Findings);
    }

    [Fact]
    public async Task Dotnet_project_without_lock_is_completed_when_the_restore_generates_it()
    {
        _trivy.Version = VersionJson("0.58.0", Now.AddDays(-1));
        _trivy.Report = EmptyReport;
        File.WriteAllText(Path.Combine(_checkout, "App.csproj"), "");
        _dotnet.WritesLockFile = true;

        var outcome = await Scanner.ScanAsync(_checkout, CancellationToken.None);

        Assert.IsType<ScanOutcome.Completed>(outcome);
        Assert.Single(_dotnet.Calls);
        Assert.Contains("--use-lock-file", _dotnet.Calls[0].ArgumentList);
    }

    [Fact]
    public async Task Dotnet_project_whose_restore_fails_is_incomplete_with_the_lock_missing()
    {
        _trivy.Version = VersionJson("0.58.0", Now.AddDays(-1));
        _trivy.Report = EmptyReport;
        File.WriteAllText(Path.Combine(_checkout, "App.csproj"), "");
        _dotnet.Result = new ProcessRunResult(ProcessOutcome.Exited, 1, "", "error NU1101");

        var outcome = await Scanner.ScanAsync(_checkout, CancellationToken.None);

        var incomplete = Assert.IsType<ScanOutcome.Incomplete>(outcome);
        Assert.Equal(["packages.lock.json"], incomplete.MissingLockFiles);
    }

    [Fact]
    public async Task Lock_files_are_generated_before_trivy_scans()
    {
        _trivy.Version = VersionJson("0.58.0", Now.AddDays(-1));
        _trivy.Report = EmptyReport;
        File.WriteAllText(Path.Combine(_checkout, "App.csproj"), "");
        _dotnet.WritesLockFile = true;
        var scanStartedBeforeRestore = false;
        _dotnet.OnCall = () => scanStartedBeforeRestore = _trivy.Calls.Any(c => c.Subcommand == "fs");

        await Scanner.ScanAsync(_checkout, CancellationToken.None);

        Assert.Single(_dotnet.Calls);
        Assert.False(scanStartedBeforeRestore, "restore must run before trivy fs");
        Assert.Contains(_trivy.Calls, c => c.Subcommand == "fs");
    }

    [Fact]
    public async Task Lock_files_are_not_generated_when_the_database_is_too_old()
    {
        _trivy.Version = VersionJson("0.58.0", Now.AddDays(-8));
        File.WriteAllText(Path.Combine(_checkout, "App.csproj"), "");
        _dotnet.WritesLockFile = true;

        var outcome = await Scanner.ScanAsync(_checkout, CancellationToken.None);

        Assert.Equal(ScanFailureReason.DatabaseTooOld, Assert.IsType<ScanOutcome.Failed>(outcome).Reason);
        Assert.Empty(_dotnet.Calls);
    }

    [Fact]
    public async Task Report_without_targets_is_incomplete_never_clean()
    {
        _trivy.Version = VersionJson("0.58.0", Now.AddDays(-1));
        _trivy.Report = """{"SchemaVersion":2}""";

        var outcome = await Scanner.ScanAsync(_checkout, CancellationToken.None);

        var incomplete = Assert.IsType<ScanOutcome.Incomplete>(outcome);
        Assert.Empty(incomplete.Findings);
    }

    [Fact]
    public async Task Database_older_than_the_limit_after_a_successful_update_fails()
    {
        _trivy.Version = VersionJson("0.58.0", Now.AddDays(-8));

        var outcome = await Scanner.ScanAsync(_checkout, CancellationToken.None);

        var failed = Assert.IsType<ScanOutcome.Failed>(outcome);
        Assert.Equal(ScanFailureReason.DatabaseTooOld, failed.Reason);
        Assert.DoesNotContain(_trivy.Calls, c => c.Subcommand == "fs");
    }

    [Fact]
    public async Task Failed_update_with_a_cache_within_the_limit_still_scans()
    {
        _trivy.Version = VersionJson("0.58.0", Now.AddDays(-3));
        _trivy.Update = new ProcessRunResult(ProcessOutcome.Exited, 1, "", "FATAL failed to download vulnerability DB");
        _trivy.Report = EmptyReport;
        WithLockFile();

        var outcome = await Scanner.ScanAsync(_checkout, CancellationToken.None);

        var completed = Assert.IsType<ScanOutcome.Completed>(outcome);
        Assert.Equal(Now.AddDays(-3), completed.DbUpdatedAt);
    }

    [Fact]
    public async Task Failed_update_with_a_cache_over_the_limit_fails_as_too_old()
    {
        _trivy.Version = VersionJson("0.58.0", Now.AddDays(-30));
        _trivy.Update = new ProcessRunResult(ProcessOutcome.TimedOut, null, "", "");

        var outcome = await Scanner.ScanAsync(_checkout, CancellationToken.None);

        Assert.Equal(ScanFailureReason.DatabaseTooOld, Assert.IsType<ScanOutcome.Failed>(outcome).Reason);
    }

    [Fact]
    public async Task Failed_update_without_any_cache_is_scanner_unavailable()
    {
        _trivy.Version = """{"Version":"0.58.0"}""";
        _trivy.Update = new ProcessRunResult(ProcessOutcome.Exited, 1, "", "no route to host");

        var outcome = await Scanner.ScanAsync(_checkout, CancellationToken.None);

        Assert.Equal(ScanFailureReason.ScannerUnavailable, Assert.IsType<ScanOutcome.Failed>(outcome).Reason);
        Assert.DoesNotContain(_trivy.Calls, c => c.Subcommand == "fs");
    }

    [Fact]
    public async Task Scan_that_times_out_is_a_timeout_failure()
    {
        _trivy.Version = VersionJson("0.58.0", Now.AddDays(-1));
        _trivy.Scan = new ProcessRunResult(ProcessOutcome.TimedOut, null, "", "");

        var outcome = await Scanner.ScanAsync(_checkout, CancellationToken.None);

        Assert.Equal(ScanFailureReason.Timeout, Assert.IsType<ScanOutcome.Failed>(outcome).Reason);
    }

    [Fact]
    public async Task Non_zero_exit_is_scanner_failed_with_the_first_line_of_stderr()
    {
        _trivy.Version = VersionJson("0.58.0", Now.AddDays(-1));
        _trivy.Scan = new ProcessRunResult(ProcessOutcome.Exited, 2, "", "FATAL boom\nstack trace");

        var outcome = await Scanner.ScanAsync(_checkout, CancellationToken.None);

        var failed = Assert.IsType<ScanOutcome.Failed>(outcome);
        Assert.Equal(ScanFailureReason.ScannerFailed, failed.Reason);
        Assert.Contains("FATAL boom", failed.Detail);
        Assert.DoesNotContain("stack trace", failed.Detail);
    }

    [Fact]
    public async Task Invalid_report_json_is_scanner_failed()
    {
        _trivy.Version = VersionJson("0.58.0", Now.AddDays(-1));
        _trivy.Report = "{ not json";

        var outcome = await Scanner.ScanAsync(_checkout, CancellationToken.None);

        Assert.Equal(ScanFailureReason.ScannerFailed, Assert.IsType<ScanOutcome.Failed>(outcome).Reason);
    }

    [Fact]
    public async Task Missing_report_file_is_scanner_failed()
    {
        _trivy.Version = VersionJson("0.58.0", Now.AddDays(-1));
        _trivy.Report = null;

        var outcome = await Scanner.ScanAsync(_checkout, CancellationToken.None);

        Assert.Equal(ScanFailureReason.ScannerFailed, Assert.IsType<ScanOutcome.Failed>(outcome).Reason);
    }

    [Fact]
    public async Task Trivy_that_cannot_be_started_is_scanner_unavailable()
    {
        _trivy.Version = null;
        _trivy.VersionResult = new ProcessRunResult(ProcessOutcome.StartFailed, null, "", "");

        var outcome = await Scanner.ScanAsync(_checkout, CancellationToken.None);

        Assert.Equal(ScanFailureReason.ScannerUnavailable, Assert.IsType<ScanOutcome.Failed>(outcome).Reason);
    }

    [Theory]
    [InlineData("0.69.4")]
    [InlineData("v0.69.4")]
    public async Task Compromised_release_is_refused_before_any_other_call(string version)
    {
        _trivy.Version = VersionJson(version, Now);

        var outcome = await Scanner.ScanAsync(_checkout, CancellationToken.None);

        Assert.Equal(ScanFailureReason.ScannerUnavailable, Assert.IsType<ScanOutcome.Failed>(outcome).Reason);
        Assert.All(_trivy.Calls, c => Assert.Equal("version", c.Subcommand));
    }

    [Fact]
    public async Task Compromised_release_is_refused_even_when_it_is_the_expected_version()
    {
        _options.ExpectedTrivyVersion = "0.69.4";
        _trivy.Version = VersionJson("0.69.4", Now);

        var outcome = await Scanner.ScanAsync(_checkout, CancellationToken.None);

        Assert.IsType<ScanOutcome.Failed>(outcome);
        Assert.All(_trivy.Calls, c => Assert.Equal("version", c.Subcommand));
    }

    [Fact]
    public async Task Version_other_than_the_expected_one_is_refused()
    {
        _options.ExpectedTrivyVersion = "0.58.0";
        _trivy.Version = VersionJson("0.58.1", Now);

        var outcome = await Scanner.ScanAsync(_checkout, CancellationToken.None);

        var failed = Assert.IsType<ScanOutcome.Failed>(outcome);
        Assert.Equal(ScanFailureReason.ScannerUnavailable, failed.Reason);
        Assert.Contains("0.58.0", failed.Detail);
        Assert.All(_trivy.Calls, c => Assert.Equal("version", c.Subcommand));
    }

    [Fact]
    public async Task Expected_version_matches_with_or_without_a_v_prefix()
    {
        _options.ExpectedTrivyVersion = "v0.58.0";
        _trivy.Version = VersionJson("0.58.0", Now.AddDays(-1));
        _trivy.Report = EmptyReport;
        WithLockFile();

        var outcome = await Scanner.ScanAsync(_checkout, CancellationToken.None);

        Assert.IsType<ScanOutcome.Completed>(outcome);
    }

    [Fact]
    public async Task Trivy_environment_has_only_whitelisted_keys_and_no_pat()
    {
        const string Pat = "s3cr3t-pat-value";
        var names = new[] { "Git__Token", "GIT_CONFIG_VALUE_0", "GITLAB_TOKEN", "HTTPS_PROXY", "TRIVY_PASSWORD" };
        var before = names.ToDictionary(n => n, Environment.GetEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable("Git__Token", Pat);
            Environment.SetEnvironmentVariable("GIT_CONFIG_VALUE_0", "Authorization: Basic " + Pat);
            Environment.SetEnvironmentVariable("GITLAB_TOKEN", Pat);
            Environment.SetEnvironmentVariable("TRIVY_PASSWORD", Pat);
            Environment.SetEnvironmentVariable("HTTPS_PROXY", "http://proxy.internal:3128");

            _trivy.Version = VersionJson("0.58.0", Now.AddDays(-1));
            _trivy.Report = EmptyReport;
            WithLockFile();

            await Scanner.ScanAsync(_checkout, CancellationToken.None);
        }
        finally
        {
            foreach (var (name, value) in before)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        Assert.Equal(["version", "image", "version", "fs"], _trivy.Calls.Select(c => c.Subcommand));
        var allowed = TrivyScanner.InheritedEnvironment
            .Append("TRIVY_CACHE_DIR")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (startInfo, _, _) in _trivy.Calls)
        {
            Assert.All(startInfo.Environment.Keys, key => Assert.Contains(key, allowed));
            Assert.DoesNotContain(startInfo.Environment.Values, v => v is not null && v.Contains(Pat));
            Assert.Equal(_options.CacheDirectory, startInfo.Environment["TRIVY_CACHE_DIR"]);
            Assert.Equal("http://proxy.internal:3128", startInfo.Environment["HTTPS_PROXY"]);
            Assert.DoesNotContain(startInfo.ArgumentList, a => a.Contains(Pat));
        }
    }

    private static string VersionJson(string version, DateTimeOffset dbUpdatedAt)
        => $$$"""
            {"Version":"{{{version}}}","VulnerabilityDB":{"Version":2,"UpdatedAt":"{{{dbUpdatedAt:O}}}","DownloadedAt":"{{{dbUpdatedAt:O}}}"}}
            """;

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Records <c>dotnet restore</c> calls; optionally writes <c>packages.lock.json</c> next to the project.</summary>
    private sealed class FakeDotnet : IProcessRunner
    {
        public bool WritesLockFile { get; set; }

        public ProcessRunResult Result { get; set; } = new(ProcessOutcome.Exited, 0, "", "");

        public List<ProcessStartInfo> Calls { get; } = [];

        public Action? OnCall { get; set; }

        public Task<ProcessRunResult> RunAsync(
            ProcessStartInfo startInfo, TimeSpan timeout, int maxCapturedChars, CancellationToken cancellationToken)
        {
            Calls.Add(startInfo);
            OnCall?.Invoke();
            if (WritesLockFile && Result.ExitCode == 0)
            {
                var project = startInfo.ArgumentList[1];
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(project)!, "packages.lock.json"), "{}");
            }

            return Task.FromResult(Result);
        }
    }

    /// <summary>Answers by Trivy subcommand; the <c>fs</c> answer writes the report file the scanner asked for.</summary>
    private sealed class FakeTrivy : IProcessRunner
    {
        public string? Version { get; set; }

        public ProcessRunResult? VersionResult { get; set; }

        public ProcessRunResult Update { get; set; } = new(ProcessOutcome.Exited, 0, "", "");

        public ProcessRunResult Scan { get; set; } = new(ProcessOutcome.Exited, 0, "", "");

        /// <summary>Written to the <c>--output</c> file; <c>null</c> writes nothing.</summary>
        public string? Report { get; set; }

        public List<(ProcessStartInfo StartInfo, TimeSpan Timeout, string Subcommand)> Calls { get; } = [];

        public Task<ProcessRunResult> RunAsync(
            ProcessStartInfo startInfo, TimeSpan timeout, int maxCapturedChars, CancellationToken cancellationToken)
        {
            var subcommand = startInfo.ArgumentList[0];
            Calls.Add((startInfo, timeout, subcommand));

            switch (subcommand)
            {
                case "version":
                    return Task.FromResult(
                        VersionResult ?? new ProcessRunResult(ProcessOutcome.Exited, 0, Version ?? "", ""));
                case "image":
                    return Task.FromResult(Update);
                case "fs":
                    if (Report is not null)
                    {
                        var output = startInfo.ArgumentList[startInfo.ArgumentList.IndexOf("--output") + 1];
                        File.WriteAllText(output, Report);
                    }

                    return Task.FromResult(Scan);
                default:
                    throw new InvalidOperationException("Unexpected Trivy call: " + subcommand);
            }
        }
    }
}
