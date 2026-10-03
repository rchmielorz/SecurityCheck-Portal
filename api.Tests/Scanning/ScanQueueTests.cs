using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using securitycheck_portal.Core.Data;
using securitycheck_portal.Core.Git;
using securitycheck_portal.Core.Scanning;

namespace securitycheck_portal.Tests.Scanning;

/// <summary>The claim and the restart recovery, against a real PostgreSQL (FOR UPDATE SKIP LOCKED is not emulated).</summary>
public sealed class ScanQueueTests(DatabasePortalFactory factory) : IClassFixture<DatabasePortalFactory>, IDisposable
{
    private static readonly DateTimeOffset Base = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    private static long _nextPatternId = 2_000_000;

    private readonly string _workRoot = Path.Combine(Path.GetTempPath(), "scanqueue-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_workRoot))
        {
            foreach (var file in Directory.EnumerateFiles(_workRoot, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_workRoot, recursive: true);
        }
    }

    private ScanQueue Queue => new(
        factory.Services.GetRequiredService<IServiceScopeFactory>(),
        Options.Create(new ScanOptions { CacheDirectory = Path.Combine(_workRoot, "cache"), WorkRoot = _workRoot }),
        Options.Create(new GitOptions()),
        TimeProvider.System,
        NullLogger<ScanQueue>.Instance);

    [SkippableFact]
    public async Task Claim_returns_null_when_nothing_is_queued()
    {
        factory.SkipIfDatabaseUnavailable();
        await ClearQueueAsync();

        Assert.Null(await Queue.ClaimNextAsync(CancellationToken.None));
    }

    [SkippableFact]
    public async Task Claim_takes_the_oldest_queued_scan_first_and_marks_it_running()
    {
        factory.SkipIfDatabaseUnavailable();
        await ClearQueueAsync();
        var newest = await AddScanAsync(ScanStatus.Queued, Base.AddMinutes(3));
        var oldest = await AddScanAsync(ScanStatus.Queued, Base.AddMinutes(1));
        var middle = await AddScanAsync(ScanStatus.Queued, Base.AddMinutes(2));
        var running = await AddScanAsync(ScanStatus.Running, Base);

        Assert.Equal(oldest, await Queue.ClaimNextAsync(CancellationToken.None));
        Assert.Equal(middle, await Queue.ClaimNextAsync(CancellationToken.None));
        Assert.Equal(newest, await Queue.ClaimNextAsync(CancellationToken.None));
        Assert.Null(await Queue.ClaimNextAsync(CancellationToken.None));

        await using var scope = factory.CreateDbScope(out var db);
        var claimed = await db.Scans.SingleAsync(s => s.Id == oldest);
        Assert.Equal(ScanStatus.Running, claimed.Status);
        Assert.NotNull(claimed.StartedAt);
        Assert.Null((await db.Scans.SingleAsync(s => s.Id == running)).StartedAt);
    }

    [SkippableFact]
    public async Task Parallel_claims_of_one_scan_have_exactly_one_winner()
    {
        factory.SkipIfDatabaseUnavailable();
        await ClearQueueAsync();
        var only = await AddScanAsync(ScanStatus.Queued, Base);

        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => Queue.ClaimNextAsync(CancellationToken.None))));

        Assert.Equal(only, Assert.Single(results, r => r is not null));
    }

    [SkippableFact]
    public async Task Parallel_claims_of_several_scans_never_return_the_same_scan_twice()
    {
        factory.SkipIfDatabaseUnavailable();
        await ClearQueueAsync();
        var ids = new List<long>();
        for (var i = 0; i < 3; i++)
        {
            ids.Add(await AddScanAsync(ScanStatus.Queued, Base.AddMinutes(i)));
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => Queue.ClaimNextAsync(CancellationToken.None))));

        var claimed = results.Where(r => r is not null).Select(r => r!.Value).ToList();
        Assert.Equal(ids.OrderBy(i => i), claimed.OrderBy(i => i));
    }

    [SkippableFact]
    public async Task Recovery_fails_running_scans_as_interrupted_and_leaves_the_others()
    {
        factory.SkipIfDatabaseUnavailable();
        await ClearQueueAsync();
        var running = await AddScanAsync(ScanStatus.Running, Base);
        var queued = await AddScanAsync(ScanStatus.Queued, Base);
        var completed = await AddScanAsync(ScanStatus.Completed, Base);

        var count = await Queue.RecoverInterruptedAsync(CancellationToken.None);

        Assert.Equal(1, count);
        await using var scope = factory.CreateDbScope(out var db);
        var recovered = await db.Scans.SingleAsync(s => s.Id == running);
        Assert.Equal(ScanStatus.Failed, recovered.Status);
        Assert.Equal(ScanFailureReason.Interrupted, recovered.FailureReason);
        Assert.NotNull(recovered.FinishedAt);
        Assert.Equal(ScanStatus.Queued, (await db.Scans.SingleAsync(s => s.Id == queued)).Status);
        Assert.Equal(ScanStatus.Completed, (await db.Scans.SingleAsync(s => s.Id == completed)).Status);
    }

    [SkippableFact]
    public async Task Recovery_removes_orphaned_checkouts_and_stale_reports_only()
    {
        factory.SkipIfDatabaseUnavailable();
        Directory.CreateDirectory(_workRoot);
        var orphan = Path.Combine(_workRoot, "4711");
        Directory.CreateDirectory(Path.Combine(orphan, ".git"));
        var readOnly = Path.Combine(orphan, ".git", "pack.idx");
        File.WriteAllText(readOnly, "x");
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        var report = Path.Combine(_workRoot, "trivy-report-abc.json");
        File.WriteAllText(report, "{}");
        var foreignDirectory = Path.Combine(_workRoot, "cache");
        Directory.CreateDirectory(foreignDirectory);
        var foreignFile = Path.Combine(_workRoot, "notes.txt");
        File.WriteAllText(foreignFile, "keep");

        await Queue.RecoverInterruptedAsync(CancellationToken.None);

        Assert.False(Directory.Exists(orphan));
        Assert.False(File.Exists(report));
        Assert.True(Directory.Exists(foreignDirectory));
        Assert.True(File.Exists(foreignFile));
    }

    [SkippableFact]
    public async Task Recovery_tolerates_a_missing_work_root()
    {
        factory.SkipIfDatabaseUnavailable();

        await Queue.RecoverInterruptedAsync(CancellationToken.None);

        Assert.False(Directory.Exists(_workRoot));
    }

    [SkippableFact]
    public async Task Sweep_fails_stale_running_scans_and_leaves_fresh_inflight_queued_and_finished_ones()
    {
        factory.SkipIfDatabaseUnavailable();
        await ClearQueueAsync();
        var limit = ScanQueue.AbandonedAfter(new ScanOptions(), new GitOptions());
        var now = DateTimeOffset.UtcNow;
        var stale = await AddScanAsync(ScanStatus.Running, Base, now - limit - TimeSpan.FromMinutes(1));
        var inFlight = await AddScanAsync(ScanStatus.Running, Base, now - limit - TimeSpan.FromMinutes(1));
        var fresh = await AddScanAsync(ScanStatus.Running, Base, now - TimeSpan.FromMinutes(1));
        var queued = await AddScanAsync(ScanStatus.Queued, Base);
        var completed = await AddScanAsync(ScanStatus.Completed, Base, now - limit - TimeSpan.FromMinutes(1));

        var count = await Queue.FailAbandonedAsync(inFlight, CancellationToken.None);

        Assert.Equal(1, count);
        await using var scope = factory.CreateDbScope(out var db);
        var swept = await db.Scans.SingleAsync(s => s.Id == stale);
        Assert.Equal(ScanStatus.Failed, swept.Status);
        Assert.Equal(ScanFailureReason.Interrupted, swept.FailureReason);
        Assert.Equal(ScanQueue.AbandonedDetail, swept.FailureDetail);
        Assert.NotNull(swept.FinishedAt);
        Assert.Equal(ScanStatus.Running, (await db.Scans.SingleAsync(s => s.Id == inFlight)).Status);
        Assert.Equal(ScanStatus.Running, (await db.Scans.SingleAsync(s => s.Id == fresh)).Status);
        Assert.Equal(ScanStatus.Queued, (await db.Scans.SingleAsync(s => s.Id == queued)).Status);
        Assert.Equal(ScanStatus.Completed, (await db.Scans.SingleAsync(s => s.Id == completed)).Status);

        // Without an in-flight scan the formerly protected one is swept too.
        Assert.Equal(1, await Queue.FailAbandonedAsync(null, CancellationToken.None));
    }

    [Fact]
    public void Abandoned_limit_is_the_sum_of_all_timeouts_plus_a_margin()
    {
        var scan = new ScanOptions { CloneTimeoutMinutes = 10, ScanTimeoutMinutes = 15, DbUpdateTimeoutMinutes = 5 };
        var git = new GitOptions { TimeoutSeconds = 30 };

        var limit = ScanQueue.AbandonedAfter(scan, git);

        // 30 min (clone + scan + DB) + 2 x 30 s git + 2 x 1 min trivy version + 5 min margin
        Assert.Equal(TimeSpan.FromMinutes(30 + 1 + 2 + 5), limit);
        Assert.True(ScanQueue.AbandonedAfter(scan, new GitOptions { TimeoutSeconds = 600 }) > limit);
    }

    // Other tests of this class leave queued scans behind; a claim is global, so start from an empty queue.
    private async Task ClearQueueAsync()
    {
        await using var scope = factory.CreateDbScope(out var db);
        await db.Scans
            .Where(s => s.Status == ScanStatus.Queued || s.Status == ScanStatus.Running)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.Status, ScanStatus.Completed));
    }

    private async Task<long> AddScanAsync(ScanStatus status, DateTimeOffset requestedAt, DateTimeOffset? startedAt = null)
    {
        await using var scope = factory.CreateDbScope(out var db);
        var scan = new Scan
        {
            PatternId = Interlocked.Increment(ref _nextPatternId),
            RepositoryId = 0,
            RepositoryUrl = $"https://git.example.invalid/team/{Guid.NewGuid():N}",
            Pattern = "2.1.*",
            Status = status,
            RequestedBy = "alice",
            RequestedAt = requestedAt,
            StartedAt = startedAt,
        };
        db.Scans.Add(scan);
        await db.SaveChangesAsync();
        return scan.Id;
    }
}
