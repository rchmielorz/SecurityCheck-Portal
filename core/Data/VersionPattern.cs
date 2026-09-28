namespace securitycheck_portal.Core.Data;

/// <summary>Outcome of the last attempt to resolve a pattern to a tag and commit.</summary>
public enum ResolutionState
{
    Resolved,
    NoMatch,
    Ambiguous,
    Error,
}

/// <summary>
/// A version pattern (e.g. <c>2.1.*</c>) of a repository. Deactivation keeps the row and its last
/// result; the pattern is unique per repository whether active or not.
/// </summary>
public sealed class VersionPattern
{
    public const int PatternMaxLength = 32;
    public const int TagMaxLength = 64;
    public const int CommitLength = 40;

    public long Id { get; set; }

    public long RepositoryId { get; set; }

    public Repository? Repository { get; set; }

    /// <summary>Canonical pattern text, e.g. <c>2.1.*</c>.</summary>
    public required string Pattern { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Login (the <c>sub</c> claim) of the user who added the pattern.</summary>
    public required string CreatedBy { get; set; }

    /// <summary>Null until the pattern has been resolved once.</summary>
    public ResolutionState? LastResolutionState { get; set; }

    public string? LastResolvedTag { get; set; }

    /// <summary>Full 40-character commit SHA of <see cref="LastResolvedTag"/>.</summary>
    public string? LastResolvedCommit { get; set; }

    public DateTimeOffset? LastResolvedAt { get; set; }
}
