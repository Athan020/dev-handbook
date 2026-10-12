using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bank.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Bank.Api.Endpoints;

/// <summary>
/// Journey B3 stage 3. Idempotency keys on POST /transfers: a client sends the same
/// Idempotency-Key header with every retry of one payment. Lab:Idempotency chooses how:
///   None (the default): the header is ignored, so every retry is a new transfer.
///   CheckFirst: look the key up before the transfer, and record it after the transfer commits.
///   Keys: claim the key in the same transaction as the transfer, with a unique constraint.
/// </summary>
public static class Idempotency
{
    public const string Header = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";

    public abstract record Claim
    {
        public sealed record New : Claim;
        public sealed record Replay(int StatusCode, string Response) : Claim;
        public sealed record Mismatch : Claim;
    }

    public static string Hash(TransferRequest request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));

    // #region claim
    // Insert the key at the start of the transfer's transaction. If another request with the
    // same key is running, this INSERT waits for it to finish: a unique index allows only one.
    // Then the key exists, and we return the response it stored instead of moving money again.
    public static async Task<Claim> ClaimAsync(BankDb db, int payer, string key, string requestHash, CancellationToken ct)
    {
        var inserted = await db.Database.ExecuteSqlAsync($"""
            INSERT INTO idempotency_keys (payer_account_id, key, request_hash, created_at)
            VALUES ({payer}, {key}, {requestHash}, now())
            ON CONFLICT (payer_account_id, key) DO NOTHING
            """, ct);
        if (inserted == 1)
        {
            return new Claim.New();
        }

        var existing = await db.IdempotencyKeys.AsNoTracking()
            .SingleAsync(k => k.PayerAccountId == payer && k.Key == key, ct);
        if (existing.RequestHash != requestHash)
        {
            return new Claim.Mismatch();
        }
        return new Claim.Replay(existing.StatusCode!.Value, existing.Response!);
    }

    // Store the response with the key, in the same transaction, just before it commits.
    public static Task CompleteAsync(BankDb db, int payer, string key, int statusCode, object response, CancellationToken ct) =>
        db.IdempotencyKeys
            .Where(k => k.PayerAccountId == payer && k.Key == key)
            .ExecuteUpdateAsync(set => set
                .SetProperty(k => k.StatusCode, statusCode)
                .SetProperty(k => k.Response, JsonSerializer.Serialize(response, JsonSerializerOptions.Web)), ct);
    // #endregion claim

    // #region check-first
    // The tempting version: look first, record afterwards, in separate steps. Two requests with
    // the same key can both look before either has recorded, and a crash after the transfer
    // commits leaves no record at all.
    public static async Task<IdempotencyKey?> FindAsync(BankDb db, int payer, string key, CancellationToken ct) =>
        await db.IdempotencyKeys.AsNoTracking().SingleOrDefaultAsync(k => k.PayerAccountId == payer && k.Key == key, ct);

    public static Task RecordAfterwardsAsync(BankDb db, int payer, string key, string requestHash, int statusCode, object response, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"""
            INSERT INTO idempotency_keys (payer_account_id, key, request_hash, status_code, response, created_at)
            VALUES ({payer}, {key}, {requestHash}, {statusCode}, {JsonSerializer.Serialize(response, JsonSerializerOptions.Web)}::jsonb, now())
            ON CONFLICT (payer_account_id, key) DO NOTHING
            """, ct);
    // #endregion check-first
}
