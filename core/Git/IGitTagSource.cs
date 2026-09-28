namespace securitycheck_portal.Core.Git;

/// <summary>Lists the tags of a remote repository as raw <c>git ls-remote --tags</c> output.</summary>
public interface IGitTagSource
{
    /// <param name="canonicalUrl">A URL produced by <see cref="RepositoryUrl.TryNormalize"/>.</param>
    Task<GitTagListing> ListTagsAsync(string canonicalUrl, CancellationToken cancellationToken);
}

/// <summary>Result of asking the Git server for tags.</summary>
public abstract record GitTagListing
{
    private GitTagListing()
    {
    }

    /// <summary>Raw output; empty when the repository has no tags.</summary>
    public sealed record Success(string Output) : GitTagListing;

    /// <summary>git timed out, could not be started or exited with an error.</summary>
    public sealed record Failure(GitErrorKind Kind, int? ExitCode) : GitTagListing;
}
