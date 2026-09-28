namespace securitycheck_portal.Core.Git;

/// <summary>Why the Git server could not be asked for tags.</summary>
public enum GitErrorKind
{
    Timeout,
    Failed,
}

/// <summary>Outcome of resolving a version pattern to a tag and commit.</summary>
public abstract record PatternResolution
{
    private PatternResolution()
    {
    }

    /// <summary>The highest matching tag and the commit it points to (peeled for annotated tags).</summary>
    public sealed record Resolved(string Tag, string CommitSha) : PatternResolution;

    /// <summary>The repository has no tag matching the pattern.</summary>
    public sealed record NoMatch : PatternResolution;

    /// <summary>The server's answer cannot be read unambiguously; nothing is guessed.</summary>
    public sealed record Ambiguous(string Reason) : PatternResolution;

    /// <summary>The server could not be reached, rejected the request or did not answer in time.</summary>
    public sealed record Error(GitErrorKind Kind) : PatternResolution;
}
