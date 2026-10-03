using System.Net;
using System.Net.Http.Json;
using System.Data.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using securitycheck_portal.Core.Data;
using securitycheck_portal.Core.Scanning;
using securitycheck_portal.Repositories;
using securitycheck_portal.Scans;

namespace securitycheck_portal.Tests;

/// <summary>
/// Scan request and result endpoints against a real PostgreSQL. Scans that the worker would produce are
/// inserted directly. Tests share one database, so each uses a repository of its own.
/// </summary>
public sealed class ScanEndpointsTests(DatabasePortalFactory factory) : IClassFixture<DatabasePortalFactory>
{
    [SkippableFact]
    public async Task Requesting_a_scan_returns_202_with_location_and_a_queued_scan()
    {
        factory.SkipIfDatabaseUnavailable();
        using var client = await LoginAsync();
        var (repositoryId, patternId) = await AddRepositoryWithPatternAsync(client);

        using var response = await client.PostAsync($"/api/patterns/{patternId}/scans", content: null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var scan = await response.Content.ReadFromJsonAsync<ScanResponse>();
        Assert.NotNull(scan);
        Assert.Equal($"/api/scans/{scan.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(patternId, scan.PatternId);
        Assert.Equal(repositoryId, scan.RepositoryId);
        Assert.Equal("2.1.*", scan.Pattern);
        Assert.EndsWith("/app.git", scan.RepositoryUrl);
        Assert.Equal("Queued", scan.Status);
        Assert.Equal("alice", scan.RequestedBy);

        var details = await client.GetFromJsonAsync<ScanDetails>($"/api/scans/{scan.Id}");
        Assert.NotNull(details);
        Assert.Equal("Queued", details.Status);
        Assert.Equal(scan.RepositoryUrl, details.RepositoryUrl);
        Assert.Null(details.FailureReason);
        Assert.Null(details.StartedAt);
        Assert.Null(details.FinishedAt);
        Assert.Empty(details.Unscanned);
        Assert.Empty(details.Findings);
    }

    [SkippableFact]
    public async Task Requesting_a_scan_writes_an_audit_event_with_snapshots()
    {
        factory.SkipIfDatabaseUnavailable();
        using var client = await LoginAsync();
        var (repositoryId, patternId) = await AddRepositoryWithPatternAsync(client);

        using var response = await client.PostAsync($"/api/patterns/{patternId}/scans", content: null);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var events = await client.GetFromJsonAsync<List<AuditEventResponse>>($"/api/repos/{repositoryId}/events");
        var latest = events![0];
        Assert.Equal("ScanRequested", latest.Action);
        Assert.Equal("alice", latest.Actor);
        Assert.Equal("2.1.*", latest.Pattern);

        await using var scope = factory.CreateDbScope(out var db);
        var stored = await db.AuditEvents.SingleAsync(e => e.PatternId == patternId && e.Action == AuditAction.ScanRequested);
        Assert.Equal(repositoryId, stored.RepositoryId);
        Assert.EndsWith("/app.git", stored.RepositoryUrl);
    }

    [SkippableTheory]
    [InlineData("POST", "/api/patterns/{id}/scans")]
    [InlineData("GET", "/api/scans/{id}")]
    public async Task Unknown_pattern_or_scan_returns_404(string method, string template)
    {
        factory.SkipIfDatabaseUnavailable();
        using var client = await LoginAsync();

        using var response = await client.SendAsync(
            new HttpRequestMessage(new HttpMethod(method), template.Replace("{id}", long.MaxValue.ToString())));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [SkippableFact]
    public async Task Inactive_pattern_cannot_be_scanned()
    {
        factory.SkipIfDatabaseUnavailable();
        using var client = await LoginAsync();
        var (repositoryId, patternId) = await AddRepositoryWithPatternAsync(client);
        using (var deactivated = await client.PostAsync($"/api/patterns/{patternId}/deactivate", content: null))
        {
            Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        }

        using var response = await client.PostAsync($"/api/patterns/{patternId}/scans", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(new ScanConflict("inactive"), await response.Content.ReadFromJsonAsync<ScanConflict>());

        var events = await client.GetFromJsonAsync<List<AuditEventResponse>>($"/api/repos/{repositoryId}/events");
        Assert.DoesNotContain(events!, e => e.Action == "ScanRequested");
    }

    [SkippableFact]
    public async Task Second_request_while_a_scan_is_active_returns_409_with_its_id()
    {
        factory.SkipIfDatabaseUnavailable();
        using var client = await LoginAsync();
        var (repositoryId, patternId) = await AddRepositoryWithPatternAsync(client);
        using var first = await client.PostAsync($"/api/patterns/{patternId}/scans", content: null);
        var firstScan = await first.Content.ReadFromJsonAsync<ScanResponse>();

        using var second = await client.PostAsync($"/api/patterns/{patternId}/scans", content: null);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(new ScanConflict("active", firstScan!.Id), await second.Content.ReadFromJsonAsync<ScanConflict>());

        // A running scan blocks as well; a finished one does not.
        await SetStatusAsync(firstScan.Id, ScanStatus.Running);
        using (var whileRunning = await client.PostAsync($"/api/patterns/{patternId}/scans", content: null))
        {
            Assert.Equal(HttpStatusCode.Conflict, whileRunning.StatusCode);
            Assert.Equal(firstScan.Id, (await whileRunning.Content.ReadFromJsonAsync<ScanConflict>())!.ScanId);
        }

        await SetStatusAsync(firstScan.Id, ScanStatus.Completed);
        using (var afterFinish = await client.PostAsync($"/api/patterns/{patternId}/scans", content: null))
        {
            Assert.Equal(HttpStatusCode.Accepted, afterFinish.StatusCode);
        }

        var events = await client.GetFromJsonAsync<List<AuditEventResponse>>($"/api/repos/{repositoryId}/events");
        Assert.Equal(2, events!.Count(e => e.Action == "ScanRequested"));
    }

    [SkippableFact]
    public async Task Concurrent_requests_create_one_scan_and_the_rest_get_409_never_500()
    {
        factory.SkipIfDatabaseUnavailable();
        using var client = await LoginAsync();
        var (repositoryId, patternId) = await AddRepositoryWithPatternAsync(client);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => Task.Run(() => client.PostAsync($"/api/patterns/{patternId}/scans", content: null))));
        try
        {
            Assert.DoesNotContain(responses, r => (int)r.StatusCode >= 500);
            var accepted = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Accepted);
            var scan = await accepted.Content.ReadFromJsonAsync<ScanResponse>();

            var rejected = responses.Where(r => r != accepted).ToList();
            Assert.All(rejected, r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
            foreach (var response in rejected)
            {
                Assert.Equal(new ScanConflict("active", scan!.Id), await response.Content.ReadFromJsonAsync<ScanConflict>());
            }

            var events = await client.GetFromJsonAsync<List<AuditEventResponse>>($"/api/repos/{repositoryId}/events");
            Assert.Single(events!, e => e.Action == "ScanRequested");
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [SkippableFact]
    public async Task Unique_index_violation_on_insert_returns_409_with_the_existing_scan_id()
    {
        factory.SkipIfDatabaseUnavailable();
        using var setupClient = await LoginAsync();
        var (repositoryId, patternId) = await AddRepositoryWithPatternAsync(setupClient);

        // Another request queues a scan after the endpoint's pre-check passed but before its own INSERT.
        long competingScanId = 0;
        var interceptor = new BeforeScanInsertInterceptor(async () =>
            competingScanId = await InsertScanAsync(repositoryId, patternId, ScanStatus.Queued));

        await using var hostFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.ConfigureDbContext<PortalDbContext>(options => options.AddInterceptors(interceptor))));
        using var client = hostFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Add(PortalFactory.RemoteIpHeader, PortalFactory.NextRemoteIp());
        using (var login = await TestAuth.LoginAsync(client, "alice", FakeLdapAuthenticator.ValidPassword))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }

        using var response = await client.PostAsync($"/api/patterns/{patternId}/scans", content: null);

        Assert.True(interceptor.Fired);
        Assert.NotEqual(0, competingScanId);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(new ScanConflict("active", competingScanId), await response.Content.ReadFromJsonAsync<ScanConflict>());

        await using var scope = factory.CreateDbScope(out var db);
        Assert.Equal(competingScanId, await db.Scans.Where(s => s.PatternId == patternId).Select(s => s.Id).SingleAsync());
        Assert.False(await db.AuditEvents.AnyAsync(e => e.PatternId == patternId && e.Action == AuditAction.ScanRequested));
    }

    /// <summary>Runs the callback once, right before the first INSERT into Scans is executed.</summary>
    private sealed class BeforeScanInsertInterceptor(Func<Task> beforeInsert) : DbCommandInterceptor
    {
        private int _fired;

        public bool Fired => Volatile.Read(ref _fired) == 1;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            await RunAsync(command);
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            await RunAsync(command);
            return result;
        }

        private async Task RunAsync(DbCommand command)
        {
            if (command.CommandText.Contains("INSERT INTO \"Scans\"", StringComparison.Ordinal)
                && Interlocked.Exchange(ref _fired, 1) == 0)
            {
                await beforeInsert();
            }
        }
    }

