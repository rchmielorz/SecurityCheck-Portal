namespace securitycheck_portal.Core.Scanning;

/// <summary>
/// Finds dependency manifests without their lock file. Trivy reads dependencies only from lock files, so
/// such a checkout can look clean while being unscanned. The rule is conservative: it may report a missing
/// lock file that Trivy would not need, never the other way round.
/// </summary>
public sealed class LockFileDetector
{
    private static readonly string[] SkippedDirectories = [".git", "node_modules", "bin", "obj"];
    private static readonly string[] NodeLockFiles = ["package-lock.json", "yarn.lock", "pnpm-lock.yaml"];

    private static readonly EnumerationOptions Enumeration = new()
    {
        IgnoreInaccessible = true,
        // A link could lead out of the checkout or loop.
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    /// <returns>Relative paths (forward slashes) of the lock files that should exist and do not.</returns>
    public IReadOnlyList<string> FindMissing(string checkoutDirectory)
    {
        var missing = new List<string>();
        Walk(checkoutDirectory, "", missing);
        missing.Sort(StringComparer.Ordinal);
        return missing;
    }

    private static void Walk(string directory, string relative, List<string> missing)
    {
        var fileNames = Directory.EnumerateFiles(directory, "*", Enumeration)
            .Select(f => Path.GetFileName(f))
            .ToList();

        if (fileNames.Any(f => f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            && !fileNames.Contains("packages.lock.json", StringComparer.OrdinalIgnoreCase))
        {
            missing.Add(relative + "packages.lock.json");
        }

        if (fileNames.Contains("package.json", StringComparer.OrdinalIgnoreCase)
            && !NodeLockFiles.Any(l => fileNames.Contains(l, StringComparer.OrdinalIgnoreCase)))
        {
            missing.Add(relative + "package-lock.json");
        }

        foreach (var child in Directory.EnumerateDirectories(directory, "*", Enumeration))
        {
            var name = Path.GetFileName(child);
            if (SkippedDirectories.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            Walk(child, relative + name + "/", missing);
        }
    }
}
