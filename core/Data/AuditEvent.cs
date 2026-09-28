namespace securitycheck_portal.Core.Data;

public enum AuditAction
{
    RepositoryAdded,
    RepositoryDeleted,
    PatternAdded,
    PatternDeleted,
    PatternActivated,
    PatternDeactivated,
}

/// <summary>
/// A change log entry. <see cref="RepositoryId"/> and <see cref="PatternId"/> are plain columns
/// without foreign keys, and the URL and pattern are snapshots, so the entry outlives the rows it describes.
/// </summary>
public sealed class AuditEvent
{
    public const int ActorMaxLength = 256;

    public long Id { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Login (the <c>sub</c> claim) of the user who made the change.</summary>
    public required string Actor { get; set; }

    public AuditAction Action { get; set; }

    public long RepositoryId { get; set; }

    /// <summary>Null for repository-level events.</summary>
    public long? PatternId { get; set; }

    /// <summary>Repository URL at the time of the event.</summary>
    public required string RepositoryUrl { get; set; }

    /// <summary>Pattern text at the time of the event; null for repository-level events.</summary>
    public string? Pattern { get; set; }
}
