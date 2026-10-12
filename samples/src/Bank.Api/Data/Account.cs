namespace Bank.Api.Data;

public sealed class Account
{
    public int Id { get; set; }
    public required string HolderName { get; set; }
    public required string Currency { get; set; }
    public DateTimeOffset OpenedAt { get; set; }

    /// <summary>
    /// B2 stage 5: how many events this account has had. Each new event takes the next number,
    /// so consumers can tell when events arrive out of order. 1 is "account opened".
    /// </summary>
    public int Version { get; set; } = 1;
}
