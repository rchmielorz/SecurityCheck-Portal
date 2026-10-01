using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using securitycheck_portal.Core.Data;
using securitycheck_portal.Core.Processes;

namespace securitycheck_portal.Core.Scanning;

/// <summary>Scans a checkout directory for known vulnerabilities and returns an explicit outcome.</summary>
public interface ITrivyScanner
{
    Task<ScanOutcome> ScanAsync(string checkoutDirectory, CancellationToken cancellationToken);
}

/// <summary>
/// Runs Trivy (<c>trivy fs --scanners vuln</c>) on a checkout. Steps: read version and DB age, try to refresh
/// the DB (a stale cache is accepted only within <see cref="ScanOptions.MaxDbAgeDays"/>), scan, parse.
/// Trivy gets an environment built from a whitelist, never a copy of ours: that one may hold the Git PAT, and
/// Trivy was a supply-chain target (v0.69.4).
///
/// Flags checked against the Trivy docs (Context7, aquasecurity/trivy): <c>trivy image --download-db-only</c>,
/// <c>--db-repository</c>, <c>--skip-db-update</c>, <c>--skip-java-db-update</c>, <c>--cache-dir</c>,
/// <c>trivy version --format json</c>. The field names of the version JSON (<c>Version</c>,
/// <c>VulnerabilityDB.UpdatedAt</c>) are not shown in those docs; they are taken from the Trivy source and covered
/// by the optional real-Trivy test (<c>TRIVY_PATH</c>).
///
/// Java DB decision: this portal scans NuGet and npm lock files only, so the Java DB (a second OCI artifact
/// from another registry, needed only to identify <c>.jar</c> files) is never downloaded and every Trivy call
/// passes <c>--skip-java-db-update</c>. A proxy that blocks the Java DB registry therefore cannot fail a scan,
/// and jar files inside a checkout are simply not analysed (they are not reported as a lock file either).
/// </summary>
public sealed class TrivyScanner(
    IOptions<ScanOptions> options,
    IProcessRunner runner,
    LockFileDetector lockFileDetector,
    TimeProvider timeProvider,
    ILogger<TrivyScanner> logger) : ITrivyScanner
{
    /// <summary>The release compromised on 2026-03-19 (CVE-2026-33634); refused whatever <c>ExpectedTrivyVersion</c> says.</summary>
    public const string ForbiddenVersion = "0.69.4";

    private const int MaxCapturedChars = 256 * 1024;
    private const int MaxDetailLength = 300;
    private static readonly TimeSpan VersionTimeout = TimeSpan.FromMinutes(1);

    /// <summary>The only variables Trivy inherits (plus <c>TRIVY_CACHE_DIR</c>, set from the options).</summary>
    public static readonly IReadOnlyList<string> InheritedEnvironment =
    [
        "PATH", "SystemRoot", "TEMP", "TMP",
        "HTTP_PROXY", "HTTPS_PROXY", "NO_PROXY",
        "http_proxy", "https_proxy", "no_proxy",
    ];

    public async Task<ScanOutcome> ScanAsync(string checkoutDirectory, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        Directory.CreateDirectory(settings.CacheDirectory);
        Directory.CreateDirectory(settings.WorkRoot);

        // 1. Version (and the DB age of the current cache).
        var info = await ReadVersionAsync(settings, cancellationToken);
        if (info.Failure is not null)
        {
            return info.Failure;
        }

        var versionFailure = CheckVersion(settings, info.Version!);
        if (versionFailure is not null)
        {
            return versionFailure;
        }

        // 2. Refresh the DB; on failure fall back to the cache.
        var dbUpdatedAt = info.DbUpdatedAt;
        var update = await runner.RunAsync(
            CreateStartInfo(settings, DownloadDbArguments(settings)),
            TimeSpan.FromMinutes(settings.DbUpdateTimeoutMinutes), MaxCapturedChars, cancellationToken);

        if (update.Outcome == ProcessOutcome.Exited && update.ExitCode == 0)
        {
            var refreshed = await ReadVersionAsync(settings, cancellationToken);
            if (refreshed.Failure is not null)
            {
                return refreshed.Failure;
            }

            dbUpdatedAt = refreshed.DbUpdatedAt;
        }
        else
        {
            logger.LogWarning("Trivy DB update did not succeed ({Outcome}, exit {ExitCode}): {Stderr}; using the cache if recent enough",
                update.Outcome, update.ExitCode, FirstLine(update.Stderr));
        }

        if (dbUpdatedAt is null)
        {
            return new ScanOutcome.Failed(ScanFailureReason.ScannerUnavailable,
                update.Outcome == ProcessOutcome.Exited && update.ExitCode == 0
                    ? "Trivy does not report when its vulnerability database was updated."
                    : "Trivy vulnerability database is missing and could not be downloaded.");
        }

        var age = timeProvider.GetUtcNow() - dbUpdatedAt.Value;
        if (age > TimeSpan.FromDays(settings.MaxDbAgeDays))
        {
            return new ScanOutcome.Failed(ScanFailureReason.DatabaseTooOld,
                $"Trivy vulnerability database is {(int)age.TotalDays} days old (limit {settings.MaxDbAgeDays}).");
        }

        // 3. Scan. The report is written next to the checkouts, never inside the scanned directory.
        var reportPath = Path.Combine(settings.WorkRoot, "trivy-report-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var scan = await runner.RunAsync(
                CreateStartInfo(settings, ScanArguments(settings, checkoutDirectory, reportPath)),
                TimeSpan.FromMinutes(settings.ScanTimeoutMinutes), MaxCapturedChars, cancellationToken);

            switch (scan.Outcome)
            {
                case ProcessOutcome.StartFailed:
                    return new ScanOutcome.Failed(ScanFailureReason.ScannerUnavailable, "Trivy could not be started.");
                case ProcessOutcome.TimedOut:
                    return new ScanOutcome.Failed(ScanFailureReason.Timeout,
                        $"Trivy did not finish within {settings.ScanTimeoutMinutes} minutes.");
                case ProcessOutcome.Exited when scan.ExitCode != 0:
                    return new ScanOutcome.Failed(ScanFailureReason.ScannerFailed,
                        $"Trivy exited with code {scan.ExitCode}: {FirstLine(scan.Stderr)}");
            }

            if (!File.Exists(reportPath))
            {
                return new ScanOutcome.Failed(ScanFailureReason.ScannerFailed, "Trivy wrote no report.");
            }

            var json = await File.ReadAllTextAsync(reportPath, Encoding.UTF8, cancellationToken);
            if (!TrivyReportParser.TryParse(json, out var report) || report is null)
            {
                return new ScanOutcome.Failed(ScanFailureReason.ScannerFailed, "Trivy report is not valid.");
            }

            // 4. Never "clean" without proof: missing lock files or no target at all means incomplete.
            var missing = lockFileDetector.FindMissing(checkoutDirectory);
            if (missing.Count > 0 || report.TargetCount == 0)
            {
                return new ScanOutcome.Incomplete(report.Findings, missing, info.Version!, dbUpdatedAt.Value);
            }

            return new ScanOutcome.Completed(report.Findings, info.Version!, dbUpdatedAt.Value);
        }
        finally
        {
            TryDelete(reportPath);
        }
    }

    /// <summary>Public so tests can check the arguments and the environment of each call.</summary>
    public static ProcessStartInfo CreateStartInfo(ScanOptions settings, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo(settings.TrivyExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetTempPath(),
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // Start from nothing: the worker's own environment may carry the Git PAT (Git__Token).
        startInfo.Environment.Clear();
        foreach (var name in InheritedEnvironment)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value))
            {
                startInfo.Environment[name] = value;
            }
        }

        startInfo.Environment["TRIVY_CACHE_DIR"] = settings.CacheDirectory;
        return startInfo;
    }

    public static IReadOnlyList<string> VersionArguments(ScanOptions settings)
        => ["version", "--format", "json", "--cache-dir", settings.CacheDirectory];

    public static IReadOnlyList<string> DownloadDbArguments(ScanOptions settings)
    {
        List<string> arguments =
            ["image", "--download-db-only", "--skip-java-db-update", "--cache-dir", settings.CacheDirectory];
        if (!string.IsNullOrWhiteSpace(settings.DbRepository))
        {
            arguments.Add("--db-repository");
            arguments.Add(settings.DbRepository);
        }

        return arguments;
    }

    public static IReadOnlyList<string> ScanArguments(ScanOptions settings, string checkoutDirectory, string reportPath)
        =>
        [
            "fs", "--scanners", "vuln", "--format", "json", "--output", reportPath,
            "--cache-dir", settings.CacheDirectory, "--skip-db-update", "--skip-java-db-update",
            checkoutDirectory,
        ];

    private sealed record VersionInfo(string? Version, DateTimeOffset? DbUpdatedAt, ScanOutcome.Failed? Failure);

    private async Task<VersionInfo> ReadVersionAsync(ScanOptions settings, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(
            CreateStartInfo(settings, VersionArguments(settings)), VersionTimeout, MaxCapturedChars, cancellationToken);

        if (result.Outcome == ProcessOutcome.StartFailed)
        {
            return Failure(ScanFailureReason.ScannerUnavailable, "Trivy could not be started.");
        }

        if (result.Outcome == ProcessOutcome.TimedOut)
        {
            return Failure(ScanFailureReason.Timeout, "Trivy version check timed out.");
        }

        if (result.ExitCode != 0)
        {
            return Failure(ScanFailureReason.ScannerUnavailable,
                $"trivy version exited with code {result.ExitCode}: {FirstLine(result.Stderr)}");
        }

        try
        {
            using var document = JsonDocument.Parse(result.Stdout);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("Version", out var version)
                || version.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(version.GetString()))
            {
                return Failure(ScanFailureReason.ScannerUnavailable, "Trivy version output has no Version.");
            }

            DateTimeOffset? updatedAt = null;
            if (root.TryGetProperty("VulnerabilityDB", out var db)
                && db.ValueKind == JsonValueKind.Object
                && db.TryGetProperty("UpdatedAt", out var updated)
                && updated.ValueKind == JsonValueKind.String
                && updated.TryGetDateTimeOffset(out var parsed))
            {
                updatedAt = parsed;
            }

            return new VersionInfo(version.GetString()!.Trim(), updatedAt, null);
        }
        catch (JsonException)
        {
            return Failure(ScanFailureReason.ScannerUnavailable, "Trivy version output is not valid JSON.");
        }

        static VersionInfo Failure(ScanFailureReason reason, string detail)
            => new(null, null, new ScanOutcome.Failed(reason, detail));
    }

    private static ScanOutcome.Failed? CheckVersion(ScanOptions settings, string version)
    {
        var normalized = version.TrimStart('v', 'V');
        if (normalized == ForbiddenVersion)
        {
            return new ScanOutcome.Failed(ScanFailureReason.ScannerUnavailable,
                $"Trivy {ForbiddenVersion} is a compromised release and is refused.");
        }

        if (!string.IsNullOrWhiteSpace(settings.ExpectedTrivyVersion)
            && normalized != settings.ExpectedTrivyVersion.Trim().TrimStart('v', 'V'))
        {
            return new ScanOutcome.Failed(ScanFailureReason.ScannerUnavailable,
                $"Trivy {normalized} found, Scan:ExpectedTrivyVersion is {settings.ExpectedTrivyVersion.Trim()}.");
        }

        return null;
    }

    private static string FirstLine(string text)
    {
        var line = text.AsSpan().TrimStart();
        var end = line.IndexOfAny('\r', '\n');
        if (end >= 0)
        {
            line = line[..end];
        }

        return line.Length > MaxDetailLength ? line[..MaxDetailLength].ToString() : line.ToString();
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException ex)
        {
            logger.LogDebug(ex, "Could not delete Trivy report {Path}", path);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogDebug(ex, "Could not delete Trivy report {Path}", path);
        }
    }
}
