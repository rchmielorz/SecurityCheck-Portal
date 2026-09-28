using securitycheck_portal.Core.Git;

namespace securitycheck_portal.Tests.Git;

public sealed class PatternResolverTests
{
    private static VersionPatternSpec Spec(string pattern)
    {
        Assert.True(VersionPatternSpec.TryParse(pattern, out var spec));
        return spec;
    }

    private static RemoteTag Tag(string name) => new(name, $"sha-of-{name}");

    [Fact]
    public void Picks_the_highest_hotfix_as_a_number()
    {
        var resolution = PatternResolver.Resolve([Tag("2.1.10"), Tag("2.1.9"), Tag("2.1.2")], Spec("2.1.*"));

        Assert.Equal(new PatternResolution.Resolved("2.1.10", "sha-of-2.1.10"), resolution);
    }

    [Fact]
    public void Skips_tags_outside_the_format()
    {
        var resolution = PatternResolver.Resolve(
            [Tag("2.1.6"), Tag("2.1.7-rc1"), Tag("2.1.7.1"), Tag("2.1.07"), Tag("v2.1.8")],
            Spec("2.1.*"));

        Assert.Equal(new PatternResolution.Resolved("2.1.6", "sha-of-2.1.6"), resolution);
    }

    [Fact]
    public void Only_malformed_tags_give_no_match()
    {
        var resolution = PatternResolver.Resolve(
            [Tag("2.1.7-rc1"), Tag("2.1.7.1"), Tag("2.1.07"), Tag("v2.1.8")],
            Spec("2.1.*"));

        Assert.IsType<PatternResolution.NoMatch>(resolution);
    }

    [Fact]
    public void Tags_of_another_line_only_give_no_match()
    {
        var resolution = PatternResolver.Resolve([Tag("2.0.5"), Tag("2.2.0"), Tag("3.1.4")], Spec("2.1.*"));

        Assert.IsType<PatternResolution.NoMatch>(resolution);
    }

    [Fact]
    public void No_tags_give_no_match()
    {
        Assert.IsType<PatternResolution.NoMatch>(PatternResolver.Resolve([], Spec("2.1.*")));
    }

    [Fact]
    public void Resolves_raw_output_to_the_peeled_commit()
    {
        var resolution = PatternResolver.Resolve(FakeGitTagSource.AppOutput, Spec("2.1.*"));

        Assert.Equal(new PatternResolution.Resolved("2.1.10", FakeGitTagSource.Commit2110), resolution);
    }
}
