using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace securitycheck_portal.Auth;

public sealed record IssuedToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>Issues HS256 tokens carrying <c>sub</c> (login) and <c>name</c> (display name).</summary>
public sealed class JwtIssuer(IOptions<JwtOptions> options, TimeProvider timeProvider)
{
    public const string UserNameClaim = JwtRegisteredClaimNames.Sub;
    public const string DisplayNameClaim = JwtRegisteredClaimNames.Name;

    private readonly JsonWebTokenHandler _handler = new();

    public IssuedToken Issue(string userName, string displayName)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.AddHours(settings.LifetimeHours);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                [UserNameClaim] = userName,
                [DisplayNameClaim] = displayName,
            },
            SigningCredentials = new SigningCredentials(settings.CreateSigningKey(), SecurityAlgorithms.HmacSha256),
        };

        return new IssuedToken(_handler.CreateToken(descriptor), expiresAt);
    }
}
