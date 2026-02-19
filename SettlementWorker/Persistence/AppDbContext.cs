using Microsoft.EntityFrameworkCore;
using SettlementWorker.Persistence.Entities;

namespace SettlementWorker.Persistence;

public class AppDbContext : DbContext
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>().ToTable("Accounts");
        modelBuilder.Entity<Transaction>().ToTable("Transactions");

        modelBuilder.Entity<Account>()
            .Property(a => a.Version)
            .IsConcurrencyToken();
    }
}
