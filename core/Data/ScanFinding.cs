namespace securitycheck_portal.Core.Data;

public enum FindingSeverity
{
    Critical,
    High,
    Medium,
    Low,
    Unknown,
}

/// <summary>A vulnerability that the scanner reported for a library in a scan.</summary>
public sealed class ScanFinding
{
    public const int LibraryMaxLength = 512;
    public const int VersionMaxLength = 128;
    public const int VulnerabilityIdMaxLength = 64;
    public const int TitleMaxLength = 1000;

    public long Id { get; set; }

    public long ScanId { get; set; }

    public Scan? Scan { get; set; }

    public required string Library { get; set; }

    public required string InstalledVersion { get; set; }

    /// <summary>CVE or other advisory identifier.</summary>
    public required string VulnerabilityId { get; set; }

    public FindingSeverity Severity { get; set; }

    public string? FixedVersion { get; set; }

    public string? Title { get; set; }

    /// <summary>Dependency files in which the library was found.</summary>
    public string[] Targets { get; set; } = [];
}
