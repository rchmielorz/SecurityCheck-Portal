using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using securitycheck_portal.Core.Data;
using securitycheck_portal.Core.Git;
using securitycheck_portal.Core.Scanning;

namespace securitycheck_portal.Tests.Scanning;

/// <summary>The scan pipeline against a real PostgreSQL, with fake Git (tags and checkout) and a fake Trivy.</summary>
public sealed class ScanJobRunnerTests : IClassFixture<DatabasePortalFactory>, IDisposable
{
    private const string Token = "glpat-secret-token-123";
    private static readonly DateTimeOffset DbDate = new(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);

    private readonly DatabasePortalFactory _factory;
    private readonly string _workRoot = Path.Combine(Path.GetTempPath(), "scanjob-" + Guid.NewGuid().ToString("N"));
    private readonly GitOptions _git = new() { AllowedHosts = [FakeGitTagSource.Host], Token = Token };
    private readonly ScriptedTagSource _tags = new();
    private readonly FakeCheckout _checkout = new();
    private readonly FakeScanner _scanner = new();

    public ScanJobRunnerTests(DatabasePortalFactory factory) => _factory = factory;

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

    private ScanJobRunner Runner
    {
        get
        {
            var gitOptions = Options.Create(_git);
            return new ScanJobRunner(
                _factory.Services.GetRequiredService<IServiceScopeFactory>(),
                new PatternResolutionService(_tags, gitOptions, NullLogger<PatternResolutionService>.Instance),
                _checkout,
                _scanner,
                gitOptions,
                Options.Create(new ScanOptions { CacheDirectory = Path.Combine(_workRoot, "cache"), WorkRoot = _workRoot }),
                TimeProvider.System,
                NullLogger<ScanJobRunner>.Instance);
        }
    }

    private string DirectoryOf(Seed seed) => Path.Combine(_workRoot, seed.ScanId.ToString());

    [SkippableFact]
    public async Task Completed_scan_stores_tag_commit_trivy_data_and_findings()
    {
        _factory.SkipIfDatabaseUnavailable();
        var seed = await SeedAsync();
        _scanner.Outcome = new ScanOutcome.Completed(
            [Finding("lodash", "CVE-1", FindingSeverity.High), Finding("axios", "CVE-2", FindingSeverity.Low)],
            "0.58.0", DbDate);

        await Runner.RunAsync(seed.ScanId, CancellationToken.None);

        var scan = await LoadAsync(seed.ScanId);
        Assert.Equal(ScanStatus.Completed, scan.Status);
        Assert.Null(scan.FailureReason);
        Assert.Equal("2.1.10", scan.ScannedTag);
        Assert.Equal(FakeGitTagSource.Commit2110, scan.ScannedCommit);
        Assert.Equal("0.58.0", scan.TrivyVersion);
        Assert.Equal(DbDate, scan.TrivyDbUpdatedAt);
        Assert.NotNull(scan.FinishedAt);
        Assert.Empty(scan.UnscannedItems);
        Assert.Equal(["axios", "lodash"], scan.Findings.Select(f => f.Library).Order());

        Assert.Equal(DirectoryOf(seed), _checkout.TargetDirectory);
        Assert.Equal(DirectoryOf(seed), _scanner.ScannedDirectory);
        Assert.True(_scanner.DirectoryExisted);
        Assert.Equal((seed.Url, "2.1.10", FakeGitTagSource.Commit2110), (_checkout.Url, _checkout.Tag, _checkout.Commit));
        Assert.False(Directory.Exists(DirectoryOf(seed)));
    }

    [SkippableFact]
    public async Task Current_scan_id_is_set_while_the_scan_runs_and_cleared_afterwards()
    {
        _factory.SkipIfDatabaseUnavailable();
        var seed = await SeedAsync();
        var runner = Runner;
        long? during = null;
        _scanner.OnScan = () => during = runner.CurrentScanId;

        Assert.Null(runner.CurrentScanId);
        await runner.RunAsync(seed.ScanId, CancellationToken.None);

        Assert.Equal(seed.ScanId, during);
        Assert.Null(runner.CurrentScanId);
    }

