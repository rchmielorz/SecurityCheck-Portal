using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using securitycheck_portal.Auth;
using securitycheck_portal.Core.Git;

namespace securitycheck_portal.Tests;

/// <summary>
/// Hosts the API in the <c>Testing</c> environment with fake configuration, a fake directory,
/// a fake Git server and a temporary web root that contains <c>index.html</c>. Nothing touches the network or AD, and
/// the connection string is never opened; tests that need a database use <see cref="DatabasePortalFactory"/>.
/// </summary>
public class PortalFactory : WebApplicationFactory<Program>
{
    public const string IndexHtml = "<!doctype html><html><body>securitycheck-portal test shell</body></html>";

    /// <summary>Test-only header that sets the connection's remote IP, so each client gets its own rate-limit partition.</summary>
    public const string RemoteIpHeader = "X-Test-Remote-Ip";

    public const string Issuer = "securitycheck-portal-tests";
    public const string Audience = "securitycheck-portal-tests";

    private static int _nextClientId;

    private readonly string _webRoot;

    public PortalFactory()
    {
        SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        _webRoot = Path.Combine(Path.GetTempPath(), "securitycheck-portal-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_webRoot);
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), IndexHtml);
    }

    public string SigningKey { get; }

    public FakeLdapAuthenticator Ldap { get; } = new();

    public FakeGitTagSource Git { get; } = new();

    /// <summary>
    /// Creates a client on <c>https://localhost</c> (so the <c>Secure</c> cookie is sent back)
    /// with a remote IP of its own, so clients do not share the login rate limit.
    /// </summary>
    public HttpClient CreatePortalClient(bool handleCookies = true, string? remoteIp = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = handleCookies,
        });

        client.DefaultRequestHeaders.Add(RemoteIpHeader, remoteIp ?? NextRemoteIp());
        return client;
    }

    public static string NextRemoteIp()
    {
        var id = Interlocked.Increment(ref _nextClientId);
        return $"10.{(id >> 16) & 0xFF}.{(id >> 8) & 0xFF}.{id & 0xFF}";
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseWebRoot(_webRoot);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Jwt:SigningKey"] = SigningKey,
                ["Auth:Jwt:Issuer"] = Issuer,
                ["Auth:Jwt:Audience"] = Audience,
                ["Auth:Jwt:LifetimeHours"] = "8",
                ["Auth:Ldap:Host"] = "ldap.invalid",
                ["Auth:Ldap:Port"] = "636",
                ["Auth:Ldap:UpnSuffix"] = "example.invalid",
                ["Auth:Ldap:SearchBase"] = "DC=example,DC=invalid",
                ["Auth:Ldap:AllowedGroupDn"] = "CN=SecurityCheck Users,OU=Groups,DC=example,DC=invalid",
                ["Auth:Ldap:ConnectTimeoutSeconds"] = "1",
                // Satisfies start-up validation; registering the DbContext does not open a connection.
                ["ConnectionStrings:Portal"] = "Host=db.invalid;Database=securitycheck_portal_tests",
                // Satisfies start-up validation; git is never started (see FakeGitTagSource).
                ["Git:AllowedHosts:0"] = FakeGitTagSource.Host,
                ["Git:Token"] = "fake-token",
                ["Git:TimeoutSeconds"] = "5",
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ILdapAuthenticator>();
            services.AddSingleton<ILdapAuthenticator>(Ldap);
            services.RemoveAll<IGitTagSource>();
            services.AddSingleton<IGitTagSource>(Git);
            services.AddSingleton<IStartupFilter, RemoteIpStartupFilter>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            try
            {
                Directory.Delete(_webRoot, recursive: true);
            }
            catch (IOException)
            {
                // Best effort: a leftover temp folder must not fail the run.
            }
        }
    }

    /// <summary>Runs before the app's pipeline (and so before the rate limiter).</summary>
    private sealed class RemoteIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(RemoteIpHeader, out var value)
                    && IPAddress.TryParse(value.ToString(), out var address))
                {
                    context.Connection.RemoteIpAddress = address;
                }

                return nextMiddleware(context);
            });

            next(app);
        };
    }
}

/// <summary>
/// Directory stand-in: <c>alice</c> with <see cref="ValidPassword"/> succeeds, <c>bob</c> is not in
/// the group, <c>down</c> simulates an unreachable server, anything else is invalid credentials.
/// </summary>
public sealed class FakeLdapAuthenticator : ILdapAuthenticator
{
    public const string ValidPassword = "correct-horse";
    public const string AliceDisplayName = "Alice Example";

    private int _callCount;

    public int CallCount => Volatile.Read(ref _callCount);

    public Task<LdapAuthResult> AuthenticateAsync(string userName, string password, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _callCount);

        LdapAuthResult result = userName switch
        {
            "down" => new LdapAuthResult.Unavailable(),
            _ when password != ValidPassword => new LdapAuthResult.InvalidCredentials(),
            "alice" => new LdapAuthResult.Success("alice", AliceDisplayName),
            "bob" => new LdapAuthResult.NotInGroup(),
            _ => new LdapAuthResult.InvalidCredentials(),
        };

        return Task.FromResult(result);
    }
}

/// <summary>
/// Git server stand-in keyed by the last path segment (case-insensitive): <c>down</c> fails,
/// <c>slow</c> times out, <c>empty</c> has no tags, anything else (e.g. <c>app</c>) has tags
/// <c>2.1.9</c>, <c>2.1.10</c> (annotated) and a few that no <c>2.1.*</c> pattern may pick.
/// </summary>
public sealed class FakeGitTagSource : IGitTagSource
{
    public const string Host = "git.internal";
    public const string AppUrl = $"https://{Host}/team/app.git";
    public const string DownUrl = $"https://{Host}/team/down.git";
    public const string SlowUrl = $"https://{Host}/team/slow.git";
    public const string EmptyUrl = $"https://{Host}/team/empty.git";

    public const string Commit219 = "1111111111111111111111111111111111111111";

    /// <summary>Commit of <c>2.1.10</c>; the annotated tag object itself has a different SHA.</summary>
    public const string Commit2110 = "2222222222222222222222222222222222222222";

    public const string AppOutput =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\trefs/tags/2.0.3\n" +
        Commit219 + "\trefs/tags/2.1.9\n" +
        "3333333333333333333333333333333333333333\trefs/tags/2.1.10\n" +
        Commit2110 + "\trefs/tags/2.1.10^{}\n" +
        "4444444444444444444444444444444444444444\trefs/tags/2.1.11-rc1\n" +
        "5555555555555555555555555555555555555555\trefs/tags/v2.1.12\n";

    private int _callCount;

    public int CallCount => Volatile.Read(ref _callCount);

    public Task<GitTagListing> ListTagsAsync(string canonicalUrl, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _callCount);

        var repository = Path.GetFileNameWithoutExtension(canonicalUrl[(canonicalUrl.LastIndexOf('/') + 1)..])
            .ToLowerInvariant();
        GitTagListing result = repository switch
        {
            "down" => new GitTagListing.Failure(GitErrorKind.Failed, 128),
            "slow" => new GitTagListing.Failure(GitErrorKind.Timeout, null),
            "empty" => new GitTagListing.Success(""),
            _ => new GitTagListing.Success(AppOutput),
        };

        return Task.FromResult(result);
    }
}
