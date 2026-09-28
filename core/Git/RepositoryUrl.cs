using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using securitycheck_portal.Core.Data;

namespace securitycheck_portal.Core.Git;

/// <summary>
/// Validates a user-supplied repository URL and produces its canonical form, used for storage,
/// uniqueness and as the only URL ever passed to git.
/// </summary>
public static partial class RepositoryUrl
{
    private const string Prefix = "https://";
    private const string GitSuffix = ".git";

    /// <summary>
    /// Accepts only <c>https://&lt;allowed host&gt;[:443]/&lt;path&gt;</c> with a path of
    /// <c>[A-Za-z0-9._~/-]</c> and no <c>.</c>/<c>..</c> segments. Returns
    /// <c>https://&lt;host&gt;/&lt;path&gt;.git</c>, all lower case, with <c>.git</c> added when missing.
    /// </summary>
    /// <remarks>
    /// GitLab answers a URL without <c>.git</c> with a 301, which git is told not to follow, and matches
    /// paths case-insensitively; lower-casing therefore gives one spelling per repository.
    /// </remarks>
    /// <remarks>
    /// The raw text is checked, not only <see cref="Uri"/>'s parse: <see cref="Uri"/> silently trims
    /// whitespace, turns <c>\</c> into <c>/</c> and removes <c>..</c> segments. Anything not starting
    /// with <c>https://</c> (options like <c>-o…</c>, <c>ext::</c>, <c>file:</c>, local and UNC paths,
    /// <c>ssh://</c>, scp-style <c>git@host:path</c>) fails the prefix check.
    /// </remarks>
    public static bool TryNormalize(
        string? input,
        IReadOnlyCollection<string> allowedHosts,
        [NotNullWhen(true)] out string? canonicalUrl)
    {
        canonicalUrl = null;

        if (string.IsNullOrEmpty(input)
            || input.Length > Repository.UrlMaxLength
            || !input.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = input[Prefix.Length..];
        var slash = rest.IndexOf('/');
        if (slash <= 0)
        {
            return false;
        }

        // The authority allows no user info, no port other than the default and nothing but a host name;
        // '?' and '#' fail here or in the path pattern below.
        var authority = AuthorityPattern().Match(rest[..slash]);
        if (!authority.Success)
        {
            return false;
        }

        var host = authority.Groups["host"].Value.ToLowerInvariant();
        if (Uri.CheckHostName(host) != UriHostNameType.Dns
            || !allowedHosts.Any(h => string.Equals(h, host, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var path = rest[slash..];
        if (!PathPattern().IsMatch(path))
        {
            return false;
        }

        path = path.ToLowerInvariant();

        if (path.EndsWith('/'))
        {
            path = path[..^1];
        }

        if (path.EndsWith(GitSuffix, StringComparison.Ordinal))
        {
            path = path[..^GitSuffix.Length];
        }

        // Empty segments ("//", or "/.git" stripped to "/") would give several spellings of one repository.
        var segments = path.Split('/')[1..];
        if (segments.Length == 0 || segments.Any(s => s is "" or "." or ".."))
        {
            return false;
        }

        var candidate = $"{Prefix}{host}{path}{GitSuffix}";
        // Checked again after ".git" is appended: the stored URL must fit the column.
        if (candidate.Length > Repository.UrlMaxLength
            || !Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || !string.Equals(uri.AbsoluteUri, candidate, StringComparison.Ordinal))
        {
            return false;
        }

        canonicalUrl = candidate;
        return true;
    }

    [GeneratedRegex(@"\A(?<host>[A-Za-z0-9.-]+)(:443)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex AuthorityPattern();

    [GeneratedRegex(@"\A/[A-Za-z0-9._~/-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex PathPattern();
}
