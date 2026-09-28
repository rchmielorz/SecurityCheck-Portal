namespace securitycheck_portal.Core.Git;

/// <summary>Picks the tag a pattern currently resolves to.</summary>
public static class PatternResolver
{
    /// <summary>
    /// The matching tag with the highest hotfix number, or <see cref="PatternResolution.NoMatch"/>.
    /// Tags outside the <c>X.Y.N</c> format (prefixes, pre-releases, leading zeros, four parts) are skipped.
    /// </summary>
    public static PatternResolution Resolve(IEnumerable<RemoteTag> tags, VersionPatternSpec spec)
    {
        RemoteTag? best = null;
        var bestHotfix = -1;

        foreach (var tag in tags)
        {
            if (spec.TryMatch(tag.Name, out var hotfix) && hotfix > bestHotfix)
            {
                best = tag;
                bestHotfix = hotfix;
            }
        }

        return best is null
            ? new PatternResolution.NoMatch()
            : new PatternResolution.Resolved(best.Name, best.CommitSha);
    }

    /// <summary>Parses raw <c>git ls-remote --tags</c> output, then resolves.</summary>
    public static PatternResolution Resolve(string lsRemoteOutput, VersionPatternSpec spec) =>
        LsRemoteParser.TryParse(lsRemoteOutput, out var tags, out var reason)
            ? Resolve(tags, spec)
            : new PatternResolution.Ambiguous(reason);
}
