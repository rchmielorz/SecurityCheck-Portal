namespace securitycheck_portal.Core.Data;

/// <summary>A Git repository registered in the portal.</summary>
public sealed class Repository
{
    public const int UrlMaxLength = 2048;
    public const int NameMaxLength = 200;

    public long Id { get; set; }

    /// <summary>Canonical HTTPS URL; unique across the portal.</summary>
    public required string Url { get; set; }

    public string? Name { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Login (the <c>sub</c> claim) of the user who added the repository.</summary>
    public required string CreatedBy { get; set; }

    public List<VersionPattern> Patterns { get; } = [];
}
