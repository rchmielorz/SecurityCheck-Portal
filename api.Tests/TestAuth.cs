using System.Net.Http.Json;

namespace securitycheck_portal.Tests;

/// <summary>Login helper shared by the test classes.</summary>
public static class TestAuth
{
    /// <summary>Posts to <c>/api/auth/login</c>; on success the client keeps the auth cookie.</summary>
    public static Task<HttpResponseMessage> LoginAsync(HttpClient client, string? userName, string? password) =>
        client.PostAsJsonAsync("/api/auth/login", new { userName, password });
}
