using Bank.Api.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Bank.Api.Tests;

/// <summary>
/// Starts a throwaway Postgres in Docker, seeds a small dataset with the same seeder the
/// labs use, and hosts the API in memory against it. Shared by every test in the class.
/// </summary>
public sealed class BankApiFixture : IAsyncLifetime
{
    public const int Accounts = 50;
    public const long Transactions = 5_000;

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.6-alpine").Build();

    public WebApplicationFactory<Program> App { get; private set; } = null!;
    public string ConnectionString => _postgres.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        App = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host => host
                .UseSetting("ConnectionStrings:Bank", ConnectionString)
                // No real replica in tests: the "replica" is the primary, which Postgres reports
                // as not replaying anything, so consistency tokens always route to the primary.
                .UseSetting("ConnectionStrings:BankReplica", ConnectionString));

        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BankDb>();
        await Seeder.SeedAsync(db, new Seeder.Options(Accounts, Transactions, BatchSize: 2_000), TextWriter.Null);
    }

    public async ValueTask DisposeAsync()
    {
        await App.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