    [SkippableFact]
    public async Task Incomplete_scan_stores_findings_and_unscanned_items()
    {
        _factory.SkipIfDatabaseUnavailable();
        var seed = await SeedAsync();
        _scanner.Outcome = new ScanOutcome.Incomplete(
            [Finding("lodash", "CVE-1", FindingSeverity.Critical)],
            [
                new UnscannedItem("src/app/packages.lock.json", UnscannedReason.NoLockFile),
                new UnscannedItem("src/web/packages.lock.json", UnscannedReason.RestoreFailed, "restore exited with 1"),
                new UnscannedItem("src/app/packages.lock.json", UnscannedReason.Unknown, "duplicate path"),
            ],
            "0.58.0", DbDate);

        await Runner.RunAsync(seed.ScanId, CancellationToken.None);

        var scan = await LoadAsync(seed.ScanId);
        Assert.Equal(ScanStatus.Incomplete, scan.Status);
        Assert.Equal(
            [
                ("src/app/packages.lock.json", UnscannedReason.NoLockFile, (string?)null),
                ("src/web/packages.lock.json", UnscannedReason.RestoreFailed, "restore exited with 1"),
            ],
            scan.UnscannedItems.OrderBy(i => i.Path, StringComparer.Ordinal).Select(i => (i.Path, i.Reason, i.Detail)));
        Assert.Single(scan.Findings);
        Assert.Equal("2.1.10", scan.ScannedTag);
    }

    [SkippableFact]
    public async Task Unscanned_item_detail_is_masked_and_clipped_and_path_is_clipped()
    {
        _factory.SkipIfDatabaseUnavailable();
        var seed = await SeedAsync();
        var longPath = new string('p', UnscannedItem.PathMaxLength + 50);
        _scanner.Outcome = new ScanOutcome.Incomplete(
            [],
            [
                new UnscannedItem("a/packages.lock.json", UnscannedReason.RestoreFailed, $"auth {Token} failed"),
                new UnscannedItem("b/packages.lock.json", UnscannedReason.RestoreFailed, new string('x', 5000)),
                new UnscannedItem("c/packages.lock.json", UnscannedReason.RestoreFailed, "  "),
                new UnscannedItem(longPath, UnscannedReason.NoLockFile),
            ],
            "0.58.0", DbDate);

        await Runner.RunAsync(seed.ScanId, CancellationToken.None);

        var items = (await LoadAsync(seed.ScanId)).UnscannedItems.ToDictionary(i => i.Path);
        Assert.Equal("auth *** failed", items["a/packages.lock.json"].Detail);
        Assert.Equal(UnscannedItem.DetailMaxLength, items["b/packages.lock.json"].Detail!.Length);
        Assert.Null(items["c/packages.lock.json"].Detail);
        Assert.Equal(longPath[..UnscannedItem.PathMaxLength], Assert.Single(items, i => i.Key.Length == UnscannedItem.PathMaxLength).Key);
    }

    [SkippableFact]
    public async Task Unscanned_item_detail_is_stored_without_control_characters_and_without_a_cut_surrogate_pair()
    {
        _factory.SkipIfDatabaseUnavailable();
        var seed = await SeedAsync();
        _scanner.Outcome = new ScanOutcome.Incomplete(
            [],
            [
                new UnscannedItem("a/packages.lock.json", UnscannedReason.RestoreFailed, "bad\0byte\u0007 here"),
                // The cut at 300 characters falls between the two halves of the emoji.
                new UnscannedItem("b/packages.lock.json", UnscannedReason.RestoreFailed, new string('x', UnscannedItem.DetailMaxLength - 1) + "\U0001F600tail"),
            ],
            "0.58.0", DbDate);

        await Runner.RunAsync(seed.ScanId, CancellationToken.None);

        var scan = await LoadAsync(seed.ScanId);
        Assert.Equal(ScanStatus.Incomplete, scan.Status);
        var items = scan.UnscannedItems.ToDictionary(i => i.Path);
        Assert.Equal("badbyte here", items["a/packages.lock.json"].Detail);
        var clipped = items["b/packages.lock.json"].Detail!;
        Assert.Equal(UnscannedItem.DetailMaxLength - 1, clipped.Length);
        Assert.All(clipped, c => Assert.False(char.IsSurrogate(c)));
    }

