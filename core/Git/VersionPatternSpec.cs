using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace securitycheck_portal.Core.Git;

/// <summary>
/// A parsed <c>X.Y.*</c> pattern: matches tags <c>X.Y.N</c> (no prefix, no leading zeros), where
/// <c>N</c> is the hotfix number compared as an integer.
/// </summary>
public sealed partial class VersionPatternSpec
{
    private VersionPatternSpec(int major, int minor)
    {
        Major = major;
        Minor = minor;
        Canonical = string.Create(CultureInfo.InvariantCulture, $"{major}.{minor}.*");
    }

    public int Major { get; }

    public int Minor { get; }

    /// <summary>The pattern text as stored, e.g. <c>2.1.*</c>.</summary>
    public string Canonical { get; }

    public static bool TryParse(string? text, [NotNullWhen(true)] out VersionPatternSpec? spec)
    {
        spec = null;
        if (text is null)
        {
            return false;
        }

        var match = PatternSyntax().Match(text);
        if (!match.Success)
        {
            return false;
        }

        spec = new VersionPatternSpec(ParseNumber(match.Groups[1]), ParseNumber(match.Groups[2]));
        return true;
    }

    /// <summary>True when <paramref name="tagName"/> is exactly <c>{Major}.{Minor}.N</c>.</summary>
    public bool TryMatch(string tagName, out int hotfix)
    {
        hotfix = 0;

        var match = TagSyntax().Match(tagName);
        if (!match.Success
            || ParseNumber(match.Groups[1]) != Major
            || ParseNumber(match.Groups[2]) != Minor)
        {
            return false;
        }

        hotfix = ParseNumber(match.Groups[3]);
        return true;
    }

    public override string ToString() => Canonical;

    // At most 9 digits, so every number fits in an int.
    private static int ParseNumber(Group group) => int.Parse(group.ValueSpan, CultureInfo.InvariantCulture);

    // \z rather than $: $ also matches before a trailing newline.
    [GeneratedRegex(@"\A(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})\.\*\z", RegexOptions.CultureInvariant)]
    private static partial Regex PatternSyntax();

    [GeneratedRegex(@"\A(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})\z", RegexOptions.CultureInvariant)]
    private static partial Regex TagSyntax();
}
