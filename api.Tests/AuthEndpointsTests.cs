using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using securitycheck_portal.Auth;

namespace securitycheck_portal.Tests;

public sealed class AuthEndpointsTests(PortalFactory factory) : IClassFixture<PortalFactory>
{
    [Fact]
    public async Task Successful_login_sets_a_hardened_cookie_and_me_returns_the_user()
    {
        using var client = factory.CreatePortalClient();

        using var login = await TestAuth.LoginAsync(client, "alice", FakeLdapAuthenticator.ValidPassword);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await login.Content.ReadFromJsonAsync<CurrentUser>();
        Assert.Equal(new CurrentUser("alice", FakeLdapAuthenticator.AliceDisplayName), body);

        var cookie = GetAuthCookie(login);
        Assert.False(string.IsNullOrEmpty(cookie.Value.Value));
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Strict, cookie.SameSite);
        Assert.NotNull(cookie.Expires);
        var expectedExpiry = DateTimeOffset.UtcNow.AddHours(8);
        Assert.InRange(cookie.Expires.Value, expectedExpiry.AddMinutes(-2), expectedExpiry.AddMinutes(2));

        using var me = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal("alice", (await me.Content.ReadFromJsonAsync<CurrentUser>())?.UserName);
    }

    [Fact]
    public async Task Logout_deletes_the_cookie_and_me_returns_401_afterwards()
    {
        using var client = factory.CreatePortalClient();
        using (var login = await TestAuth.LoginAsync(client, "alice", FakeLdapAuthenticator.ValidPassword))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }

        using var logout = await client.PostAsync("/api/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        var cookie = GetAuthCookie(logout);
        Assert.True(string.IsNullOrEmpty(cookie.Value.Value));
        Assert.NotNull(cookie.Expires);
        Assert.True(cookie.Expires.Value < DateTimeOffset.UtcNow, "The deletion cookie must already be expired.");

        using var me = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Theory]
    [InlineData("alice", "wrong-password", HttpStatusCode.Unauthorized)]
    [InlineData("nobody", FakeLdapAuthenticator.ValidPassword, HttpStatusCode.Unauthorized)]
    [InlineData("bob", FakeLdapAuthenticator.ValidPassword, HttpStatusCode.Forbidden)]
    [InlineData("down", FakeLdapAuthenticator.ValidPassword, HttpStatusCode.ServiceUnavailable)]
    public async Task Failed_login_maps_the_directory_result_and_sets_no_cookie(
        string userName, string password, HttpStatusCode expected)
    {
        using var client = factory.CreatePortalClient();

        using var login = await TestAuth.LoginAsync(client, userName, password);

        Assert.Equal(expected, login.StatusCode);
        Assert.False(login.Headers.Contains(HeaderNames.SetCookie));
    }

    [Theory]
    [InlineData(null, FakeLdapAuthenticator.ValidPassword)]
    [InlineData("", FakeLdapAuthenticator.ValidPassword)]
    [InlineData("   ", FakeLdapAuthenticator.ValidPassword)]
    [InlineData("alice", null)]
    [InlineData("alice", "")]
    [InlineData("alice", "   ")]
    [InlineData("alice", "\t\r\n")]
    public async Task Blank_login_or_password_returns_400_without_calling_the_directory(
        string? userName, string? password)
    {
        using var client = factory.CreatePortalClient();
        var callsBefore = factory.Ldap.CallCount;

        using var login = await TestAuth.LoginAsync(client, userName, password);

        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
        Assert.Equal(callsBefore, factory.Ldap.CallCount);
    }

    [Fact]
    public async Task Sixth_login_attempt_within_a_minute_from_one_address_returns_429()
    {
        var remoteIp = PortalFactory.NextRemoteIp();
        using var client = factory.CreatePortalClient(remoteIp: remoteIp);

        // Its own login, so the per-account throttle does not block alice in other tests.
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var allowed = await TestAuth.LoginAsync(client, "carol", "wrong-password");
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }

        using var limited = await TestAuth.LoginAsync(client, "alice", FakeLdapAuthenticator.ValidPassword);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);

        // The limit is per address: another address is still allowed.
        using var otherClient = factory.CreatePortalClient();
        using var other = await TestAuth.LoginAsync(otherClient, "alice", FakeLdapAuthenticator.ValidPassword);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    [Fact]
    public async Task Sixth_attempt_after_five_failures_for_one_account_returns_429_from_any_address()
    {
        for (var attempt = 1; attempt <= FailedLoginThrottle.PermitLimit; attempt++)
        {
            using var client = factory.CreatePortalClient();
            using var failed = await TestAuth.LoginAsync(client, "dave", "wrong-password");
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        var callsBefore = factory.Ldap.CallCount;
        using var freshClient = factory.CreatePortalClient();
        using var limited = await TestAuth.LoginAsync(freshClient, "DAVE", "wrong-password");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(callsBefore, factory.Ldap.CallCount);
    }

    [Fact]
    public async Task Correctly_signed_current_token_is_accepted()
    {
        // Control case: proves the hand-crafted tokens below are rejected for the intended reason.
        var now = DateTimeOffset.UtcNow;
        var token = CreateToken(factory.SigningKey, notBefore: now.AddMinutes(-5), expires: now.AddHours(1));

        using var response = await GetMeWithTokenAsync(token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Token_signed_with_another_key_returns_401()
    {
        var otherKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var now = DateTimeOffset.UtcNow;
        var token = CreateToken(otherKey, notBefore: now.AddMinutes(-5), expires: now.AddHours(1));

        using var response = await GetMeWithTokenAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Expired_token_returns_401()
    {
        // Expired well beyond the one-minute clock skew.
        var now = DateTimeOffset.UtcNow;
        var token = CreateToken(factory.SigningKey, notBefore: now.AddHours(-9), expires: now.AddMinutes(-5));

        using var response = await GetMeWithTokenAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static SetCookieHeaderValue GetAuthCookie(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues(HeaderNames.SetCookie, out var values), "Set-Cookie header missing.");
        var cookies = SetCookieHeaderValue.ParseList(values.ToList());
        return Assert.Single(cookies, c => c.Name.Value == AuthCookie.Name);
    }

    private async Task<HttpResponseMessage> GetMeWithTokenAsync(string token)
    {
        using var client = factory.CreatePortalClient(handleCookies: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Add(HeaderNames.Cookie, $"{AuthCookie.Name}={token}");
        return await client.SendAsync(request);
    }

    private static string CreateToken(string signingKey, DateTimeOffset notBefore, DateTimeOffset expires) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = PortalFactory.Issuer,
            Audience = PortalFactory.Audience,
            IssuedAt = notBefore.UtcDateTime,
            NotBefore = notBefore.UtcDateTime,
            Expires = expires.UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                [JwtIssuer.UserNameClaim] = "alice",
                [JwtIssuer.DisplayNameClaim] = FakeLdapAuthenticator.AliceDisplayName,
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256),
        });
}