    [SkippableFact]
    public async Task Findings_are_sorted_by_severity_then_library_then_vulnerability_id_ordinally()
    {
        factory.SkipIfDatabaseUnavailable();
        using var client = await LoginAsync();
        var (repositoryId, patternId) = await AddRepositoryWithPatternAsync(client);
        var scanId = await InsertScanAsync(repositoryId, patternId, ScanStatus.Completed, findings:
        [
            Finding("zlib", "CVE-2024-0002", FindingSeverity.Unknown),
            Finding("Zeta", "CVE-2024-0001", FindingSeverity.High),
            Finding("alpha", "CVE-2024-0009", FindingSeverity.High),
            Finding("alpha", "CVE-2024-0003", FindingSeverity.High),
            Finding("beta", "CVE-2024-0005", FindingSeverity.Low),
            Finding("beta", "CVE-2024-0004", FindingSeverity.Critical),
            Finding("gamma", "CVE-2024-0006", FindingSeverity.Medium),
            Finding("Alpha", "CVE-2024-0007", FindingSeverity.Unknown),
        ]);

        var details = await client.GetFromJsonAsync<ScanDetails>($"/api/scans/{scanId}");

        Assert.NotNull(details);
        Assert.Equal(
            new[]
            {
                "Critical beta CVE-2024-0004",
                // Ordinal: "Zeta" (upper case) sorts before "alpha".
                "High Zeta CVE-2024-0001",
                "High alpha CVE-2024-0003",
                "High alpha CVE-2024-0009",
                "Medium gamma CVE-2024-0006",
                "Low beta CVE-2024-0005",
                "Unknown Alpha CVE-2024-0007",
                "Unknown zlib CVE-2024-0002",
            },
            details.Findings.Select(f => $"{f.Severity} {f.Library} {f.VulnerabilityId}"));
    }