    [SkippableTheory]
    [InlineData(ScanFailureReason.ScannerUnavailable)]
    [InlineData(ScanFailureReason.DatabaseTooOld)]
    [InlineData(ScanFailureReason.Timeout)]
    [InlineData(ScanFailureReason.ScannerFailed)]
    public async Task Scanner_failure_reason_is_passed_through(ScanFailureReason reason)
    {
        _factory.SkipIfDatabaseUnavailable();
        var seed = await SeedAsync();
        _scanner.Outcome = new ScanOutcome.Failed(reason, "scanner says no");

        await Runner.RunAsync(seed.ScanId, CancellationToken.None);

        var scan = await LoadAsync(seed.ScanId);
        Assert.Equal(ScanStatus.Failed, scan.Status);
        Assert.Equal(reason, scan.FailureReason);
        Assert.Equal("scanner says no", scan.FailureDetail);
        Assert.Empty(scan.Findings);
        Assert.NotNull(scan.FinishedAt);
        Assert.Equal("2.1.10", scan.ScannedTag);
        Assert.False(Directory.Exists(DirectoryOf(seed)));
    }

    [SkippableFact]
    public async Task Deleted_pattern_fails_the_scan_as_pattern_not_resolved()
    {
        _factory.SkipIfDatabaseUnavailable();
        var seed = await SeedAsync();
        await using (var scope = _factory.CreateDbScope(out var db))
        {
            db.VersionPatterns.Remove(await db.VersionPatterns.SingleAsync(p => p.Id == seed.PatternId));
            await db.SaveChangesAsync();
        }

        await Runner.RunAsync(seed.ScanId, CancellationToken.None);

        await AssertNotResolvedAsync(seed, "deleted");
    }

    [SkippableFact]
    public async Task Inactive_pattern_fails_the_scan_as_pattern_not_resolved()
    {
        _factory.SkipIfDatabaseUnavailable();
        var seed = await SeedAsync(isActive: false);

        await Runner.RunAsync(seed.ScanId, CancellationToken.None);

        await AssertNotResolvedAsync(seed, "deactivated");
    }

    [SkippableFact]
    public async Task Pattern_without_a_matching_tag_fails_as_pattern_not_resolved()
    {
        _factory.SkipIfDatabaseUnavailable();
        var seed = await SeedAsync(pattern: "9.9.*");

        await Runner.RunAsync(seed.ScanId, CancellationToken.None);

        await AssertNotResolvedAsync(seed, "No tag");
    }

    [SkippableFact]
    public async Task Ambiguous_tags_fail_as_pattern_not_resolved()
    {
        _factory.SkipIfDatabaseUnavailable();
        var seed = await SeedAsync();
        _tags.Output = "this is not ls-remote output";

        await Runner.RunAsync(seed.ScanId, CancellationToken.None);

        await AssertNotResolvedAsync(seed, "ambiguous");
    }

    [SkippableFact]
    public async Task Unreachable_git_server_fails_as_pattern_not_resolved()
    {
        _factory.SkipIfDatabaseUnavailable();
        var seed = await SeedAsync(repositoryName: "down");

        await Runner.RunAsync(seed.ScanId, CancellationToken.None);

        await AssertNotResolvedAsync(seed, "Git server");
    }

    [SkippableFact]
    public async Task Checkout_failure_fails_the_scan_as_git_failed_and_removes_the_directory()
    {
        _factory.SkipIfDatabaseUnavailable();
        var seed = await SeedAsync();
        _checkout.Result = new GitCheckoutResult.Failure(GitErrorKind.Failed, 128);

        await Runner.RunAsync(seed.ScanId, CancellationToken.None);

        var scan = await LoadAsync(seed.ScanId);
        Assert.Equal(ScanFailureReason.GitFailed, scan.FailureReason);
        Assert.Contains("128", scan.FailureDetail);
        Assert.Equal("2.1.10", scan.ScannedTag);
        Assert.Equal(0, _scanner.Calls);
        Assert.True(_checkout.CreatedDirectory);
        Assert.False(Directory.Exists(DirectoryOf(seed)));
    }

