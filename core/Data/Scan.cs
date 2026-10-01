namespace securitycheck_portal.Core.Data;

public enum ScanStatus
{
    Queued,
    Running,
    Completed,
    Incomplete,
    Failed,
}

/// <summary>Why a scan ended as <see cref="ScanStatus.Failed"/> (or was cut short).</summary>
public enum ScanFailureReason
{
    PatternNotResolved,
    GitFailed,
    CommitMismatch,
    ScannerUnavailable,
    DatabaseTooOld,
    Timeout,
    ScannerFailed,
    Interrupted,
}

/// <summary>
/// One scan of a version pattern. <see cref="RepositoryId"/> and <see cref="PatternId"/> are plain columns
/// without foreign keys, and the URL and pattern are snapshots, so the scan history outlives the rows
/// it describes. At most one scan per pattern is <see cref="ScanStatus.Queued"/> or
/// <see cref="ScanStatus.Running"/> (partial unique index).
/// </summary>
public sealed class Scan
{
    public const int FailureDetailMaxLength = 1000;
    public const int TrivyVersionMaxLength = 64;

    public long Id { get; set; }

    public long PatternId { get; set; }

    public long RepositoryId { get; set; }

    /// <summary>Repository URL at the time of the request.</summary>
    public required string RepositoryUrl { get; set; }

    /// <summary>Pattern text at the time of the request.</summary>
    public required string Pattern { get; set; }

    public ScanStatus Status { get; set; }

    public ScanFailureReason? FailureReason { get; set; }

    /// <summary>Short, human-readable detail of the failure; never contains secrets.</summary>
    public string? FailureDetail { get; set; }

    /// <summary>Login (the <c>sub</c> claim) of the user who requested the scan.</summary>
    public required string RequestedBy { get; set; }

    public DateTimeOffset RequestedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>Tag that was scanned.</summary>
    public string? ScannedTag { get; set; }

    /// <summary>Full 40-character commit SHA that was scanned.</summary>
    public string? ScannedCommit { get; set; }

    public string? TrivyVersion { get; set; }

    public DateTimeOffset? TrivyDbUpdatedAt { get; set; }

    /// <summary>Dependency manifests found without their lock file.</summary>
    public string[] MissingLockFiles { get; set; } = [];

    public List<ScanFinding> Findings { get; } = [];
}
