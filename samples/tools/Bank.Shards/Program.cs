// Experiments for B1 stage 7: how keys map to shards, and what that does to your data.
//
//   dotnet run --project tools/Bank.Shards -- hashes
//       Which of 4 shards a few accounts land on, by string.GetHashCode() and by a stable hash.
//       Run it twice: the first column changes between runs, the second doesn't.
//
//   dotnet run --project tools/Bank.Shards -- move [--from 4] [--to 5] [--accounts 100000]
//       How many accounts change shard when you add a shard, with modulo and with a ring.
//
//   dotnet run --project tools/Bank.Shards -- balance [--shards 8]
//       Rows per shard for the lab's real data (it reads per-account counts from Postgres),
//       when accounts are spread across shards with a consistent-hash ring.
using System.Globalization;
using Bank.Sharding;
using Npgsql;

var command = args.FirstOrDefault() ?? "hashes";
int Option(string name, int fallback)
{
    var i = Array.IndexOf(args, $"--{name}");
    return i >= 0 && i + 1 < args.Length ? int.Parse(args[i + 1], CultureInfo.InvariantCulture) : fallback;
}

switch (command)
{
    case "hashes":
        Hashes();
        break;

    case "move":
        Move(Option("from", 4), Option("to", 5), Option("accounts", 100_000));
        break;
    case "balance":
        await BalanceAsync(Option("shards", 8));
        break;
    default:
        Console.WriteLine("Commands: hashes, move, balance");
        break;
}

static void Hashes()
{
    Console.WriteLine("account      GetHashCode() % 4    stable hash % 4");
    var stable = new ModuloRouter(4);
    foreach (var id in new[] { 1, 2, 3, 42, 50_000 })
    {
        var key = $"account:{id}";
        // string.GetHashCode is randomised per process, so this column changes from run to run.
        var naive = (key.GetHashCode() & int.MaxValue) % 4;
        Console.WriteLine($"{key,-12} {naive,17} {stable.ShardFor(key),18}");
    }
}

static void Move(int from, int to, int accounts)
{
    var keys = Enumerable.Range(1, accounts).Select(id => $"account:{id}").ToArray();

    var before = new ModuloRouter(from);
    var after = new ModuloRouter(to);
    var moduloMoved = keys.Count(k => before.ShardFor(k) != after.ShardFor(k));

    var ringBefore = new ConsistentHashRing(Enumerable.Range(0, from).Select(n => $"shard-{n}"));
    var ringAfter = new ConsistentHashRing(Enumerable.Range(0, to).Select(n => $"shard-{n}"));
    var ringMoved = keys.Count(k => ringBefore.ShardFor(k) != ringAfter.ShardFor(k));

    Console.WriteLine($"Going from {from} to {to} shards, for {accounts:N0} accounts:");
    Console.WriteLine($"  modulo:          {moduloMoved,8:N0} accounts move ({100.0 * moduloMoved / accounts:N1}%)");
    Console.WriteLine($"  consistent hash: {ringMoved,8:N0} accounts move ({100.0 * ringMoved / accounts:N1}%)");
    Console.WriteLine($"  (the least possible is {100.0 * (to - from) / to:N1}%: the new shards' fair share)");
}

static async Task BalanceAsync(int shards)
{
    const string connectionString = "Host=localhost;Port=5433;Database=bank;Username=bank;Password=bank";
    await using var dataSource = NpgsqlDataSource.Create(connectionString);
    await using var command = dataSource.CreateCommand(
        "SELECT account_id, count(*) FROM transactions GROUP BY account_id");
    await using var reader = await command.ExecuteReaderAsync();

    var ring = new ConsistentHashRing(Enumerable.Range(0, shards).Select(n => $"shard-{n}"));
    var rows = new Dictionary<string, long>();
    var accountsPerShard = new Dictionary<string, int>();
    long total = 0, busiestAccount = 0;
    while (await reader.ReadAsync())
    {
        var shard = ring.ShardFor($"account:{reader.GetInt32(0)}");
        var count = reader.GetInt64(1);
        rows[shard] = rows.GetValueOrDefault(shard) + count;
        accountsPerShard[shard] = accountsPerShard.GetValueOrDefault(shard) + 1;
        total += count;
        busiestAccount = Math.Max(busiestAccount, count);
    }

    var fair = (double)total / shards;
    Console.WriteLine($"{total:N0} transactions over {shards} shards (a fair share is {fair:N0}):");
    foreach (var (shard, count) in rows.OrderByDescending(r => r.Value))
    {
        Console.WriteLine($"  {shard,-9} {count,12:N0} rows  {accountsPerShard[shard],7:N0} accounts  {count / fair,5:N2}x fair share");
    }
    Console.WriteLine($"The busiest single account has {busiestAccount:N0} rows ({100.0 * busiestAccount / total:N1}% of all).");
}
