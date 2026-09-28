using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace securitycheck_portal.Core.Data;

/// <summary>Database settings, bound from <c>ConnectionStrings</c>.</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "ConnectionStrings";

    public const string PortalConnectionName = nameof(Portal);

    /// <summary>Set only through user-secrets or an environment variable, never in appsettings*.json.</summary>
    [Required(ErrorMessage = "ConnectionStrings:Portal is required (set it with dotnet user-secrets in api/).")]
    public string Portal { get; set; } = "";
}

public static class DataServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="PortalDbContext"/> on PostgreSQL. A missing or empty <c>ConnectionStrings:Portal</c>
    /// stops the app on start. The database is not migrated here.
    /// </summary>
    public static IServiceCollection AddPortalData(this IServiceCollection services)
    {
        services.AddOptions<DatabaseOptions>()
            .BindConfiguration(DatabaseOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Read from configuration rather than the validated options, so design-time tools
        // (dotnet ef migrations add) can build the model without a connection string. The running
        // app is still guarded by ValidateOnStart above.
        services.AddDbContext<PortalDbContext>((serviceProvider, options) =>
            options.UseNpgsql(serviceProvider.GetRequiredService<IConfiguration>()
                .GetConnectionString(DatabaseOptions.PortalConnectionName)));

        return services;
    }
}
