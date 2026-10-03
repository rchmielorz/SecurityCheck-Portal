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
/// <see cref="GenerateAsync"/> also returns an <see cref="UnscannedItem"/> with the reason for every lock file it
/// could not produce (restore failed, timed out, dotnet not started, budget used up, or a directory with several
/// projects, for which dotnet is not run).
/// Accepted risk: <c>dotnet restore</c> evaluates MSBuild files from the repository (<c>Directory.Build.targets</c>,
/// the project itself, <c>global.json</c> msbuild-sdks) and honors a <c>NuGet.config</c> found in the checkout, so
/// repository content runs with the rights of the worker account. All this class does about it is keep our own
/// environment out: the process gets an environment built from a whitelist (never a copy of ours, which may hold
/// the Git PAT). Files in the worker profile, including its <c>NuGet.config</c> with feed credentials, stay
/// reachable. The repositories come from the internal GitLab and are trusted to that extent.
/// The total time is limited by <see cref="ScanOptions.RestoreTotalTimeoutMinutes"/>; projects not restored in
/// time stay without a lock file. A lock file that a failed or timed-out restore may have left behind is removed.
/// </summary>
public sealed class DotnetLockFileGenerator(
    IOptions<ScanOptions> options,
    IProcessRunner runner,
    LockFileDetector lockFileDetector,
    TimeProvider timeProvider,
    ILogger<DotnetLockFileGenerator> logger)
{
    private const int MaxCapturedChars = 64 * 1024;

    /// <summary>The Trivy whitelist plus what NuGet needs to find its profile, config and package cache.</summary>
    public static readonly IReadOnlyList<string> InheritedEnvironment =
    [
        .. TrivyScanner.InheritedEnvironment,
        "USERPROFILE", "APPDATA", "LOCALAPPDATA", "HOME", "ProgramData", "ProgramFiles",
    ];

    public async Task<IReadOnlyList<UnscannedItem>> GenerateAsync(
        string checkoutDirectory, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var perProject = TimeSpan.FromMinutes(settings.RestoreTimeoutMinutes);
        var budget = TimeSpan.FromMinutes(settings.RestoreTotalTimeoutMinutes);
        var started = timeProvider.GetUtcNow();
        var projects = lockFileDetector.FindDotnetProjectsWithoutLock(checkoutDirectory);
        var unscanned = new Dictionary<string, UnscannedItem>(StringComparer.Ordinal);

        // One lock file cannot be generated for several projects in a directory, so dotnet is not run for them.
        foreach (var lockRelative in lockFileDetector.FindDirectoriesWithMultipleProjects(checkoutDirectory))
        {
            unscanned[lockRelative] = new UnscannedItem(lockRelative, UnscannedReason.MultipleProjects);
        }

        for (var i = 0; i < projects.Count; i++)
        {
            var remaining = budget - (timeProvider.GetUtcNow() - started);
            if (remaining <= TimeSpan.Zero)
            {
                logger.LogWarning(
                    "dotnet restore budget of {Minutes} minutes used up; {Count} project(s) not restored",
                    settings.RestoreTotalTimeoutMinutes, projects.Count - i);
                for (var j = i; j < projects.Count; j++)
                {
                    var skipped = LockRelativePath(projects[j]);
                    unscanned[skipped] = new UnscannedItem(skipped, UnscannedReason.RestoreBudgetExceeded);
                }

                break;
            }

            var project = projects[i];
            var projectPath = Path.Combine(checkoutDirectory, project.Replace('/', Path.DirectorySeparatorChar));
            var lockPath = Path.Combine(Path.GetDirectoryName(projectPath)!, "packages.lock.json");
            var lockExisted = File.Exists(lockPath);

            var result = await runner.RunAsync(
                CreateStartInfo(settings, projectPath), remaining < perProject ? remaining : perProject,
                MaxCapturedChars, cancellationToken);

            UnscannedItem? failure = null;
            var lockRelative = LockRelativePath(project);
            switch (result.Outcome)
            {
                case ProcessOutcome.StartFailed:
                    logger.LogWarning("dotnet could not be started to restore {Project}", project);
                    failure = new UnscannedItem(lockRelative, UnscannedReason.DotnetNotStarted);
                    break;
                case ProcessOutcome.TimedOut:
                    logger.LogWarning("dotnet restore of {Project} did not finish in time", project);
                    failure = new UnscannedItem(lockRelative, UnscannedReason.RestoreTimedOut);
                    break;
                case ProcessOutcome.Exited when result.ExitCode != 0:
                    var firstLine = TextHelpers.FirstLine(
                        string.IsNullOrWhiteSpace(result.Stderr) ? result.Stdout : result.Stderr);
                    logger.LogWarning("dotnet restore of {Project} exited with code {ExitCode}: {Output}",
                        project, result.ExitCode, firstLine);
                    failure = new UnscannedItem(
                        lockRelative, UnscannedReason.RestoreFailed, firstLine.Length == 0 ? null : firstLine);
                    break;
            }

            if (failure is not null)
            {
                unscanned[failure.Path] = failure;

                // A killed or failed restore may leave a half-written lock file, which would count as "present".
                if (!lockExisted)
                {
                    TryDelete(lockPath);
                }
            }
        }

        return unscanned.Values.OrderBy(item => item.Path, StringComparer.Ordinal).ToList();
    }

    /// <summary>The lock file path of a project, relative to the checkout, in the format of <c>FindMissing</c>.</summary>
    private static string LockRelativePath(string project)
    {
        var slash = project.LastIndexOf('/');
        return slash < 0 ? "packages.lock.json" : project[..(slash + 1)] + "packages.lock.json";
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not delete the partial lock file {Path}", path);
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
