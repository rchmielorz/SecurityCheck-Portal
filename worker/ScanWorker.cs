using Microsoft.Extensions.Options;
using securitycheck_portal.Core.Data;
using securitycheck_portal.Core.Scanning;

namespace securitycheck_portal.Worker;

/// <summary>
/// Takes queued scans one at a time. On start it takes the single-worker lock (<see cref="WorkerLock"/>; without it
/// the process stops with exit code 1), then fails the scans a previous process left running; then, every
/// <see cref="ScanOptions.PollIntervalSeconds"/>, it claims the oldest queued scan and runs it.
/// </summary>
public sealed class ScanWorker(
    ScanQueue queue,
    ScanJobRunner runner,
    IOptions<ScanOptions> options,
    IOptions<DatabaseOptions> databaseOptions,
    IHostApplicationLifetime lifetime,
    TimeProvider timeProvider,
    ILogger<ScanWorker> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    private DateTimeOffset _lastSweep = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromSeconds(options.Value.PollIntervalSeconds);

        WorkerLock? workerLock = null;
        try
        {
            // Recovery and the sweep fail rows and delete checkouts they do not own, so they need proof that this is
            // the only worker. The lock connection stays open until the process stops.
            workerLock = await AcquireLockAsync(pollInterval, stoppingToken);
            if (workerLock is null)
            {
                logger.LogError(
                    "Another SecurityCheck worker holds the lock (PostgreSQL advisory lock {Key}); this worker will not start. "
                    + "Stop the other instance and start this one again", WorkerLock.Key);
                Environment.ExitCode = 1;
                lifetime.StopApplication();
                return;
            }

            await RecoverAsync(pollInterval, stoppingToken);

            logger.LogInformation("Scan worker started; polling every {Seconds} s", pollInterval.TotalSeconds);
            while (!stoppingToken.IsCancellationRequested)
            {
                if (!await TryRunNextAsync(stoppingToken))
                {
                    await Task.Delay(pollInterval, timeProvider, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        finally
        {
            if (workerLock is not null)
            {
                await workerLock.DisposeAsync();
            }
        }
    }

    // The database may not be reachable yet after boot, so this retries; only a held lock is a final answer (null).
    private async Task<WorkerLock?> AcquireLockAsync(TimeSpan retryDelay, CancellationToken stoppingToken)
    {
        while (true)
        {
            try
            {
                return await WorkerLock.TryAcquireAsync(databaseOptions.Value.Portal, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Could not take the worker lock; retrying in {Seconds} s", retryDelay.TotalSeconds);
                await Task.Delay(retryDelay, timeProvider, stoppingToken);
            }
        }
    }

    // The database may not be reachable yet when the service starts after boot, so this retries instead of crashing.
    private async Task RecoverAsync(TimeSpan retryDelay, CancellationToken stoppingToken)
    {
        while (true)
        {
            try
            {
                await queue.RecoverInterruptedAsync(stoppingToken);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Recovery of interrupted scans failed; retrying in {Seconds} s", retryDelay.TotalSeconds);
                await Task.Delay(retryDelay, timeProvider, stoppingToken);
            }
        }
    }

    // Safety net for a scan whose result could not be stored: fails stale Running rows, at most once per interval.
    private async Task SweepAsync(CancellationToken stoppingToken)
    {
        var now = timeProvider.GetUtcNow();
        if (now - _lastSweep < SweepInterval)
        {
            return;
        }

        _lastSweep = now;
        try
        {
            await queue.FailAbandonedAsync(runner.CurrentScanId, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Sweep of abandoned scans failed");
        }
    }

    /// <returns>True when a scan was run (so the next one is looked up without waiting).</returns>
    private async Task<bool> TryRunNextAsync(CancellationToken stoppingToken)
    {
        try
        {
            await SweepAsync(stoppingToken);

            var scanId = await queue.ClaimNextAsync(stoppingToken);
            if (scanId is null)
            {
                return false;
            }

            logger.LogInformation("Running scan {ScanId}", scanId);
            await runner.RunAsync(scanId.Value, stoppingToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never let one bad iteration stop the service.
            logger.LogError(ex, "Scan loop iteration failed");
            return false;
        }
    }
}
