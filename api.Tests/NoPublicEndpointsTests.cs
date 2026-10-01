using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace securitycheck_portal.Tests;

/// <summary>
/// Guards the "no public page" requirement: every endpoint requires a signed-in user unless it is
/// on the explicit allow-list below. Adding an anonymous endpoint means editing this list on purpose.
/// </summary>
public sealed class NoPublicEndpointsTests(PortalFactory factory) : IClassFixture<PortalFactory>
{
    private const string SpaFallbackPattern = "{*path:nonfile}";

    private static readonly string[] AllowedAnonymousEndpoints =
    [
        "POST /api/auth/login",
        $"GET,HEAD {SpaFallbackPattern}",
    ];

    [Fact]
    public void Only_allow_listed_endpoints_allow_anonymous_access()
    {
        var anonymous = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(Describe)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(AllowedAnonymousEndpoints.Order(StringComparer.Ordinal), anonymous);
    }

    [Fact]
    public void Fallback_policy_requires_an_authenticated_user()
    {
        var options = factory.Services.GetRequiredService<IOptions<AuthorizationOptions>>().Value;

        Assert.NotNull(options.FallbackPolicy);
        Assert.Contains(options.FallbackPolicy.Requirements, r => r is DenyAnonymousAuthorizationRequirement);
    }

    [Theory]
    [InlineData("GET", "/api/me")]
    [InlineData("POST", "/api/auth/logout")]
    [InlineData("GET", "/api/does-not-exist")]
    [InlineData("GET", "/api/repos")]
    [InlineData("POST", "/api/patterns/1/resolve")]
    [InlineData("POST", "/api/patterns/1/scans")]
    [InlineData("GET", "/api/scans/1")]
    public async Task Api_requests_without_cookie_get_401_and_no_html(string method, string path)
    {
        using var client = factory.CreatePortalClient();

        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("<html", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/repos/42")]
    public async Task Spa_routes_without_cookie_serve_index_html(string path)
    {
        using var client = factory.CreatePortalClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(PortalFactory.IndexHtml, await response.Content.ReadAsStringAsync());
        Assert.True(response.Headers.CacheControl?.NoCache);
    }

    private static string Describe(RouteEndpoint endpoint)
    {
        var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
        var method = methods is { Count: > 0 } ? string.Join(",", methods.Order(StringComparer.Ordinal)) : "*";
        return $"{method} {endpoint.RoutePattern.RawText}";
    }
}
