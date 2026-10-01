using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using securitycheck_portal.Core.Processes;

namespace securitycheck_portal.Core.Scanning;

public static class ScanServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Trivy scanner and its helpers. A missing <c>Scan:CacheDirectory</c> or <c>Scan:WorkRoot</c>
    /// stops the app on start. The checkout (<c>AddGitResolution</c>) reads <c>Scan:CloneTimeoutMinutes</c> too.
    /// </summary>
    public static IServiceCollection AddScanning(this IServiceCollection services)
    {
        services.AddOptions<ScanOptions>()
            .BindConfiguration(ScanOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(o => !string.IsNullOrWhiteSpace(o.CacheDirectory) && !string.IsNullOrWhiteSpace(o.WorkRoot),
                "Scan:CacheDirectory and Scan:WorkRoot must not be blank.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<LockFileDetector>();
        services.AddSingleton<ITrivyScanner, TrivyScanner>();
        // The pipeline needs AddPortalData and AddGitResolution too (the worker registers all three).
        services.AddSingleton<ScanQueue>();
        services.AddSingleton<ScanJobRunner>();

        return services;
    }
}
