using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using Microsoft.Extensions.Options;
using Novell.Directory.Ldap;

namespace securitycheck_portal.Auth;

/// <summary>
/// Authenticates against Active Directory over LDAPS: binds as <c>{login}@{UpnSuffix}</c> and,
/// with the user's own credentials (no service account), looks the user up with a filter that
/// requires (possibly nested) membership in the allowed group.
/// </summary>
public sealed class LdapAuthenticator(IOptions<LdapOptions> options, ILogger<LdapAuthenticator> logger)
    : ILdapAuthenticator
{
    private static readonly string[] Attributes = ["displayName"];

    public async Task<LdapAuthResult> AuthenticateAsync(
        string userName, string password, CancellationToken cancellationToken)
    {
        // Callers reject empty passwords too, but an empty simple bind is an anonymous bind in AD
        // and would "succeed", so never let one reach the directory.
        if (!LdapFilter.IsValidUserName(userName) || string.IsNullOrWhiteSpace(password))
        {
            return new LdapAuthResult.InvalidCredentials();
        }

        var settings = options.Value;
        var timeout = TimeSpan.FromSeconds(settings.ConnectTimeoutSeconds);

        // The timeout bounds the whole exchange (connect, TLS handshake, bind, search).
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        var token = timeoutCts.Token;

        var connectionOptions = new LdapConnectionOptions()
            .UseSsl()
            .ConfigureRemoteCertificateValidationCallback(AcceptOnlyValidCertificate);

        using var connection = new LdapConnection(connectionOptions)
        {
            ConnectionTimeout = (int)timeout.TotalMilliseconds,
        };

        try
        {
            await connection.ConnectAsync(settings.Host, settings.Port, token);

            try
            {
                await connection.BindAsync($"{userName}@{settings.UpnSuffix}", password, token);
            }
            catch (LdapException ex) when (ex.ResultCode == LdapException.InvalidCredentials)
            {
                return new LdapAuthResult.InvalidCredentials();
            }

            var results = await connection.SearchAsync(
                settings.SearchBase,
                LdapConnection.ScopeSub,
                LdapFilter.UserInGroup(userName, settings.AllowedGroupDn),
                Attributes,
                false,
                token);

            while (await results.HasMoreAsync(token))
            {
                LdapEntry entry;
                try
                {
                    entry = await results.NextAsync(token);
                }
                catch (LdapReferralException)
                {
                    // AD returns continuation references for other partitions; they are not the user.
                    continue;
                }

                var displayName = entry.GetStringValueOrDefault("displayName", null);
                return new LdapAuthResult.Success(
                    userName, string.IsNullOrWhiteSpace(displayName) ? userName : displayName);
            }

            return new LdapAuthResult.NotInGroup();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("LDAP request to {Host}:{Port} timed out after {Timeout}",
                settings.Host, settings.Port, timeout);
            return new LdapAuthResult.Unavailable();
        }
        catch (Exception ex) when (ex is LdapException or SocketException or IOException or AuthenticationException)
        {
            logger.LogWarning(ex, "LDAP server {Host}:{Port} is unavailable", settings.Host, settings.Port);
            return new LdapAuthResult.Unavailable();
        }
    }

    // Set explicitly instead of relying on the library default: only a certificate that chains
    // to a trusted root and matches the host name is accepted.
    private static bool AcceptOnlyValidCertificate(
        object sender,
        System.Security.Cryptography.X509Certificates.X509Certificate? certificate,
        System.Security.Cryptography.X509Certificates.X509Chain? chain,
        SslPolicyErrors sslPolicyErrors) =>
        sslPolicyErrors == SslPolicyErrors.None;
}
