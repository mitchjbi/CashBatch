namespace CashBatch.Domain;

public enum BatchStatus { Open, Processing, Completed, Failed }
public enum PaymentStatus { Imported, AutoApplied, NeedsReview, Exported }

public class Batch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime ImportedAt { get; set; }
    public string ImportedBy { get; set; } = "";
    public string SourceFilename { get; set; } = "";
    public DateTime? DepositDate { get; set; } // from CSV header/rows
    public string? CustomerBatchNumber { get; set; } // from CSV header/rows
    public string? DepositNumber { get; set; } // set at export time
    public BatchStatus Status { get; set; } = BatchStatus.Open;
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}

public class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BatchId { get; set; }
    public Batch Batch { get; set; } = null!;
    public int SequenceNumber { get; set; } // row sequence within the batch
    // Numeric identity from DB for ERP/export friendliness
    public decimal PaymentNumber { get; set; }
    public string? CustomerId { get; set; }   // from bank or lookup
    public string? OriginalCustomerId { get; set; } // raw value from CSV (may be "0")
    public decimal Amount { get; set; }
    public string CheckNumber { get; set; } = "";
    public string? BankNumber { get; set; } // routing number from CSV
    public string? AccountNumber { get; set; } // account number from CSV
    public string? BankAccount { get; set; } // kept for backward compat; will mirror AccountNumber
    public string? RemitterName { get; set; }
    public string? City { get; set; }
    public int? InvoiceNumber { get; set; }
    public string? TransactionType { get; set; }
    public string? Category { get; set; }
    public string? RemitAddressHash { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Imported;
    public ICollection<PaymentLine> Lines { get; set; } = new List<PaymentLine>();
}

public class PaymentLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PaymentId { get; set; }
    public Payment Payment { get; set; } = null!;
    // Denormalized numeric parent key for export convenience
    public decimal PaymentNumber { get; set; }
    public string? InvoiceNo { get; set; }
    public decimal AppliedAmount { get; set; }
    public bool WasAutoMatched { get; set; }
    // Amounts deducted from the invoice balance that contributed to the match
    public decimal? FreightTakenAmt { get; set; }
    public decimal? TermsTakenAmt { get; set; }
    // Optional ERP branch identifier returned by open invoices SP (varchar 8)
    public string? BranchId { get; set; }
}

public class CustomerLookup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string KeyType { get; set; } = ""; // "BankAcct" | "AddrHash"
    public string KeyValue { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public double Confidence { get; set; } = 1.0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class MatchLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PaymentId { get; set; }
    public string Level { get; set; } = "Info";
    public string Message { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
