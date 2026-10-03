namespace securitycheck_portal.Core.Processes;

/// <summary>Small text helpers shared by the Git and Trivy runners.</summary>
internal static class TextHelpers
{
    private const int MaxLoggedStderrLength = 300;

    /// <summary>First line of a process's stderr, cut short, for log messages.</summary>
    internal static string FirstLine(string text)
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
