using Microsoft.Extensions.DependencyInjection;
using securitycheck_portal.Core.Git;

namespace securitycheck_portal.Tests.Git;

/// <summary>The service as registered by the API, with the fake Git server from <see cref="PortalFactory"/>.</summary>
public sealed class PatternResolutionServiceTests(PortalFactory factory) : IClassFixture<PortalFactory>
{
    private static readonly VersionPatternSpec Spec21 =
        VersionPatternSpec.TryParse("2.1.*", out var spec) ? spec : throw new InvalidOperationException();

    private PatternResolutionService Service => factory.Services.GetRequiredService<PatternResolutionService>();

    [Fact]
    public async Task Resolves_to_the_highest_tag_and_its_commit()
    {
        var resolution = await Service.ResolveAsync(FakeGitTagSource.AppUrl, Spec21, CancellationToken.None);

        Assert.Equal(new PatternResolution.Resolved("2.1.10", FakeGitTagSource.Commit2110), resolution);
    }

    [Fact]
    public async Task Repository_without_tags_gives_no_match()
    {
        var resolution = await Service.ResolveAsync(FakeGitTagSource.EmptyUrl, Spec21, CancellationToken.None);

        Assert.IsType<PatternResolution.NoMatch>(resolution);
    }

    [Theory]
    [InlineData(FakeGitTagSource.DownUrl, GitErrorKind.Failed)]
    [InlineData(FakeGitTagSource.SlowUrl, GitErrorKind.Timeout)]
    public async Task Server_errors_are_reported_with_their_kind(string url, GitErrorKind kind)
    {
        var resolution = await Service.ResolveAsync(url, Spec21, CancellationToken.None);

        Assert.Equal(new PatternResolution.Error(kind), resolution);
    }

    [Theory]
    [InlineData("https://evil.example/team/app")]
    [InlineData("https://git.internal/team/app")]
    [InlineData("https://git.internal/Team/App.git")]
    [InlineData("-oProxyCommand=x")]
    public async Task Urls_that_are_not_canonical_on_an_allowed_host_never_reach_git(string url)
    {
        var before = factory.Git.CallCount;

        var resolution = await Service.ResolveAsync(url, Spec21, CancellationToken.None);

        Assert.Equal(new PatternResolution.Error(GitErrorKind.Failed), resolution);
        Assert.Equal(before, factory.Git.CallCount);
    }
}
