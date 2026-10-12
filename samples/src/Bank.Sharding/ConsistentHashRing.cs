namespace Bank.Sharding;

// #region ring
/// <summary>
/// Consistent hashing. Each shard is placed on a ring at many points ("virtual nodes"),
/// by hashing "shard#n". A key belongs to the first shard point at or after its own hash,
/// wrapping around. Adding a shard takes over only the arcs just before its new points,
/// so only about 1/N of the keys move, all of them to the new shard.
/// </summary>
public sealed class ConsistentHashRing
{
    private readonly ulong[] _points;
    private readonly string[] _owners;

    public ConsistentHashRing(IEnumerable<string> shards, int virtualNodes = 128)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(virtualNodes, 1);
        var points = shards
            .SelectMany(shard => Enumerable.Range(0, virtualNodes)
                .Select(n => (Hash: StableHash.Of($"{shard}#{n}"), Shard: shard)))
            .OrderBy(p => p.Hash)
            .ToArray();
        if (points.Length == 0) throw new ArgumentException("At least one shard is needed.", nameof(shards));

        _points = points.Select(p => p.Hash).ToArray();
        _owners = points.Select(p => p.Shard).ToArray();
    }

    public string ShardFor(string key)
    {
        var index = Array.BinarySearch(_points, StableHash.Of(key));
        if (index < 0) index = ~index;          // first point after the key's hash
        if (index == _points.Length) index = 0; // past the last point: wrap around
        return _owners[index];
    }
}
// #endregion ring
