namespace CashBatch.Application;

public record BatchDto(Guid Id, string? BatchName, DateTime ImportedAt, string ImportedBy, string SourceFilename, string Status, int? TemplateId);
public record PaymentDto(
    Guid Id,
    Guid BatchId,
    string? CustomerId,
    decimal Amount,
    string CheckNumber,
    string? RemitterName,
    string? City,
    string? InvoiceNumber,
    string? OrderNumber,
    string? TransactionType,
    string? Category,
    DateTime? TransactionDate,
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
public record CustomerLookupInfo(string? CustomerId, string? Name, string? MailCity);

public record ExportOptions(
    string ExportDirectory,
    int FiscalYear,
    int Period,
    string BankNumber,
    string GLBankAccountNumber,
    string ARAccountNumber,
    string TermsAccountNumber,
    string AllowedAccountNumber,
    string BatchName);

// Template DTOs and service contract
public record CashTemplateDetailDto(
    int DetailId,
    int TemplateId,
    string TargetField,
    string? SourceHeader,
    int? SourceColumnIndex,
    int? FixedWidthStart,
    int? FixedWidthLength,
    bool IsRequired,
    string? DefaultValue,
    string? Transform,
    string? ValidationRule,
    string? Notes);

public record CashTemplateDto(
    int TemplateId,
    string Name,
    string? Description,
    string FileType,
    bool HasHeaders,
    int HeaderRowIndex,
    int DataStartRowIndex,
    string? Delimiter,
    string? QuoteChar,
    string? EscapeChar,
    string? Culture,
    string? DateFormats,
    string? Encoding,
    string? WorksheetName,
    bool IsActive,
    string? CreatedBy,
    DateTime CreatedAtUtc,
    string? ModifiedBy,
    DateTime? ModifiedAtUtc,
    IReadOnlyList<CashTemplateDetailDto> Details);

public interface ITemplateService
{
    Task<IReadOnlyList<CashTemplateDto>> GetAllAsync(bool onlyActive = true);
    Task<CashTemplateDto?> GetByIdAsync(int templateId);
}

public interface IImportService
{
    Task<BatchDto> ImportAsync(string filePath, string importedBy, string? depositNumber, int? templateId);
}

public interface IMatchingService
{
    Task AutoApplyAsync(Guid batchId);
}

public interface IBatchService
{
    Task<IReadOnlyList<BatchDto>> GetRecentAsync(int take = 100);
    Task<IReadOnlyList<BatchDto>> GetRecentByStatusAsync(string status, int take = 100);
    Task<IReadOnlyList<PaymentDto>> GetPaymentsAsync(Guid batchId);
    Task<IReadOnlyList<PaymentDto>> GetNeedsReviewAsync(Guid batchId);
    Task<IReadOnlyList<AppliedLineDto>> GetAppliedAsync(Guid paymentId);
    Task AssignCustomerAsync(Guid paymentId, string customerId);
    Task<string?> GetPossibleCustomerIdAsync(string invoiceNumber);
    Task<CustomerLookupInfo?> GetCustomerLookupAsync(string invoiceNumber);
    Task CloseBatchesAsync(IEnumerable<Guid> batchIds);
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
