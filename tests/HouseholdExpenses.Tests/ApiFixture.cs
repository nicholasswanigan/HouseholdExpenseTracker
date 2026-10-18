using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HouseholdExpenses.Core.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HouseholdExpenses.Tests;

/// <summary>Spins up the real API against a throw-away SQLite file.</summary>
public sealed class ApiFixture : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"household-tests-{Guid.NewGuid():N}.db");

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public ApiFixture()
    {
        // Environment variables are visible to Program's eager config reads, unlike late-bound test settings.
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", $"Data Source={_dbPath}");
        // Tests create their own users; the demo data is exercised explicitly in DemoDataTests.
        Environment.SetEnvironmentVariable("Seed__DemoData", "false");
    }

    public async Task<(HttpClient Client, UserDto User)> RegisterAsync(string name)
    {
        var client = CreateClient();
        var email = $"{name.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com";
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "Passw0rd!", name), Json);
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return (client, auth.User);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch (IOException) { /* best effort */ }
    }
}

public static class HttpExtensions
{
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(ApiFixture.Json))!;
    }

    public static Task<HttpResponseMessage> PostJsonAsync<T>(this HttpClient c, string url, T body) =>
        c.PostAsJsonAsync(url, body, ApiFixture.Json);
}

/// <summary>
/// All API tests share one server and database. The fixture configures the app through environment
/// variables, which are process-wide, so two fixtures running in parallel would trample each other.
/// </summary>
[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
