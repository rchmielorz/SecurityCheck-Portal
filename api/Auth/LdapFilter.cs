using System.Text;
using System.Text.RegularExpressions;

namespace securitycheck_portal.Auth;

/// <summary>Login format validation and RFC 4515 escaping of LDAP filter values.</summary>
public static partial class LdapFilter
{
    /// <summary>
    /// Only plain sAMAccountName-style logins are accepted. Anything else is rejected
    /// before a connection to the directory is opened.
    /// </summary>
    public static bool IsValidUserName(string? userName) =>
        userName is not null && UserNamePattern().IsMatch(userName);

    /// <summary>
    /// Escapes <c>\ * ( )</c> and NUL as required by RFC 4515. Defence in depth on top of
    /// <see cref="IsValidUserName"/>, and required for configured values such as group DNs.
    /// </summary>
    public static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(c switch
            {
                '\\' => @"\5c",
                '*' => @"\2a",
                '(' => @"\28",
                ')' => @"\29",
                '\0' => @"\00",
                _ => c.ToString(),
            });
        }

        return builder.ToString();
    }

    /// <summary>
    /// Finds the user entry only when it is a member of <paramref name="allowedGroupDn"/>,
    /// including nested membership (LDAP_MATCHING_RULE_IN_CHAIN). Searching by the same UPN that
    /// was bound guarantees the authorized entry is the bound account.
    /// </summary>
    public static string UserInGroup(string userPrincipalName, string allowedGroupDn) =>
        $"(&(objectClass=user)(userPrincipalName={Escape(userPrincipalName)})" +
        $"(memberOf:1.2.840.113556.1.4.1941:={Escape(allowedGroupDn)}))";

    // \z instead of $: in .NET, $ also matches before a trailing newline.
    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex UserNamePattern();
}
