using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using securitycheck_portal.Core.Data;
using securitycheck_portal.Core.Processes;
using securitycheck_portal.Core.Scanning;

namespace securitycheck_portal.Tests.Scanning;

/// <summary>
/// Real Trivy on a small temp repository. Set <c>TRIVY_PATH</c> to the Trivy binary; it needs network access to
/// download the vulnerability DB (cached in a folder under the temp directory), so it is skipped otherwise.
/// Class name contains "TrivyScanner" so it is picked up by the TrivyScanner filter.
/// </summary>
public sealed class TrivyScannerIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "trivy-it-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [SkippableFact]
    public async Task Real_trivy_finds_vulnerabilities_when_the_lock_file_exists()
    {
        var scanner = CreateScanner();
        var checkout = Path.Combine(_root, "with-lock");
        Directory.CreateDirectory(checkout);
        File.WriteAllText(Path.Combine(checkout, "App.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(checkout, "packages.lock.json"), LockFileWithOldNewtonsoft);

        var outcome = await scanner.ScanAsync(checkout, CancellationToken.None);

        Assert.False(outcome is ScanOutcome.Failed, outcome.ToString());
        var completed = Assert.IsType<ScanOutcome.Completed>(outcome);
        var finding = Assert.Single(completed.Findings, f => f.VulnerabilityId == "CVE-2024-21907");
        Assert.Equal("Newtonsoft.Json", finding.Library);
        Assert.Equal("12.0.1", finding.InstalledVersion);
        Assert.NotEmpty(completed.TrivyVersion);
    }

    [SkippableFact]
    public async Task Real_trivy_without_a_lock_file_is_incomplete()
    {
        var scanner = CreateScanner();
        var checkout = Path.Combine(_root, "no-lock");
        Directory.CreateDirectory(checkout);
        File.WriteAllText(Path.Combine(checkout, "App.csproj"), "<Project />");

        var outcome = await scanner.ScanAsync(checkout, CancellationToken.None);

        Assert.False(outcome is ScanOutcome.Failed, outcome.ToString());
        var incomplete = Assert.IsType<ScanOutcome.Incomplete>(outcome);
        Assert.Empty(incomplete.Findings);
        Assert.Equal(["packages.lock.json"], incomplete.Unscanned.Select(u => u.Path));
    }

    private TrivyScanner CreateScanner()
    {
        var trivy = Environment.GetEnvironmentVariable("TRIVY_PATH");
        Skip.If(string.IsNullOrWhiteSpace(trivy), "TRIVY_PATH nie ustawiony — test integracyjny pominięty");

        var options = new ScanOptions
        {
            TrivyExecutablePath = trivy!,
            // Kept between runs: the DB download is the slow part.
            CacheDirectory = Path.Combine(Path.GetTempPath(), "securitycheck-trivy-it-cache"),
            WorkRoot = Path.Combine(_root, "work"),
            ExpectedTrivyVersion = Environment.GetEnvironmentVariable("TRIVY_EXPECTED_VERSION"),
        };
        Directory.CreateDirectory(_root);

        var runner = new ProcessRunner(NullLogger<ProcessRunner>.Instance);
        return new TrivyScanner(
            Options.Create(options),
            runner,
            new LockFileDetector(),
            new DotnetLockFileGenerator(
                Options.Create(options), runner, new LockFileDetector(), TimeProvider.System, NullLogger<DotnetLockFileGenerator>.Instance),
            TimeProvider.System,
            NullLogger<TrivyScanner>.Instance);
    }

    // Newtonsoft.Json below 13.0.1 is affected by CVE-2024-21907 (GHSA-5crp-9r3c-p9vr).
    private const string LockFileWithOldNewtonsoft = """
        {
          "version": 1,
          "dependencies": {
            "net8.0": {
              "Newtonsoft.Json": {
                "type": "Direct",
                "requested": "[12.0.1, )",
                "resolved": "12.0.1",
                "contentHash": "AAAA"
              }
            }
          }
        }
        """;
}
