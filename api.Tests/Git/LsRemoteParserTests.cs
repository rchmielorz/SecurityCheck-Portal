using securitycheck_portal.Core.Git;

namespace securitycheck_portal.Tests.Git;

public sealed class LsRemoteParserTests
{
    private const string ShaA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string ShaB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void Lightweight_tag_points_at_its_own_sha()
    {
        Assert.True(LsRemoteParser.TryParse($"{ShaA}\trefs/tags/2.1.7\n", out var tags, out _));

        Assert.Equal([new RemoteTag("2.1.7", ShaA)], tags);
    }

    [Fact]
    public void Annotated_tag_points_at_the_peeled_commit_not_the_tag_object()
    {
        var output = $"{ShaA}\trefs/tags/2.1.7\n{ShaB}\trefs/tags/2.1.7^{{}}\n";

        Assert.True(LsRemoteParser.TryParse(output, out var tags, out _));

        Assert.Equal([new RemoteTag("2.1.7", ShaB)], tags);
    }

    [Fact]
    public void Windows_line_endings_and_upper_case_sha_are_accepted()
    {
        var output = $"{ShaA.ToUpperInvariant()}\trefs/tags/2.1.7\r\n";

        Assert.True(LsRemoteParser.TryParse(output, out var tags, out _));

        Assert.Equal([new RemoteTag("2.1.7", ShaA)], tags);
    }

    [Fact]
    public void Refs_outside_tags_are_ignored()
    {
        var output = $"{ShaA}\tHEAD\n{ShaA}\trefs/heads/main\n{ShaB}\trefs/tags/2.1.7\n";

        Assert.True(LsRemoteParser.TryParse(output, out var tags, out _));

        Assert.Equal([new RemoteTag("2.1.7", ShaB)], tags);
    }

    [Fact]
    public void Empty_output_has_no_tags()
    {
        Assert.True(LsRemoteParser.TryParse("", out var tags, out _));

        Assert.Empty(tags);
    }

    [Fact]
    public void The_same_ref_twice_with_the_same_sha_is_accepted()
    {
        var output = $"{ShaA}\trefs/tags/2.1.7\n{ShaA}\trefs/tags/2.1.7\n";

        Assert.True(LsRemoteParser.TryParse(output, out var tags, out _));

        Assert.Single(tags);
    }

    [Theory]
    [InlineData("not a line")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\trefs/tags/2.1.7")] // 39 hex
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa refs/tags/2.1.7")] // space, not tab
    [InlineData("gggggggggggggggggggggggggggggggggggggggg\trefs/tags/2.1.7")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\trefs/tags/2.1.7 extra")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\trefs/tags/")]
    public void Malformed_line_is_ambiguous(string line)
    {
        var output = $"{ShaB}\trefs/tags/2.1.6\n{line}\n";

        Assert.False(LsRemoteParser.TryParse(output, out _, out var reason));
        Assert.False(string.IsNullOrEmpty(reason));
    }

    [Fact]
    public void The_same_ref_with_different_shas_is_ambiguous()
    {
        var output = $"{ShaA}\trefs/tags/2.1.7\n{ShaB}\trefs/tags/2.1.7\n";

        Assert.False(LsRemoteParser.TryParse(output, out _, out _));
    }

    [Fact]
    public void Peeled_line_without_its_tag_line_is_ambiguous()
    {
        Assert.False(LsRemoteParser.TryParse($"{ShaB}\trefs/tags/2.1.7^{{}}\n", out _, out _));
    }

    [Fact]
    public void Malformed_output_resolves_to_ambiguous()
    {
        Assert.True(VersionPatternSpec.TryParse("2.1.*", out var spec));

        var resolution = PatternResolver.Resolve("garbage\n", spec);

        Assert.IsType<PatternResolution.Ambiguous>(resolution);
    }
}
