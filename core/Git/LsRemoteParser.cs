using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace securitycheck_portal.Core.Git;

/// <summary>A tag on the remote and the commit it points to.</summary>
public sealed record RemoteTag(string Name, string CommitSha);

/// <summary>Reads the output of <c>git ls-remote --tags</c> (without <c>--refs</c>).</summary>
public static partial class LsRemoteParser
{
    private const string TagPrefix = "refs/tags/";
    private const string PeeledSuffix = "^{}";

    /// <summary>
    /// Returns every tag with its commit: the <c>^{}</c> line of an annotated tag when present,
    /// otherwise the tag's own line. Fails with a reason on a malformed line, a ref listed twice
    /// with different SHAs, or a <c>^{}</c> line without its tag line. Refs outside
    /// <c>refs/tags/</c> are ignored.
    /// </summary>
    public static bool TryParse(
        string output,
        out IReadOnlyList<RemoteTag> tags,
        [NotNullWhen(false)] out string? ambiguityReason)
    {
        tags = [];
        ambiguityReason = null;

        var direct = new Dictionary<string, string>(StringComparer.Ordinal);
        var peeled = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            var match = LinePattern().Match(line);
            if (!match.Success)
            {
                ambiguityReason = "ls-remote output contains a malformed line.";
                return false;
            }

            var sha = match.Groups["sha"].Value.ToLowerInvariant();
            var refName = match.Groups["ref"].Value;
            if (!refName.StartsWith(TagPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var name = refName[TagPrefix.Length..];
            var target = direct;
            if (name.EndsWith(PeeledSuffix, StringComparison.Ordinal))
            {
                name = name[..^PeeledSuffix.Length];
                target = peeled;
            }

            if (name.Length == 0)
            {
                ambiguityReason = "ls-remote output contains a tag without a name.";
                return false;
            }

            if (target.TryGetValue(name, out var existing))
            {
                if (existing != sha)
                {
                    ambiguityReason = $"Tag '{name}' is listed twice with different SHAs.";
                    return false;
                }

                continue;
            }

            target[name] = sha;
        }

        var orphan = peeled.Keys.FirstOrDefault(name => !direct.ContainsKey(name));
        if (orphan is not null)
        {
            ambiguityReason = $"Tag '{orphan}' has a peeled line without its tag line.";
            return false;
        }

        tags = direct
            .Select(pair => new RemoteTag(pair.Key, peeled.GetValueOrDefault(pair.Key, pair.Value)))
            .ToList();
        return true;
    }

    [GeneratedRegex(@"\A(?<sha>[0-9a-fA-F]{40})\t(?<ref>[^\s]+)\z", RegexOptions.CultureInvariant)]
    private static partial Regex LinePattern();
}