    [SkippableFact]
    public async Task Scan_details_return_stored_values_and_findings_as_stored()
    {
        factory.SkipIfDatabaseUnavailable();
        using var client = await LoginAsync();
        var (repositoryId, patternId) = await AddRepositoryWithPatternAsync(client);
        var finishedAt = new DateTimeOffset(2026, 9, 28, 10, 5, 0, TimeSpan.Zero);
        var scanId = await InsertScanAsync(
            repositoryId,
            patternId,
            ScanStatus.Incomplete,
            findings:
            [
                // One row per (library, version, vulnerability); the files it was found in are merged.
                new ScanFinding
                {
                    Library = "lodash",
                    InstalledVersion = "4.17.15",
                    VulnerabilityId = "CVE-2021-23337",
                    Severity = FindingSeverity.High,
                    FixedVersion = "4.17.21",
                    Title = "Command injection",
                    Targets = ["web/package-lock.json", "admin/package-lock.json"],
                },
            ],
            configure: scan =>
            {
                scan.ScannedTag = "2.1.10";
                scan.ScannedCommit = FakeGitTagSource.Commit2110;
                scan.TrivyVersion = "0.70.0";
                scan.TrivyDbUpdatedAt = finishedAt.AddHours(-3);
                scan.StartedAt = finishedAt.AddMinutes(-2);
                scan.FinishedAt = finishedAt;
                scan.UnscannedItems.Add(new ScanUnscannedItem { Path = "web/package.json", Reason = UnscannedReason.RestoreFailed, Detail = "restore failed" });
                scan.UnscannedItems.Add(new ScanUnscannedItem { Path = "api/api.csproj", Reason = UnscannedReason.NoLockFile });
            });

        var details = await client.GetFromJsonAsync<ScanDetails>($"/api/scans/{scanId}");

        Assert.NotNull(details);
        Assert.Equal("Incomplete", details.Status);
        Assert.Equal("2.1.10", details.ScannedTag);
        Assert.Equal(FakeGitTagSource.Commit2110, details.ScannedCommit);
        Assert.Equal("0.70.0", details.TrivyVersion);
        Assert.Equal(finishedAt.AddHours(-3), details.TrivyDbUpdatedAt);
        Assert.Equal(finishedAt, details.FinishedAt);
        Assert.Equal(
            [("api/api.csproj", "NoLockFile", (string?)null), ("web/package.json", "RestoreFailed", "restore failed")],
            details.Unscanned.Select(u => (u.Path, u.Reason, u.Detail)));

        var finding = Assert.Single(details.Findings);
        Assert.Equal("lodash", finding.Library);
        Assert.Equal("4.17.15", finding.InstalledVersion);
        Assert.Equal("CVE-2021-23337", finding.VulnerabilityId);
        Assert.Equal("High", finding.Severity);
        Assert.Equal("4.17.21", finding.FixedVersion);
        Assert.Equal("Command injection", finding.Title);
        Assert.Equal(new[] { "web/package-lock.json", "admin/package-lock.json" }, finding.Targets);
    }

