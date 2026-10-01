using securitycheck_portal.Core.Data;

namespace securitycheck_portal.Scans;

/// <summary>A requested scan; <see cref="Status"/> is a <see cref="ScanStatus"/> name.</summary>
public sealed record ScanResponse(
    long Id,
    long PatternId,
    long RepositoryId,
    string RepositoryUrl,
    string Pattern,
    string Status,
    string RequestedBy,
    DateTimeOffset RequestedAt);

/// <summary>One vulnerability; <see cref="Severity"/> is a <see cref="FindingSeverity"/> name.</summary>
public sealed record ScanFindingResponse(
    string Library,
    string InstalledVersion,
    string VulnerabilityId,
    string Severity,
    string? FixedVersion,
    string? Title,
    IReadOnlyList<string> Targets);

/// <summary>
/// A scan with its findings, sorted by severity (Critical first, Unknown last), then library name and
/// vulnerability id. <see cref="Status"/> is a <see cref="ScanStatus"/> name and <see cref="FailureReason"/>
/// a <see cref="ScanFailureReason"/> name.
/// </summary>
public sealed record ScanDetails(
    long Id,
    long PatternId,
    long RepositoryId,
    string RepositoryUrl,
    string Pattern,
    string Status,
    string? FailureReason,
    string? FailureDetail,
    string RequestedBy,
    DateTimeOffset RequestedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? ScannedTag,
    string? ScannedCommit,
    string? TrivyVersion,
    DateTimeOffset? TrivyDbUpdatedAt,
    IReadOnlyList<string> MissingLockFiles,
    IReadOnlyList<ScanFindingResponse> Findings);

/// <summary>
/// Body of a 409 on requesting a scan: <see cref="Active"/> (a queued or running scan exists, see
/// <see cref="ScanId"/>) or <see cref="Inactive"/> (the pattern is deactivated).
/// </summary>
public sealed record ScanConflict(string Reason, long? ScanId = null)
{
    public const string Active = "active";
    public const string Inactive = "inactive";
}
