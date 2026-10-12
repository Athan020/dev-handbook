using Bank.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bank.Api.Tests;

/// <summary>
/// The labs rely on the seeded data and the starting schema having particular shapes.
/// These tests pin those shapes down, so a change to the seeder can't silently break a lab.
/// </summary>
public sealed class SeedDataTests(BankApiFixture fixture) : IClassFixture<BankApiFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Seeds_the_requested_number_of_rows()
    {
        await using var scope = fixture.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BankDb>();

        Assert.Equal(BankApiFixture.Accounts, await db.Accounts.CountAsync(Ct));
        Assert.Equal(BankApiFixture.Transactions, await db.Transactions.LongCountAsync(Ct));
    }

    [Fact]
    public async Task Low_account_ids_get_far_more_transactions()
    {
        await using var scope = fixture.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BankDb>();

        var busiest = await db.Transactions.CountAsync(t => t.AccountId == 1, Ct);
        var average = BankApiFixture.Transactions / BankApiFixture.Accounts;

        Assert.True(busiest > 5 * average, $"account 1 has {busiest} transactions; the average is {average}");
    }

    [Fact]
    public async Task No_seeded_transaction_is_dated_in_the_future()
    {
        await using var scope = fixture.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BankDb>();

        var latest = await db.Transactions.MaxAsync(t => t.CreatedAt, Ct);

        Assert.True(latest <= DateTimeOffset.UtcNow, $"latest seeded transaction is {latest:O}");
    }

    [Fact]
    public async Task Seeding_twice_does_not_duplicate_data()
    {
        await using var scope = fixture.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BankDb>();

        await Seeder.SeedAsync(db, new Seeder.Options(BankApiFixture.Accounts, BankApiFixture.Transactions), TextWriter.Null, Ct);

        Assert.Equal(BankApiFixture.Transactions, await db.Transactions.LongCountAsync(Ct));
    }

    [Fact]
    public async Task Starting_schema_indexes_only_primary_keys()
    {
        // Journey B1 starts here: stage 2 is where the reader adds the indexes.
        await using var scope = fixture.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BankDb>();

        var indexes = await db.Database
            .SqlQuery<string>($"SELECT indexname AS \"Value\" FROM pg_indexes WHERE schemaname = 'public' AND tablename IN ('accounts', 'transactions') ORDER BY indexname")
            .ToListAsync(Ct);

        Assert.Equal(["pk_accounts", "pk_transactions"], indexes);
    }
}
