using CashBatch.Application;
using CashBatch.Domain;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;

namespace CashBatch.Infrastructure.Services;

public class ImportService : IImportService
{
    private readonly AppDbContext _db;
    private readonly ILogger<ImportService> _log;
    public ImportService(AppDbContext db, ILogger<ImportService> log) { _db = db; _log = log; }

    public async Task<BatchDto> ImportAsync(string filePath, string importedBy, string? depositNumber)
    {
        var batch = new Batch { ImportedAt = DateTime.Now, ImportedBy = importedBy, SourceFilename = Path.GetFileName(filePath), DepositNumber = string.IsNullOrWhiteSpace(depositNumber) ? null : depositNumber };
        _db.Batches.Add(batch);

        using var reader = new StreamReader(filePath);
        var cfg = new CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = null,
            DetectColumnCountChanges = false,
            IgnoreBlankLines = true,
            TrimOptions = TrimOptions.Trim,
            PrepareHeaderForMatch = args => args.Header?.Trim()
        };
        using var csv = new CsvReader(reader, cfg);

        // Skip possible prelude rows until we hit the header (starts with "Transaction Type")
        while (await csv.ReadAsync())
        {
            _ = csv.TryGetField(0, out string? first);
            if (string.Equals(first, "Transaction Type", StringComparison.OrdinalIgnoreCase))
            {
                csv.ReadHeader();
                break;
            }
        }

        while (await csv.ReadAsync())
        {
            // Skip summary/footer rows like: Transaction Count,60,Total Amount,"59,617.11"
            _ = csv.TryGetField(0, out string? firstCell);
            if (!string.IsNullOrEmpty(firstCell) && firstCell.StartsWith("Transaction Count", StringComparison.OrdinalIgnoreCase))
                break;

            // Read fields by header names from the sample provided (use TryGetField to be safe)
            csv.TryGetField<string>("Transaction Type", out var txnType);
            csv.TryGetField<string>("Deposit Date", out var depositDateStr);
            csv.TryGetField<string>("Customer Batch Number", out var custBatchNum);
            csv.TryGetField<int>("Sequence Number", out var seq);
            csv.TryGetField<string>("Bank Number", out var bankNum);
            csv.TryGetField<string>("Account Number", out var acctNum);
            csv.TryGetField<string>("Check Number", out var checkNo);
            csv.TryGetField<string>("Remitter Name", out var remitter);
            csv.TryGetField<string>("City", out var city);
            csv.TryGetField<string>("Invoice Number", out var invoiceNumberStr);
            csv.TryGetField<string>("Category", out var category);
            csv.TryGetField<string>("Customer Number", out var customerNo);
            csv.TryGetField<decimal>("Invoice Amount", out var amount);

            if (batch.DepositDate is null && DateTime.TryParse(depositDateStr, out var dep))
                batch.DepositDate = dep;
            if (string.IsNullOrEmpty(batch.CustomerBatchNumber))
                batch.CustomerBatchNumber = custBatchNum;

            var originalCustomer = customerNo; // keep raw from CSV including "0"
            var resolvedCustomer = (string.IsNullOrWhiteSpace(customerNo) || customerNo == "0") ? null : customerNo;

            // Keep BankAccount mirrored from account number for backward compatibility
            var bankAccountMirror = string.IsNullOrWhiteSpace(acctNum) ? null : acctNum;

            var p = new Payment
            {
                Batch = batch,
                SequenceNumber = seq,
                TransactionType = string.IsNullOrWhiteSpace(txnType) ? null : txnType,
                BankNumber = string.IsNullOrWhiteSpace(bankNum) ? null : bankNum,
                AccountNumber = string.IsNullOrWhiteSpace(acctNum) ? null : acctNum,
                BankAccount = bankAccountMirror,
                CheckNumber = checkNo ?? string.Empty,
                RemitterName = string.IsNullOrWhiteSpace(remitter) ? null : remitter,
                City = string.IsNullOrWhiteSpace(city) ? null : city,
                InvoiceNumber = int.TryParse(invoiceNumberStr, out var invNo) ? invNo : null,
                Category = string.IsNullOrWhiteSpace(category) ? null : category,
                OriginalCustomerId = string.IsNullOrWhiteSpace(originalCustomer) ? null : originalCustomer,
                CustomerId = resolvedCustomer,
                Amount = amount,
                RemitAddressHash = null
            };
            _db.Payments.Add(p);
        }
        await _db.SaveChangesAsync();
        return new BatchDto(batch.Id, batch.DepositNumber, batch.ImportedAt, batch.ImportedBy, batch.SourceFilename, batch.Status.ToString());
    }

    // No remit address in the provided CSV sample; keep placeholder for potential future use.
    private static string? HashAddr(CsvReader csv) => null;
}
