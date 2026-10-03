using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using securitycheck_portal.Core.Data;
using securitycheck_portal.Core.Git;

namespace securitycheck_portal.Core.Scanning;

/// <summary>
/// The queue is the <c>Scans</c> table: a scan is claimed by moving it from <c>Queued</c> to <c>Running</c> in one
/// statement, and a worker that died leaves <c>Running</c> rows that are failed on the next start.
/// </summary>
public sealed class ScanQueue(
    IServiceScopeFactory scopeFactory,
    IOptions<ScanOptions> options,
    IOptions<GitOptions> gitOptions,
    TimeProvider timeProvider,
    ILogger<ScanQueue> logger)
{
    public const string InterruptedDetail = "Scan was interrupted because the worker stopped.";
    public const string AbandonedDetail = "Scan abandoned: no result recorded in time.";

    private static readonly TimeSpan AbandonedMargin = TimeSpan.FromMinutes(5);

    // git ls-remote (pattern resolution) and git rev-parse (after the clone), both limited by Git:TimeoutSeconds.
    private const int GitShortCallsPerScan = 2;

    // SKIP LOCKED: a concurrent claim moves on to the next row instead of waiting for, or taking, the same one.
    private const string ClaimSql = """
        UPDATE "Scans"
        SET "Status" = 'Running', "StartedAt" = @now
        WHERE "Id" = (
            SELECT "Id" FROM "Scans"
            WHERE "Status" = 'Queued'
            ORDER BY "RequestedAt", "Id"
            LIMIT 1
            FOR UPDATE SKIP LOCKED)
        RETURNING "Id"
        """;

    /// <summary>Claims the oldest queued scan, or returns null when there is none.</summary>
    public async Task<long?> ClaimNextAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();

        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            await using var command = new NpgsqlCommand(ClaimSql, connection);
            command.Parameters.AddWithValue("now", timeProvider.GetUtcNow());

            var id = await command.ExecuteScalarAsync(cancellationToken);
            return id is long claimed ? claimed : null;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// Run once when the worker starts, before the first claim: scans left <c>Running</c> by a previous
    /// process become <c>Failed(Interrupted)</c>, and their checkouts and stale reports are removed.
    /// </summary>
    public async Task<int> RecoverInterruptedAsync(CancellationToken cancellationToken)
    {
        int interrupted;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var now = timeProvider.GetUtcNow();

            interrupted = await db.Scans
                .Where(s => s.Status == ScanStatus.Running)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.Status, ScanStatus.Failed)
                    .SetProperty(s => s.FailureReason, (ScanFailureReason?)ScanFailureReason.Interrupted)
                    .SetProperty(s => s.FailureDetail, InterruptedDetail)
                    .SetProperty(s => s.FinishedAt, (DateTimeOffset?)now),
                    cancellationToken);
        }

        if (interrupted > 0)
        {
            logger.LogWarning("{Count} scan(s) were left running by a previous worker and are now failed", interrupted);
        }

        WorkDirectory.DeleteOrphans(options.Value, logger);
        return interrupted;
    }

    /// <summary>
    /// How long a scan may stay <c>Running</c> before the sweep treats it as abandoned: the sum of every timeout
    /// the pipeline can spend (git ls-remote and rev-parse use <c>Git:TimeoutSeconds</c>, the clone, the DB update
    /// and the scan their own limits, <c>trivy version</c> runs twice) plus a margin.
    /// </summary>
    public static TimeSpan AbandonedAfter(ScanOptions options, GitOptions gitOptions)
        => TimeSpan.FromMinutes(options.CloneTimeoutMinutes + options.ScanTimeoutMinutes + options.DbUpdateTimeoutMinutes)
           + TimeSpan.FromSeconds(gitOptions.TimeoutSeconds * GitShortCallsPerScan)
           + TrivyScanner.VersionTimeout * TrivyScanner.VersionCallsPerScan
           + AbandonedMargin;

    /// <summary>
    /// Periodic safety net for a result that could not be stored (the database failed after the claim): one UPDATE
    /// fails every <c>Running</c> scan older than <see cref="AbandonedAfter"/> as <c>Failed(Interrupted)</c>,
    /// except <paramref name="inFlightScanId"/>, the scan this worker is processing right now.
    /// </summary>
    /// <remarks>
    /// The exclusion is a defensive parameter only: the worker loop is serial, so the sweep never runs while a scan
    /// is in flight. What makes the sweep safe is that exactly one worker exists (<see cref="WorkerLock"/>); if the
    /// loop ever becomes concurrent, the exclusion is already wired.
    /// </remarks>
    public async Task<int> FailAbandonedAsync(long? inFlightScanId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var now = timeProvider.GetUtcNow();
        var cutoff = now - AbandonedAfter(options.Value, gitOptions.Value);
        var excluded = inFlightScanId ?? -1;

        var abandoned = await db.Scans
            .Where(s => s.Status == ScanStatus.Running && s.StartedAt < cutoff && s.Id != excluded)
            .ExecuteUpdateAsync(set => set
                .SetProperty(s => s.Status, ScanStatus.Failed)
                .SetProperty(s => s.FailureReason, (ScanFailureReason?)ScanFailureReason.Interrupted)
                .SetProperty(s => s.FailureDetail, AbandonedDetail)
                .SetProperty(s => s.FinishedAt, (DateTimeOffset?)now),
                cancellationToken);

        if (abandoned > 0)
        {
            logger.LogWarning("{Count} abandoned running scan(s) were failed", abandoned);
        }

        return abandoned;
    }
}
