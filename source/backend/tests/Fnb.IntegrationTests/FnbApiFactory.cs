using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Fnb.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fnb.IntegrationTests;

/// <summary>
/// Boots the real API (in-process worker, InMemory broker) against a throw-away SQL Server database.
/// Default: LocalDB. Override with env FNB_TEST_SQL (e.g. SQL Server container in CI); a unique database name is appended.
/// </summary>
public sealed class FnbApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Password = "Test@Passw0rd!";
    private readonly string _connectionString;

    public FnbApiFactory()
    {
        var baseCs = Environment.GetEnvironmentVariable("FNB_TEST_SQL")
            ?? "Server=(localdb)\\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True";
        _connectionString = $"{baseCs.TrimEnd(';')};Database=FnbTest_{Guid.NewGuid():N}";
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Sql", _connectionString);
        builder.UseSetting("ConnectionStrings:Redis", "");
        builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-0123456789-abcdefghijklmnop");
        builder.UseSetting("Ordering:TrackingTokenSecret", "integration-test-tracking-secret-0123456789abcdef");
        builder.UseSetting("Messaging:Provider", "InMemory");
        builder.UseSetting("Hosting:EnableInProcessWorker", "true");
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Seed:Enabled", "true");
        builder.UseSetting("Seed:DevPassword", Password);
        builder.UseSetting("Outbox:PollIntervalMs", "100");
        builder.UseSetting("RateLimiting:PublicOrderPerMinute", "10000");
        builder.UseSetting("RateLimiting:PublicReadPerMinute", "10000");
        builder.UseSetting("RateLimiting:LoginPerMinute", "10000");
    }

    public ValueTask InitializeAsync()
    {
        _ = Server; // force host start (migrate + seed)
        return ValueTask.CompletedTask;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        try
        {
            var csb = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(_connectionString);
            var dbName = csb.InitialCatalog;
            csb.InitialCatalog = "master";
            Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(csb.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"IF DB_ID(N'{dbName}') IS NOT NULL BEGIN ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{dbName}]; END";
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Microsoft.Data.SqlClient.SqlException)
        {
            // best effort cleanup of the throw-away database
        }
    }

    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public async Task<HttpClient> LoginAsync(string userName)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { userName, password = Password });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("accessToken").GetString());
        return client;
    }

    public Task<Guid> BranchIdAsync(string code) =>
        WithDbAsync(db => db.Branches.Where(b => b.Code == code).Select(b => b.Id).SingleAsync());
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<FnbApiFactory>
{
    public const string Name = "api";
}
