using securitycheck_portal.Auth;

namespace securitycheck_portal.Tests;

public sealed class LdapFilterTests
{
    [Theory]
    [InlineData(@"\", @"\5c")]
    [InlineData("*", @"\2a")]
    [InlineData("(", @"\28")]
    [InlineData(")", @"\29")]
    [InlineData("\0", @"\00")]
    [InlineData("jan.kowalski", "jan.kowalski")]
    [InlineData(@"a*)(cn=*)\", @"a\2a\29\28cn=\2a\29\5c")]
    public void Escape_encodes_filter_special_characters(string value, string expected)
    {
        Assert.Equal(expected, LdapFilter.Escape(value));
    }

    [Fact]
    public void UserInGroup_escapes_both_the_login_and_the_group_dn()
    {
        var filter = LdapFilter.UserInGroup("a*@example.invalid", "CN=Team (Ops),DC=example,DC=invalid");

        Assert.Equal(
            @"(&(objectClass=user)(userPrincipalName=a\2a@example.invalid)" +
            @"(memberOf:1.2.840.113556.1.4.1941:=CN=Team \28Ops\29,DC=example,DC=invalid))",
            filter);
    }

    [Theory]
    [InlineData("jan.kowalski")]
    [InlineData("j_k-01")]
    public void IsValidUserName_accepts_plain_logins(string userName)
    {
        Assert.True(LdapFilter.IsValidUserName(userName));
    }

    [Fact]
    public void IsValidUserName_accepts_a_login_of_exactly_64_characters()
    {
        Assert.True(LdapFilter.IsValidUserName(new string('a', 64)));
    }

    [Theory]
    [InlineData("a*")]
    [InlineData("a)(cn=*")]
    [InlineData(@"DOMENA\user")]
    [InlineData("user@x")]
    [InlineData("jan.kowalski\n")]
    [InlineData("")]
    [InlineData(null)]
    public void IsValidUserName_rejects_logins_that_could_alter_the_filter(string? userName)
    {
        Assert.False(LdapFilter.IsValidUserName(userName));
    }

    [Fact]
    public void IsValidUserName_rejects_a_login_longer_than_64_characters()
    {
        Assert.False(LdapFilter.IsValidUserName(new string('a', 65)));
    }
}
