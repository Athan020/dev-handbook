using Bank.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Bank.Api.Endpoints;

public sealed record AccountResponse(int Id, string HolderName, string Currency, DateTimeOffset OpenedAt);

public sealed record TransactionResponse(
    long Id, decimal Amount, string Description, DateTimeOffset CreatedAt, string? CounterpartyName);

public sealed record TransactionPage(int Page, int PageSize, IReadOnlyList<TransactionResponse> Items);

public static class AccountEndpoints
{
    public const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var accounts = app.MapGroup("/accounts");
        accounts.MapGet("/{id:int}", GetAccount);
        accounts.MapGet("/{id:int}/transactions", GetTransactions);
        return app;
    }

    private static async Task<IResult> GetAccount(int id, BankDb db, CancellationToken ct)
    {
        var account = await db.Accounts.AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new AccountResponse(a.Id, a.HolderName, a.Currency, a.OpenedAt))
            .SingleOrDefaultAsync(ct);

        return account is null ? Results.NotFound() : Results.Ok(account);
    }

    /// <summary>
    /// Newest transactions first, one page at a time.
    /// This is the version that "works in development": correct, but written without
    /// thinking about scale. Journey B1 measures it and improves it stage by stage.
    /// </summary>
    private static async Task<IResult> GetTransactions(
        int id, BankDb db, CancellationToken ct, int page = 1, int pageSize = 20)
    {
        if (page < 1 || pageSize < 1 || pageSize > MaxPageSize)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["paging"] = [$"page must be 1 or more, and pageSize between 1 and {MaxPageSize}."],
            });
        }

        if (!await db.Accounts.AnyAsync(a => a.Id == id, ct))
        {
            return Results.NotFound();
        }

        var transactions = await db.Transactions.AsNoTracking()
            .Where(t => t.AccountId == id)
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        // Deliberately naive, and the subject of B1 stage 3: one extra query per
        // transfer to look up the counterparty's name (the N+1 query problem).
        var items = new List<TransactionResponse>(transactions.Count);
        foreach (var t in transactions)
        {
            string? counterpartyName = null;
            if (t.CounterpartyAccountId is int counterpartyId)
            {
                counterpartyName = await db.Accounts.AsNoTracking()
                    .Where(a => a.Id == counterpartyId)
                    .Select(a => a.HolderName)
                    .SingleOrDefaultAsync(ct);
            }

            items.Add(new TransactionResponse(t.Id, t.Amount, t.Description, t.CreatedAt, counterpartyName));
        }

        return Results.Ok(new TransactionPage(page, pageSize, items));
    }
}
