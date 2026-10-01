using Microsoft.Extensions.DependencyInjection;
using securitycheck_portal.Core.Processes;

namespace securitycheck_portal.Core.Git;

public static class GitServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="PatternResolutionService"/> on top of <c>git ls-remote</c>. Missing
    /// <c>Git:Token</c> or <c>Git:AllowedHosts</c> stops the app on start.
    /// </summary>
    public static IServiceCollection AddGitResolution(this IServiceCollection services)
    {
        services.AddOptions<GitOptions>()
            .BindConfiguration(GitOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(o => o.AllowedHosts.All(h => !string.IsNullOrWhiteSpace(h)),
                "Git:AllowedHosts must not contain empty entries.")
            .Validate(o => !o.UserName.Contains(':'),
                "Git:UserName must not contain ':' (it is sent as Basic auth).")
            .ValidateOnStart();

        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IGitTagSource, GitCliTagSource>();
        services.AddSingleton<IGitCheckout, GitCliCheckout>();
        services.AddSingleton<PatternResolutionService>();

        return services;
    }
}
