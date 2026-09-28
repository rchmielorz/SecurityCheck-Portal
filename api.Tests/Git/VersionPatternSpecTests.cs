using securitycheck_portal.Core.Git;

namespace securitycheck_portal.Tests.Git;

public sealed class VersionPatternSpecTests
{
    [Theory]
    [InlineData("2.1.*", 2, 1)]
    [InlineData("0.0.*", 0, 0)]
    [InlineData("10.20.*", 10, 20)]
    [InlineData("999999999.0.*", 999999999, 0)]
    public void TryParse_accepts_major_minor_star(string text, int major, int minor)
    {
        Assert.True(VersionPatternSpec.TryParse(text, out var spec));
        Assert.Equal(major, spec.Major);
        Assert.Equal(minor, spec.Minor);
        Assert.Equal(text, spec.Canonical);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2.*")]
    [InlineData("2.1.7")]
    [InlineData("v2.1.*")]
    [InlineData("02.1.*")]
    [InlineData("2.01.*")]
    [InlineData("2.1.x")]
    [InlineData("2.1.*.*")]
    [InlineData("2.1.*\n")]
    [InlineData(" 2.1.*")]
    [InlineData("1000000000.1.*")]
    public void TryParse_rejects_anything_else(string? text)
    {
        Assert.False(VersionPatternSpec.TryParse(text, out _));
    }

    [Theory]
    [InlineData("2.1.0", 0)]
    [InlineData("2.1.7", 7)]
    [InlineData("2.1.10", 10)]
    public void TryMatch_accepts_tags_of_the_same_line(string tag, int expectedHotfix)
    {
        Assert.True(VersionPatternSpec.TryParse("2.1.*", out var spec));

        Assert.True(spec.TryMatch(tag, out var hotfix));
        Assert.Equal(expectedHotfix, hotfix);
    }

    [Theory]
    [InlineData("2.1.07")]
    [InlineData("2.1.7-rc1")]
    [InlineData("2.1.7.1")]
    [InlineData("v2.1.8")]
    [InlineData("2.10.1")]
    [InlineData("12.1.1")]
    [InlineData("2.1")]
    [InlineData("2.1.")]
    [InlineData("2.1.7\n")]
    public void TryMatch_rejects_other_tags(string tag)
    {
        Assert.True(VersionPatternSpec.TryParse("2.1.*", out var spec));

        Assert.False(spec.TryMatch(tag, out _));
    }
}
