using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace Bank.Api.Data;

/// <summary>
/// B1 stage 6: decides whether a read goes to the primary or the read replica.
/// The Lab:Reads setting picks the policy:
///   Primary (the default): every read goes to the primary.
///   Replica: every read goes to the replica, however far behind it is.
///   ReadYourWrites: reads go to the replica, unless the client sends a consistency token
///   from a recent write that the replica hasn't replayed yet; then they go to the primary.
/// </summary>
public sealed partial class ReadRouting(BankDb primary, ReplicaOptions replica, IConfiguration config)
{
    public const string TokenHeader = "X-Consistency-Token";
    public const string ServedByHeader = "X-Served-By";

    public async Task<BankDb> ChooseAsync(HttpContext http, CancellationToken ct)
    {
        var useReplica = config["Lab:Reads"] switch
        {
            "Replica" => true,
            "ReadYourWrites" => await ReplicaHasCaughtUpAsync(http.Request.Headers[TokenHeader].ToString(), ct),
            _ => false,
        };

        http.Response.Headers[ServedByHeader] = useReplica ? "replica" : "primary";
        if (!useReplica)
        {
            return primary;
        }

        var db = new BankDb(replica.Options);
        http.Response.RegisterForDisposeAsync(db);
        return db;
    }

    // #region read-your-writes
    // A write returns the primary's WAL position (LSN) after it commits. A later read that
    // carries that token may use the replica only once the replica has replayed at least
    // that far; until then it goes to the primary. Reads without a token use the replica.
    private async Task<bool> ReplicaHasCaughtUpAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token))
        {
            return true;
        }

        if (!LsnPattern().IsMatch(token))
        {
            return false;
        }

        await using var db = new BankDb(replica.Options);
        var caughtUp = await db.Database
            .SqlQuery<bool?>($"SELECT pg_last_wal_replay_lsn() >= {token}::pg_lsn AS \"Value\"")
            .SingleAsync(ct);

        // NULL means the server isn't replaying anything (it isn't a replica): don't trust it.
        return caughtUp == true;
    }
    // #endregion read-your-writes

    /// <summary>The primary's current WAL position, to hand back to the client after a write.</summary>
    public static Task<string> CurrentLsnAsync(BankDb db, CancellationToken ct) =>
        db.Database.SqlQuery<string>($"SELECT pg_current_wal_lsn()::text AS \"Value\"").SingleAsync(ct);

    [GeneratedRegex("^[0-9A-F]{1,8}/[0-9A-F]{1,8}$")]
    private static partial Regex LsnPattern();
}

/// <summary>EF Core options for connecting to the read replica (ConnectionStrings:BankReplica).</summary>
public sealed record ReplicaOptions(DbContextOptions<BankDb> Options);
