using DotNet.Testcontainers.Builders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using securitycheck_portal.Core.Data;
using Testcontainers.PostgreSql;

namespace securitycheck_portal.Tests;

/// <summary>
/// <see cref="PortalFactory"/> backed by a real PostgreSQL 17 in a Docker container, started once per
/// test class and migrated to the latest schema. Without Docker the container is not started and
/// tests call <see cref="SkipIfDatabaseUnavailable"/> to be reported as skipped instead of failed.
/// </summary>
public sealed class DatabasePortalFactory : PortalFactory, IAsyncLifetime
{
    public const string DockerUnavailableMessage = "Docker niedostępny — testy bazy pominięte";

    private PostgreSqlContainer? _container;

    public bool IsDatabaseAvailable => _container is not null;

    /// <summary>Call first in every <c>[SkippableFact]</c> that uses this factory.</summary>
    public void SkipIfDatabaseUnavailable() => Skip.If(!IsDatabaseAvailable, DockerUnavailableMessage);

    /// <summary>A scope with a fresh <see cref="PortalDbContext"/>; dispose it after use.</summary>
    public AsyncServiceScope CreateDbScope(out PortalDbContext db)
    {
        var scope = Services.CreateAsyncScope();
        db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        return scope;
    }

    async Task IAsyncLifetime.InitializeAsync()
    {
        PostgreSqlContainer container;
        try
        {
            container = new PostgreSqlBuilder("postgres:17-alpine").Build();
            await container.StartAsync();
        }
        catch (DockerUnavailableException)
        {
            // Tests skip themselves through SkipIfDatabaseUnavailable.
            return;
        }

        _container = container;

        // Builds the host with the container's connection string (see ConfigureWebHost).
        await using var scope = CreateDbScope(out var db);
        await db.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        if (_container is null)
        {
            return;
        }

        // Added after the base configuration, so it replaces the placeholder connection string.
        var connectionString = _container.GetConnectionString();
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Portal"] = connectionString,
            });
        });
    }
}
