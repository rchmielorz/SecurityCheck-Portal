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

    private long _currentScanId;

    /// <summary>The scan being processed right now, or null; the abandoned-scan sweep must not touch it.</summary>
    public long? CurrentScanId
    {
        get
        {
            var id = Interlocked.Read(ref _currentScanId);
            return id > 0 ? id : null;
        }
    }

    private sealed record Resolved(string Tag, string Commit);

    /// <param name="scanId">A scan already claimed by <see cref="ScanQueue.ClaimNextAsync"/> (status <c>Running</c>).</param>
    /// <remarks>
    /// When <paramref name="cancellationToken"/> is cancelled (the worker is stopping), the scan is marked
    /// <c>Failed(Interrupted)</c> if the database still answers, and the method returns without throwing. If the
    /// update is impossible the row stays <c>Running</c> and <see cref="ScanQueue.FailAbandonedAsync"/> (periodic)
    /// or <see cref="ScanQueue.RecoverInterruptedAsync"/> (next start) fixes it.
    /// </remarks>
    public async Task RunAsync(long scanId, CancellationToken cancellationToken)
    {
        var options = scanOptions.Value;
        var directory = WorkDirectory.ForScan(options, scanId);
        Interlocked.Exchange(ref _currentScanId, scanId);

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
            Interlocked.Exchange(ref _currentScanId, 0);
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

        // 4. Trivy (it also generates missing .NET lock files, looks for the ones still missing and decides between completed and incomplete).
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
            // The row stays Running; the periodic sweep (or the next start) marks it Interrupted.
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

        var finished = timeProvider.GetUtcNow();
        var scannedTag = resolved?.Tag;
        var scannedCommit = resolved?.Commit;

        ScanStatus status;
        ScanFailureReason? failureReason = null;
        string? failureDetail = null;
        string? trivyVersion = null;
        DateTimeOffset? dbUpdatedAt = null;
        IReadOnlyList<ScanFinding> findings = [];
        IReadOnlyList<UnscannedItem> unscanned = [];

        switch (outcome)
        {
            case ScanOutcome.Completed completed:
                status = ScanStatus.Completed;
                (findings, trivyVersion, dbUpdatedAt) = (completed.Findings, ClipVersion(completed.TrivyVersion), completed.DbUpdatedAt);
                break;
            case ScanOutcome.Incomplete incomplete:
                status = ScanStatus.Incomplete;
                (findings, trivyVersion, dbUpdatedAt) = (incomplete.Findings, ClipVersion(incomplete.TrivyVersion), incomplete.DbUpdatedAt);
                unscanned = incomplete.Unscanned;
                break;
            case ScanOutcome.Failed failed:
                status = ScanStatus.Failed;
                failureReason = failed.Reason;
                failureDetail = Sanitize(failed.Detail);
                logger.LogWarning("Scan {ScanId} failed ({Reason}): {Detail}", scanId, failed.Reason, failureDetail);
                break;
            default:
                throw new InvalidOperationException("Unknown scan outcome.");
        }

        // Failed(Interrupted) written by the sweep or by recovery is terminal: the status change is one conditional
        // UPDATE (WHERE Status = 'Running'), in the same transaction as the findings, so a late result can neither
        // overwrite it nor leave findings behind.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var updated = await db.Scans
            .Where(s => s.Id == scanId && s.Status == ScanStatus.Running)
            .ExecuteUpdateAsync(set => set
                .SetProperty(s => s.Status, status)
                .SetProperty(s => s.FinishedAt, (DateTimeOffset?)finished)
                .SetProperty(s => s.ScannedTag, scannedTag)
                .SetProperty(s => s.ScannedCommit, scannedCommit)
                .SetProperty(s => s.FailureReason, failureReason)
                .SetProperty(s => s.FailureDetail, failureDetail)
                .SetProperty(s => s.TrivyVersion, trivyVersion)
                .SetProperty(s => s.TrivyDbUpdatedAt, dbUpdatedAt),
                cancellationToken);

        if (updated == 0)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            logger.LogWarning("Scan {ScanId} is not running any more; its result is dropped", scanId);
            return;
        }

        foreach (var finding in findings)
        {
            finding.ScanId = scanId;
            db.ScanFindings.Add(finding);
        }

        var seenPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in unscanned)
        {
            var path = Clip(item.Path, UnscannedItem.PathMaxLength);
            if (!seenPaths.Add(path))
            {
                continue;
            }

            var detail = item.Detail is null ? null : Clip(Sanitize(item.Detail), UnscannedItem.DetailMaxLength);
            db.ScanUnscannedItems.Add(new ScanUnscannedItem
            {
                ScanId = scanId,
                Path = path,
                Reason = item.Reason,
                Detail = string.IsNullOrEmpty(detail) ? null : detail,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Cuts to <paramref name="maxLength"/> without leaving half of a surrogate pair at the end.</summary>
    private static string Clip(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        var cut = maxLength > 0 && char.IsHighSurrogate(text[maxLength - 1]) ? maxLength - 1 : maxLength;
        return text[..cut];
    }

    private static string ClipVersion(string trivyVersion)
        => trivyVersion.Length > Scan.TrivyVersionMaxLength ? trivyVersion[..Scan.TrivyVersionMaxLength] : trivyVersion;

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

        // PostgreSQL rejects \0 in text, and the whole result transaction would fail with it.
        if (line.Any(char.IsControl))
        {
            line = new string(line.Where(c => !char.IsControl(c)).ToArray());
        }

        return Clip(line, Scan.FailureDetailMaxLength);
    }
}
