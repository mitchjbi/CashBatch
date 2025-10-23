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
    public DbSet<CashTemplate> CashTemplates => Set<CashTemplate>();
    public DbSet<CashTemplateDetail> CashTemplateDetails => Set<CashTemplateDetail>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Map to existing table names in JBI database
        b.HasDefaultSchema("dbo");
        b.Entity<Batch>().ToTable("cash_batches");
        b.Entity<Payment>().ToTable("cash_payments");
        b.Entity<PaymentLine>().ToTable("cash_payment_lines");
        b.Entity<CustomerLookup>().ToTable("cash_customer_lookups");
        b.Entity<MatchLog>().ToTable("cash_match_logs");
        b.Entity<CashTemplate>().ToTable("cash_template");
        b.Entity<CashTemplateDetail>().ToTable("cash_template_detail");

        b.Entity<Batch>().HasMany(x => x.Payments).WithOne(x => x.Batch).HasForeignKey(x => x.BatchId);
        b.Entity<Payment>().HasMany(x => x.Lines).WithOne(x => x.Payment).HasForeignKey(x => x.PaymentId);
        b.Entity<CustomerLookup>().HasIndex(x => new { x.KeyType, x.KeyValue }).IsUnique();
        b.Entity<Payment>().Property(p => p.Amount).HasColumnType("decimal(18,2)");
        b.Entity<PaymentLine>().Property(p => p.AppliedAmount).HasColumnType("decimal(18,2)");
        b.Entity<PaymentLine>().Property(p => p.FreightTakenAmt).HasColumnType("decimal(18,2)");
        b.Entity<PaymentLine>().Property(p => p.TermsTakenAmt).HasColumnType("decimal(18,2)");
        b.Entity<PaymentLine>().Property(p => p.BranchId).HasMaxLength(8);

        // cash_template mapping
        b.Entity<CashTemplate>().HasKey(t => t.TemplateId);
        b.Entity<CashTemplate>().Property(t => t.TemplateId).HasColumnName("template_id");
        b.Entity<CashTemplate>().Property(t => t.Name).HasColumnName("name");
        b.Entity<CashTemplate>().Property(t => t.Description).HasColumnName("description");
        b.Entity<CashTemplate>().Property(t => t.FileType).HasColumnName("file_type");
        b.Entity<CashTemplate>().Property(t => t.HasHeaders).HasColumnName("has_headers");
        b.Entity<CashTemplate>().Property(t => t.HeaderRowIndex).HasColumnName("header_row_index");
        b.Entity<CashTemplate>().Property(t => t.DataStartRowIndex).HasColumnName("data_start_row_index");
        b.Entity<CashTemplate>().Property(t => t.Delimiter).HasColumnName("delimiter");
        b.Entity<CashTemplate>().Property(t => t.QuoteChar).HasColumnName("quote_char");
        b.Entity<CashTemplate>().Property(t => t.EscapeChar).HasColumnName("escape_char");
        b.Entity<CashTemplate>().Property(t => t.Culture).HasColumnName("culture");
        b.Entity<CashTemplate>().Property(t => t.DateFormats).HasColumnName("date_formats");
        b.Entity<CashTemplate>().Property(t => t.Encoding).HasColumnName("encoding");
        b.Entity<CashTemplate>().Property(t => t.WorksheetName).HasColumnName("worksheet_name");
        b.Entity<CashTemplate>().Property(t => t.IsActive).HasColumnName("is_active");
        b.Entity<CashTemplate>().Property(t => t.CreatedBy).HasColumnName("created_by");
        b.Entity<CashTemplate>().Property(t => t.CreatedAtUtc).HasColumnName("created_at_utc");
        b.Entity<CashTemplate>().Property(t => t.ModifiedBy).HasColumnName("modified_by");
        b.Entity<CashTemplate>().Property(t => t.ModifiedAtUtc).HasColumnName("modified_at_utc");
        b.Entity<CashTemplate>().HasMany(t => t.Details).WithOne(d => d.Template).HasForeignKey(d => d.TemplateId).OnDelete(DeleteBehavior.Cascade);

        // cash_template_detail mapping
        b.Entity<CashTemplateDetail>().HasKey(d => d.DetailId);
        b.Entity<CashTemplateDetail>().Property(d => d.DetailId).HasColumnName("detail_id");
        b.Entity<CashTemplateDetail>().Property(d => d.TemplateId).HasColumnName("template_id");
        b.Entity<CashTemplateDetail>().Property(d => d.TargetField).HasColumnName("target_field");
        b.Entity<CashTemplateDetail>().Property(d => d.SourceHeader).HasColumnName("source_header");
        b.Entity<CashTemplateDetail>().Property(d => d.SourceColumnIndex).HasColumnName("source_column_index");
        b.Entity<CashTemplateDetail>().Property(d => d.FixedWidthStart).HasColumnName("fixed_width_start");
        b.Entity<CashTemplateDetail>().Property(d => d.FixedWidthLength).HasColumnName("fixed_width_length");
        b.Entity<CashTemplateDetail>().Property(d => d.IsRequired).HasColumnName("is_required");
        b.Entity<CashTemplateDetail>().Property(d => d.DefaultValue).HasColumnName("default_value");
        b.Entity<CashTemplateDetail>().Property(d => d.Transform).HasColumnName("transform");
        b.Entity<CashTemplateDetail>().Property(d => d.ValidationRule).HasColumnName("validation_rule");
        b.Entity<CashTemplateDetail>().Property(d => d.Notes).HasColumnName("notes");

        // Additional properties introduced for CSV import
        b.Entity<Batch>().Property(p => p.DepositDate);
        b.Entity<Batch>().Property(p => p.CustomerBatchNumber);
        b.Entity<Batch>().Property(p => p.BatchName).HasMaxLength(50);
        b.Entity<Batch>().Property(p => p.TemplateId);
        b.Entity<Payment>().Property(p => p.SequenceNumber);
        b.Entity<Payment>().Property(p => p.BankNumber);
        b.Entity<Payment>().Property(p => p.AccountNumber);
        b.Entity<Payment>().Property(p => p.RemitterName);
        b.Entity<Payment>().Property(p => p.City).HasMaxLength(50);
        b.Entity<Payment>().Property(p => p.InvoiceNumber).HasMaxLength(50);
        b.Entity<Payment>().Property(p => p.OrderNumber).HasMaxLength(50);
        b.Entity<Payment>().Property(p => p.TransactionDate);
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
