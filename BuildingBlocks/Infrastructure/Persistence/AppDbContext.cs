using BuildingBlocks.Domain.Entities;
using BuildingBlocks.Messaging.Outbox;
using Microsoft.EntityFrameworkCore;


namespace BuildingBlocks.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
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
