using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using securitycheck_portal.Repositories;

namespace securitycheck_portal.Tests;

/// <summary>
/// Repository, pattern and change-log endpoints against a real PostgreSQL and the fake Git server.
/// Tests share one database, so each uses a repository URL of its own; the last path segment still
/// picks the fake's behaviour (<c>app</c>, <c>down</c>, <c>empty</c>).
/// </summary>
public sealed class RepositoryEndpointsTests(DatabasePortalFactory factory) : IClassFixture<DatabasePortalFactory>
{
    [SkippableFact]
    public async Task Added_repository_is_listed_and_returned_with_details()
    {
        factory.SkipIfDatabaseUnavailable();
        using var client = await LoginAsync();
        var url = NewUrl("app");

        using var created = await client.PostAsJsonAsync("/api/repos", new { url, name = "  Aplikacja  " });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var repository = await created.Content.ReadFromJsonAsync<RepositoryDetails>();
        Assert.NotNull(repository);
        Assert.Equal(url + ".git", repository.Url);
        Assert.Equal("Aplikacja", repository.Name);
        Assert.Equal("alice", repository.CreatedBy);
        Assert.Empty(repository.Patterns);
        Assert.Equal($"/api/repos/{repository.Id}", created.Headers.Location?.OriginalString);

        var list = await client.GetFromJsonAsync<List<RepositorySummary>>("/api/repos");
        Assert.Contains(new RepositorySummary(repository.Id, repository.Url, "Aplikacja", 0), list!);

        var details = await client.GetFromJsonAsync<RepositoryDetails>($"/api/repos/{repository.Id}");
        Assert.Equal(repository.Url, details!.Url);
    }

