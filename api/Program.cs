using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using securitycheck_portal.Auth;
using securitycheck_portal.Core.Data;
using securitycheck_portal.Core.Git;
using securitycheck_portal.Repositories;
using securitycheck_portal.Scans;

var builder = WebApplication.CreateBuilder(args);

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Options are validated at startup so a missing or weak setting fails fast, not at the first login.
builder.Services.AddOptions<LdapOptions>()
    .BindConfiguration(LdapOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<JwtOptions>()
    .BindConfiguration(JwtOptions.SectionName)
    .ValidateDataAnnotations()
    .Validate(o => string.IsNullOrEmpty(o.SigningKey) || o.HasStrongSigningKey(),
        $"Auth:Jwt:SigningKey must be at least {JwtOptions.MinSigningKeyBytes} bytes (HS256).")
    .ValidateOnStart();

// ConnectionStrings:Portal is validated on start as well; migrations are applied by dotnet ef, not here.
builder.Services.AddPortalData();

// Git:Token and Git:AllowedHosts are validated on start; the token comes from user-secrets or the environment.
builder.Services.AddGitResolution();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<JwtIssuer>();
builder.Services.AddSingleton<ILdapAuthenticator, LdapAuthenticator>();
builder.Services.AddSingleton<FailedLoginThrottle>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

// Configured through options (not read eagerly here) so test hosts can override configuration.
builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
    {
        var jwt = jwtOptions.Value;
        bearer.MapInboundClaims = false;
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = jwt.CreateSigningKey(),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = JwtIssuer.DisplayNameClaim,
        };
        bearer.Events = new JwtBearerEvents
        {
            // The token is read only from the sc_auth cookie; the Authorization header is ignored.
            OnMessageReceived = context =>
            {
                var token = AuthCookie.Read(context.Request);
                if (token is null)
                {
                    context.NoResult();
                }
                else
                {
                    context.Token = token;
                }

                return Task.CompletedTask;
            },
        };
    });

// Protected by default: anonymous access requires an explicit AllowAnonymous.
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AuthEndpoints.LoginRateLimitPolicy, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            // RemoteIpAddress can be null (e.g. in-memory test server); share one partition then.
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

// index.html must be revalidated on every load: each build replaces the hashed assets it
// references, so a cached copy would point at files that no longer exist.
var spaFileOptions = new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        if (string.Equals(ctx.File.Name, "index.html", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers.CacheControl = "no-cache";
        }
    },
};

// SPA assets are public: they render the login form and contain no data.
app.UseStaticFiles(spaFileOptions);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    // Protected by the fallback policy like any other endpoint.
    app.MapOpenApi();
}

app.MapAuthEndpoints();
app.MapRepositoryEndpoints();
app.MapScanEndpoints();

// Unknown /api routes must not fall through to index.html. Anonymous callers get 401 from the
// fallback policy; authenticated ones get 404.
app.Map("/api/{**rest}", () => Results.NotFound())
    .ExcludeFromDescription();

app.MapFallbackToFile("index.html", spaFileOptions)
    .AllowAnonymous();

app.Run();

public partial class Program;
