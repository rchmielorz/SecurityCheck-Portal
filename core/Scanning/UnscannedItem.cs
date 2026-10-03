namespace securitycheck_portal.Core.Scanning;

/// <summary>Why a dependency manifest of the checkout was not scanned.</summary>
public enum UnscannedReason
{
    NoLockFile,
    RestoreFailed,
    RestoreTimedOut,
    DotnetNotStarted,
    RestoreBudgetExceeded,
    MultipleProjects,
    Unknown,
}

/// <summary>
/// A lock file that should exist but does not, with the reason. <paramref name="Path"/> has the format of
/// <see cref="LockFileDetector.FindMissing"/> paths (relative, forward slashes, e.g. <c>src/App/packages.lock.json</c>).
/// </summary>
public sealed record UnscannedItem(string Path, UnscannedReason Reason, string? Detail = null)
{
    public const int PathMaxLength = 1024;
    public const int DetailMaxLength = 300;
}
