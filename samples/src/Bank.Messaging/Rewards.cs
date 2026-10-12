using Npgsql;

namespace Bank.Messaging;

/// <summary>
/// B2 stage 3: the rewards service awards a point for every R100 transferred. Its side effect
/// is a database row, so duplicate deliveries are easy to count. Worker:Idempotent chooses the
/// version.
/// </summary>
public sealed class Rewards(NpgsqlDataSource db)
{
    public static int PointsFor(decimal amount) => (int)Math.Floor(amount / 100m);

    // #region rewards-naive
    // Award the points. Correct if every message arrives exactly once, which it won't.
    public async Task AwardAsync(TransferCompleted transfer, CancellationToken ct)
    {
        await using var command = db.CreateCommand(
            "INSERT INTO reward_points (account_id, transfer_id, points, awarded_at) VALUES ($1, $2, $3, now())");
        command.Parameters.AddWithValue(transfer.FromAccountId);
        command.Parameters.AddWithValue(transfer.TransferId);
        command.Parameters.AddWithValue(PointsFor(transfer.Amount));
        await command.ExecuteNonQueryAsync(ct);
    }
    // #endregion rewards-naive

    // #region rewards-idempotent
    // Record the message id and award the points in one transaction. A redelivered message
    // finds its id already recorded, so it changes nothing; and if the worker dies before
    // committing, neither happened, so the redelivery does both.
    public async Task AwardOnceAsync(TransferCompleted transfer, string messageId, CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await using var record = new NpgsqlCommand(
            "INSERT INTO processed_messages (consumer, message_id, processed_at) VALUES ('rewards', $1, now()) " +
            "ON CONFLICT DO NOTHING", connection, transaction);
        record.Parameters.AddWithValue(messageId);
        if (await record.ExecuteNonQueryAsync(ct) == 0)
        {
            return; // seen it before: already awarded
        }

        await using var award = new NpgsqlCommand(
            "INSERT INTO reward_points (account_id, transfer_id, points, awarded_at) VALUES ($1, $2, $3, now())",
            connection, transaction);
        award.Parameters.AddWithValue(transfer.FromAccountId);
        award.Parameters.AddWithValue(transfer.TransferId);
        award.Parameters.AddWithValue(PointsFor(transfer.Amount));
        await award.ExecuteNonQueryAsync(ct);

        await transaction.CommitAsync(ct);
    }
    // #endregion rewards-idempotent
}
