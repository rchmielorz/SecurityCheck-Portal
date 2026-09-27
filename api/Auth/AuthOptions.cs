using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace securitycheck_portal.Auth;

/// <summary>Active Directory connection settings, bound from <c>Auth:Ldap</c>.</summary>
public sealed class LdapOptions
{
    public const string SectionName = "Auth:Ldap";

    [Required]
    public string Host { get; set; } = "";

    [Range(1, 65535)]
    public int Port { get; set; } = 636;

    /// <summary>Users bind as <c>{login}@{UpnSuffix}</c>.</summary>
    [Required]
    public string UpnSuffix { get; set; } = "";

    [Required]
    public string SearchBase { get; set; } = "";

    /// <summary>Only members of this group (directly or through nested groups) may sign in.</summary>
    [Required]
    public string AllowedGroupDn { get; set; } = "";

    [Range(1, 300)]
    public int ConnectTimeoutSeconds { get; set; } = 5;
}

/// <summary>Token settings, bound from <c>Auth:Jwt</c>.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Auth:Jwt";

    /// <summary>HS256 requires a key of at least 256 bits.</summary>
    public const int MinSigningKeyBytes = 32;

    [Required]
    public string Issuer { get; set; } = "";

    [Required]
    public string Audience { get; set; } = "";

    [Range(1, int.MaxValue)]
    public int LifetimeHours { get; set; } = 8;

    /// <summary>Set only through user-secrets or an environment variable, never in appsettings*.json.</summary>
    [Required]
    public string SigningKey { get; set; } = "";

    public bool HasStrongSigningKey() =>
        Encoding.UTF8.GetByteCount(SigningKey) >= MinSigningKeyBytes;

    public SymmetricSecurityKey CreateSigningKey() =>
        new(Encoding.UTF8.GetBytes(SigningKey));
}