    [SkippableFact]
    public async Task Checkout_timeout_fails_the_scan_as_timeout()
    {
        _factory.SkipIfDatabaseUnavailable();
        var seed = await SeedAsync();
        _checkout.Result = new GitCheckoutResult.Failure(GitErrorKind.Timeout, null);

        await Runner.RunAsync(seed.ScanId, CancellationToken.None);

        Assert.Equal(ScanFailureReason.Timeout, (await LoadAsync(seed.ScanId)).FailureReason);
    }

    [SkippableFact]
    public async Task Moved_tag_fails_the_scan_as_commit_mismatch_without_running_trivy()
    {
        _factory.SkipIfDatabaseUnavailable();
        var seed = await SeedAsync();
        _checkout.Result = new GitCheckoutResult.CommitMismatch(new string('9', 40));

        await Runner.RunAsync(seed.ScanId, CancellationToken.None);

        var scan = await LoadAsync(seed.ScanId);
        Assert.Equal(ScanStatus.Failed, scan.Status);
        Assert.Equal(ScanFailureReason.CommitMismatch, scan.FailureReason);
        Assert.Contains(new string('9', 40), scan.FailureDetail);
        Assert.Equal(0, _scanner.Calls);
        Assert.False(Directory.Exists(DirectoryOf(seed)));
    }

    [SkippableFact]
    public async Task Exception_in_the_scanner_fails_the_scan_and_removes_the_directory()
    {
        _factory.SkipIfDatabaseUnavailable();
        var seed = await SeedAsync();
        _scanner.Throw = new InvalidOperationException("kaboom " + Token);

        await Runner.RunAsync(seed.ScanId, CancellationToken.None);

        var scan = await LoadAsync(seed.ScanId);
        Assert.Equal(ScanStatus.Failed, scan.Status);
        Assert.Equal(ScanFailureReason.ScannerFailed, scan.FailureReason);
        Assert.DoesNotContain(Token, scan.FailureDetail);
        Assert.False(Directory.Exists(DirectoryOf(seed)));
    }

    [SkippableFact]
    public async Task Shutdown_marks_the_scan_interrupted_and_removes_the_directory()
    {
        _factory.SkipIfDatabaseUnavailable();
        var seed = await SeedAsync();
        using var cts = new CancellationTokenSource();
        _scanner.Throw = new OperationCanceledException(cts.Token);
        await cts.CancelAsync();

        await Runner.RunAsync(seed.ScanId, cts.Token);

        var scan = await LoadAsync(seed.ScanId);
        Assert.Equal(ScanStatus.Failed, scan.Status);
        Assert.Equal(ScanFailureReason.Interrupted, scan.FailureReason);
        Assert.False(Directory.Exists(DirectoryOf(seed)));
    }

    [SkippableFact]
    public async Task Failure_detail_is_one_line_without_the_token()
    {
        _factory.SkipIfDatabaseUnavailable();
        var seed = await SeedAsync();
        var basic = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{_git.UserName}:{Token}"));
        _scanner.Outcome = new ScanOutcome.Failed(ScanFailureReason.ScannerFailed,
            $"  fatal: auth with {Token} and Basic {basic} failed\nsecond line\n{new string('x', 5000)}");

        await Runner.RunAsync(seed.ScanId, CancellationToken.None);

        var detail = (await LoadAsync(seed.ScanId)).FailureDetail!;
        Assert.DoesNotContain(Token, detail);
        Assert.DoesNotContain(basic, detail);
        Assert.Equal("fatal: auth with *** and Basic *** failed", detail);
    }

    [SkippableFact]
    public async Task Failure_detail_is_cut_to_the_column_length()
    {
        _factory.SkipIfDatabaseUnavailable();
        var seed = await SeedAsync();
        _scanner.Outcome = new ScanOutcome.Failed(ScanFailureReason.ScannerFailed, new string('x', 5000));

        await Runner.RunAsync(seed.ScanId, CancellationToken.None);

        Assert.Equal(Scan.FailureDetailMaxLength, (await LoadAsync(seed.ScanId)).FailureDetail!.Length);
    }

