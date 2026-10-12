using Npgsql;

namespace Bank.Messaging;

/// <summary>
/// B2 stage 5: the statements service keeps a running balance per account, built from account
/// events. Each event carries the account's sequence number, so the service can tell:
///   sequence = last + 1: the next event, in order; apply it.
///   sequence &lt;= last:   seen already (a duplicate); ignore it.
///   sequence &gt; last + 1: something is missing or late: out of order.
/// This version applies out-of-order events anyway and records the problem, so you can count
/// them; stage 5 is about not receiving them in the first place.
/// </summary>
public sealed class Statements(NpgsqlDataSource db)
{
    public async Task OpenAsync(AccountOpened opened, CancellationToken ct)
    {
        await using var command = db.CreateCommand(
            "INSERT INTO statement_accounts (account_id, last_sequence, balance) VALUES ($1, 1, 0) ON CONFLICT DO NOTHING");
        command.Parameters.AddWithValue(opened.AccountId);
        await command.ExecuteNonQueryAsync(ct);
    }

    // #region apply-entry
    public async Task PostAsync(AccountEntryPosted entry, CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await using var read = new NpgsqlCommand(
            "SELECT last_sequence FROM statement_accounts WHERE account_id = $1 FOR UPDATE", connection, transaction);
        read.Parameters.AddWithValue(entry.AccountId);
        var last = (int?)await read.ExecuteScalarAsync(ct);

        if (last >= entry.Sequence)
        {
            return; // a duplicate: already applied
        }

        if (last is null || entry.Sequence != last + 1)
        {
            var problem = last is null
                ? "entry before the account was opened"
                : $"out of order: expected {last + 1}, got {entry.Sequence}";
            await using var record = new NpgsqlCommand(
                "INSERT INTO statement_problems (account_id, sequence, problem, at) VALUES ($1, $2, $3, now())",
                connection, transaction);
            record.Parameters.AddWithValue(entry.AccountId);
            record.Parameters.AddWithValue(entry.Sequence);
            record.Parameters.AddWithValue(problem);
            await record.ExecuteNonQueryAsync(ct);
        }

        await using var apply = new NpgsqlCommand(
            "INSERT INTO statement_accounts (account_id, last_sequence, balance) VALUES ($1, $2, $3) " +
            "ON CONFLICT (account_id) DO UPDATE SET balance = statement_accounts.balance + $3, " +
            "last_sequence = GREATEST(statement_accounts.last_sequence, $2)", connection, transaction);
        apply.Parameters.AddWithValue(entry.AccountId);
        apply.Parameters.AddWithValue(entry.Sequence);
        apply.Parameters.AddWithValue(entry.Amount);
        await apply.ExecuteNonQueryAsync(ct);

        await transaction.CommitAsync(ct);
    }
    // #endregion apply-entry
}
