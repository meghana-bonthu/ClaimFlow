using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaimFlow.Api.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ClaimFlow.IntegrationTests;

/// <summary>
/// Boots the real API (auth, EF Core, background worker) against a throw-away SQLite file.
/// Demo claims are disabled so each test controls its own data; reference data and demo users are seeded.
/// </summary>
public sealed class ClaimFlowApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"claimflow-tests-{Guid.NewGuid():N}.db");

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // UseSetting (not ConfigureAppConfiguration) so values are visible while Program.cs builds services.
        builder.UseSetting("ConnectionStrings:ClaimFlow", $"Data Source={_dbPath}");
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-0123456789-abcdefghij");
        builder.UseSetting("Seed:DemoClaims", "false");
        builder.UseSetting("Seed:DemoUsers", "true");
        builder.UseSetting("RateLimiting:LoginPermitsPerMinute", "1000");
    }

    public async Task<HttpClient> CreateClientAsAsync(string email, string password)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>(Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return client;
    }

    public Task<HttpClient> AdminAsync() => CreateClientAsAsync("admin@claimflow.dev", "Admin#2026");
    public Task<HttpClient> SubmitterAsync() => CreateClientAsAsync("submitter@claimflow.dev", "Submit#2026");
    public Task<HttpClient> AdjudicatorAsync() => CreateClientAsAsync("adjudicator@claimflow.dev", "Review#2026");

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch (IOException) { /* best effort */ }
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ClaimFlowApiFactory>
{
    public const string Name = "api";
}