    [SkippableFact]
    public async Task Pattern_row_is_not_written_by_the_scan()
    {
        _factory.SkipIfDatabaseUnavailable();
        var seed = await SeedAsync();
        var resolvedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        await using (var scope = _factory.CreateDbScope(out var db))
        {
            var row = await db.VersionPatterns.SingleAsync(p => p.Id == seed.PatternId);
            row.LastResolutionState = ResolutionState.Resolved;
            row.LastResolvedTag = "2.1.9";
            row.LastResolvedCommit = FakeGitTagSource.Commit219;
            row.LastResolvedAt = resolvedAt;
            await db.SaveChangesAsync();
        }

        _scanner.Outcome = new ScanOutcome.Completed([], "0.58.0", DbDate);
        await Runner.RunAsync(seed.ScanId, CancellationToken.None);

        await using var verify = _factory.CreateDbScope(out var after);
        var pattern = await after.VersionPatterns.AsNoTracking().SingleAsync(p => p.Id == seed.PatternId);
        Assert.Equal(ResolutionState.Resolved, pattern.LastResolutionState);
        Assert.Equal("2.1.9", pattern.LastResolvedTag);
        Assert.Equal(FakeGitTagSource.Commit219, pattern.LastResolvedCommit);
        Assert.Equal(resolvedAt, pattern.LastResolvedAt);
        Assert.Equal("2.1.10", (await LoadAsync(seed.ScanId)).ScannedTag);
    }

