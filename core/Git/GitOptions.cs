using System.ComponentModel.DataAnnotations;

namespace securitycheck_portal.Core.Git;

/// <summary>Access to the internal Git server, bound from <c>Git</c>.</summary>
public sealed class GitOptions
{
    public const string SectionName = "Git";

    /// <summary><c>git</c> from PATH by default; on the server set an absolute path to <c>git.exe</c>.</summary>
    [Required]
    public string ExecutablePath { get; set; } = "git";

    /// <summary>Hosts that repository URLs may point to (compared case-insensitively).</summary>
    [MinLength(1, ErrorMessage = "Git:AllowedHosts must list at least one host.")]
    public string[] AllowedHosts { get; set; } = [];

    /// <summary>User name sent with the token in the Basic <c>Authorization</c> header.</summary>
    [Required]
    public string UserName { get; set; } = "pat";

    /// <summary>Read-only PAT. Set only through user-secrets or an environment variable, never in appsettings*.json.</summary>
    [Required(ErrorMessage = "Git:Token is required (set it with dotnet user-secrets in api/).")]
    public string Token { get; set; } = "";

    [Range(1, 600)]
    public int TimeoutSeconds { get; set; } = 30;
}
