using System.Diagnostics;
using System.Text;

namespace securitycheck_portal.Core.Git;

/// <summary>Per-command transfer behaviour of a git call that talks to the server.</summary>
/// <param name="LowSpeedLimit">Bytes per second below which a transfer counts as stalled.</param>
/// <param name="LowSpeedTime">Seconds the transfer may stay stalled before git gives up.</param>
/// <param name="LongPaths">Sets <c>core.longpaths</c> (Windows paths over 260 characters).</param>
public sealed record GitTransferLimits(int LowSpeedLimit, int LowSpeedTime, bool LongPaths)
{
    /// <summary><c>ls-remote</c>: a short answer is expected, so a stall of 20 s is a failure.</summary>
    public static GitTransferLimits ListRemoteLimits { get; } = new(1000, 20, false);

    /// <summary>
    /// <c>clone</c>: the server packs objects before sending any data, so the stall limit is minutes;
    /// the overall limit is the caller's timeout.
    /// </summary>
    public static GitTransferLimits CloneLimits { get; } = new(1000, 300, true);
}

/// <summary>
/// Builds hardened git invocations. The PAT travels only in the child's environment as an
/// <c>Authorization</c> header scoped to the repository URL, never in argv.
/// </summary>
public static class GitProcessFactory
{
    /// <summary>A git command that talks to <paramref name="canonicalUrl"/> and so carries the PAT.</summary>
    /// <param name="arguments">Subcommand arguments, appended after the <c>-c</c> settings (the caller adds <c>--end-of-options</c> and the URL).</param>
    /// <param name="workingDirectory">Defaults to the temp directory, outside any repository.</param>
    public static ProcessStartInfo CreateRemote(
        GitOptions settings,
        string canonicalUrl,
        IEnumerable<string> arguments,
        GitTransferLimits limits,
        bool isWindows,
        string? workingDirectory = null)
    {
        var startInfo = Build(settings, arguments, limits, isWindows, workingDirectory);
        var environment = startInfo.Environment;

        // Scoped to this URL, so git sends the header to no other address.
        environment["GIT_CONFIG_COUNT"] = "1";
        environment["GIT_CONFIG_KEY_0"] = $"http.{canonicalUrl}.extraHeader";
        environment["GIT_CONFIG_VALUE_0"] = "Authorization: Basic " + Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{settings.UserName}:{settings.Token}"));

        return startInfo;
    }

    /// <summary>A git command that only reads a local repository: no token, same hardened environment.</summary>
    public static ProcessStartInfo CreateLocal(
        GitOptions settings, string workingDirectory, IEnumerable<string> arguments, bool isWindows)
        => Build(settings, arguments, null, isWindows, workingDirectory);

    private static ProcessStartInfo Build(
        GitOptions settings,
        IEnumerable<string> arguments,
        GitTransferLimits? limits,
        bool isWindows,
        string? workingDirectory)
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
            // Outside any repository by default, so no local .git/config is read (the app may run inside a checkout).
            WorkingDirectory = workingDirectory ?? Path.GetTempPath(),
        };

        // An empty helper resets the list, so Git Credential Manager never runs (and never waits).
        List<string> config =
        [
            "credential.helper=",
            "http.followRedirects=false",
            // Pinned: the ignored system/global config can no longer turn verification off,
            // but an explicit value keeps the PAT header off unverified TLS by construction.
            "http.sslVerify=true",
        ];
        if (limits is not null)
        {
            config.Add($"http.lowSpeedLimit={limits.LowSpeedLimit}");
            config.Add($"http.lowSpeedTime={limits.LowSpeedTime}");
        }

        if (isWindows)
        {
            // Trusts the internal CA through the Windows certificate store.
            config.Add("http.sslBackend=schannel");
        }

        if (limits is { LongPaths: true })
        {
            config.Add("core.longpaths=true");
        }

        foreach (var entry in config)
        {
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(entry);
        }

        foreach (var argument in arguments)
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

        return startInfo;
    }
}