    [SkippableFact]
    public async Task Same_repository_in_another_spelling_returns_409()
    {
        factory.SkipIfDatabaseUnavailable();
        using var client = await LoginAsync();
        var prefix = $"https://{FakeGitTagSource.Host}/{Guid.NewGuid():N}";

        using var first = await client.PostAsJsonAsync("/api/repos", new { url = $"{prefix}/App.git/" });
        using var second = await client.PostAsJsonAsync("/api/repos", new { url = $"{prefix}/app" });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [SkippableTheory]
    [InlineData("https://github.com/team/app", null)]
    [InlineData("http://git.internal/team/app", null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    [InlineData("https://git.internal/team/long-name", 201)]
    public async Task Invalid_url_or_too_long_name_returns_400(string? url, int? nameLength)
    {
        factory.SkipIfDatabaseUnavailable();
        using var client = await LoginAsync();
        var name = nameLength is { } length ? new string('n', length) : null;

        using var response = await client.PostAsJsonAsync("/api/repos", new { url, name });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [SkippableFact]
    public async Task Added_pattern_is_resolved_to_the_highest_matching_tag()
    {
        factory.SkipIfDatabaseUnavailable();
        using var client = await LoginAsync();
        var repositoryId = await AddRepositoryAsync(client, "app");

        using var response = await client.PostAsJsonAsync($"/api/repos/{repositoryId}/patterns", new { pattern = "2.1.*" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var pattern = await response.Content.ReadFromJsonAsync<PatternResponse>();
        Assert.NotNull(pattern);
        Assert.Equal($"/api/patterns/{pattern.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(repositoryId, pattern.RepositoryId);
        Assert.Equal("2.1.*", pattern.Pattern);
        Assert.True(pattern.IsActive);
        Assert.Equal("alice", pattern.CreatedBy);
        Assert.NotNull(pattern.LastResolution);
        Assert.Equal("Resolved", pattern.LastResolution.State);
        Assert.Equal("2.1.10", pattern.LastResolution.Tag);
        Assert.Equal(FakeGitTagSource.Commit2110, pattern.LastResolution.Commit);
        Assert.NotNull(pattern.LastResolution.ResolvedAt);

        var details = await client.GetFromJsonAsync<RepositoryDetails>($"/api/repos/{repositoryId}");
        var listed = Assert.Single(details!.Patterns);
        Assert.Equal(pattern.Id, listed.Id);
        Assert.Equal(FakeGitTagSource.Commit2110, listed.LastResolution?.Commit);

        var list = await client.GetFromJsonAsync<List<RepositorySummary>>("/api/repos");
        Assert.Equal(1, Assert.Single(list!, r => r.Id == repositoryId).ActivePatternCount);
    }

    [SkippableTheory]
    [InlineData("down", "Error")]
    [InlineData("empty", "NoMatch")]
    public async Task Pattern_is_saved_even_when_it_does_not_resolve(string repositoryName, string expectedState)
    {
        factory.SkipIfDatabaseUnavailable();
        using var client = await LoginAsync();
        var repositoryId = await AddRepositoryAsync(client, repositoryName);

        using var response = await client.PostAsJsonAsync($"/api/repos/{repositoryId}/patterns", new { pattern = "2.1.*" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var pattern = await response.Content.ReadFromJsonAsync<PatternResponse>();
        Assert.Equal(expectedState, pattern!.LastResolution?.State);
        Assert.Null(pattern.LastResolution?.Tag);
        Assert.Null(pattern.LastResolution?.Commit);
    }

    [SkippableTheory]
    [InlineData("2.1")]
    [InlineData("2.1.x")]
    [InlineData("v2.1.*")]
    [InlineData(" 2.1.*")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Invalid_pattern_returns_400(string? pattern)
    {
        factory.SkipIfDatabaseUnavailable();
        using var client = await LoginAsync();
        var repositoryId = await AddRepositoryAsync(client, "app");

        using var response = await client.PostAsJsonAsync($"/api/repos/{repositoryId}/patterns", new { pattern });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [SkippableFact]
    public async Task Duplicate_pattern_conflicts_and_inactive_pattern_cannot_be_resolved()
    {
        factory.SkipIfDatabaseUnavailable();
        using var client = await LoginAsync();
        var repositoryId = await AddRepositoryAsync(client, "app");
        var patternId = await AddPatternAsync(client, repositoryId, "2.1.*");

        using (var duplicate = await client.PostAsJsonAsync($"/api/repos/{repositoryId}/patterns", new { pattern = "2.1.*" }))
        {
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            Assert.Equal(new PatternConflict("exists"), await duplicate.Content.ReadFromJsonAsync<PatternConflict>());
        }

        using (var deactivated = await client.PostAsync($"/api/patterns/{patternId}/deactivate", content: null))
        {
            Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
            var pattern = await deactivated.Content.ReadFromJsonAsync<PatternResponse>();
            Assert.False(pattern!.IsActive);
            Assert.Equal("Resolved", pattern.LastResolution?.State);
        }

        using (var duplicate = await client.PostAsJsonAsync($"/api/repos/{repositoryId}/patterns", new { pattern = "2.1.*" }))
        {
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            Assert.Equal(new PatternConflict("inactive"), await duplicate.Content.ReadFromJsonAsync<PatternConflict>());
        }

        var gitCallsBefore = factory.Git.CallCount;
        using (var resolve = await client.PostAsync($"/api/patterns/{patternId}/resolve", content: null))
        {
            Assert.Equal(HttpStatusCode.Conflict, resolve.StatusCode);
        }

        Assert.Equal(gitCallsBefore, factory.Git.CallCount);

        var list = await client.GetFromJsonAsync<List<RepositorySummary>>("/api/repos");
        Assert.Equal(0, Assert.Single(list!, r => r.Id == repositoryId).ActivePatternCount);

        using (var activated = await client.PostAsync($"/api/patterns/{patternId}/activate", content: null))
        {
            Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
            Assert.True((await activated.Content.ReadFromJsonAsync<PatternResponse>())!.IsActive);
        }

        using (var resolve = await client.PostAsync($"/api/patterns/{patternId}/resolve", content: null))
        {
            Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);
            var pattern = await resolve.Content.ReadFromJsonAsync<PatternResponse>();
            Assert.Equal("2.1.10", pattern!.LastResolution?.Tag);
        }

        Assert.Equal(gitCallsBefore + 1, factory.Git.CallCount);
    }

    [SkippableFact]
    public async Task Resolve_stores_an_error_result_and_returns_200()
    {
        factory.SkipIfDatabaseUnavailable();
        using var client = await LoginAsync();
        var repositoryId = await AddRepositoryAsync(client, "down");
        var patternId = await AddPatternAsync(client, repositoryId, "2.1.*");

        using var resolve = await client.PostAsync($"/api/patterns/{patternId}/resolve", content: null);

        Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);
        Assert.Equal("Error", (await resolve.Content.ReadFromJsonAsync<PatternResponse>())!.LastResolution?.State);
    }

    [SkippableFact]
    public async Task Switching_to_the_current_state_logs_nothing()
    {
        factory.SkipIfDatabaseUnavailable();
        using var client = await LoginAsync();
        var repositoryId = await AddRepositoryAsync(client, "app");
        var patternId = await AddPatternAsync(client, repositoryId, "2.1.*");

        using var activated = await client.PostAsync($"/api/patterns/{patternId}/activate", content: null);
        using var deactivated = await client.PostAsync($"/api/patterns/{patternId}/deactivate", content: null);
        using var deactivatedAgain = await client.PostAsync($"/api/patterns/{patternId}/deactivate", content: null);

        Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        Assert.Equal(HttpStatusCode.OK, deactivatedAgain.StatusCode);
        Assert.False((await deactivatedAgain.Content.ReadFromJsonAsync<PatternResponse>())!.IsActive);

        var events = await client.GetFromJsonAsync<List<AuditEventResponse>>($"/api/repos/{repositoryId}/events");
        Assert.Equal(new[] { "PatternDeactivated", "PatternAdded", "RepositoryAdded" }, events!.Select(e => e.Action));
    }

    [SkippableFact]
    public async Task Repository_can_be_deleted_only_after_its_patterns()
    {
        factory.SkipIfDatabaseUnavailable();
        using var client = await LoginAsync();
        var repositoryId = await AddRepositoryAsync(client, "app");
        var patternId = await AddPatternAsync(client, repositoryId, "2.1.*");

        // An inactive pattern blocks the deletion too.
        using (var deactivated = await client.PostAsync($"/api/patterns/{patternId}/deactivate", content: null))
        {
            Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        }

        using (var blocked = await client.DeleteAsync($"/api/repos/{repositoryId}"))
        {
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        }

        using (var patternDeleted = await client.DeleteAsync($"/api/patterns/{patternId}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, patternDeleted.StatusCode);
        }

        using (var deleted = await client.DeleteAsync($"/api/repos/{repositoryId}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        using var details = await client.GetAsync($"/api/repos/{repositoryId}");
        Assert.Equal(HttpStatusCode.NotFound, details.StatusCode);
    }

    [SkippableFact]
    public async Task Change_log_lists_events_newest_first_and_keeps_them_after_deletion()
    {
        factory.SkipIfDatabaseUnavailable();
        using var client = await LoginAsync();
        var repositoryId = await AddRepositoryAsync(client, "app");
        var patternId = await AddPatternAsync(client, repositoryId, "2.1.*");

        foreach (var action in (string[])["deactivate", "activate"])
        {
            using var response = await client.PostAsync($"/api/patterns/{patternId}/{action}", content: null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using (var patternDeleted = await client.DeleteAsync($"/api/patterns/{patternId}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, patternDeleted.StatusCode);
        }

        var events = await client.GetFromJsonAsync<List<AuditEventResponse>>($"/api/repos/{repositoryId}/events");
        Assert.NotNull(events);

        Assert.Equal(
            new[] { "PatternDeleted", "PatternActivated", "PatternDeactivated", "PatternAdded", "RepositoryAdded" },
            events.Select(e => e.Action));
        Assert.All(events, e => Assert.Equal("alice", e.Actor));
        Assert.Equal(new string?[] { "2.1.*", "2.1.*", "2.1.*", "2.1.*", null }, events.Select(e => e.Pattern));

        using (var deleted = await client.DeleteAsync($"/api/repos/{repositoryId}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        using (var afterDeletion = await client.GetAsync($"/api/repos/{repositoryId}/events"))
        {
            Assert.Equal(HttpStatusCode.NotFound, afterDeletion.StatusCode);
        }

        await using var scope = factory.CreateDbScope(out var db);
        var stored = await db.AuditEvents
            .Where(e => e.RepositoryId == repositoryId)
            .OrderBy(e => e.Id)
            .Select(e => e.Action.ToString())
            .ToListAsync();
        Assert.Equal(
            new[] { "RepositoryAdded", "PatternAdded", "PatternDeactivated", "PatternActivated", "PatternDeleted", "RepositoryDeleted" },
            stored);
    }

    [SkippableTheory]
    [InlineData("GET", "/api/repos/{repo}")]
    [InlineData("DELETE", "/api/repos/{repo}")]
    [InlineData("GET", "/api/repos/{repo}/events")]
    [InlineData("POST", "/api/repos/{repo}/patterns")]
    [InlineData("DELETE", "/api/patterns/{pattern}")]
    [InlineData("POST", "/api/patterns/{pattern}/deactivate")]
    [InlineData("POST", "/api/patterns/{pattern}/activate")]
    [InlineData("POST", "/api/patterns/{pattern}/resolve")]
    public async Task Missing_repository_or_pattern_returns_404(string method, string template)
    {
        factory.SkipIfDatabaseUnavailable();
        using var client = await LoginAsync();
        var path = template.Replace("{repo}", long.MaxValue.ToString()).Replace("{pattern}", long.MaxValue.ToString());

        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = method == "POST" ? JsonContent.Create(new { pattern = "2.1.*" }) : null,
        };
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static string NewUrl(string repositoryName) =>
        $"https://{FakeGitTagSource.Host}/{Guid.NewGuid():N}/{repositoryName}";

    private async Task<HttpClient> LoginAsync()
    {
        var client = factory.CreatePortalClient();
        using var login = await TestAuth.LoginAsync(client, "alice", FakeLdapAuthenticator.ValidPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return client;
    }

    private static async Task<long> AddRepositoryAsync(HttpClient client, string repositoryName)
    {
        using var response = await client.PostAsJsonAsync("/api/repos", new { url = NewUrl(repositoryName) });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RepositoryDetails>())!.Id;
    }

    private static async Task<long> AddPatternAsync(HttpClient client, long repositoryId, string pattern)
    {
        using var response = await client.PostAsJsonAsync($"/api/repos/{repositoryId}/patterns", new { pattern });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PatternResponse>())!.Id;
    }
}
