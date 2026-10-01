using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using securitycheck_portal.Core.Data;
using securitycheck_portal.Core.Git;

namespace securitycheck_portal.Core.Scanning;

/// <summary>
/// Runs one claimed scan: re-resolves the pattern, checks out the exact commit, runs Trivy and stores the result.
/// The pattern row is never written (<c>LastResolved*</c> belongs to the API's resolve endpoint). Every failure
/// ends the scan with an explicit <see cref="ScanFailureReason"/>, and the checkout is always deleted.
/// </summary>
public sealed class ScanJobRunner(
    IServiceScopeFactory scopeFactory,
    PatternResolutionService resolutionService,
    IGitCheckout checkout,
    ITrivyScanner scanner,
    IOptions<GitOptions> gitOptions,
    IOptions<ScanOptions> scanOptions,
    TimeProvider timeProvider,
    ILogger<ScanJobRunner> logger)
{
    private static readonly TimeSpan PersistTimeout = TimeSpan.FromSeconds(30);

    private sealed record Resolved(string Tag, string Commit);

    /// <param name="scanId">A scan already claimed by <see cref="ScanQueue.ClaimNextAsync"/> (status <c>Running</c>).</param>
    /// <remarks>
    /// When <paramref name="cancellationToken"/> is cancelled (the worker is stopping), the scan is marked
    /// <c>Failed(Interrupted)</c> if the database still answers, and the method returns without throwing. If the
    /// update is impossible the row stays <c>Running</c> and <see cref="ScanQueue.RecoverInterruptedAsync"/>
    /// fixes it on the next start.
    /// </remarks>
    public async Task RunAsync(long scanId, CancellationToken cancellationToken)
    {
        var options = scanOptions.Value;
        var directory = WorkDirectory.ForScan(options, scanId);

        try
        {
            ScanSnapshot? scan = await LoadScanAsync(scanId, cancellationToken);
            if (scan is null)
            {
                logger.LogWarning("Scan {ScanId} was claimed but is not running any more; skipping", scanId);
                return;
            }

            var (outcome, resolved) = await ExecuteAsync(scan, directory, cancellationToken);
            await FinishAsync(scanId, outcome, resolved);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Scan {ScanId} was cancelled because the worker is stopping", scanId);
            await TryFinishAsync(scanId,
                new ScanOutcome.Failed(ScanFailureReason.Interrupted, ScanQueue.InterruptedDetail), null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Scan {ScanId} failed unexpectedly", scanId);
            await TryFinishAsync(scanId,
                new ScanOutcome.Failed(ScanFailureReason.ScannerFailed, "Unexpected error: " + ex.GetType().Name), null);
        }
        finally
        {
            WorkDirectory.Delete(directory, logger);
        }
    }

    private sealed record ScanSnapshot(long PatternId);

    private async Task<ScanSnapshot?> LoadScanAsync(long scanId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();

        return await db.Scans.AsNoTracking()
            .Where(s => s.Id == scanId && s.Status == ScanStatus.Running)
            .Select(s => new ScanSnapshot(s.PatternId))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<(ScanOutcome Outcome, Resolved? Resolved)> ExecuteAsync(
        ScanSnapshot scan, string directory, CancellationToken cancellationToken)
    {
        // 1. The pattern as it is now. Scans have no foreign key to patterns, so it may be gone or inactive.
        PatternInfo? pattern;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            pattern = await db.VersionPatterns.AsNoTracking()
                .Where(p => p.Id == scan.PatternId)
                .Select(p => new PatternInfo(p.Pattern, p.IsActive, p.Repository!.Url))
                .SingleOrDefaultAsync(cancellationToken);
        }

        if (pattern is null)
        {
            return (NotResolved("The pattern was deleted before the scan started."), null);
        }

        if (!pattern.IsActive)
        {
            return (NotResolved("The pattern was deactivated before the scan started."), null);
        }

        if (!VersionPatternSpec.TryParse(pattern.Pattern, out var spec))
        {
            return (NotResolved("The stored pattern is not a valid X.Y.* pattern."), null);
        }

        // 2. Re-resolve against the Git server: the scan runs on the tag as it is now, not as it was at the last check.
        var resolution = await resolutionService.ResolveAsync(pattern.RepositoryUrl, spec, cancellationToken);
        if (resolution is not PatternResolution.Resolved resolved)
        {
            return (NotResolved(resolution switch
            {
                PatternResolution.NoMatch => "No tag matches the pattern.",
                PatternResolution.Ambiguous ambiguous => "The tags are ambiguous: " + ambiguous.Reason,
                PatternResolution.Error { Kind: GitErrorKind.Timeout } => "The Git server did not answer in time.",
                PatternResolution.Error => "The Git server could not be queried.",
                _ => "The pattern could not be resolved.",
            }), null);
        }

        var tag = new Resolved(resolved.Tag, resolved.CommitSha);

        // 3. Checkout of exactly the resolved commit.
        WorkDirectory.Delete(directory, logger);
        Directory.CreateDirectory(scanOptions.Value.WorkRoot);

        var result = await checkout.CheckoutAsync(
            pattern.RepositoryUrl, resolved.Tag, resolved.CommitSha, directory, cancellationToken);
        switch (result)
        {
            case GitCheckoutResult.Success:
                break;
            case GitCheckoutResult.CommitMismatch mismatch:
                return (new ScanOutcome.Failed(ScanFailureReason.CommitMismatch,
                    $"Tag {resolved.Tag} now points to {mismatch.ActualCommit}, expected {resolved.CommitSha}."), tag);
            case GitCheckoutResult.Failure failure:
                return (failure.Kind == GitErrorKind.Timeout
                    ? new ScanOutcome.Failed(ScanFailureReason.Timeout, "Git did not finish cloning in time.")
                    : new ScanOutcome.Failed(ScanFailureReason.GitFailed,
                        failure.ExitCode is { } code ? $"git exited with code {code}." : "git could not be run."), tag);
            default:
                throw new InvalidOperationException("Unknown checkout result.");
        }

        // 4. Trivy (it also looks for missing lock files and decides between completed and incomplete).
        return (await scanner.ScanAsync(directory, cancellationToken), tag);
    }

    private sealed record PatternInfo(string Pattern, bool IsActive, string RepositoryUrl);

    private static ScanOutcome.Failed NotResolved(string detail)
        => new(ScanFailureReason.PatternNotResolved, detail);

    private async Task TryFinishAsync(long scanId, ScanOutcome outcome, Resolved? resolved)
    {
        try
        {
            await FinishAsync(scanId, outcome, resolved);
        }
        catch (Exception ex)
        {
            // The row stays Running; the next start of the worker marks it Interrupted.
            logger.LogError(ex, "Could not store the result of scan {ScanId}", scanId);
        }
    }

    /// <summary>Stores the outcome. Not tied to the caller's token: a finished scan should be saved during shutdown.</summary>
    private async Task FinishAsync(long scanId, ScanOutcome outcome, Resolved? resolved)
    {
        using var timeout = new CancellationTokenSource(PersistTimeout);
        var cancellationToken = timeout.Token;

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();

        var scan = await db.Scans.SingleOrDefaultAsync(
            s => s.Id == scanId && s.Status == ScanStatus.Running, cancellationToken);
        if (scan is null)
        {
            logger.LogWarning("Scan {ScanId} is not running any more; its result is dropped", scanId);
            return;
        }

        scan.FinishedAt = timeProvider.GetUtcNow();
        if (resolved is not null)
        {
            scan.ScannedTag = resolved.Tag;
            scan.ScannedCommit = resolved.Commit;
        }

        switch (outcome)
        {
            case ScanOutcome.Completed completed:
                scan.Status = ScanStatus.Completed;
                Apply(scan, completed.Findings, [], completed.TrivyVersion, completed.DbUpdatedAt);
                break;
            case ScanOutcome.Incomplete incomplete:
                scan.Status = ScanStatus.Incomplete;
                Apply(scan, incomplete.Findings, incomplete.MissingLockFiles, incomplete.TrivyVersion, incomplete.DbUpdatedAt);
                break;
            case ScanOutcome.Failed failed:
                scan.Status = ScanStatus.Failed;
                scan.FailureReason = failed.Reason;
                scan.FailureDetail = Sanitize(failed.Detail);
                logger.LogWarning("Scan {ScanId} failed ({Reason}): {Detail}", scanId, failed.Reason, scan.FailureDetail);
                break;
            default:
                throw new InvalidOperationException("Unknown scan outcome.");
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static void Apply(
        Scan scan, IReadOnlyList<ScanFinding> findings, IReadOnlyList<string> missingLockFiles,
        string trivyVersion, DateTimeOffset dbUpdatedAt)
    {
        scan.TrivyVersion = trivyVersion.Length > Scan.TrivyVersionMaxLength
            ? trivyVersion[..Scan.TrivyVersionMaxLength]
            : trivyVersion;
        scan.TrivyDbUpdatedAt = dbUpdatedAt;
        scan.MissingLockFiles = [.. missingLockFiles];
        foreach (var finding in findings)
        {
            scan.Findings.Add(finding);
        }
    }

    /// <summary>One trimmed line, without the Git token (plain or as the Basic-auth value) and not longer than the column.</summary>
    internal string Sanitize(string? detail)
    {
        var text = detail ?? "";
        var token = gitOptions.Value.Token;
        if (!string.IsNullOrEmpty(token))
        {
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{gitOptions.Value.UserName}:{token}"));
            text = text.Replace(basic, "***", StringComparison.Ordinal)
                .Replace(token, "***", StringComparison.Ordinal);
        }

        var line = text.Trim();
        var end = line.IndexOfAny(['\r', '\n']);
        if (end >= 0)
        {
            line = line[..end].TrimEnd();
        }

        return line.Length > Scan.FailureDetailMaxLength ? line[..Scan.FailureDetailMaxLength] : line;
    }
}
