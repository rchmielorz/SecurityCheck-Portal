using securitycheck_portal.Core.Git;

namespace securitycheck_portal.Tests.Git;

public sealed class RepositoryUrlTests
{
    private static readonly string[] AllowedHosts = ["git.internal"];

    [Theory]
    [InlineData("https://git.internal/team/app.git", "https://git.internal/team/app.git")]
    [InlineData("https://git.internal/team/app", "https://git.internal/team/app.git")]
    [InlineData("https://git.internal/team/app/", "https://git.internal/team/app.git")]
    [InlineData("https://git.internal/team/app.git/", "https://git.internal/team/app.git")]
    [InlineData("https://git.internal/Team/App.GIT/", "https://git.internal/team/app.git")]
    [InlineData("https://GIT.Internal/team/app", "https://git.internal/team/app.git")]
    [InlineData("https://git.internal:443/team/app", "https://git.internal/team/app.git")]
    [InlineData("https://git.internal/team/My-App_2.x~1", "https://git.internal/team/my-app_2.x~1.git")]
    public void TryNormalize_accepts_https_urls_on_allowed_hosts(string input, string expected)
    {
        Assert.True(RepositoryUrl.TryNormalize(input, AllowedHosts, out var canonical));
        Assert.Equal(expected, canonical);
    }

    [Fact]
    public void TryNormalize_compares_allowed_hosts_case_insensitively()
    {
        Assert.True(RepositoryUrl.TryNormalize("https://git.internal/team/app", ["GIT.INTERNAL"], out var canonical));
        Assert.Equal("https://git.internal/team/app.git", canonical);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://git.internal/team/app")]
    [InlineData("https://evil.example/team/app")]
    [InlineData("https://git.internal.evil.example/team/app")]
    [InlineData("https://user:pat@git.internal/team/app")]
    [InlineData("https://git.internal@evil.example/team/app")]
    [InlineData("https://git.internal/team/app?x")]
    [InlineData("https://git.internal/team/app#x")]
    [InlineData("https://git.internal/team/../app")]
    [InlineData("https://git.internal/team/./app")]
    [InlineData("https://git.internal/..")]
    [InlineData("https://git.internal//team/app")]
    [InlineData("https://git.internal/")]
    [InlineData("https://git.internal")]
    [InlineData("https://git.internal/.git")]
    [InlineData("https://git.internal/team/app%2e%2e")]
    [InlineData("https://git.internal/team app")]
    [InlineData(" https://git.internal/team/app")]
    [InlineData("https://git.internal/team/app\n")]
    [InlineData(@"https://git.internal\team\app")]
    [InlineData("https://git.internal:8443/team/app")]
    [InlineData("-oProxyCommand=x")]
    [InlineData("ext::sh")]
    [InlineData("file:///c:/x")]
    [InlineData(@"c:\repos\app")]
    [InlineData(@"\\srv\share")]
    [InlineData("ssh://git.internal/team/app")]
    [InlineData("git@git.internal:team/app")]
    public void TryNormalize_rejects_unsafe_or_foreign_urls(string? input)
    {
        Assert.False(RepositoryUrl.TryNormalize(input, AllowedHosts, out var canonical));
        Assert.Null(canonical);
    }
}