    [SkippableFact]
    public async Task Scan_that_is_not_running_is_left_alone()
    {
        _factory.SkipIfDatabaseUnavailable();
        var seed = await SeedAsync(scanStatus: ScanStatus.Queued);

        await Runner.RunAsync(seed.ScanId, CancellationToken.None);

        Assert.Equal(ScanStatus.Queued, (await LoadAsync(seed.ScanId)).Status);
        Assert.Equal(0, _scanner.Calls);
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Result_of_a_scan_swept_to_failed_in_the_meantime_is_dropped(bool incomplete)
    {
        _factory.SkipIfDatabaseUnavailable();
        var seed = await SeedAsync();
        _scanner.Outcome = incomplete
            ? new ScanOutcome.Incomplete([Finding("lodash", "CVE-1", FindingSeverity.High)], [new UnscannedItem("a/packages.lock.json", UnscannedReason.NoLockFile)], "0.58.0", DbDate)
            : new ScanOutcome.Completed([Finding("lodash", "CVE-1", FindingSeverity.High)], "0.58.0", DbDate);

        // The sweep (or recovery) fails the row after the runner read it as Running and before it writes the result.
        _scanner.OnScan = () =>
        {
            using var scope = _factory.CreateDbScope(out var db);
            db.Scans.Where(s => s.Id == seed.ScanId).ExecuteUpdate(set => set
                .SetProperty(s => s.Status, ScanStatus.Failed)
                .SetProperty(s => s.FailureReason, (ScanFailureReason?)ScanFailureReason.Interrupted)
                .SetProperty(s => s.FailureDetail, ScanQueue.AbandonedDetail));
        };

        await Runner.RunAsync(seed.ScanId, CancellationToken.None);

        var scan = await LoadAsync(seed.ScanId);
        Assert.Equal(ScanStatus.Failed, scan.Status);
        Assert.Equal(ScanFailureReason.Interrupted, scan.FailureReason);
        Assert.Equal(ScanQueue.AbandonedDetail, scan.FailureDetail);
        Assert.Empty(scan.Findings);
        Assert.Null(scan.TrivyVersion);
        Assert.Empty(scan.UnscannedItems);
    }

    private async Task AssertNotResolvedAsync(Seed seed, string detailPart)
    {
        var scan = await LoadAsync(seed.ScanId);
        Assert.Equal(ScanStatus.Failed, scan.Status);
        Assert.Equal(ScanFailureReason.PatternNotResolved, scan.FailureReason);
        Assert.Contains(detailPart, scan.FailureDetail, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(scan.FinishedAt);
        Assert.Null(scan.ScannedCommit);
        Assert.Equal(0, _checkout.Calls);
        Assert.Equal(0, _scanner.Calls);
    }

    private sealed record Seed(long ScanId, long PatternId, string Url);

    private async Task<Seed> SeedAsync(
        string pattern = "2.1.*", bool isActive = true, string repositoryName = "app", ScanStatus scanStatus = ScanStatus.Running)
    {
        var url = $"https://{FakeGitTagSource.Host}/{Guid.NewGuid():N}/{repositoryName}.git";
        await using var scope = _factory.CreateDbScope(out var db);

        var repository = new Repository { Url = url, CreatedAt = DateTimeOffset.UtcNow, CreatedBy = "alice" };
        db.Repositories.Add(repository);
        await db.SaveChangesAsync();

        var versionPattern = new VersionPattern
        {
            RepositoryId = repository.Id,
            Pattern = pattern,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "alice",
        };
        db.VersionPatterns.Add(versionPattern);
        await db.SaveChangesAsync();

        var scan = new Scan
        {
            PatternId = versionPattern.Id,
            RepositoryId = repository.Id,
            RepositoryUrl = url,
            Pattern = pattern,
            Status = scanStatus,
            RequestedBy = "alice",
            RequestedAt = DateTimeOffset.UtcNow,
            StartedAt = DateTimeOffset.UtcNow,
        };
        db.Scans.Add(scan);
        await db.SaveChangesAsync();

        return new Seed(scan.Id, versionPattern.Id, url);
    }

    private async Task<Scan> LoadAsync(long scanId)
    {
        await using var scope = _factory.CreateDbScope(out var db);
        return await db.Scans.AsNoTracking().Include(s => s.Findings).Include(s => s.UnscannedItems).SingleAsync(s => s.Id == scanId);
    }

    private static ScanFinding Finding(string library, string id, FindingSeverity severity) => new()
    {
        Library = library,
        InstalledVersion = "1.0.0",
        VulnerabilityId = id,
        Severity = severity,
        Targets = ["packages.lock.json"],
    };

    private sealed class ScriptedTagSource : IGitTagSource
    {
        public string Output { get; set; } = FakeGitTagSource.AppOutput;

        public Task<GitTagListing> ListTagsAsync(string canonicalUrl, CancellationToken cancellationToken)
            => Task.FromResult<GitTagListing>(canonicalUrl.EndsWith("/down.git", StringComparison.Ordinal)
                ? new GitTagListing.Failure(GitErrorKind.Failed, 128)
                : new GitTagListing.Success(Output));
    }

    /// <summary>Creates a directory with a read-only file in it, like a real clone does.</summary>
    private sealed class FakeCheckout : IGitCheckout
    {
        public GitCheckoutResult Result { get; set; } = new GitCheckoutResult.Success();

        public Action? OnScan { get; set; }

        public int Calls { get; private set; }

        public bool CreatedDirectory { get; private set; }

        public string? Url { get; private set; }

        public string? Tag { get; private set; }

        public string? Commit { get; private set; }

        public string? TargetDirectory { get; private set; }

        public Task<GitCheckoutResult> CheckoutAsync(
            string canonicalUrl, string tag, string expectedCommit, string targetDirectory, CancellationToken cancellationToken)
        {
            Calls++;
            OnScan?.Invoke();
            (Url, Tag, Commit, TargetDirectory) = (canonicalUrl, tag, expectedCommit, targetDirectory);

            Directory.CreateDirectory(Path.Combine(targetDirectory, ".git"));
            var file = Path.Combine(targetDirectory, ".git", "pack.idx");
            File.WriteAllText(file, "x");
            File.SetAttributes(file, FileAttributes.ReadOnly);
            CreatedDirectory = true;

            return Task.FromResult(Result);
        }
    }

    private sealed class FakeScanner : ITrivyScanner
    {
        public ScanOutcome Outcome { get; set; } = new ScanOutcome.Completed([], "0.58.0", DbDate);

        public Exception? Throw { get; set; }

        public Action? OnScan { get; set; }

        public int Calls { get; private set; }

        public string? ScannedDirectory { get; private set; }

        public bool DirectoryExisted { get; private set; }

        public Task<ScanOutcome> ScanAsync(string checkoutDirectory, CancellationToken cancellationToken)
        {
            Calls++;
            OnScan?.Invoke();
            ScannedDirectory = checkoutDirectory;
            DirectoryExisted = Directory.Exists(checkoutDirectory);

            return Throw is null ? Task.FromResult(Outcome) : Task.FromException<ScanOutcome>(Throw);
        }
    }
}
