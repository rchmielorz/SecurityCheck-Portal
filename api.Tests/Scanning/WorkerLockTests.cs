using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using securitycheck_portal.Core.Scanning;

namespace securitycheck_portal.Tests.Scanning;

/// <summary>The single-worker advisory lock, against a real PostgreSQL.</summary>
public sealed class WorkerLockTests(DatabasePortalFactory factory) : IClassFixture<DatabasePortalFactory>
{
    private string ConnectionString
        => factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("Portal")!;

    [SkippableFact]
    public async Task Second_concurrent_acquire_fails_and_the_lock_is_free_again_after_dispose()
    {
        factory.SkipIfDatabaseUnavailable();

        var first = await WorkerLock.TryAcquireAsync(ConnectionString, CancellationToken.None);
        Assert.NotNull(first);

        var second = await WorkerLock.TryAcquireAsync(ConnectionString, CancellationToken.None);
        Assert.Null(second);

        await first.DisposeAsync();

        await using var third = await WorkerLock.TryAcquireAsync(ConnectionString, CancellationToken.None);
        Assert.NotNull(third);
    }

    [SkippableFact]
    public async Task Lock_is_held_for_as_long_as_the_instance_lives()
    {
        factory.SkipIfDatabaseUnavailable();

        await using var held = await WorkerLock.TryAcquireAsync(ConnectionString, CancellationToken.None);
        Assert.NotNull(held);

        // Time passes and unrelated connections come and go; the lock stays with its own session.
        await Task.Delay(200);
        for (var i = 0; i < 3; i++)
        {
            Assert.Null(await WorkerLock.TryAcquireAsync(ConnectionString, CancellationToken.None));
        }
    }
}
