using CashBatch.Application;
using CashBatch.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using Dapper;

namespace CashBatch.Infrastructure.Services;

public class BatchService : IBatchService
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly ILogger<BatchService> _log;
    private readonly IConfiguration _cfg;
    private readonly ILookupService _lookup;

    public BatchService(IDbContextFactory<AppDbContext> factory, ILogger<BatchService> log, IConfiguration cfg, ILookupService lookup)
    {
        _factory = factory;
        _log = log;
        _cfg = cfg;
        _lookup = lookup;
    }

    public async Task<IReadOnlyList<BatchDto>> GetRecentAsync(int take = 50)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Batches.AsNoTracking()
            .OrderByDescending(b => b.ImportedAt)
            .Take(take)
            .Select(b => new BatchDto(b.Id, b.DepositNumber, b.ImportedAt, b.ImportedBy, b.SourceFilename, b.Status.ToString()))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<PaymentDto>> GetPaymentsAsync(Guid batchId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Payments.AsNoTracking()
            .Where(p => p.BatchId == batchId)
            .OrderBy(p => p.CheckNumber)
            .Select(p => new PaymentDto(
                p.Id,
                p.BatchId,
                p.CustomerId,
                p.Amount,
                p.CheckNumber,
                p.RemitterName,
                p.City,
                p.InvoiceNumber,
                p.BankNumber,
                p.AccountNumber,
                p.AccountNumber ?? p.BankAccount,
                p.RemitAddressHash,
                p.Status.ToString(),
                (p.OriginalCustomerId ?? string.Empty) != (p.CustomerId ?? string.Empty),
                p.OriginalCustomerId))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<PaymentDto>> GetNeedsReviewAsync(Guid batchId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Payments.AsNoTracking()
            .Where(p => p.BatchId == batchId && p.Status == PaymentStatus.NeedsReview)
            .OrderBy(p => p.CheckNumber)
            .Select(p => new PaymentDto(
                p.Id,
                p.BatchId,
                p.CustomerId,
                p.Amount,
                p.CheckNumber,
                p.RemitterName,
                p.City,
                p.InvoiceNumber,
                p.BankNumber,
                p.AccountNumber,
                p.AccountNumber ?? p.BankAccount,
                p.RemitAddressHash,
                p.Status.ToString(),
                (p.OriginalCustomerId ?? string.Empty) != (p.CustomerId ?? string.Empty),
                p.OriginalCustomerId))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<AppliedLineDto>> GetAppliedAsync(Guid paymentId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var payment = await db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == paymentId);
        if (payment == null)
        {
            return Array.Empty<AppliedLineDto>();
        }

        var lines = await db.PaymentLines.AsNoTracking()
            .Where(l => l.PaymentId == paymentId)
            .OrderBy(l => l.InvoiceNo)
            .ToListAsync();

        // Build lookup of ERP invoice info by InvoiceNo (case-insensitive)
        var infoByInv = new Dictionary<string, (DateTime? NetDueDate, decimal? AmountRemaining, decimal? FreightAllowedAmt, decimal? TermsAmount, string? BranchId)>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(payment.CustomerId))
        {
            try
            {
                var connStr = _cfg.GetConnectionString("ReadDb");
                if (!string.IsNullOrWhiteSpace(connStr))
                {
                    using var conn = new SqlConnection(connStr);
                    await conn.OpenAsync();
                    async Task<IEnumerable<dynamic>> CallWithParamAsync(string paramName)
                    {
                        var dp = new DynamicParameters();
                        dp.Add(paramName.TrimStart('@'), payment.CustomerId);
                        return await conn.QueryAsync("jbi_sp_cash_batch_open_invoices", dp, commandType: System.Data.CommandType.StoredProcedure);
                    }

                    var paramCandidates = new[] { "CustomerId", "CustomerNo", "Customer", "CustomerNumber", "cust_no", "custnum", "cust_id", "customer_id" };
                    IEnumerable<dynamic> rows = Array.Empty<dynamic>();
                    foreach (var name in paramCandidates)
                    {
                        try
                        {
                            rows = await CallWithParamAsync(name);
                            break;
                        }
                        catch { /* try next */ }
                    }
                    foreach (var row in rows)
                    {
                        try
                        {
                            var dict = row as IDictionary<string, object>;
                            string? inv = null;
                            DateTime? due = null;
                            decimal? rem = null;
                            decimal? freight = null;
                            decimal? terms = null;
                            string? branchId = null;
                            if (dict != null)
                            {
                                inv = dict.FirstOrDefault(k => string.Equals(k.Key, "invoice_no", StringComparison.OrdinalIgnoreCase) || string.Equals(k.Key, "InvoiceNo", StringComparison.OrdinalIgnoreCase) || string.Equals(k.Key, "invoice_number", StringComparison.OrdinalIgnoreCase)).Value?.ToString();
                                object? v;
                                v = dict.FirstOrDefault(k => string.Equals(k.Key, "net_due_date", StringComparison.OrdinalIgnoreCase) || string.Equals(k.Key, "NetDueDate", StringComparison.OrdinalIgnoreCase) || string.Equals(k.Key, "due_date", StringComparison.OrdinalIgnoreCase)).Value;
                                if (v != null) { if (v is DateTime dt) due = dt; else if (DateTime.TryParse(Convert.ToString(v), out var dt2)) due = dt2; }
                                v = dict.FirstOrDefault(k => string.Equals(k.Key, "amount_remaining", StringComparison.OrdinalIgnoreCase) || string.Equals(k.Key, "AmountRemaining", StringComparison.OrdinalIgnoreCase)).Value;
                                if (v != null) { rem = Convert.ToDecimal(v); }
                                v = dict.FirstOrDefault(k => string.Equals(k.Key, "freight_allowed_amt", StringComparison.OrdinalIgnoreCase) || string.Equals(k.Key, "FreightAllowedAmt", StringComparison.OrdinalIgnoreCase)).Value;
                                if (v != null) { freight = Convert.ToDecimal(v); }
                                v = dict.FirstOrDefault(k => string.Equals(k.Key, "terms_amount", StringComparison.OrdinalIgnoreCase) || string.Equals(k.Key, "TermsAmount", StringComparison.OrdinalIgnoreCase) || string.Equals(k.Key, "terms_amt", StringComparison.OrdinalIgnoreCase)).Value;
                                if (v != null) { terms = Convert.ToDecimal(v); }
                                v = dict.FirstOrDefault(k => string.Equals(k.Key, "branch_id", StringComparison.OrdinalIgnoreCase) || string.Equals(k.Key, "BranchId", StringComparison.OrdinalIgnoreCase) || string.Equals(k.Key, "branchid", StringComparison.OrdinalIgnoreCase) || string.Equals(k.Key, "branch", StringComparison.OrdinalIgnoreCase)).Value;
                                if (v != null) { branchId = Convert.ToString(v); if (string.IsNullOrWhiteSpace(branchId)) branchId = null; }
                            }
                            else
                            {
                                dynamic d = row;
                                inv = (d.invoice_no ?? d.InvoiceNo ?? d.invoice_number)?.ToString();
                                try { due = (DateTime?)(d.net_due_date ?? d.NetDueDate ?? d.due_date); } catch { }
                                try { rem = (decimal?)(d.amount_remaining ?? d.AmountRemaining); } catch { }
                                try { freight = (decimal?)(d.freight_allowed_amt ?? d.FreightAllowedAmt); } catch { }
                                try { terms = (decimal?)(d.terms_amount ?? d.TermsAmount ?? d.terms_amt); } catch { }
                                try { branchId = (string?)(d.branch_id ?? d.BranchId ?? d.branchid ?? d.branch); } catch { }
                                if (string.IsNullOrWhiteSpace(branchId)) branchId = null;
                            }
                            if (!string.IsNullOrWhiteSpace(inv) && !infoByInv.ContainsKey(inv!))
                                infoByInv[inv!] = (due, rem, freight, terms, branchId);
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Unable to enrich applied lines with ERP invoice info for PaymentId={PaymentId}", paymentId);
            }
        }

        var result = lines
            .Select(l =>
            {
                infoByInv.TryGetValue(l.InvoiceNo ?? string.Empty, out var info);
                return new AppliedLineDto(l.InvoiceNo, info.NetDueDate, info.AmountRemaining, info.FreightAllowedAmt, info.TermsAmount, info.BranchId ?? l.BranchId, l.AppliedAmount, l.WasAutoMatched);
            })
            .ToList();

        _log.LogInformation("Loaded {Count} applied line(s) for PaymentId={PaymentId}", result.Count, paymentId);
        return result;
    }

    public async Task AssignCustomerAsync(Guid paymentId, string customerId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var p = await db.Payments.FirstOrDefaultAsync(x => x.Id == paymentId);
        if (p == null) return;
        p.CustomerId = customerId;
        await db.SaveChangesAsync();

        var key = (p.BankNumber ?? "") + "|" + (p.AccountNumber ?? p.BankAccount ?? "");
        if (!string.IsNullOrWhiteSpace(key.Trim('|')))
        {
            await _lookup.UpsertAsync("BankRouteAcct", key, customerId, 1.0);
        }
    }

    public async Task<string?> GetPossibleCustomerIdAsync(int invoiceNumber)
    {
        try
        {
            var connStr = _cfg.GetConnectionString("ReadDb");
            if (string.IsNullOrWhiteSpace(connStr)) return null;

            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var dp = new DynamicParameters();
            dp.Add("InvoiceNo", invoiceNumber.ToString(), System.Data.DbType.AnsiString);

            var rows = await conn.QueryAsync("jbi_sp_cash_batch_customer_lookup", dp, commandType: System.Data.CommandType.StoredProcedure);
            var first = rows.FirstOrDefault();
            if (first == null) return null;

            if (first is IDictionary<string, object> dict)
            {
                var kv = dict.FirstOrDefault(k => string.Equals(k.Key, "customer_id", StringComparison.OrdinalIgnoreCase));
                if (!kv.Equals(default(KeyValuePair<string, object>)))
                {
                    var val = Convert.ToString(kv.Value);
                    return string.IsNullOrWhiteSpace(val) ? null : val;
                }
            }
            else
            {
                try
                {
                    dynamic d = first;
                    string? val = (string?)d.customer_id;
                    if (!string.IsNullOrWhiteSpace(val)) return val;
                }
                catch { }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
