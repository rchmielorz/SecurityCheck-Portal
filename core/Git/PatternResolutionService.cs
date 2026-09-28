using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace securitycheck_portal.Core.Git;

/// <summary>Resolves a version pattern of a repository against the Git server's current tags.</summary>
public sealed class PatternResolutionService(
    IGitTagSource tagSource,
    IOptions<GitOptions> options,
    ILogger<PatternResolutionService> logger)
{
    /// <param name="canonicalUrl">A URL produced by <see cref="RepositoryUrl.TryNormalize"/>.</param>
    public async Task<PatternResolution> ResolveAsync(
        string canonicalUrl, VersionPatternSpec spec, CancellationToken cancellationToken)
    {
        // Checked again here: a stored URL whose host was later removed from Git:AllowedHosts must not reach git.
        if (!RepositoryUrl.TryNormalize(canonicalUrl, options.Value.AllowedHosts, out var normalized)
            || normalized != canonicalUrl)
        {
            logger.LogWarning("Refusing to query {Url}: not a canonical URL on an allowed host", canonicalUrl);
            return new PatternResolution.Error(GitErrorKind.Failed);
        }

        return await tagSource.ListTagsAsync(canonicalUrl, cancellationToken) switch
        {
            GitTagListing.Success success => PatternResolver.Resolve(success.Output, spec),
            GitTagListing.Failure failure => new PatternResolution.Error(failure.Kind),
            _ => throw new InvalidOperationException("Unknown tag listing result."),
        };
    }
}
