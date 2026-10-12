using System.IO.Hashing;
using System.Text;

namespace Bank.Sharding;

public static class StableHash
{
    // #region stable-hash
    /// <summary>
    /// A 64-bit hash of the key's UTF-8 bytes. XxHash3 is fast, well distributed, and gives
    /// the same answer in every process, on every machine and in every .NET version, which
    /// routing needs. string.GetHashCode() gives none of those guarantees.
    /// </summary>
    public static ulong Of(string key) => XxHash3.HashToUInt64(Encoding.UTF8.GetBytes(key));
    // #endregion stable-hash
}
