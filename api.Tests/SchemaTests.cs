using Microsoft.EntityFrameworkCore;
using Npgsql;
using securitycheck_portal.Core.Data;
using securitycheck_portal.Core.Scanning;

namespace securitycheck_portal.Tests;

/// <summary>
/// Checks the migrated schema in a real PostgreSQL: uniqueness and the delete restriction are enforced
/// by the database (not only by application code), and audit events outlive their repository.
/// </summary>
public sealed class SchemaTests(DatabasePortalFactory factory) : IClassFixture<DatabasePortalFactory>
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [SkippableFact]
    public async Task Duplicate_repository_url_violates_a_unique_constraint()
    {
        factory.SkipIfDatabaseUnavailable();
        var url = NewUrl();
        await AddRepositoryAsync(url);

        await using var scope = factory.CreateDbScope(out var db);
        db.Repositories.Add(NewRepository(url));

        await AssertPostgresErrorAsync(PostgresErrorCodes.UniqueViolation, () => db.SaveChangesAsync());
    }

    [SkippableFact]
    public async Task Duplicate_pattern_of_a_repository_violates_a_unique_constraint_even_when_inactive()
    {
        factory.SkipIfDatabaseUnavailable();
        var repositoryId = await AddRepositoryAsync(NewUrl());
        await AddPatternAsync(repositoryId, "2.1.*", isActive: false);

        await using var scope = factory.CreateDbScope(out var db);
        db.VersionPatterns.Add(NewPattern(repositoryId, "2.1.*"));

        await AssertPostgresErrorAsync(PostgresErrorCodes.UniqueViolation, () => db.SaveChangesAsync());
    }

    [SkippableFact]
    public async Task Same_pattern_in_another_repository_is_allowed()
    {
        factory.SkipIfDatabaseUnavailable();
        var firstId = await AddRepositoryAsync(NewUrl());
        var secondId = await AddRepositoryAsync(NewUrl());

        await AddPatternAsync(firstId, "2.1.*");
        await AddPatternAsync(secondId, "2.1.*");
    }

    [SkippableFact]
    public async Task Deleting_a_repository_that_has_a_pattern_violates_a_foreign_key()
    {
        factory.SkipIfDatabaseUnavailable();
        var repositoryId = await AddRepositoryAsync(NewUrl());
        await AddPatternAsync(repositoryId, "2.1.*");

        // A fresh context that does not track the pattern, so the database (not EF) has to refuse.
        await using var scope = factory.CreateDbScope(out var db);
        var repository = await db.Repositories.SingleAsync(r => r.Id == repositoryId);
        db.Repositories.Remove(repository);

        await AssertPostgresErrorAsync(PostgresErrorCodes.ForeignKeyViolation, () => db.SaveChangesAsync());
    }

    [SkippableFact]
    public async Task Deleting_a_repository_keeps_its_audit_events()
    {
        factory.SkipIfDatabaseUnavailable();
        var url = NewUrl();
        var repositoryId = await AddRepositoryAsync(url);

        await using (var scope = factory.CreateDbScope(out var db))
        {
            db.AuditEvents.Add(new AuditEvent
            {
                OccurredAt = Now,
                Actor = "alice",
                Action = AuditAction.RepositoryAdded,
                RepositoryId = repositoryId,
                RepositoryUrl = url,
            });
            await db.SaveChangesAsync();
        }

        await using (var scope = factory.CreateDbScope(out var db))
        {
            var repository = await db.Repositories.SingleAsync(r => r.Id == repositoryId);
            db.Repositories.Remove(repository);
            await db.SaveChangesAsync();
        }

        await using (var scope = factory.CreateDbScope(out var db))
        {
            Assert.False(await db.Repositories.AnyAsync(r => r.Id == repositoryId));
            var auditEvent = await db.AuditEvents.SingleAsync(e => e.RepositoryId == repositoryId);
            Assert.Equal(AuditAction.RepositoryAdded, auditEvent.Action);
            Assert.Equal(url, auditEvent.RepositoryUrl);
        }
    }

    [SkippableTheory]
    [InlineData(ScanStatus.Queued)]
    [InlineData(ScanStatus.Running)]
    public async Task Second_active_scan_for_the_same_pattern_violates_a_unique_constraint(ScanStatus firstStatus)
    {
        factory.SkipIfDatabaseUnavailable();
        var patternId = NewPatternId();
        await AddScanAsync(patternId, firstStatus);

        foreach (var secondStatus in new[] { ScanStatus.Queued, ScanStatus.Running })
        {
            await using var scope = factory.CreateDbScope(out var db);
            db.Scans.Add(NewScan(patternId, secondStatus));

            await AssertPostgresErrorAsync(PostgresErrorCodes.UniqueViolation, () => db.SaveChangesAsync());
        }
    }

    [SkippableTheory]
    [InlineData(ScanStatus.Completed)]
    [InlineData(ScanStatus.Failed)]
    [InlineData(ScanStatus.Incomplete)]
    public async Task New_scan_is_allowed_after_the_previous_one_finished(ScanStatus finishedStatus)
    {
        factory.SkipIfDatabaseUnavailable();
        var patternId = NewPatternId();
        await AddScanAsync(patternId, finishedStatus);
        await AddScanAsync(patternId, finishedStatus);

        await AddScanAsync(patternId, ScanStatus.Queued);
    }

    [SkippableFact]
    public async Task Duplicate_finding_in_a_scan_violates_a_unique_constraint()
    {
        factory.SkipIfDatabaseUnavailable();
        var scanId = await AddScanAsync(NewPatternId(), ScanStatus.Completed);
        await AddFindingAsync(scanId);

        await using var scope = factory.CreateDbScope(out var db);
        db.ScanFindings.Add(NewFinding(scanId));

        await AssertPostgresErrorAsync(PostgresErrorCodes.UniqueViolation, () => db.SaveChangesAsync());
    }

    [SkippableFact]
    public async Task Deleting_a_scan_deletes_its_findings()
    {
        factory.SkipIfDatabaseUnavailable();
        var scanId = await AddScanAsync(NewPatternId(), ScanStatus.Completed);
        await AddFindingAsync(scanId);

        await using (var scope = factory.CreateDbScope(out var db))
        {
            var scan = await db.Scans.SingleAsync(s => s.Id == scanId);
            db.Scans.Remove(scan);
            await db.SaveChangesAsync();
        }

        await using (var scope = factory.CreateDbScope(out var db))
        {
            Assert.False(await db.ScanFindings.AnyAsync(f => f.ScanId == scanId));
        }
    }

    [SkippableFact]
    public async Task Duplicate_unscanned_path_in_a_scan_violates_a_unique_constraint()
    {
        factory.SkipIfDatabaseUnavailable();
        var scanId = await AddScanAsync(NewPatternId(), ScanStatus.Incomplete);
        await AddUnscannedItemAsync(scanId);

        await using var scope = factory.CreateDbScope(out var db);
        db.ScanUnscannedItems.Add(NewUnscannedItem(scanId));

        await AssertPostgresErrorAsync(PostgresErrorCodes.UniqueViolation, () => db.SaveChangesAsync());
    }

    [SkippableFact]
    public async Task Deleting_a_scan_deletes_its_unscanned_items()
    {
        factory.SkipIfDatabaseUnavailable();
        var scanId = await AddScanAsync(NewPatternId(), ScanStatus.Incomplete);
        await AddUnscannedItemAsync(scanId);

        await using (var scope = factory.CreateDbScope(out var db))
        {
            var scan = await db.Scans.SingleAsync(s => s.Id == scanId);
            db.Scans.Remove(scan);
            await db.SaveChangesAsync();
        }

        await using (var scope = factory.CreateDbScope(out var db))
        {
            Assert.False(await db.ScanUnscannedItems.AnyAsync(i => i.ScanId == scanId));
        }
    }

    [SkippableFact]
    public async Task Deleting_a_pattern_and_its_repository_keeps_the_scan_and_its_findings()
    {
        factory.SkipIfDatabaseUnavailable();
        var url = NewUrl();
        var repositoryId = await AddRepositoryAsync(url);
        var patternId = await AddPatternAsync(repositoryId, "2.1.*");
        var scanId = await AddScanAsync(patternId, ScanStatus.Completed, repositoryId, url);
        await AddFindingAsync(scanId);

        await using (var scope = factory.CreateDbScope(out var db))
        {
            db.VersionPatterns.Remove(await db.VersionPatterns.SingleAsync(p => p.Id == patternId));
            await db.SaveChangesAsync();
            db.Repositories.Remove(await db.Repositories.SingleAsync(r => r.Id == repositoryId));
            await db.SaveChangesAsync();
        }

        await using (var scope = factory.CreateDbScope(out var db))
        {
            var scan = await db.Scans.SingleAsync(s => s.Id == scanId);
            Assert.Equal(patternId, scan.PatternId);
            Assert.Equal(url, scan.RepositoryUrl);
            Assert.True(await db.ScanFindings.AnyAsync(f => f.ScanId == scanId));
        }
    }

    private static long _nextPatternId = 1_000_000;

    // Scans reference patterns by a plain column, so unique ids are enough for tests that need no pattern row.
    private static long NewPatternId() => Interlocked.Increment(ref _nextPatternId);

    private static Scan NewScan(long patternId, ScanStatus status, long repositoryId = 0, string? url = null) =>
        new()
        {
            PatternId = patternId,
            RepositoryId = repositoryId,
            RepositoryUrl = url ?? NewUrl(),
            Pattern = "2.1.*",
            Status = status,
            RequestedBy = "alice",
            RequestedAt = Now,
        };

    private static ScanFinding NewFinding(long scanId) =>
        new()
        {
            ScanId = scanId,
            Library = "lodash",
            InstalledVersion = "4.17.20",
            VulnerabilityId = "CVE-2021-23337",
            Severity = FindingSeverity.High,
        };

    private static ScanUnscannedItem NewUnscannedItem(long scanId) =>
        new()
        {
            ScanId = scanId,
            Path = "src/app/packages.lock.json",
            Reason = UnscannedReason.NoLockFile,
        };

    private async Task AddUnscannedItemAsync(long scanId)
    {
        await using var scope = factory.CreateDbScope(out var db);
        db.ScanUnscannedItems.Add(NewUnscannedItem(scanId));
        await db.SaveChangesAsync();
    }

    private async Task<long> AddScanAsync(long patternId, ScanStatus status, long repositoryId = 0, string? url = null)
    {
        await using var scope = factory.CreateDbScope(out var db);
        var scan = NewScan(patternId, status, repositoryId, url);
        db.Scans.Add(scan);
        await db.SaveChangesAsync();
        return scan.Id;
    }

    private async Task AddFindingAsync(long scanId)
    {
        await using var scope = factory.CreateDbScope(out var db);
        db.ScanFindings.Add(NewFinding(scanId));
        await db.SaveChangesAsync();
    }

    private static string NewUrl() => $"https://git.example.invalid/team/{Guid.NewGuid():N}";

    private static Repository NewRepository(string url) =>
        new() { Url = url, CreatedAt = Now, CreatedBy = "alice" };

    private static VersionPattern NewPattern(long repositoryId, string pattern, bool isActive = true) =>
        new() { RepositoryId = repositoryId, Pattern = pattern, IsActive = isActive, CreatedAt = Now, CreatedBy = "alice" };

    private async Task<long> AddRepositoryAsync(string url)
    {
        await using var scope = factory.CreateDbScope(out var db);
        var repository = NewRepository(url);
        db.Repositories.Add(repository);
        await db.SaveChangesAsync();
        return repository.Id;
    }

    private async Task<long> AddPatternAsync(long repositoryId, string pattern, bool isActive = true)
    {
        await using var scope = factory.CreateDbScope(out var db);
        var versionPattern = NewPattern(repositoryId, pattern, isActive);
        db.VersionPatterns.Add(versionPattern);
        await db.SaveChangesAsync();
        return versionPattern.Id;
    }

    private static async Task AssertPostgresErrorAsync(string sqlState, Func<Task> action)
    {
        var exception = await Assert.ThrowsAsync<DbUpdateException>(action);
        var postgres = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(sqlState, postgres.SqlState);
    }
}
