using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Bank.Api.Data;

public sealed class BankDb(DbContextOptions<BankDb> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();

    // Starting state for journey B1: only primary keys are indexed. Postgres doesn't index
    // foreign key columns automatically, and many hand-written schemas don't either. EF Core
    // adds those indexes by convention, so we turn the convention off; B1 stage 2 is where
    // you decide which indexes the queries actually need.
    protected override void ConfigureConventions(ModelConfigurationBuilder conventions) =>
        conventions.Conventions.Remove<ForeignKeyIndexConvention>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Account>(account =>
        {
            account.ToTable("accounts");
            account.Property(a => a.HolderName).HasMaxLength(200);
            account.Property(a => a.Currency).HasMaxLength(3).IsFixedLength();
        });

        model.Entity<Transaction>(transaction =>
        {
            transaction.ToTable("transactions");
            transaction.Property(t => t.Amount).HasPrecision(18, 2);
            transaction.Property(t => t.Description).HasMaxLength(200);

            transaction.HasOne(t => t.Account).WithMany()
                .HasForeignKey(t => t.AccountId).OnDelete(DeleteBehavior.Restrict);
            transaction.HasOne(t => t.CounterpartyAccount).WithMany()
                .HasForeignKey(t => t.CounterpartyAccountId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
