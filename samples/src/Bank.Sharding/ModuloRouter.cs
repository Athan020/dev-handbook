namespace Bank.Sharding;

/// <summary>
/// Picks a shard as hash(key) mod N. Simple and even, but changing N moves most keys:
/// with N to N+1 shards, a key stays put only if both remainders agree, about 1 time in N+1.
/// </summary>
public sealed class ModuloRouter(int shards)
{
    public int Shards { get; } = shards > 0 ? shards : throw new ArgumentOutOfRangeException(nameof(shards));

    public int ShardFor(string key) => (int)(StableHash.Of(key) % (ulong)Shards);
}
