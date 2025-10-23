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
        var rows = await db.Batches.AsNoTracking()
            .OrderByDescending(b => b.ImportedAt)
            .Take(take)
            .Select(b => new { b.Id, b.BatchName, b.ImportedAt, b.ImportedBy, b.SourceFilename, StatusNum = (int)b.Status, b.TemplateId })
            .ToListAsync();
        return rows
            .Select(b => new BatchDto(b.Id, b.BatchName, b.ImportedAt, b.ImportedBy, b.SourceFilename, b.StatusNum == 0 ? "Open" : b.StatusNum == 1 ? "Closed" : b.StatusNum.ToString(), b.TemplateId))
            .ToList();
    }

    public async Task<IReadOnlyList<BatchDto>> GetRecentByStatusAsync(string status, int take = 100)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var isClosed = string.Equals(status, "Closed", StringComparison.OrdinalIgnoreCase);
        var rows = await db.Batches.AsNoTracking()
            .Where(b => (int)b.Status == (isClosed ? 1 : 0))
            .OrderByDescending(b => b.ImportedAt)
            .Take(take)
            .Select(b => new { b.Id, b.BatchName, b.ImportedAt, b.ImportedBy, b.SourceFilename, StatusNum = (int)b.Status, b.TemplateId })
            .ToListAsync();
        return rows
            .Select(b => new BatchDto(b.Id, b.BatchName, b.ImportedAt, b.ImportedBy, b.SourceFilename, b.StatusNum == 0 ? "Open" : b.StatusNum == 1 ? "Closed" : b.StatusNum.ToString(), b.TemplateId))
            .ToList();
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
                p.OrderNumber,
                p.TransactionType,
                p.Category,
                p.TransactionDate,
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
                p.OrderNumber,
                p.TransactionType,
                p.Category,
                p.TransactionDate,
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

    public async Task<string?> GetPossibleCustomerIdAsync(string invoiceNumber)
    {
        try
        {
            var connStr = _cfg.GetConnectionString("ReadDb");
            if (string.IsNullOrWhiteSpace(connStr)) return null;

            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var dp = new DynamicParameters();
            dp.Add("InvoiceNo", invoiceNumber, System.Data.DbType.AnsiString);

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

    public async Task<CustomerLookupInfo?> GetCustomerLookupAsync(string invoiceNumber)
    {
        try
        {
            var connStr = _cfg.GetConnectionString("ReadDb");
            if (string.IsNullOrWhiteSpace(connStr)) return null;

            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var dp = new DynamicParameters();
            dp.Add("InvoiceNo", invoiceNumber, System.Data.DbType.AnsiString);

            var rows = await conn.QueryAsync("jbi_sp_cash_batch_customer_lookup", dp, commandType: System.Data.CommandType.StoredProcedure);
            var first = rows.FirstOrDefault();
            if (first == null) return null;

            string? custId = null;
            string? name = null;
            string? mailCity = null;

            if (first is IDictionary<string, object> dict)
            {
                string? GetStr(params string[] names)
                {
                    foreach (var n in names)
                    {
                        var kv = dict.FirstOrDefault(k => string.Equals(k.Key, n, StringComparison.OrdinalIgnoreCase));
                        if (!kv.Equals(default(KeyValuePair<string, object>)))
                            return Convert.ToString(kv.Value);
                    }
                    return null;
                }
                custId = GetStr("customer_id", "CustomerId", "cust_id", "custno", "cust_no");
                name = GetStr("name", "customer_name", "cust_name");
                mailCity = GetStr("mail_city", "city", "mailcity");
            }
            else
            {
                try
                {
                    dynamic d = first;
                    try { custId = (string?)(d.customer_id ?? d.CustomerId ?? d.cust_id ?? d.custno ?? d.cust_no); } catch { }
                    try { name = (string?)(d.name ?? d.customer_name ?? d.cust_name); } catch { }
                    try { mailCity = (string?)(d.mail_city ?? d.city ?? d.mailcity); } catch { }
                }
                catch { }
            }

            if (string.IsNullOrWhiteSpace(custId) && string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(mailCity))
                return null;

            return new CustomerLookupInfo(
                string.IsNullOrWhiteSpace(custId) ? null : custId,
                string.IsNullOrWhiteSpace(name) ? null : name,
                string.IsNullOrWhiteSpace(mailCity) ? null : mailCity);
        }
        catch
        {
            return null;
        }
    }

    public async Task CloseBatchesAsync(IEnumerable<Guid> batchIds)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var ids = batchIds.Distinct().ToList();
        if (ids.Count == 0) return;
        var rows = await db.Batches.Where(b => ids.Contains(b.Id)).ToListAsync();
        foreach (var b in rows)
        {
            // Set to numeric 1 (Closed)
            b.Status = (BatchStatus)1;
        }
        await db.SaveChangesAsync();
    }
}
