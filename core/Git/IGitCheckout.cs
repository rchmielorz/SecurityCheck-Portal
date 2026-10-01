namespace securitycheck_portal.Core.Git;

/// <summary>Puts the code of one tag into a local directory.</summary>
public interface IGitCheckout
{
    /// <summary>
    /// Shallow-clones <paramref name="tag"/> into <paramref name="targetDirectory"/> (absent or empty) and checks
    /// that HEAD is <paramref name="expectedCommit"/>. The directory is left in place; the caller removes it.
    /// </summary>
    /// <param name="canonicalUrl">A URL produced by <see cref="RepositoryUrl.TryNormalize"/>.</param>
    /// <param name="expectedCommit">The commit the tag was resolved to by <see cref="PatternResolutionService"/>.</param>
    Task<GitCheckoutResult> CheckoutAsync(
        string canonicalUrl, string tag, string expectedCommit, string targetDirectory, CancellationToken cancellationToken);
}

/// <summary>Result of cloning a tag.</summary>
public abstract record GitCheckoutResult
{
    private GitCheckoutResult()
    {
    }

    /// <summary>HEAD of the clone is the expected commit.</summary>
    public sealed record Success : GitCheckoutResult;

    /// <summary>The tag now points to another commit than the one resolved: it was moved in between.</summary>
    public sealed record CommitMismatch(string ActualCommit) : GitCheckoutResult;

    /// <summary>git timed out, could not be started or exited with an error.</summary>
    public sealed record Failure(GitErrorKind Kind, int? ExitCode) : GitCheckoutResult;
}
