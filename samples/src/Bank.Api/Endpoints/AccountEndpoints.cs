using System.Buffers.Binary;
using Bank.Api.Data;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Bank.Api.Endpoints;

public sealed record AccountResponse(int Id, string HolderName, string Currency, DateTimeOffset OpenedAt);

public sealed record TransactionResponse(
    long Id, decimal Amount, string Description, DateTimeOffset CreatedAt, string? CounterpartyName);

public sealed record TransactionPage(int Page, int PageSize, IReadOnlyList<TransactionResponse> Items);

public sealed record TransactionFeed(IReadOnlyList<TransactionResponse> Items, string? NextCursor);

public static class AccountEndpoints
{
    public const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var accounts = app.MapGroup("/accounts");
        accounts.MapGet("/{id:int}", GetAccount);
        accounts.MapGet("/{id:int}/transactions", GetTransactions);
        accounts.MapGet("/{id:int}/transactions/feed", GetTransactionFeed);
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
    /// Newest transactions first, one page at a time (offset paging).
    /// Two implementations, chosen with the Lab:Transactions setting, so you can measure
    /// them against each other: Naive (the default, as first written) and Projected
    /// (B1 stage 3's fix).
    /// </summary>
    private static async Task<IResult> GetTransactions(
        int id, BankDb db, IConfiguration config, CancellationToken ct, int page = 1, int pageSize = 20)
    {
        if (page < 1 || pageSize < 1 || pageSize > MaxPageSize)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["paging"] = [$"page must be 1 or more, and pageSize between 1 and {MaxPageSize}."],
            });
        }

        var items = config["Lab:Transactions"] == "Projected"
            ? await ProjectedPageAsync(db, id, page, pageSize, ct)
            : await NaivePageAsync(db, id, page, pageSize, ct);

        return items is null ? Results.NotFound() : Results.Ok(new TransactionPage(page, pageSize, items));
    }

    // #region naive
    // The version that "works in development": correct, but written without thinking about
    // scale. One query to check the account exists, one for the page, then one more per
    // transfer to look up the counterparty's name: the N+1 query problem.
    private static async Task<List<TransactionResponse>?> NaivePageAsync(
        BankDb db, int id, int page, int pageSize, CancellationToken ct)
    {
        if (!await db.Accounts.AnyAsync(a => a.Id == id, ct))
        {
            return null;
        }

        var transactions = await db.Transactions.AsNoTracking()
            .Where(t => t.AccountId == id)
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

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

        return items;
    }
    // #endregion naive

    // #region projected
    // One round trip in the common case. Select only the columns the response needs, and let
    // the database join to the counterparty: EF turns the navigation into a LEFT JOIN. Only
    // when the page comes back empty do we ask whether the account exists at all.
    private static async Task<List<TransactionResponse>?> ProjectedPageAsync(
        BankDb db, int id, int page, int pageSize, CancellationToken ct)
    {
        var items = await db.Transactions
            .Where(t => t.AccountId == id)
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new TransactionResponse(
                t.Id, t.Amount, t.Description, t.CreatedAt,
                t.CounterpartyAccount == null ? null : t.CounterpartyAccount.HolderName))
            .ToListAsync(ct);

        if (items.Count == 0 && !await db.Accounts.AnyAsync(a => a.Id == id, ct))
        {
            return null;
        }

        return items;
    }
    // #endregion projected

    // #region keyset
    // Keyset (cursor) paging. Instead of skipping rows, each page starts strictly after the
    // last row the client saw, using (created_at, id) as the position; id breaks ties when
    // two transactions share a timestamp. With an index on (account_id, created_at DESC,
    // id DESC), every page costs the same, however deep, and new transactions arriving
    // between requests can't shift rows onto the next page.
    private static async Task<IResult> GetTransactionFeed(
        int id, BankDb db, CancellationToken ct, int pageSize = 20, string? cursor = null)
    {
        if (pageSize < 1 || pageSize > MaxPageSize)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["pageSize"] = [$"pageSize must be between 1 and {MaxPageSize}."],
            });
        }

        var query = db.Transactions.Where(t => t.AccountId == id);
        if (cursor is not null)
        {
            if (!Cursor.TryDecode(cursor, out var after))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["cursor"] = ["cursor is not valid. Use the nextCursor from a previous response."],
                });
            }

            // Translates to a row comparison: (created_at, id) < (@createdAt, @id)
            query = query.Where(t => EF.Functions.LessThan(
                ValueTuple.Create(t.CreatedAt, t.Id),
                ValueTuple.Create(after.CreatedAt, after.Id)));
        }

        // Fetch one extra row to learn whether there is a next page.
        var rows = await query
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .Take(pageSize + 1)
            .Select(t => new TransactionResponse(
                t.Id, t.Amount, t.Description, t.CreatedAt,
                t.CounterpartyAccount == null ? null : t.CounterpartyAccount.HolderName))
            .ToListAsync(ct);

        if (rows.Count == 0 && cursor is null && !await db.Accounts.AnyAsync(a => a.Id == id, ct))
        {
            return Results.NotFound();
        }

        var items = rows.Take(pageSize).ToList();
        var next = rows.Count > pageSize ? Cursor.Encode(items[^1].CreatedAt, items[^1].Id) : null;
        return Results.Ok(new TransactionFeed(items, next));
    }
    // #endregion keyset

    /// <summary>
    /// An opaque, URL-safe cursor. Clients should treat it as a token, not parse it, so the
    /// encoding can change without breaking them.
    /// </summary>
    private static class Cursor
    {
        public static string Encode(DateTimeOffset createdAt, long id)
        {
            Span<byte> bytes = stackalloc byte[16];
            BinaryPrimitives.WriteInt64BigEndian(bytes, createdAt.UtcTicks);
            BinaryPrimitives.WriteInt64BigEndian(bytes[8..], id);
            return WebEncoders.Base64UrlEncode(bytes.ToArray());
        }

        public static bool TryDecode(string value, out (DateTimeOffset CreatedAt, long Id) position)
        {
            position = default;
            byte[] bytes;
            try
            {
                bytes = WebEncoders.Base64UrlDecode(value);
            }
            catch (FormatException)
            {
                return false;
            }

            if (bytes.Length != 16) return false;
            var ticks = BinaryPrimitives.ReadInt64BigEndian(bytes);
            if (ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks) return false;

            position = (new DateTimeOffset(ticks, TimeSpan.Zero), BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(8)));
            return true;
        }
    }
}
