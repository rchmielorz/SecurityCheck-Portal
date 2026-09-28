using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using securitycheck_portal.Auth;
using securitycheck_portal.Core.Data;
using securitycheck_portal.Core.Git;

namespace securitycheck_portal.Repositories;

/// <summary>
/// Repositories, their version patterns and the change log. Every change is stored together with its
/// audit event; constraint violations the database catches (e.g. two requests at once) become 409, not 500.
/// </summary>
public static class RepositoryEndpoints
{
    private const int MaxEvents = 200;

    public static IEndpointRouteBuilder MapRepositoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api");

        var repos = api.MapGroup("/repos");
        repos.MapGet("", ListRepositoriesAsync);
        repos.MapPost("", AddRepositoryAsync);
        repos.MapGet("/{id:long}", GetRepositoryAsync);
        repos.MapDelete("/{id:long}", DeleteRepositoryAsync);
        repos.MapGet("/{id:long}/events", GetEventsAsync);
        repos.MapPost("/{id:long}/patterns", AddPatternAsync);

        var patterns = api.MapGroup("/patterns");
        patterns.MapDelete("/{id:long}", DeletePatternAsync);
        patterns.MapPost("/{id:long}/deactivate", DeactivatePatternAsync);
        patterns.MapPost("/{id:long}/activate", ActivatePatternAsync);
        patterns.MapPost("/{id:long}/resolve", ResolvePatternAsync);

