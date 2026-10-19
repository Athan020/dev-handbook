using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace Bank.Modular.Tests;

/// <summary>A throwaway Postgres in Docker, and the modular monolith hosted in memory against it.</summary>
public sealed class ModularFixture : IAsyncLifetime
{
    public const int Accounts = 50;

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.6-alpine").Build();

    public WebApplicationFactory<Program> App { get; private set; } = null!;
    public string ConnectionString => _postgres.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        App = new WebApplicationFactory<Program>().WithWebHostBuilder(host => host
            .UseSetting("ConnectionStrings:Modular", ConnectionString)
            .UseSetting("Modular:SeedAccounts", Accounts.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        App.CreateClient().Dispose(); // starts the host, which creates and seeds the tables
    }

    public async ValueTask DisposeAsync()
    {
        await App.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

public sealed record PayeeDto(Guid PayeeId, int CustomerAccountId, int PayeeAccountId, string Name, string Reference);
public sealed record AccountDto(int AccountId, string HolderName, decimal Balance);
public sealed record EntryDto(long Position, Guid TransferId, int AccountId, decimal Amount, string Reference, DateTimeOffset PostedAt);
public sealed record StatementDto(int AccountId, int Year, int Month, decimal MoneyIn, decimal MoneyOut);
public sealed record BooksDto(int Accounts, decimal TotalBalance, decimal ExpectedTotal, decimal EntriesNet, int AccountsDisagreeing);

public static class ModularClient
{
    public static async Task<PayeeDto> FirstPayeeAsync(this HttpClient http, int accountId) =>
        (await http.GetFromJsonAsync<List<PayeeDto>>($"/accounts/{accountId}/payees", TestContext.Current.CancellationToken))![0];

    public static async Task<decimal> BalanceAsync(this HttpClient http, int accountId) =>
        (await http.GetFromJsonAsync<AccountDto>($"/accounts/{accountId}", TestContext.Current.CancellationToken))!.Balance;

    public static Task<HttpResponseMessage> PayAsync(this HttpClient http, int fromAccountId, Guid payeeId, decimal amount) =>
        http.PostAsJsonAsync("/transfers", new { fromAccountId, payeeId, amount }, TestContext.Current.CancellationToken);
}
