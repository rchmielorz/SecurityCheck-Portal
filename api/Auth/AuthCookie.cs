namespace securitycheck_portal.Auth;

/// <summary>The <c>sc_auth</c> cookie that carries the JWT; the only place the token is read from.</summary>
public static class AuthCookie
{
    public const string Name = "sc_auth";

    public static void Append(HttpResponse response, IssuedToken token) =>
        response.Cookies.Append(Name, token.Token, CreateOptions(token.ExpiresAt));

    public static void Delete(HttpResponse response) =>
        response.Cookies.Delete(Name, CreateOptions(expires: null));

    public static string? Read(HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out var value) && !string.IsNullOrEmpty(value) ? value : null;

    private static CookieOptions CreateOptions(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Expires = expires,
        IsEssential = true,
    };
}
