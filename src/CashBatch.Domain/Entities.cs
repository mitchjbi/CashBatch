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
    public BatchStatus Status { get; set; } = BatchStatus.Open;
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}

public class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BatchId { get; set; }
    public Batch Batch { get; set; } = null!;
    public int SequenceNumber { get; set; } // row sequence within the batch
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
    public string? InvoiceNo { get; set; }
    public decimal AppliedAmount { get; set; }
    public bool WasAutoMatched { get; set; }
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