    [SkippableFact]
    public async Task Failed_scan_exposes_reason_and_detail()
    {
        factory.SkipIfDatabaseUnavailable();
        using var client = await LoginAsync();
        var (repositoryId, patternId) = await AddRepositoryWithPatternAsync(client);
        var scanId = await InsertScanAsync(repositoryId, patternId, ScanStatus.Failed, configure: scan =>
        {
            scan.FailureReason = ScanFailureReason.GitFailed;
            scan.FailureDetail = "clone failed";
        });

        var details = await client.GetFromJsonAsync<ScanDetails>($"/api/scans/{scanId}");

        Assert.Equal("Failed", details!.Status);
        Assert.Equal("GitFailed", details.FailureReason);
        Assert.Equal("clone failed", details.FailureDetail);
    }

    [SkippableFact]
    public async Task Repository_details_show_the_scan_with_the_highest_id_as_latest()
    {
        factory.SkipIfDatabaseUnavailable();
        using var client = await LoginAsync();
        var (repositoryId, patternId) = await AddRepositoryWithPatternAsync(client);
        var otherPatternId = await AddPatternAsync(client, repositoryId, "2.0.*");

        // The scan with the higher ID was requested earlier; the ID decides, not RequestedAt.
        var now = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
        await InsertScanAsync(repositoryId, patternId, ScanStatus.Completed, findings:
        [
            Finding("a", "CVE-1", FindingSeverity.Low),
            Finding("b", "CVE-2", FindingSeverity.Low),
            Finding("c", "CVE-3", FindingSeverity.Low),
        ], configure: scan =>
        {
            scan.RequestedAt = now;
            scan.FinishedAt = now.AddMinutes(5);
            scan.ScannedTag = "2.1.9";
        });
        var latestId = await InsertScanAsync(repositoryId, patternId, ScanStatus.Incomplete, findings:
        [
            Finding("a", "CVE-1", FindingSeverity.High),
        ], configure: scan =>
        {
            scan.RequestedAt = now.AddDays(-1);
            scan.FinishedAt = now.AddDays(-1).AddMinutes(5);
            scan.ScannedTag = "2.1.10";
        });

        var details = await client.GetFromJsonAsync<RepositoryDetails>($"/api/repos/{repositoryId}");

        Assert.NotNull(details);
        var scanned = Assert.Single(details.Patterns, p => p.Id == patternId);
        Assert.NotNull(scanned.LatestScan);
        Assert.Equal(latestId, scanned.LatestScan.Id);
        Assert.Equal("Incomplete", scanned.LatestScan.Status);
        Assert.Equal(now.AddDays(-1).AddMinutes(5), scanned.LatestScan.FinishedAt);
        Assert.Equal(1, scanned.LatestScan.FindingsCount);
        Assert.Equal("2.1.10", scanned.LatestScan.ScannedTag);

        Assert.Null(Assert.Single(details.Patterns, p => p.Id == otherPatternId).LatestScan);
    }

