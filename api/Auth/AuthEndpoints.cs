using System.Security.Claims;

namespace securitycheck_portal.Auth;

public sealed record LoginRequest(string? UserName, string? Password);

public sealed record CurrentUser(string UserName, string DisplayName);

public static class AuthEndpoints
{
    public const string LoginRateLimitPolicy = "login";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api");

        api.MapPost("/auth/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting(LoginRateLimitPolicy);

        api.MapPost("/auth/logout", Logout);

        api.MapGet("/me", Me);

        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext httpContext,
        ILdapAuthenticator authenticator,
        JwtIssuer jwtIssuer,
        FailedLoginThrottle throttle,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        // An empty password is an anonymous bind in AD, which succeeds: reject before touching LDAP.
        if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.BadRequest();
        }

        // Invalid-format logins never reach LDAP, so only valid ones can count toward AD lockout.
        var countsTowardLockout = LdapFilter.IsValidUserName(request.UserName);
        if (countsTowardLockout && throttle.IsBlocked(request.UserName))
        {
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        var result = await authenticator.AuthenticateAsync(request.UserName, request.Password, cancellationToken);

        if (countsTowardLockout && result is LdapAuthResult.InvalidCredentials)
        {
            throttle.RecordFailure(request.UserName);
        }

        if (result is LdapAuthResult.Success success)
        {
            AuthCookie.Append(httpContext.Response, jwtIssuer.Issue(success.UserName, success.DisplayName));
            return Results.Ok(new CurrentUser(success.UserName, success.DisplayName));
        }

        // Never log the password, and never log raw input: an invalid login may carry CR/LF or a
        // password typed into the wrong field.
        var logger = loggerFactory.CreateLogger(typeof(AuthEndpoints).FullName!);
        logger.LogWarning("Failed login for {UserName} from {RemoteIp}: {Outcome}",
            countsTowardLockout ? request.UserName : "<invalid>",
            httpContext.Connection.RemoteIpAddress, result.GetType().Name);

        return result switch
        {
            LdapAuthResult.InvalidCredentials => Results.StatusCode(StatusCodes.Status401Unauthorized),
            LdapAuthResult.NotInGroup => Results.StatusCode(StatusCodes.Status403Forbidden),
            _ => Results.StatusCode(StatusCodes.Status503ServiceUnavailable),
        };
    }

    private static IResult Logout(HttpContext httpContext)
    {
        AuthCookie.Delete(httpContext.Response);
        return Results.NoContent();
    }

    private static IResult Me(ClaimsPrincipal user)
    {
        var userName = user.FindFirstValue(JwtIssuer.UserNameClaim);
        if (string.IsNullOrEmpty(userName))
        {
            return Results.Unauthorized();
        }

        var displayName = user.FindFirstValue(JwtIssuer.DisplayNameClaim);
        return Results.Ok(new CurrentUser(userName, string.IsNullOrEmpty(displayName) ? userName : displayName));
    }
}
