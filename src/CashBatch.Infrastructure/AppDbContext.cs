using CashBatch.Domain;
using Microsoft.EntityFrameworkCore;

namespace CashBatch.Infrastructure;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> opt) : base(opt) { }

    public DbSet<Batch> Batches => Set<Batch>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentLine> PaymentLines => Set<PaymentLine>();
    public DbSet<CustomerLookup> CustomerLookups => Set<CustomerLookup>();
    public DbSet<MatchLog> MatchLogs => Set<MatchLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Map to existing table names in JBI database
        b.HasDefaultSchema("dbo");
        b.Entity<Batch>().ToTable("cash_batches");
        b.Entity<Payment>().ToTable("cash_payments");
        b.Entity<PaymentLine>().ToTable("cash_payment_lines");
        b.Entity<CustomerLookup>().ToTable("cash_customer_lookups");
        b.Entity<MatchLog>().ToTable("cash_match_logs");

        b.Entity<Batch>().HasMany(x => x.Payments).WithOne(x => x.Batch).HasForeignKey(x => x.BatchId);
        b.Entity<Payment>().HasMany(x => x.Lines).WithOne(x => x.Payment).HasForeignKey(x => x.PaymentId);
        b.Entity<CustomerLookup>().HasIndex(x => new { x.KeyType, x.KeyValue }).IsUnique();
        b.Entity<Payment>().Property(p => p.Amount).HasColumnType("decimal(18,2)");
        b.Entity<PaymentLine>().Property(p => p.AppliedAmount).HasColumnType("decimal(18,2)");
        b.Entity<PaymentLine>().Property(p => p.FreightTakenAmt).HasColumnType("decimal(18,2)");
        b.Entity<PaymentLine>().Property(p => p.TermsTakenAmt).HasColumnType("decimal(18,2)");
        b.Entity<PaymentLine>().Property(p => p.BranchId).HasMaxLength(8);

        // Additional properties introduced for CSV import
        b.Entity<Batch>().Property(p => p.DepositDate);
        b.Entity<Batch>().Property(p => p.CustomerBatchNumber);
        b.Entity<Batch>().Property(p => p.DepositNumber);
        b.Entity<Payment>().Property(p => p.SequenceNumber);
        b.Entity<Payment>().Property(p => p.BankNumber);
        b.Entity<Payment>().Property(p => p.AccountNumber);
        b.Entity<Payment>().Property(p => p.RemitterName);
        b.Entity<Payment>().Property(p => p.City).HasMaxLength(50);
        b.Entity<Payment>().Property(p => p.InvoiceNumber);
        b.Entity<Payment>().Property(p => p.OriginalCustomerId);
        b.Entity<Payment>().Property(p => p.TransactionType);
        b.Entity<Payment>().Property(p => p.Category);

        // New numeric identity for payments, unique index for lookups/exports
        b.Entity<Payment>()
            .Property(p => p.PaymentNumber)
            .HasColumnType("decimal(19,0)")
            .UseIdentityColumn();
        b.Entity<Payment>()
            .HasIndex(p => p.PaymentNumber)
            .IsUnique();

        // Expose denormalized PaymentNumber on lines
        b.Entity<PaymentLine>().Property(l => l.PaymentNumber).HasColumnType("decimal(19,0)");
    }
}
