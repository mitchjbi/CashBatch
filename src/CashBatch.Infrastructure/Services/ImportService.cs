using CashBatch.Application;
using CashBatch.Domain;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace CashBatch.Infrastructure.Services;

public class ImportService : IImportService
{
    private readonly AppDbContext _db;
    private readonly ILogger<ImportService> _log;
    public ImportService(AppDbContext db, ILogger<ImportService> log) { _db = db; _log = log; }

    public async Task<BatchDto> ImportAsync(string filePath, string importedBy, string? depositNumber, int? templateId)
    {
        if (templateId.HasValue)
        {
            return await ImportWithTemplateAsync(filePath, importedBy, depositNumber, templateId.Value);
        }
        // Normalize BatchName to fit DB constraints (nvarchar(50))
        string? bn = string.IsNullOrWhiteSpace(depositNumber) ? null : depositNumber.Trim();
        if (!string.IsNullOrEmpty(bn) && bn.Length > 50) bn = bn.Substring(0, 50);
        var batch = new Batch { ImportedAt = DateTime.Now, ImportedBy = importedBy, SourceFilename = Path.GetFileName(filePath), BatchName = bn };
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
            csv.TryGetField<string>("Order Number", out var orderNumberStr);
            csv.TryGetField<string>("Transaction Date", out var transactionDateStr);
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
                InvoiceNumber = string.IsNullOrWhiteSpace(invoiceNumberStr) ? null : invoiceNumberStr,
                OrderNumber = string.IsNullOrWhiteSpace(orderNumberStr) ? null : orderNumberStr,
                TransactionDate = DateTime.TryParse(transactionDateStr, out var txnDt) ? txnDt : null,
                Category = string.IsNullOrWhiteSpace(category) ? null : category,
                OriginalCustomerId = string.IsNullOrWhiteSpace(originalCustomer) ? null : originalCustomer,
                CustomerId = resolvedCustomer,
                Amount = amount,
                RemitAddressHash = null
            };
            _db.Payments.Add(p);
        }
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            _log.LogError(ex, "Failed to save imported batch. Inner: {Inner}", ex.InnerException?.Message);
            throw;
        }
        return new BatchDto(batch.Id, batch.BatchName, batch.ImportedAt, batch.ImportedBy, batch.SourceFilename, batch.Status.ToString(), batch.TemplateId);
    }

    // No remit address in the provided CSV sample; keep placeholder for potential future use.
    private static string? HashAddr(CsvReader csv) => null;

    private async Task<BatchDto> ImportWithTemplateAsync(string filePath, string importedBy, string? depositNumber, int templateId)
    {
        var template = await _db.CashTemplates.Include(t => t.Details).AsNoTracking().FirstOrDefaultAsync(t => t.TemplateId == templateId)
            ?? throw new InvalidOperationException($"Template {templateId} not found");

        if (!string.Equals(template.FileType, "CSV", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"FileType '{template.FileType}' not supported yet.");

        var culture = !string.IsNullOrWhiteSpace(template.Culture) ? new CultureInfo(template.Culture) : CultureInfo.InvariantCulture;
        var dateFormats = (template.DateFormats ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var batch = new Batch
        {
            ImportedAt = DateTime.Now,
            ImportedBy = importedBy,
            SourceFilename = Path.GetFileName(filePath),
            BatchName = string.IsNullOrWhiteSpace(depositNumber) ? null : depositNumber,
            TemplateId = templateId
        };
        _db.Batches.Add(batch);

        using var reader = new StreamReader(filePath, detectEncodingFromByteOrderMarks: true);
        var cfg = new CsvConfiguration(culture)
        {
            HasHeaderRecord = template.HasHeaders,
            MissingFieldFound = null,
            BadDataFound = null,
            DetectColumnCountChanges = false,
            IgnoreBlankLines = true,
            TrimOptions = TrimOptions.Trim,
            PrepareHeaderForMatch = args => args.Header?.Trim(),
            Delimiter = string.IsNullOrEmpty(template.Delimiter) ? "," : template.Delimiter,
            Quote = string.IsNullOrEmpty(template.QuoteChar) ? '"' : template.QuoteChar![0],
            Mode = CsvMode.RFC4180
        };
        using var csv = new CsvReader(reader, cfg);

        // Advance to header row and read header if present
        int currentRow = 0;
        while (await csv.ReadAsync())
        {
            currentRow++;
            if (currentRow == template.HeaderRowIndex)
            {
                if (template.HasHeaders)
                {
                    csv.ReadHeader();
                }
                break;
            }
        }

        // Move to the row just before the first data row (so the processing loop reads it first)
        while (currentRow < template.DataStartRowIndex - 1)
        {
            if (!await csv.ReadAsync()) break;
            currentRow++;
        }

        // Build a normalized header name -> index map for resilient matching
        var headerIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (template.HasHeaders && csv.HeaderRecord is not null)
        {
            for (int i = 0; i < csv.HeaderRecord.Length; i++)
            {
                var h = Normalize(csv.HeaderRecord[i]);
                if (!string.IsNullOrEmpty(h) && !headerIndex.ContainsKey(h))
                    headerIndex[h] = i;
            }
        }

        // Pre-calc details
        var details = template.Details.ToList();

        while (await csv.ReadAsync())
        {
            // Break on summary/footer like "Transaction Count" if present
            try
            {
                if (csv.TryGetField(0, out string? firstCell) && !string.IsNullOrEmpty(firstCell) && firstCell.StartsWith("Transaction Count", StringComparison.OrdinalIgnoreCase))
                    break;
            }
            catch { /* ignore */ }

            var p = new Payment
            {
                Batch = batch,
                Status = PaymentStatus.Imported
            };

            bool anyValue = false;
            foreach (var d in details)
            {
                string? raw = null;
                // Resolve value from source header or column index
                if (template.HasHeaders && !string.IsNullOrWhiteSpace(d.SourceHeader))
                {
                    var key = Normalize(d.SourceHeader!);
                    if (headerIndex.TryGetValue(key, out var col))
                    {
                        csv.TryGetField(col, out raw);
                    }
                    else
                    {
                        _log.LogWarning("Template header '{Header}' not found in file.", d.SourceHeader);
                    }
                }
                else if (d.SourceColumnIndex.HasValue)
                {
                    var idx = Math.Max(0, d.SourceColumnIndex.Value - 1);
                    csv.TryGetField(idx, out raw);
                }

                if (string.IsNullOrWhiteSpace(raw))
                    raw = d.DefaultValue;

                raw = raw?.Trim();

                if (d.IsRequired && string.IsNullOrWhiteSpace(raw))
                {
                    // Required missing; mark NeedsReview and continue row mapping
                    p.Status = PaymentStatus.NeedsReview;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(raw)) anyValue = true;

                ApplyField(d.TargetField, raw, p, batch, culture, dateFormats);
            }

            // For template-driven import, do not auto-mirror AccountNumber into BankAccount.
            // If a template explicitly maps BankAccount, it will be set via ApplyField.

            if (!anyValue)
                continue; // skip empty rows

            _db.Payments.Add(p);
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            _log.LogError(ex, "Failed to save imported batch (template). Inner: {Inner}", ex.InnerException?.Message);
            throw;
        }
        return new BatchDto(batch.Id, batch.BatchName, batch.ImportedAt, batch.ImportedBy, batch.SourceFilename, batch.Status.ToString(), batch.TemplateId);
        static string Normalize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            // remove BOM and non-breaking spaces, then trim, collapse spaces, lower
            var cleaned = s.Replace("\uFEFF", string.Empty).Replace('\u00A0', ' ');
            var trimmed = cleaned.Trim();
            var sb = new System.Text.StringBuilder(trimmed.Length);
            bool lastWasSpace = false;
            foreach (var ch in trimmed)
            {
                if (char.IsWhiteSpace(ch))
                {
                    if (!lastWasSpace) { sb.Append(' '); lastWasSpace = true; }
                }
                else { sb.Append(char.ToLowerInvariant(ch)); lastWasSpace = false; }
            }
            return sb.ToString();
        }
    }

    private static void ApplyField(string targetField, string? raw, Payment p, Batch batch, CultureInfo culture, string[] dateFormats)
    {
        if (string.IsNullOrWhiteSpace(targetField)) return;
        var name = targetField.Trim();

        bool TryParseDate(string s, out DateTime dt)
        {
            if (dateFormats.Length > 0)
                return DateTime.TryParseExact(s, dateFormats, culture, DateTimeStyles.None, out dt);
            return DateTime.TryParse(s, culture, DateTimeStyles.None, out dt);
        }

        switch (name)
        {
            // Batch-level fields
            case "DepositDate":
                if (!string.IsNullOrWhiteSpace(raw) && TryParseDate(raw, out var dep))
                    batch.DepositDate ??= dep;
                break;
            case "CustomerBatchNumber":
                if (!string.IsNullOrWhiteSpace(raw) && string.IsNullOrWhiteSpace(batch.CustomerBatchNumber)) batch.CustomerBatchNumber = raw;
                break;

            // Payment strings
            case "TransactionType": p.TransactionType = NullIfEmpty(raw); break;
            case "BankNumber": p.BankNumber = NullIfEmpty(raw); break;
            case "AccountNumber": p.AccountNumber = NullIfEmpty(raw); break;
            case "CheckNumber": p.CheckNumber = raw ?? string.Empty; break;
            case "RemitterName": p.RemitterName = NullIfEmpty(raw); break;
            case "City": p.City = NullIfEmpty(raw); break;
            case "Category": p.Category = NullIfEmpty(raw); break;
            case "OriginalCustomerId": p.OriginalCustomerId = NullIfEmpty(raw); break;
            case "CustomerId":
                var cust = NullIfEmpty(raw);
                p.OriginalCustomerId ??= cust;
                p.CustomerId = string.IsNullOrWhiteSpace(cust) || cust == "0" ? null : cust;
                break;

            // Payment numerics
            case "SequenceNumber":
                if (int.TryParse(raw, NumberStyles.Integer, culture, out var seq)) p.SequenceNumber = seq;
                break;
            case "InvoiceNumber":
                p.InvoiceNumber = NullIfEmpty(raw);
                break;
            case "OrderNumber":
                p.OrderNumber = NullIfEmpty(raw);
                break;
            case "TransactionDate":
                if (!string.IsNullOrWhiteSpace(raw) && TryParseDate(raw, out var trn)) p.TransactionDate = trn; else p.TransactionDate = null;
                break;
            case "Amount":
            case "InvoiceAmount":
                if (decimal.TryParse(raw, NumberStyles.Any, culture, out var amt)) p.Amount = amt;
                break;

            // Fallback: ignore unknown for now
            default:
                break;
        }

        static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
    }
}
