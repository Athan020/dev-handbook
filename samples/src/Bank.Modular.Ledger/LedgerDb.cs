using Microsoft.EntityFrameworkCore;

namespace Bank.Modular.Ledger;

// Everything in this module is internal except LedgerModule: other modules can't see these
// types, so they can't depend on them.

internal sealed class Account
{
    public int AccountId { get; set; }
    public string HolderName { get; set; } = "";
    public decimal Balance { get; set; }
}

internal sealed class Entry
{
    /// <summary>Order of posting across the whole ledger, which makes the entries a feed.</summary>
    public long Position { get; set; }
    public Guid TransferId { get; set; }
    public int AccountId { get; set; }
    public decimal Amount { get; set; }
    public string Reference { get; set; } = "";
    public DateTimeOffset PostedAt { get; set; }
}

internal sealed class LedgerDb(DbContextOptions<LedgerDb> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Entry> Entries => Set<Entry>();
    public DbSet<TransferRequest> TransferRequests => Set<TransferRequest>();
    public DbSet<CardTopUp> CardTopUps => Set<CardTopUp>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        // The module's tables live in its own schema, which no other module maps.
        model.HasDefaultSchema(LedgerModule.Schema);

        model.Entity<Account>(account =>
        {
            account.ToTable("accounts");
            account.Property(a => a.AccountId).ValueGeneratedNever();
            account.Property(a => a.HolderName).HasMaxLength(200);
            account.Property(a => a.Balance).HasPrecision(18, 2);
        });

        model.Entity<Entry>(entry =>
        {
            entry.ToTable("entries");
            entry.HasKey(e => e.Position);
            entry.Property(e => e.Amount).HasPrecision(18, 2);
            entry.Property(e => e.Reference).HasMaxLength(200);
            entry.HasIndex(e => new { e.AccountId, e.PostedAt });
            entry.HasIndex(e => e.TransferId);
        });

        model.Entity<TransferRequest>(request =>
        {
            request.ToTable("transfer_requests");
            request.HasKey(r => r.TransferId);
            request.Property(r => r.Amount).HasPrecision(18, 2);
            request.Property(r => r.Reference).HasMaxLength(200);
            request.Property(r => r.Status).HasMaxLength(20);
            request.Property(r => r.Reason).HasMaxLength(200);
            request.HasIndex(r => new { r.Status, r.NextAttemptAt });
        });

        model.Entity<CardTopUp>(topUp =>
        {
            topUp.ToTable("card_top_ups");
            topUp.HasKey(t => t.TopUpId);
            topUp.Property(t => t.CardId).HasMaxLength(50);
            topUp.Property(t => t.Amount).HasPrecision(18, 2);
            topUp.Property(t => t.Status).HasMaxLength(20);
            topUp.Property(t => t.Detail).HasMaxLength(200);
            topUp.HasIndex(t => new { t.Status, t.NextAttemptAt });
        });
    }
}
