using Microsoft.Extensions.Logging;

namespace securitycheck_portal.Core.Scanning;

/// <summary>Removal of checkouts under <see cref="ScanOptions.WorkRoot"/>, which hold read-only git objects on Windows.</summary>
internal static class WorkDirectory
{
    public const string ReportPrefix = "trivy-report-";

    public static string ForScan(ScanOptions options, long scanId)
        => Path.Combine(options.WorkRoot, scanId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Best effort; never throws, because it runs in <c>finally</c> blocks.</summary>
    public static void Delete(string directory, ILogger logger)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            // Git marks pack files read-only; Directory.Delete would fail on them.
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not delete work directory {Directory}", directory);
        }
    }

    /// <summary>
    /// Removes what a crashed worker left behind: checkout folders named by a scan id (digits only) and stale
    /// Trivy reports. Anything else in <c>WorkRoot</c> is left alone.
    /// </summary>
    public static void DeleteOrphans(ScanOptions options, ILogger logger)
    {
        if (!Directory.Exists(options.WorkRoot))
        {
            return;
        }

        try
        {
            foreach (var directory in Directory.EnumerateDirectories(options.WorkRoot))
            {
                var name = Path.GetFileName(directory);
                if (name.Length > 0 && name.All(char.IsAsciiDigit))
                {
                    Delete(directory, logger);
                }
            }

            foreach (var report in Directory.EnumerateFiles(options.WorkRoot, ReportPrefix + "*.json"))
            {
                try
                {
                    File.Delete(report);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(ex, "Could not delete stale Trivy report {Path}", report);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not clean up {WorkRoot}", options.WorkRoot);
        }
    }
}
