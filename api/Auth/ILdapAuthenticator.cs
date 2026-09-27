namespace securitycheck_portal.Auth;

/// <summary>Turns a login and password into exactly one <see cref="LdapAuthResult"/>.</summary>
public interface ILdapAuthenticator
{
    Task<LdapAuthResult> AuthenticateAsync(string userName, string password, CancellationToken cancellationToken);
}

/// <summary>Outcome of an authentication attempt against the directory.</summary>
public abstract record LdapAuthResult
{
    private LdapAuthResult()
    {
    }

    /// <summary>Credentials are valid and the user belongs to the allowed group.</summary>
    public sealed record Success(string UserName, string DisplayName) : LdapAuthResult;

    /// <summary>Wrong login or password (LDAP result code 49), or a login in an unsupported format.</summary>
    public sealed record InvalidCredentials : LdapAuthResult;

    /// <summary>The bind succeeded, but the user is not a member of the allowed group.</summary>
    public sealed record NotInGroup : LdapAuthResult;

    /// <summary>The directory could not be reached (connection, TLS, timeout).</summary>
    public sealed record Unavailable : LdapAuthResult;
}
