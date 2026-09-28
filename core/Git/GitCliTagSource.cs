using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace securitycheck_portal.Core.Git;

/// <summary>
/// Runs <c>git ls-remote --tags</c> non-interactively with a time limit. The PAT travels only in the
/// child's environment as an <c>Authorization</c> header scoped to the repository URL, never in argv.
/// </summary>
public sealed class GitCliTagSource(IOptions<GitOptions> options, ILogger<GitCliTagSource> logger) : IGitTagSource
{
    // git ls-remote --exit-code: 2 means no ref matched, i.e. the repository has no tags.
    private const int NoRefsExitCode = 2;
    private const int MaxLoggedStderrLength = 300;

    public async Task<GitTagListing> ListTagsAsync(string canonicalUrl, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
        var startInfo = CreateStartInfo(settings, canonicalUrl, OperatingSystem.IsWindows());

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            logger.LogError(ex, "Could not start git ({ExecutablePath})", settings.ExecutablePath);
            return new GitTagListing.Failure(GitErrorKind.Failed, null);
        }

        // Nothing is ever written to git; a closed stdin makes any prompt fail instead of waiting.
        process.StandardInput.Close();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        var token = timeoutCts.Token;

        // Both pipes are drained concurrently, so a full stderr buffer cannot block git.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(token);
        var stderrTask = process.StandardError.ReadToEndAsync(token);

        try
        {
            await process.WaitForExitAsync(token);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            switch (process.ExitCode)
            {
                case 0:
                    return new GitTagListing.Success(stdout);
                case NoRefsExitCode:
                    return new GitTagListing.Success("");
                default:
                    logger.LogWarning("git ls-remote for {Url} exited with code {ExitCode}: {Stderr}",
                        canonicalUrl, process.ExitCode, FirstLine(stderr));
                    return new GitTagListing.Failure(GitErrorKind.Failed, process.ExitCode);
            }
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            // Observe the (cancelled) reads; their results no longer matter.
            await ((Task)Task.WhenAll(stdoutTask, stderrTask)).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            logger.LogWarning("git ls-remote for {Url} timed out after {Timeout}", canonicalUrl, timeout);
            return new GitTagListing.Failure(GitErrorKind.Timeout, null);
        }
    }

    /// <summary>
    /// Builds the git invocation. Public so tests can check that the token is only in the environment.
    /// </summary>
    public static ProcessStartInfo CreateStartInfo(GitOptions settings, string canonicalUrl, bool isWindows)
    {
        var startInfo = new ProcessStartInfo(settings.ExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            // Outside any repository, so no local .git/config is read (the app may run inside a checkout).
            WorkingDirectory = Path.GetTempPath(),
        };

        // An empty helper resets the list, so Git Credential Manager never runs (and never waits).
        string[] config =
        [
            "credential.helper=",
            "http.followRedirects=false",
            // Pinned: the ignored system/global config can no longer turn verification off,
            // but an explicit value keeps the PAT header off unverified TLS by construction.
            "http.sslVerify=true",
            "http.lowSpeedLimit=1000",
            "http.lowSpeedTime=20",
        ];
        foreach (var entry in config)
        {
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(entry);
        }

        if (isWindows)
        {
            // Trusts the internal CA through the Windows certificate store.
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("http.sslBackend=schannel");
        }

        // --end-of-options: the URL can never be read as an option.
        foreach (var argument in (string[])["ls-remote", "--tags", "--exit-code", "--end-of-options", canonicalUrl])
        {
            startInfo.ArgumentList.Add(argument);
        }

        var environment = startInfo.Environment;
        // Inherited GIT_* variables (GIT_SSL_NO_VERIFY, GIT_CONFIG_PARAMETERS, …) and the system and
        // global gitconfig (sslVerify, url.*.insteadOf, proxies) would otherwise still apply.
        foreach (var key in environment.Keys.Where(k => k.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            environment.Remove(key);
        }

        environment["GIT_CONFIG_NOSYSTEM"] = "1";
        environment["GIT_CONFIG_GLOBAL"] = "/dev/null"; // Git for Windows maps /dev/null to NUL.
        environment["GIT_TERMINAL_PROMPT"] = "0";
        environment["GCM_INTERACTIVE"] = "false";
        environment["GIT_ALLOW_PROTOCOL"] = "https";

        // Scoped to this URL, so git sends the header to no other address.
        environment["GIT_CONFIG_COUNT"] = "1";
        environment["GIT_CONFIG_KEY_0"] = $"http.{canonicalUrl}.extraHeader";
        environment["GIT_CONFIG_VALUE_0"] = "Authorization: Basic " + Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{settings.UserName}:{settings.Token}"));

        return startInfo;
    }

    private void KillTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Already exited between the timeout and the kill.
            logger.LogDebug(ex, "git process could not be killed");
        }
    }

    private static string FirstLine(string text)
    {
        var line = text.AsSpan().TrimStart();
        var end = line.IndexOfAny('\r', '\n');
        if (end >= 0)
        {
            line = line[..end];
        }

        return line.Length > MaxLoggedStderrLength ? line[..MaxLoggedStderrLength].ToString() : line.ToString();
    }
}
