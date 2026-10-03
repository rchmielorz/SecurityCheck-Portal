using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using securitycheck_portal.Core.Processes;

namespace securitycheck_portal.Core.Git;

/// <summary>
/// Runs <c>git ls-remote --tags</c> non-interactively with a time limit. The PAT travels only in the
/// child's environment as an <c>Authorization</c> header scoped to the repository URL, never in argv.
/// </summary>
public sealed class GitCliTagSource(
    IOptions<GitOptions> options, IProcessRunner runner, ILogger<GitCliTagSource> logger) : IGitTagSource
{
    // git ls-remote --exit-code: 2 means no ref matched, i.e. the repository has no tags.
    private const int NoRefsExitCode = 2;
    private const int MaxCapturedChars = 8 * 1024 * 1024;

    public async Task<GitTagListing> ListTagsAsync(string canonicalUrl, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
        var startInfo = CreateStartInfo(settings, canonicalUrl, OperatingSystem.IsWindows());

        var result = await runner.RunAsync(startInfo, timeout, MaxCapturedChars, cancellationToken);

        switch (result.Outcome)
        {
            case ProcessOutcome.StartFailed:
                logger.LogError("Could not start git ({ExecutablePath})", settings.ExecutablePath);
                return new GitTagListing.Failure(GitErrorKind.Failed, null);
            case ProcessOutcome.TimedOut:
                logger.LogWarning("git ls-remote for {Url} timed out after {Timeout}", canonicalUrl, timeout);
                return new GitTagListing.Failure(GitErrorKind.Timeout, null);
        }

        switch (result.ExitCode)
        {
            case 0:
                return new GitTagListing.Success(result.Stdout);
            case NoRefsExitCode:
                return new GitTagListing.Success("");
            default:
                logger.LogWarning("git ls-remote for {Url} exited with code {ExitCode}: {Stderr}",
                    canonicalUrl, result.ExitCode, TextHelpers.FirstLine(result.Stderr));
                return new GitTagListing.Failure(GitErrorKind.Failed, result.ExitCode);
        }
    }

    /// <summary>
    /// Builds the git invocation. Public so tests can check that the token is only in the environment.
    /// </summary>
    public static ProcessStartInfo CreateStartInfo(GitOptions settings, string canonicalUrl, bool isWindows)
        => GitProcessFactory.CreateRemote(
            settings,
            canonicalUrl,
            // --end-of-options: the URL can never be read as an option.
            ["ls-remote", "--tags", "--exit-code", "--end-of-options", canonicalUrl],
            GitTransferLimits.ListRemoteLimits,
            isWindows);
}
