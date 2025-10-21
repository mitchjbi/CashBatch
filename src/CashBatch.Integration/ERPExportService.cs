using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using CashBatch.Application;
using CashBatch.Domain;
using CashBatch.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace CashBatch.Integration;

public class ERPExportService : IERPExportService
{
    private readonly AppDbContext _db;
    private readonly ILogger<ERPExportService> _log;
    private readonly IConfiguration _cfg;

    public ERPExportService(AppDbContext db, ILogger<ERPExportService> log, IConfiguration cfg)
    { _db = db; _log = log; _cfg = cfg; }

    public async Task<int> ExportAutoAppliedAsync(Guid batchId, ExportOptions options)
    {
        // Load batch info as no-tracking to avoid poisoning the context with tracked state
        var batch = await _db.Batches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchId)
            ?? throw new InvalidOperationException("Batch not found.");
        var depositNumber = options.DepositNumber;

        // Important: clear any previously tracked entities so we fetch fresh CustomerId values
        // Some payments may have been updated in a different DbContext during Auto-Apply
        _db.ChangeTracker.Clear();

        var payments = await _db.Payments
            .Where(p => p.BatchId == batchId && p.Status == PaymentStatus.AutoApplied)
            .Include(p => p.Lines)
            .ToListAsync();

        Directory.CreateDirectory(options.ExportDirectory);

        // Build header rows (one per payment)
        var headerLines = new List<string>();
        // Build detail rows (one per line)
        var detailLines = new List<string>();

        string DateFmt(DateTime dt) => dt.ToString("MM/dd/yy", CultureInfo.InvariantCulture);
        string Amt(decimal d) => d.ToString("0.00", CultureInfo.InvariantCulture);
        static string ReplaceGlBranchSuffix(string gl, string? branchId)
        {
            if (string.IsNullOrWhiteSpace(gl)) return gl;
            if (string.IsNullOrWhiteSpace(branchId)) return gl;
            // BranchId is always two characters like "01", "02".
            var bid = branchId.Trim();
            if (bid.Length != 2) return gl;
            if (gl.Length < 2) return gl;
            return gl.Substring(0, gl.Length - 2) + bid;
        }

        foreach (var p in payments)
        {
            var terms = p.Lines.Sum(l => l.TermsTakenAmt ?? 0m);
            var allowed = p.Lines.Sum(l => l.FreightTakenAmt ?? 0m);

            // Determine BranchId to use for header GL accounts: always use the first invoice line's BranchId
            // If it's null/missing, use the default GL values from settings (no replacement).
            var firstLine = p.Lines.FirstOrDefault();
            var headerBranchId = firstLine?.BranchId;
            var arAcct = ReplaceGlBranchSuffix(options.ARAccountNumber, headerBranchId);
            var termsAcct = ReplaceGlBranchSuffix(options.TermsAccountNumber, headerBranchId);
            var allowedAcct = ReplaceGlBranchSuffix(options.AllowedAccountNumber, headerBranchId);

            // Header fields (tab-delimited, no title row)
            // Payment Type ID | Payment Description | Payment Date | Payment Amount | Terms Amount | Allowed Amount | Check Number | CC Name | CC Number | CC Exp Date | CC Auth Date | CC Auth Number | Period | Year for Period | Deposit Number | Receipt Number | Company ID | Remitter ID | Date Received | Bank Number | GL Bank Acct | AR Acct | Terms Acct | Allowed Acct | Approved | Current Variance Amount
            var header = string.Join('\t', new[]
            {
                "2",
                "CashBatch",
                DateFmt(DateTime.Now),
                Amt(p.Amount),
                Amt(terms),
                Amt(allowed),
                p.CheckNumber ?? string.Empty,
                string.Empty, // CC Name
                string.Empty, // CC Number
                string.Empty, // CC Exp Date
                string.Empty, // CC Auth Date
                string.Empty, // CC Auth Number
                options.Period.ToString(CultureInfo.InvariantCulture),
                options.FiscalYear.ToString(CultureInfo.InvariantCulture),
                depositNumber ?? string.Empty,
                p.PaymentNumber.ToString(CultureInfo.InvariantCulture),
                "1", // Company ID
                p.CustomerId ?? string.Empty,
                DateFmt(batch.ImportedAt),
                options.BankNumber,
                options.GLBankAccountNumber,
                arAcct,
                termsAcct,
                allowedAcct,
                "Y",
                Amt(0m)
            });
            headerLines.Add(header);

            // Detail lines for this payment
            foreach (var l in p.Lines)
            {
                var det = string.Join('\t', new[]
                {
                    l.PaymentNumber.ToString(CultureInfo.InvariantCulture), // Receipt Number
                    p.CustomerId ?? string.Empty, // Customer ID
                    l.InvoiceNo ?? string.Empty, // Invoice Number
                    "1", // Company ID
                    Amt(l.AppliedAmount), // Payment Amount
                    Amt(l.TermsTakenAmt ?? 0m), // Terms Amount
                    Amt(l.FreightTakenAmt ?? 0m), // Allowed Amount
                    Amt(0m) // Current Variance Amount
                });
                detailLines.Add(det);
            }

            // Mark exported
            p.Status = PaymentStatus.Exported;
        }

        // Write files (overwrite)
        var headerPath = Path.Combine(options.ExportDirectory, "CashReceipts.txt");
        var detailPath = Path.Combine(options.ExportDirectory, "CashReceiptLines.txt");
        await File.WriteAllLinesAsync(headerPath, headerLines);
        await File.WriteAllLinesAsync(detailPath, detailLines);

        var count = payments.Count;

        // Persist status changes and deposit number on the batch
        var batchToUpdate = await _db.Batches.FirstAsync(b => b.Id == batchId);
        batchToUpdate.DepositNumber = depositNumber;
        await _db.SaveChangesAsync();
        return count;
    }
}
