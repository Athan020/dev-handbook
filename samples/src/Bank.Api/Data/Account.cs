namespace Bank.Api.Data;

public sealed class Account
{
    public int Id { get; set; }
    public required string HolderName { get; set; }
    public required string Currency { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
}
