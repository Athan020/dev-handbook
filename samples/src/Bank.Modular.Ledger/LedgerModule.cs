using Bank.Modular.Contracts.Ledger;
using Bank.Modular.Contracts.Payees;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bank.Modular.Ledger;

/// <summary>
/// The ledger module: accounts, balances, transfers and the entries they post. This class is
/// its only public type: the host uses it to switch the module on.
/// </summary>
public static class LedgerModule
{
    public const string Schema = "ledger";

    public static IServiceCollection AddLedgerModule(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<LedgerDb>(options => options
            .UseNpgsql(config.GetConnectionString("Ledger") ?? config.GetConnectionString("Modular"))
            .UseSnakeCaseNamingConvention());
        services.AddScoped<ILedger, LedgerService>();
        services.AddScoped<Transfers>();
        services.AddTransient(_ => new LedgerSeed(config.GetValue("Modular:SeedAccounts", 1000)));
        return services;
    }

    // #region pay
    public static IEndpointRouteBuilder MapLedger(this IEndpointRouteBuilder app)
    {
        // Pay one of your saved payees. The ledger doesn't know payees: it asks the payees
        // module, through its contract, which account the payee is.
        app.MapPost("/transfers", async (PayRequest request, IPayees payees, Transfers transfers, CancellationToken ct) =>
        {
            if (request.Amount <= 0) return Results.Problem(statusCode: 400, title: "The amount must be positive.");

            var payee = await payees.GetAsync(request.FromAccountId, request.PayeeId, ct);
            if (payee is null) return Results.Problem(statusCode: 404, title: "No such payee for this account.");

            var (outcome, transferId) = await transfers.MoveAsync(
                request.FromAccountId, payee.PayeeAccountId, request.Amount, request.Reference ?? payee.Reference, ct);
            return outcome switch
            {
                TransferOutcome.Completed => Results.Created($"/transfers/{transferId}", new { transferId }),
                TransferOutcome.InsufficientFunds => Results.Problem(statusCode: 422, title: "Insufficient funds."),
                _ => Results.Problem(statusCode: 404, title: "No such account."),
            };
        });
        // #endregion pay

        app.MapGet("/accounts/{accountId:int}", async (int accountId, ILedger ledger, CancellationToken ct) =>
            await ledger.GetAccountAsync(accountId, ct) is { } account ? Results.Ok(account) : Results.NotFound());

        app.MapGet("/accounts/{accountId:int}/entries", async (int accountId, DateTimeOffset from, DateTimeOffset to, ILedger ledger, CancellationToken ct) =>
            Results.Ok(await ledger.EntriesAsync(accountId, from, to, ct)));

        app.MapGet("/ledger/feed", async (long after, int? max, ILedger ledger, CancellationToken ct) =>
            Results.Ok(await ledger.ReadFeedAsync(after, Math.Clamp(max ?? 500, 1, 5000), ct)));

        return app;
    }

    /// <summary>Creates the module's tables if they don't exist yet, and seeds its accounts.</summary>
    public static async Task PrepareAsync(IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LedgerDb>();
        var seed = scope.ServiceProvider.GetRequiredService<LedgerSeed>();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Several processes may start at once (stage 3 runs the ledger on its own); one prepares.
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtext({Schema}))", ct);
        var exists = await db.Database.SqlQuery<bool>($"SELECT to_regclass('ledger.accounts') IS NOT NULL AS \"Value\"").SingleAsync(ct);
        if (!exists)
        {
            await db.Database.ExecuteSqlRawAsync(db.Database.GenerateCreateScript(), ct);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO ledger.accounts (account_id, holder_name, balance)
                SELECT g, 'Customer ' || g, 10000 FROM generate_series(1, {seed.Accounts}) AS g
                """, ct);
        }
        await transaction.CommitAsync(ct);
    }
}

internal sealed record LedgerSeed(int Accounts);

internal sealed record PayRequest(int FromAccountId, Guid PayeeId, decimal Amount, string? Reference);
