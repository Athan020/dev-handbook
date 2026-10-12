using Bank.Sharding;

namespace Bank.Api.Tests;

/// <summary>B1 stage 7: the claims the stage makes about routing keys to shards.</summary>
public sealed class ShardingTests
{
    private static readonly string[] Accounts = Enumerable.Range(1, 100_000).Select(id => $"account:{id}").ToArray();

    private static string[] Shards(int count) => Enumerable.Range(0, count).Select(n => $"shard-{n}").ToArray();

    [Fact]
    public void The_stable_hash_never_changes()
    {
        // Pinned: if this value ever changes, every key would move to a different shard.
        Assert.Equal(1_368_385_643_028_950_576UL, StableHash.Of("account:1"));
    }

    [Fact]
    public void Modulo_moves_most_keys_when_a_shard_is_added()
    {
        var before = new ModuloRouter(4);
        var after = new ModuloRouter(5);

        var moved = Accounts.Count(k => before.ShardFor(k) != after.ShardFor(k));

        Assert.InRange(moved / (double)Accounts.Length, 0.75, 0.85); // about 4 in 5
    }

    [Fact]
    public void A_ring_moves_about_a_fair_share_and_only_to_the_new_shard()
    {
        var before = new ConsistentHashRing(Shards(4));
        var after = new ConsistentHashRing(Shards(5));

        var moved = Accounts.Where(k => before.ShardFor(k) != after.ShardFor(k)).ToList();

        Assert.InRange(moved.Count / (double)Accounts.Length, 0.15, 0.25); // about 1 in 5
        Assert.All(moved, k => Assert.Equal("shard-4", after.ShardFor(k)));
    }

    [Fact]
    public void Removing_a_shard_from_a_ring_moves_only_its_keys()
    {
        var before = new ConsistentHashRing(Shards(5));
        var after = new ConsistentHashRing(Shards(5).Where(s => s != "shard-2"));

        Assert.All(
            Accounts.Where(k => before.ShardFor(k) != after.ShardFor(k)),
            k => Assert.Equal("shard-2", before.ShardFor(k)));
    }

    [Theory]
    [InlineData(1, 0.10, 3.0)]   // one point per shard: very uneven
    [InlineData(128, 0.80, 1.20)] // many virtual nodes: close to even
    public void Virtual_nodes_even_out_the_ring(int virtualNodes, double minShare, double maxShare)
    {
        var ring = new ConsistentHashRing(Shards(8), virtualNodes);
        var fair = Accounts.Length / 8.0;

        var shares = Accounts.GroupBy(ring.ShardFor).Select(g => g.Count() / fair).ToList();

        if (virtualNodes == 1)
        {
            Assert.True(shares.Max() - shares.Min() > 0.5, $"expected an uneven ring, got {shares.Min():N2} to {shares.Max():N2}");
        }
        else
        {
            Assert.All(shares, share => Assert.InRange(share, minShare, maxShare));
        }
    }

    [Fact]
    public void Math_Abs_of_the_smallest_int_overflows()
    {
        // Why `Math.Abs(key.GetHashCode()) % n` can throw for an unlucky key.
        Assert.Throws<OverflowException>(() => Math.Abs(int.MinValue));
    }
}
