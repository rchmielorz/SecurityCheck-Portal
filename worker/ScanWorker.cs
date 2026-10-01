using Microsoft.Extensions.Options;
using securitycheck_portal.Core.Scanning;

namespace securitycheck_portal.Worker;

/// <summary>
/// Takes queued scans one at a time. On start it fails the scans a previous process left running; then, every
/// <see cref="ScanOptions.PollIntervalSeconds"/>, it claims the oldest queued scan and runs it.
/// </summary>
public sealed class ScanWorker(
    ScanQueue queue,
    ScanJobRunner runner,
    IOptions<ScanOptions> options,
    TimeProvider timeProvider,
    ILogger<ScanWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromSeconds(options.Value.PollIntervalSeconds);

        try
        {
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

    /// <returns>True when a scan was run (so the next one is looked up without waiting).</returns>
    private async Task<bool> TryRunNextAsync(CancellationToken stoppingToken)
    {
        try
        {
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
