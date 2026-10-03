using securitycheck_portal.Core.Data;

namespace securitycheck_portal.Core.Scanning;

/// <summary>Explicit result of scanning one checkout. "No vulnerabilities" is only ever a <see cref="Completed"/>.</summary>
public abstract record ScanOutcome
{
    private ScanOutcome()
    {
    }

    /// <summary>Lock files for every manifest were present or generated and Trivy found at least one target.</summary>
    public sealed record Completed(
        IReadOnlyList<ScanFinding> Findings, string TrivyVersion, DateTimeOffset DbUpdatedAt) : ScanOutcome;

    /// <summary>
    /// Findings are shown, but the absence of others cannot be confirmed: lock files are missing
    /// (not present and not generated) or Trivy found no dependency file at all (then <c>MissingLockFiles</c> may be empty).
    /// </summary>
    public sealed record Incomplete(
        IReadOnlyList<ScanFinding> Findings,
        IReadOnlyList<string> MissingLockFiles,
        string TrivyVersion,
        DateTimeOffset DbUpdatedAt) : ScanOutcome;

    /// <summary>No result. <c>Detail</c> is one short line without secrets.</summary>
    public sealed record Failed(ScanFailureReason Reason, string Detail) : ScanOutcome;
}