    [SkippableFact]
    public async Task Latest_scan_is_not_filled_by_the_other_pattern_responses()
    {
        factory.SkipIfDatabaseUnavailable();
        using var client = await LoginAsync();
        var (repositoryId, patternId) = await AddRepositoryWithPatternAsync(client);
        await InsertScanAsync(repositoryId, patternId, ScanStatus.Completed);

        using var deactivated = await client.PostAsync($"/api/patterns/{patternId}/deactivate", content: null);
        using var activated = await client.PostAsync($"/api/patterns/{patternId}/activate", content: null);
        using var resolved = await client.PostAsync($"/api/patterns/{patternId}/resolve", content: null);

        foreach (var response in new[] { deactivated, activated, resolved })
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Null((await response.Content.ReadFromJsonAsync<PatternResponse>())!.LatestScan);
        }

        var details = await client.GetFromJsonAsync<RepositoryDetails>($"/api/repos/{repositoryId}");
        Assert.NotNull(Assert.Single(details!.Patterns).LatestScan);
    }

    private static ScanFinding Finding(string library, string vulnerabilityId, FindingSeverity severity) => new()
    {
        Library = library,
        InstalledVersion = "1.0.0",
        VulnerabilityId = vulnerabilityId,
        Severity = severity,
        Targets = ["package-lock.json"],
    };

    private async Task<long> InsertScanAsync(
        long repositoryId,
        long patternId,
        ScanStatus status,
        IEnumerable<ScanFinding>? findings = null,
        Action<Scan>? configure = null)
    {
        await using var scope = factory.CreateDbScope(out var db);
        var scan = new Scan
        {
            PatternId = patternId,
            RepositoryId = repositoryId,
            RepositoryUrl = FakeGitTagSource.AppUrl,
            Pattern = "2.1.*",
            Status = status,
            RequestedBy = "alice",
            RequestedAt = DateTimeOffset.UtcNow,
        };
        configure?.Invoke(scan);
        scan.Findings.AddRange(findings ?? []);

        db.Scans.Add(scan);
        await db.SaveChangesAsync();
        return scan.Id;
    }

    private async Task SetStatusAsync(long scanId, ScanStatus status)
    {
        await using var scope = factory.CreateDbScope(out var db);
        await db.Scans.Where(s => s.Id == scanId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status));
    }

    private async Task<HttpClient> LoginAsync()
    {
        var client = factory.CreatePortalClient();
        using var login = await TestAuth.LoginAsync(client, "alice", FakeLdapAuthenticator.ValidPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return client;
    }

    private static async Task<(long RepositoryId, long PatternId)> AddRepositoryWithPatternAsync(HttpClient client)
    {
        var url = $"https://{FakeGitTagSource.Host}/{Guid.NewGuid():N}/app";
        using var created = await client.PostAsJsonAsync("/api/repos", new { url });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var repositoryId = (await created.Content.ReadFromJsonAsync<RepositoryDetails>())!.Id;
        return (repositoryId, await AddPatternAsync(client, repositoryId, "2.1.*"));
    }

    private static async Task<long> AddPatternAsync(HttpClient client, long repositoryId, string pattern)
    {
        using var response = await client.PostAsJsonAsync($"/api/repos/{repositoryId}/patterns", new { pattern });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PatternResponse>())!.Id;
    }
}
