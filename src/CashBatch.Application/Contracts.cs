namespace CashBatch.Application;

public record BatchDto(Guid Id, string? DepositNumber, DateTime ImportedAt, string ImportedBy, string SourceFilename, string Status);
public record PaymentDto(
    Guid Id,
    Guid BatchId,
    string? CustomerId,
    decimal Amount,
    string CheckNumber,
    string? RemitterName,
    string? City,
    int? InvoiceNumber,
    string? BankNumber,
    string? AccountNumber,
    string? BankAccount,
    string? RemitAddressHash,
    string Status,
    bool IsLookupCustomer,
    string? OriginalCustomerId);
public record AppliedLineDto(string? InvoiceNo, DateTime? NetDueDate, decimal? AmountRemaining, decimal? FreightAllowedAmt, decimal? TermsAmount, string? BranchId, decimal AppliedAmount, bool WasAutoMatched);
public record LookupDto(Guid Id, string KeyType, string KeyValue, string CustomerId, double Confidence);
public record LogDto(Guid Id, Guid PaymentId, string Level, string Message, DateTime CreatedAt);

public record ExportOptions(
    string ExportDirectory,
    int FiscalYear,
    int Period,
    string BankNumber,
    string GLBankAccountNumber,
    string ARAccountNumber,
    string TermsAccountNumber,
    string AllowedAccountNumber,
    string DepositNumber);

public interface IImportService
{
    Task<BatchDto> ImportAsync(string filePath, string importedBy, string? depositNumber);
}

public interface IMatchingService
{
    Task AutoApplyAsync(Guid batchId);
}

public interface IBatchService
{
    Task<IReadOnlyList<BatchDto>> GetRecentAsync(int take = 50);
    Task<IReadOnlyList<PaymentDto>> GetPaymentsAsync(Guid batchId);
    Task<IReadOnlyList<PaymentDto>> GetNeedsReviewAsync(Guid batchId);
    Task<IReadOnlyList<AppliedLineDto>> GetAppliedAsync(Guid paymentId);
    Task AssignCustomerAsync(Guid paymentId, string customerId);
    Task<string?> GetPossibleCustomerIdAsync(int invoiceNumber);
}

public interface ILookupService
{
    Task UpsertAsync(string keyType, string keyValue, string customerId, double confidence = 1.0);
    Task<string?> ResolveCustomerAsync(string? bankAcct, string? addrHash);
    Task<string?> ResolveCustomerAsync(string? bankNumber, string? accountNumber, string? bankAcct, string? addrHash);
    Task<IReadOnlyList<LookupDto>> GetAllAsync();
}

public interface IERPExportService
{
    Task<int> ExportAutoAppliedAsync(Guid batchId, ExportOptions options);
}