        return endpoints;
    }

    private static async Task<IResult> ListRepositoriesAsync(PortalDbContext db, CancellationToken cancellationToken)
    {
        var repositories = await db.Repositories
            .AsNoTracking()
            .OrderBy(r => (r.Name ?? r.Url).ToLower())
            .ThenBy(r => r.Url)
            .Select(r => new RepositorySummary(r.Id, r.Url, r.Name, r.Patterns.Count(p => p.IsActive)))
            .ToListAsync(cancellationToken);

        return Results.Ok(repositories);
    }

    private static async Task<IResult> AddRepositoryAsync(
        AddRepositoryRequest request,
        ClaimsPrincipal user,
        PortalDbContext db,
        IOptions<GitOptions> gitOptions,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (GetActor(user) is not { } actor)
        {
            return Results.Unauthorized();
        }

        if (!RepositoryUrl.TryNormalize(request.Url, gitOptions.Value.AllowedHosts, out var url))
        {
            return Results.BadRequest();
        }

        var name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim();
        if (name is { Length: > Repository.NameMaxLength })
        {
            return Results.BadRequest();
        }

        if (await db.Repositories.AnyAsync(r => r.Url == url, cancellationToken))
        {
            return Results.Conflict();
        }

        var now = timeProvider.GetUtcNow();
        var repository = new Repository { Url = url, Name = name, CreatedAt = now, CreatedBy = actor };

        var outcome = await InsertWithEventAsync(db, repository, () => new AuditEvent
        {
            OccurredAt = now,
            Actor = actor,
            Action = AuditAction.RepositoryAdded,
            RepositoryId = repository.Id,
            RepositoryUrl = repository.Url,
        }, cancellationToken);

        if (outcome != SaveOutcome.Saved)
        {
            return ToResult(outcome);
        }

        return Results.Created($"/api/repos/{repository.Id}", ToDetails(repository, []));
    }

    private static async Task<IResult> GetRepositoryAsync(long id, PortalDbContext db, CancellationToken cancellationToken)
    {
        var repository = await db.Repositories
            .AsNoTracking()
            .Include(r => r.Patterns.OrderBy(p => p.Id))
            .SingleOrDefaultAsync(r => r.Id == id, cancellationToken);

        return repository is null
            ? Results.NotFound()
            : Results.Ok(ToDetails(repository, repository.Patterns));
    }

    private static async Task<IResult> DeleteRepositoryAsync(
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

        var repository = await db.Repositories.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (repository is null)
        {
            return Results.NotFound();
        }

        // Inactive patterns count too; the Restrict foreign key backs this check up.
        if (await db.VersionPatterns.AnyAsync(p => p.RepositoryId == id, cancellationToken))
        {
            return Results.Conflict();
        }

        db.Repositories.Remove(repository);
        db.AuditEvents.Add(new AuditEvent
        {
            OccurredAt = timeProvider.GetUtcNow(),
            Actor = actor,
            Action = AuditAction.RepositoryDeleted,
            RepositoryId = repository.Id,
            RepositoryUrl = repository.Url,
        });

        var outcome = await TrySaveAsync(db, cancellationToken);
        return outcome == SaveOutcome.Saved ? Results.NoContent() : ToResult(outcome);
    }

    private static async Task<IResult> GetEventsAsync(long id, PortalDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Repositories.AnyAsync(r => r.Id == id, cancellationToken))
        {
            return Results.NotFound();
        }

        var events = await db.AuditEvents
            .AsNoTracking()
            .Where(e => e.RepositoryId == id)
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .Take(MaxEvents)
            .ToListAsync(cancellationToken);

        return Results.Ok(events.Select(e =>
            new AuditEventResponse(e.Id, e.OccurredAt, e.Actor, e.Action.ToString(), e.Pattern)));
    }

    private static async Task<IResult> AddPatternAsync(
        long id,
        AddPatternRequest request,
        ClaimsPrincipal user,
        PortalDbContext db,
        PatternResolutionService resolutionService,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (GetActor(user) is not { } actor)
        {
            return Results.Unauthorized();
        }

        if (!VersionPatternSpec.TryParse(request.Pattern, out var spec))
        {
            return Results.BadRequest();
        }

        var repository = await db.Repositories.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (repository is null)
        {
            return Results.NotFound();
        }

        if (await FindPatternConflictAsync(db, id, spec.Canonical, cancellationToken) is { } conflict)
        {
            return conflict;
        }

        var now = timeProvider.GetUtcNow();
        var pattern = new VersionPattern
        {
            RepositoryId = id,
            Pattern = spec.Canonical,
            CreatedAt = now,
            CreatedBy = actor,
        };

        var outcome = await InsertWithEventAsync(db, pattern, () => new AuditEvent
        {
            OccurredAt = now,
            Actor = actor,
            Action = AuditAction.PatternAdded,
            RepositoryId = id,
            PatternId = pattern.Id,
            RepositoryUrl = repository.Url,
            Pattern = pattern.Pattern,
        }, cancellationToken);

        if (outcome == SaveOutcome.UniqueViolation)
        {
            // Added by a concurrent request after the check above; report it like the check would have.
            db.ChangeTracker.Clear();
            return await FindPatternConflictAsync(db, id, spec.Canonical, cancellationToken)
                ?? Results.Conflict(new PatternConflict(PatternConflict.Exists));
        }

        if (outcome == SaveOutcome.ForeignKeyViolation)
        {
            // The repository was deleted by a concurrent request after it was read.
            return Results.NotFound();
        }

        if (outcome != SaveOutcome.Saved)
        {
            return ToResult(outcome);
        }

        // The pattern is kept whatever the Git server answers; an Error state is a stored result too.
        await ResolveAsync(pattern, repository.Url, spec, resolutionService, timeProvider, cancellationToken);
        outcome = await TrySaveAsync(db, cancellationToken);
        if (outcome != SaveOutcome.Saved)
        {
            return ToResult(outcome);
        }

        return Results.Created($"/api/patterns/{pattern.Id}", PatternResponse.From(pattern));
    }

    private static async Task<IResult> DeletePatternAsync(
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

        var pattern = await FindPatternAsync(db, id, cancellationToken);
        if (pattern is null)
        {
            return Results.NotFound();
        }

        db.VersionPatterns.Remove(pattern);
        db.AuditEvents.Add(CreatePatternEvent(pattern, AuditAction.PatternDeleted, actor, timeProvider));

        var outcome = await TrySaveAsync(db, cancellationToken);
        return outcome == SaveOutcome.Saved ? Results.NoContent() : ToResult(outcome);
    }

    private static Task<IResult> DeactivatePatternAsync(
        long id,
        ClaimsPrincipal user,
        PortalDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
        SetPatternActiveAsync(id, isActive: false, user, db, timeProvider, cancellationToken);

    private static Task<IResult> ActivatePatternAsync(
        long id,
        ClaimsPrincipal user,
        PortalDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
        SetPatternActiveAsync(id, isActive: true, user, db, timeProvider, cancellationToken);

    private static async Task<IResult> SetPatternActiveAsync(
        long id,
        bool isActive,
        ClaimsPrincipal user,
        PortalDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (GetActor(user) is not { } actor)
        {
            return Results.Unauthorized();
        }

        var pattern = await FindPatternAsync(db, id, cancellationToken);
        if (pattern is null)
        {
            return Results.NotFound();
        }

        // Already in the requested state: nothing changed, so nothing is logged.
        if (pattern.IsActive == isActive)
        {
            return Results.Ok(PatternResponse.From(pattern));
        }

        pattern.IsActive = isActive;
        db.AuditEvents.Add(CreatePatternEvent(
            pattern,
            isActive ? AuditAction.PatternActivated : AuditAction.PatternDeactivated,
            actor,
            timeProvider));

        var outcome = await TrySaveAsync(db, cancellationToken);
        return outcome == SaveOutcome.Saved ? Results.Ok(PatternResponse.From(pattern)) : ToResult(outcome);
    }

    private static async Task<IResult> ResolvePatternAsync(
        long id,
        PortalDbContext db,
        PatternResolutionService resolutionService,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var pattern = await FindPatternAsync(db, id, cancellationToken);
        if (pattern is null)
        {
            return Results.NotFound();
        }

        if (!pattern.IsActive)
        {
            return Results.Conflict();
        }

        if (!VersionPatternSpec.TryParse(pattern.Pattern, out var spec))
        {
            throw new InvalidOperationException($"Stored pattern {pattern.Id} is not a valid version pattern.");
        }

        await ResolveAsync(pattern, pattern.Repository!.Url, spec, resolutionService, timeProvider, cancellationToken);

        var outcome = await TrySaveAsync(db, cancellationToken);
        return outcome == SaveOutcome.Saved ? Results.Ok(PatternResponse.From(pattern)) : ToResult(outcome);
    }

    private static string? GetActor(ClaimsPrincipal user)
    {
        var userName = user.FindFirstValue(JwtIssuer.UserNameClaim);
        return string.IsNullOrEmpty(userName) ? null : userName;
    }

    private static Task<VersionPattern?> FindPatternAsync(PortalDbContext db, long id, CancellationToken cancellationToken) =>
        db.VersionPatterns
            .Include(p => p.Repository)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    private static async Task<IResult?> FindPatternConflictAsync(
        PortalDbContext db, long repositoryId, string pattern, CancellationToken cancellationToken)
    {
        var existing = await db.VersionPatterns
            .AsNoTracking()
            .Where(p => p.RepositoryId == repositoryId && p.Pattern == pattern)
            .Select(p => new { p.IsActive })
            .SingleOrDefaultAsync(cancellationToken);

        return existing switch
        {
            null => null,
            { IsActive: true } => Results.Conflict(new PatternConflict(PatternConflict.Exists)),
            _ => Results.Conflict(new PatternConflict(PatternConflict.Inactive)),
        };
    }

    private static AuditEvent CreatePatternEvent(
        VersionPattern pattern, AuditAction action, string actor, TimeProvider timeProvider) => new()
    {
        OccurredAt = timeProvider.GetUtcNow(),
        Actor = actor,
        Action = action,
        RepositoryId = pattern.RepositoryId,
        PatternId = pattern.Id,
        RepositoryUrl = pattern.Repository!.Url,
        Pattern = pattern.Pattern,
    };

    private static async Task ResolveAsync(
        VersionPattern pattern,
        string repositoryUrl,
        VersionPatternSpec spec,
        PatternResolutionService resolutionService,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var resolution = await resolutionService.ResolveAsync(repositoryUrl, spec, cancellationToken);

        (pattern.LastResolutionState, pattern.LastResolvedTag, pattern.LastResolvedCommit) = resolution switch
        {
            PatternResolution.Resolved resolved => (ResolutionState.Resolved, resolved.Tag, resolved.CommitSha),
            PatternResolution.NoMatch => (ResolutionState.NoMatch, null, null),
            PatternResolution.Ambiguous => (ResolutionState.Ambiguous, null, null),
            PatternResolution.Error => (ResolutionState.Error, (string?)null, (string?)null),
            _ => throw new InvalidOperationException("Unknown pattern resolution."),
        };
        pattern.LastResolvedAt = timeProvider.GetUtcNow();
    }

    /// <summary>
    /// Inserts <paramref name="entity"/> and the audit event built after its ID is known, in one
    /// transaction: the event references the generated ID through a plain column, not a navigation.
    /// </summary>
    private static async Task<SaveOutcome> InsertWithEventAsync(
        PortalDbContext db, object entity, Func<AuditEvent> createEvent, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        db.Add(entity);
        var outcome = await TrySaveAsync(db, cancellationToken);
        if (outcome != SaveOutcome.Saved)
        {
            return outcome;
        }

        db.AuditEvents.Add(createEvent());
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return SaveOutcome.Saved;
    }

    private static async Task<SaveOutcome> TrySaveAsync(PortalDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return SaveOutcome.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            // The row was deleted by another request after it was read.
            return SaveOutcome.NotFound;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation,
        } postgres)
        {
            return postgres.SqlState == PostgresErrorCodes.UniqueViolation
                ? SaveOutcome.UniqueViolation
                : SaveOutcome.ForeignKeyViolation;
        }
    }

    private static IResult ToResult(SaveOutcome outcome) => outcome switch
    {
        SaveOutcome.UniqueViolation or SaveOutcome.ForeignKeyViolation => Results.Conflict(),
        SaveOutcome.NotFound => Results.NotFound(),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };

    private static RepositoryDetails ToDetails(Repository repository, IEnumerable<VersionPattern> patterns) => new(
        repository.Id,
        repository.Url,
        repository.Name,
        repository.CreatedAt,
        repository.CreatedBy,
        patterns.Select(PatternResponse.From).ToList());

    private enum SaveOutcome
    {
        Saved,
        UniqueViolation,
        ForeignKeyViolation,
        NotFound,
    }
}
