using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using securitycheck_portal.Core.Processes;

namespace securitycheck_portal.Core.Git;

/// <summary>
/// <c>git clone --depth 1 --branch tag</c> followed by <c>git rev-parse HEAD</c>. Submodules are not fetched.
/// Same hardening and token handling as <see cref="GitCliTagSource"/>, but with the clone's own, longer limits.
/// </summary>
public sealed class GitCliCheckout(
    IOptions<GitOptions> options, IProcessRunner runner, ILogger<GitCliCheckout> logger) : IGitCheckout
{
    public static readonly TimeSpan DefaultCloneTimeout = TimeSpan.FromMinutes(10);

    private const int MaxCapturedChars = 64 * 1024;

    /// <summary>Overall limit of the clone. Not <c>Git:TimeoutSeconds</c>, which is the short <c>ls-remote</c> limit.</summary>
    public TimeSpan CloneTimeout { get; init; } = DefaultCloneTimeout;

    public async Task<GitCheckoutResult> CheckoutAsync(
        string canonicalUrl, string tag, string expectedCommit, string targetDirectory, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var isWindows = OperatingSystem.IsWindows();

        var clone = await runner.RunAsync(
            CreateCloneStartInfo(settings, canonicalUrl, tag, targetDirectory, isWindows),
            CloneTimeout, MaxCapturedChars, cancellationToken);

        var failure = ToFailure(clone, "clone", canonicalUrl);
        if (failure is not null)
        {
            return failure;
        }

        var revParse = await runner.RunAsync(
            CreateRevParseStartInfo(settings, targetDirectory, isWindows),
            TimeSpan.FromSeconds(settings.TimeoutSeconds), MaxCapturedChars, cancellationToken);

        failure = ToFailure(revParse, "rev-parse", canonicalUrl);
        if (failure is not null)
        {
            return failure;
        }

        var actual = revParse.Stdout.Trim();
        if (!string.Equals(actual, expectedCommit, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Tag {Tag} of {Url} points to {Actual}, expected {Expected}",
                tag, canonicalUrl, actual, expectedCommit);
            return new GitCheckoutResult.CommitMismatch(actual);
        }

        return new GitCheckoutResult.Success();
    }

    /// <summary>Public so tests can check the arguments and that the token is only in the environment.</summary>
    public static ProcessStartInfo CreateCloneStartInfo(
        GitOptions settings, string canonicalUrl, string tag, string targetDirectory, bool isWindows)
        => GitProcessFactory.CreateRemote(
            settings,
            canonicalUrl,
            // --end-of-options: the URL can never be read as an option.
            [
                "clone", "--depth", "1", "--branch", tag, "--no-recurse-submodules",
                "--end-of-options", canonicalUrl, targetDirectory,
            ],
            GitTransferLimits.CloneLimits,
            isWindows);

    public static ProcessStartInfo CreateRevParseStartInfo(GitOptions settings, string targetDirectory, bool isWindows)
        => GitProcessFactory.CreateLocal(settings, targetDirectory, ["rev-parse", "HEAD"], isWindows);

    private GitCheckoutResult.Failure? ToFailure(ProcessRunResult result, string step, string canonicalUrl)
    {
        switch (result.Outcome)
        {
            case ProcessOutcome.StartFailed:
                logger.LogError("Could not start git for {Step}", step);
                return new GitCheckoutResult.Failure(GitErrorKind.Failed, null);
            case ProcessOutcome.TimedOut:
                logger.LogWarning("git {Step} for {Url} timed out", step, canonicalUrl);
                return new GitCheckoutResult.Failure(GitErrorKind.Timeout, null);
            case ProcessOutcome.Exited when result.ExitCode != 0:
                logger.LogWarning("git {Step} for {Url} exited with code {ExitCode}: {Stderr}",
                    step, canonicalUrl, result.ExitCode, GitCliTagSource.FirstLine(result.Stderr));
                return new GitCheckoutResult.Failure(GitErrorKind.Failed, result.ExitCode);
            default:
                return null;
        }
    }
}
