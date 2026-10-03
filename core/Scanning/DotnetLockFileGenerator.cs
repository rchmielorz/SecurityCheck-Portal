using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using securitycheck_portal.Core.Processes;

namespace securitycheck_portal.Core.Scanning;

/// <summary>
/// Writes the <c>packages.lock.json</c> that Trivy needs for a .NET project, by running
/// <c>dotnet restore &lt;project&gt; --use-lock-file</c> in the checkout (the checkout is deleted after the scan).
/// Best effort: a project whose restore fails, times out or cannot be started is logged and skipped, and stays
/// in <see cref="LockFileDetector.FindMissing"/>, which decides between completed and incomplete afterwards.
/// <c>dotnet restore</c> evaluates MSBuild files from the repository, so the process gets an environment built
/// from a whitelist (never a copy of ours, which may hold the Git PAT) and NuGet feeds come only from the
/// <c>NuGet.config</c> of the worker account.
/// </summary>
public sealed class DotnetLockFileGenerator(
    IOptions<ScanOptions> options,
    IProcessRunner runner,
    LockFileDetector lockFileDetector,
    ILogger<DotnetLockFileGenerator> logger)
{
    private const int MaxCapturedChars = 64 * 1024;

    /// <summary>The Trivy whitelist plus what NuGet needs to find its profile, config and package cache.</summary>
    public static readonly IReadOnlyList<string> InheritedEnvironment =
    [
        .. TrivyScanner.InheritedEnvironment,
        "USERPROFILE", "APPDATA", "LOCALAPPDATA", "HOME", "ProgramData", "ProgramFiles",
    ];

    public async Task GenerateAsync(string checkoutDirectory, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var timeout = TimeSpan.FromMinutes(settings.RestoreTimeoutMinutes);

        foreach (var project in lockFileDetector.FindDotnetProjectsWithoutLock(checkoutDirectory))
        {
            var projectPath = Path.Combine(checkoutDirectory, project.Replace('/', Path.DirectorySeparatorChar));
            var result = await runner.RunAsync(
                CreateStartInfo(settings, projectPath), timeout, MaxCapturedChars, cancellationToken);

            switch (result.Outcome)
            {
                case ProcessOutcome.StartFailed:
                    logger.LogWarning("dotnet could not be started to restore {Project}", project);
                    break;
                case ProcessOutcome.TimedOut:
                    logger.LogWarning("dotnet restore of {Project} did not finish within {Minutes} minutes",
                        project, settings.RestoreTimeoutMinutes);
                    break;
                case ProcessOutcome.Exited when result.ExitCode != 0:
                    logger.LogWarning("dotnet restore of {Project} exited with code {ExitCode}: {Output}",
                        project, result.ExitCode,
                        TextHelpers.FirstLine(string.IsNullOrWhiteSpace(result.Stderr) ? result.Stdout : result.Stderr));
                    break;
            }
        }
    }

    /// <summary>Public so tests can check the arguments and the environment of each call.</summary>
    public static ProcessStartInfo CreateStartInfo(ScanOptions settings, string projectPath)
    {
        var startInfo = new ProcessStartInfo(settings.DotnetExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(projectPath) ?? Path.GetTempPath(),
        };

        foreach (var argument in new[] { "restore", projectPath, "--use-lock-file", "--nologo" })
        {
            startInfo.ArgumentList.Add(argument);
        }

        // Start from nothing: the worker's own environment may carry the Git PAT (Git__Token).
        startInfo.Environment.Clear();
        foreach (var name in InheritedEnvironment)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value))
            {
                startInfo.Environment[name] = value;
            }
        }

        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        return startInfo;
    }
}
