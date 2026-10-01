using securitycheck_portal.Core.Data;

namespace securitycheck_portal.Repositories;

public sealed record AddRepositoryRequest(string? Url, string? Name);

public sealed record AddPatternRequest(string? Pattern);

public sealed record RepositorySummary(long Id, string Url, string? Name, int ActivePatternCount);

public sealed record RepositoryDetails(
    long Id,
    string Url,
    string? Name,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    IReadOnlyList<PatternResponse> Patterns);

/// <summary>Last resolution of a pattern; <see cref="State"/> is a <see cref="ResolutionState"/> name.</summary>
public sealed record ResolutionResponse(string State, string? Tag, string? Commit, DateTimeOffset? ResolvedAt);

/// <summary>
/// Latest scan of a pattern (the one with the highest ID); <see cref="Status"/> is a <see cref="ScanStatus"/>
/// name. <see cref="FindingsCount"/> counts stored findings, so it is 0 for scans that have none (not
/// finished, failed, or clean); <see cref="ScannedTag"/> is null until the worker has resolved the pattern.
/// </summary>
public sealed record LatestScanResponse(
    long Id,
    string Status,
    DateTimeOffset? FinishedAt,
    int FindingsCount,
    string? ScannedTag);

/// <summary><see cref="LatestScan"/> is filled only by <c>GET /api/repos/{id}</c>; other responses leave it null.</summary>
public sealed record PatternResponse(
    long Id,
    long RepositoryId,
    string Pattern,
    bool IsActive,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    ResolutionResponse? LastResolution,
    LatestScanResponse? LatestScan = null)
{
    public static PatternResponse From(VersionPattern pattern, LatestScanResponse? latestScan = null) => new(
        pattern.Id,
        pattern.RepositoryId,
        pattern.Pattern,
        pattern.IsActive,
        pattern.CreatedAt,
        pattern.CreatedBy,
        pattern.LastResolutionState is { } state
            ? new ResolutionResponse(state.ToString(), pattern.LastResolvedTag, pattern.LastResolvedCommit, pattern.LastResolvedAt)
            : null,
        latestScan);
}

/// <summary>A change log entry; <see cref="Action"/> is an <see cref="AuditAction"/> name.</summary>
public sealed record AuditEventResponse(long Id, DateTimeOffset OccurredAt, string Actor, string Action, string? Pattern);

/// <summary>Body of a 409 on adding a pattern: <see cref="Exists"/> or <see cref="Inactive"/>.</summary>
public sealed record PatternConflict(string Conflict)
{
    public const string Exists = "exists";
    public const string Inactive = "inactive";
}
