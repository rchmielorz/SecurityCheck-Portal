using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using securitycheck_portal.Auth;
using securitycheck_portal.Core.Data;

namespace securitycheck_portal.Scans;

/// <summary>
/// Requesting a scan and reading its result. The API only stores the request (status Queued) together
/// with its audit event; the worker runs it. The partial unique index on active scans decides a race
/// between two requests, and the loser gets 409 with the winner's ID, never 500.
/// </summary>
public static class ScanEndpoints
{
    public static IEndpointRouteBuilder MapScanEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api");
        api.MapPost("/patterns/{id:long}/scans", RequestScanAsync);
        api.MapGet("/scans/{id:long}", GetScanAsync);
        return endpoints;
    }

    private static async Task<IResult> RequestScanAsync(
        long id,
        ClaimsPrincipal user,
        PortalDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (GetActor(user) is not { } actor)
        {
            return Results.Unauthorized();
        }

        var pattern = await db.VersionPatterns
            .AsNoTracking()
            .Include(p => p.Repository)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (pattern is null)
        {
            return Results.NotFound();
        }

        if (!pattern.IsActive)
        {
            return Results.Conflict(new ScanConflict(ScanConflict.Inactive));
        }

        if (await FindActiveScanIdAsync(db, id, cancellationToken) is { } activeId)
        {
            return Results.Conflict(new ScanConflict(ScanConflict.Active, activeId));
        }

        var now = timeProvider.GetUtcNow();
        var scan = new Scan
        {
            PatternId = pattern.Id,
            RepositoryId = pattern.RepositoryId,
            RepositoryUrl = pattern.Repository!.Url,
            Pattern = pattern.Pattern,
            Status = ScanStatus.Queued,
            RequestedBy = actor,
            RequestedAt = now,
        };

        var saved = await InsertWithEventAsync(db, scan, () => new AuditEvent
        {
            OccurredAt = now,
            Actor = actor,
            Action = AuditAction.ScanRequested,
            RepositoryId = scan.RepositoryId,
            PatternId = scan.PatternId,
            RepositoryUrl = scan.RepositoryUrl,
            Pattern = scan.Pattern,
        }, cancellationToken);

        if (!saved)
        {
            // Another request queued a scan for this pattern after the check above.
            db.ChangeTracker.Clear();
            var existingId = await FindActiveScanIdAsync(db, id, cancellationToken);
            return Results.Conflict(new ScanConflict(ScanConflict.Active, existingId));
        }

        return Results.Accepted($"/api/scans/{scan.Id}", new ScanResponse(
            scan.Id,
            scan.PatternId,
            scan.RepositoryId,
            scan.RepositoryUrl,
            scan.Pattern,
            scan.Status.ToString(),
            scan.RequestedBy,
            scan.RequestedAt));
    }

    private static async Task<IResult> GetScanAsync(long id, PortalDbContext db, CancellationToken cancellationToken)
    {
        var scan = await db.Scans
            .AsNoTracking()
            .Include(s => s.Findings)
            .SingleOrDefaultAsync(s => s.Id == id, cancellationToken);

        return scan is null ? Results.NotFound() : Results.Ok(ToDetails(scan));
    }

    private static ScanDetails ToDetails(Scan scan)
    {
        // FindingSeverity is declared Critical..Unknown, so its numeric value is the sort rank.
        var findings = scan.Findings
            .OrderBy(f => (int)f.Severity)
            .ThenBy(f => f.Library, StringComparer.Ordinal)
            .ThenBy(f => f.VulnerabilityId, StringComparer.Ordinal)
            .ThenBy(f => f.InstalledVersion, StringComparer.Ordinal)
            .Select(f => new ScanFindingResponse(
                f.Library,
                f.InstalledVersion,
                f.VulnerabilityId,
                f.Severity.ToString(),
                f.FixedVersion,
                f.Title,
                f.Targets))
            .ToList();

        return new ScanDetails(
            scan.Id,
            scan.PatternId,
            scan.RepositoryId,
            scan.RepositoryUrl,
            scan.Pattern,
            scan.Status.ToString(),
            scan.FailureReason?.ToString(),
            scan.FailureDetail,
            scan.RequestedBy,
            scan.RequestedAt,
            scan.StartedAt,
            scan.FinishedAt,
            scan.ScannedTag,
            scan.ScannedCommit,
            scan.TrivyVersion,
            scan.TrivyDbUpdatedAt,
            scan.MissingLockFiles,
            findings);
    }

    private static string? GetActor(ClaimsPrincipal user)
    {
        var userName = user.FindFirstValue(JwtIssuer.UserNameClaim);
        return string.IsNullOrEmpty(userName) ? null : userName;
    }

    private static Task<long?> FindActiveScanIdAsync(PortalDbContext db, long patternId, CancellationToken cancellationToken) =>
        db.Scans
            .AsNoTracking()
            .Where(s => s.PatternId == patternId && (s.Status == ScanStatus.Queued || s.Status == ScanStatus.Running))
            .Select(s => (long?)s.Id)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Inserts the scan and its audit event in one transaction (the event needs the generated scan ID).
    /// Returns false when the partial unique index rejected the scan, i.e. another scan is active.
    /// </summary>
    private static async Task<bool> InsertWithEventAsync(
        PortalDbContext db, Scan scan, Func<AuditEvent> createEvent, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        db.Scans.Add(scan);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
        })
        {
            return false;
        }

        db.AuditEvents.Add(createEvent());
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
