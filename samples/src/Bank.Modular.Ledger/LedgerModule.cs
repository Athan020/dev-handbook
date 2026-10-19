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
            .UseNpgsql(config.GetConnectionString("Ledger") ?? config.GetConnectionString("Modular"),
                // The module records its own migrations, in its own schema.
                npgsql => npgsql.MigrationsHistoryTable("__migrations", Schema))
            .UseSnakeCaseNamingConvention());
        services.AddScoped<ILedger, LedgerService>();
        services.AddScoped<Transfers>();
        services.AddTransient(_ => new LedgerSeed(config.GetValue("Modular:SeedAccounts", 1000)));

        // B5 stage 4: the checks a transfer needs, and whether transfers are made while the
        // customer waits, or accepted and made afterwards (Lab:Transfers=Accept).
        services.AddSingleton(ChecksOptions.From(config));
        services.AddHttpClient<TransferChecks>(http =>
        {
            http.BaseAddress = new Uri(config["FakeServices:Url"] ?? "http://localhost:5090");
            http.Timeout = TimeSpan.FromMilliseconds(config.GetValue("Transfers:CheckTimeoutMs", 2000));
        });
        var accept = config["Lab:Transfers"] == "Accept";
        services.AddSingleton(new TransferMode(accept));
        if (accept)
        {
            services.AddSingleton(new ProcessorOptions(
                config.GetValue("Transfers:ProcessorBatchSize", 32), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)));
            services.AddHostedService<TransferProcessor>();
        }
        return services;
    }

    // #region pay
    public static IEndpointRouteBuilder MapLedger(this IEndpointRouteBuilder app)
    {
        // Pay one of your saved payees. The ledger doesn't know payees: it asks the payees
        // module, through its contract, which account the payee is.
        app.MapPost("/transfers", async (PayRequest request, TransferMode mode, IPayees payees, TransferChecks checks,
            Transfers transfers, LedgerDb db, TimeProvider time, CancellationToken ct) =>
        {
            if (request.Amount <= 0) return Results.Problem(statusCode: 400, title: "The amount must be positive.");
            if (mode.Accept) return await AcceptAsync(request, db, time, ct);

            var payee = await payees.GetAsync(request.FromAccountId, request.PayeeId, ct);
            if (payee is null) return Results.Problem(statusCode: 404, title: "No such payee for this account.");

            // Stage 4: and every check the transfer needs, before any money moves.
            await checks.RunAsync(request.FromAccountId, payee.PayeeAccountId, request.Amount, ct);

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

        app.MapGet("/transfers/{transferId:guid}", async (Guid transferId, LedgerDb db, CancellationToken ct) =>
        {
            if (await db.TransferRequests.AsNoTracking().FirstOrDefaultAsync(r => r.TransferId == transferId, ct) is { } request)
                return Results.Ok(new { request.TransferId, request.Status, request.Reason, request.Attempts, request.AcceptedAt, request.FinishedAt });
            return await db.Entries.AnyAsync(e => e.TransferId == transferId, ct)
                ? Results.Ok(new { transferId, Status = TransferStatus.Completed })
                : Results.NotFound();
        });

        app.MapGet("/accounts/{accountId:int}", async (int accountId, ILedger ledger, CancellationToken ct) =>
            await ledger.GetAccountAsync(accountId, ct) is { } account ? Results.Ok(account) : Results.NotFound());

        app.MapGet("/accounts/{accountId:int}/entries", async (int accountId, DateTimeOffset from, DateTimeOffset to, ILedger ledger, CancellationToken ct) =>
            Results.Ok(await ledger.EntriesAsync(accountId, from, to, ct)));

        app.MapGet("/ledger/check", async (LedgerDb db, CancellationToken ct) => Results.Ok(await EntityEndpoints.CheckAsync(db, ct)));

        if (app.ServiceProvider.GetRequiredService<IConfiguration>().GetValue("Lab:EntityServices", false))
            EntityEndpoints.Map(app);

        app.MapGet("/ledger/feed", async (long after, int? max, ILedger ledger, CancellationToken ct) =>
            Results.Ok(await ledger.ReadFeedAsync(after, Math.Clamp(max ?? 500, 1, 5000), ct)));

        return app;
    }

    // #region accept
    /// <summary>
    /// Accepts a transfer without asking anyone else: it only needs the ledger's own database.
    /// The processor makes it, or rejects it, afterwards; the customer follows its status.
    /// </summary>
    private static async Task<IResult> AcceptAsync(PayRequest request, LedgerDb db, TimeProvider time, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var accepted = new TransferRequest
        {
            TransferId = Guid.CreateVersion7(), FromAccountId = request.FromAccountId, PayeeId = request.PayeeId,
            Amount = request.Amount, Reference = request.Reference, AcceptedAt = now, NextAttemptAt = now,
        };
        db.TransferRequests.Add(accepted);
        await db.SaveChangesAsync(ct);
        return Results.Accepted($"/transfers/{accepted.TransferId}", new { accepted.TransferId, accepted.Status });
    }
    // #endregion accept

    /// <summary>
    /// Brings the module's tables up to date with its migrations, then seeds its accounts if it
    /// has none. Several processes may start at once (stage 3 runs the ledger on its own), so
    /// one migrates while the others wait.
    /// </summary>
    public static async Task PrepareAsync(IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LedgerDb>();
        var seed = scope.ServiceProvider.GetRequiredService<LedgerSeed>();

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_lock(hashtext({Schema}))", ct);
            await db.Database.MigrateAsync(ct);
            if (!await db.Accounts.AnyAsync(ct))
            {
                await db.Database.ExecuteSqlAsync($"""
                    INSERT INTO ledger.accounts (account_id, holder_name, balance)
                    SELECT g, 'Customer ' || g, {LedgerSeed.OpeningBalance} FROM generate_series(1, {seed.Accounts}) AS g
                    """, ct);
            }
        }
        finally
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock(hashtext({Schema}))", CancellationToken.None);
            await db.Database.CloseConnectionAsync();
        }
    }
}

internal sealed record LedgerSeed(int Accounts)
{
    public const decimal OpeningBalance = 10_000m;
}

internal sealed record PayRequest(int FromAccountId, Guid PayeeId, decimal Amount, string? Reference);
