using Microsoft.EntityFrameworkCore;
using Npgsql;
using securitycheck_portal.Core.Data;

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

    private async Task AddPatternAsync(long repositoryId, string pattern, bool isActive = true)
    {
        await using var scope = factory.CreateDbScope(out var db);
        db.VersionPatterns.Add(NewPattern(repositoryId, pattern, isActive));
        await db.SaveChangesAsync();
    }

    private static async Task AssertPostgresErrorAsync(string sqlState, Func<Task> action)
    {
        var exception = await Assert.ThrowsAsync<DbUpdateException>(action);
        var postgres = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(sqlState, postgres.SqlState);
    }
}
