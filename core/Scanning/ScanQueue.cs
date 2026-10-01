using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using securitycheck_portal.Core.Data;

namespace securitycheck_portal.Core.Scanning;

/// <summary>
/// The queue is the <c>Scans</c> table: a scan is claimed by moving it from <c>Queued</c> to <c>Running</c> in one
/// statement, and a worker that died leaves <c>Running</c> rows that are failed on the next start.
/// </summary>
public sealed class ScanQueue(
    IServiceScopeFactory scopeFactory,
    IOptions<ScanOptions> options,
    TimeProvider timeProvider,
    ILogger<ScanQueue> logger)
{
    public const string InterruptedDetail = "Scan was interrupted because the worker stopped.";

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
}
