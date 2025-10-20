using Microsoft.Data.SqlClient;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using CashBatch.Application;
using CashBatch.Domain;

namespace CashBatch.Infrastructure.Services
{
    public class MatchingService : IMatchingService
    {
        private readonly IDbContextFactory<AppDbContext> _factory;
        private readonly IConfiguration _cfg;
        private readonly ILogger<MatchingService> _log;

        public MatchingService(IDbContextFactory<AppDbContext> factory, IConfiguration cfg, ILogger<MatchingService> log)
        {
            _factory = factory;
            _cfg = cfg;
            _log = log;
        }

        // Main entry point called from UI
        public async Task AutoApplyAsync(Guid batchId)
        {
            _log.LogInformation("Auto-apply started for batch {BatchId}", batchId);

            await using var db = await _factory.CreateDbContextAsync();

            var payments = await db.Payments
                .Where(p => p.BatchId == batchId && (p.Status == PaymentStatus.Imported || p.Status == PaymentStatus.NeedsReview))
                .Include(p => p.Lines)
                .ToListAsync();

            var importedCount = payments.Count(p => p.Status == PaymentStatus.Imported);
            var needsCount = payments.Count(p => p.Status == PaymentStatus.NeedsReview);
            _log.LogInformation("Found {Imported} imported and {Needs} needs-review payments to process", importedCount, needsCount);

            await using var tx = await db.Database.BeginTransactionAsync();

            foreach (var p in payments)
            {
                try
                {
                    _log.LogInformation("Processing payment {PaymentId} Check#{Check} Amount={Amount} CustomerId={CustomerId}", p.Id, p.CheckNumber, p.Amount, p.CustomerId ?? "<null>");

                    if (string.IsNullOrEmpty(p.CustomerId))
                        p.CustomerId = await ResolveCustomerAsync(db, p);

                    if (string.IsNullOrEmpty(p.CustomerId))
                    {
                        _log.LogWarning("No CustomerId for payment {PaymentId}, marking NeedsReview", p.Id);
                        p.Status = PaymentStatus.NeedsReview;
                        await db.SaveChangesAsync();
                        continue;
                    }

                    _log.LogInformation("Querying open invoices for CustomerId={CustomerId}", p.CustomerId);
                    var invoices = await GetOpenInvoicesAsync(p.CustomerId);
                    var invCount = invoices.Count();
                    _log.LogInformation("Retrieved {Count} open invoices for CustomerId={CustomerId}", invCount, p.CustomerId);
                    var match = TryFindExactMatch(invoices, p.Amount);

                    if (match != null)
                    {
                        _log.LogInformation("Found exact match with {Count} invoices totaling {Total}", match.Count, match.Sum(x => x.Item2));

                        // Delete any existing lines for this payment directly in SQL to avoid per-row concurrency checks
                        await db.Database.ExecuteSqlRawAsync(
                            "DELETE FROM [dbo].[cash_payment_lines] WHERE [PaymentId] = {0}", p.Id);

                        // Detach any tracked line entities for this payment so EF doesn't attempt second deletes/updates
                        var trackedLines = db.ChangeTracker.Entries<PaymentLine>()
                            .Where(e => e.Entity.PaymentId == p.Id)
                            .ToList();
                        foreach (var tl in trackedLines) tl.State = EntityState.Detached;

                        var newLines = match.Select(m => new PaymentLine
                        {
                            PaymentId = p.Id,
                            InvoiceNo = m.Item1.InvoiceNo,
                            AppliedAmount = m.Item2,
                            WasAutoMatched = true
                        }).ToList();

                        // Insert lines explicitly via DbSet to guarantee tracking and persistence
                        await db.PaymentLines.AddRangeAsync(newLines);

                        // Keep navigation in sync for in-memory usage
                        p.Lines.Clear();
                        foreach (var nl in newLines) p.Lines.Add(nl);

                        p.Status = PaymentStatus.AutoApplied;
                        db.Entry(p).Property(x => x.Status).IsModified = true;
                        if (!string.IsNullOrEmpty(p.CustomerId))
                            db.Entry(p).Property(x => x.CustomerId!).IsModified = true;
                    }
                    else
                    {
                        _log.LogInformation("No exact match for payment {PaymentId}, marking NeedsReview", p.Id);
                        p.Status = PaymentStatus.NeedsReview;
                    }

                    _log.LogInformation("Saving payment {PaymentId} with Status={Status} and {LineCount} line(s)", p.Id, p.Status, p.Lines.Count);
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException cex)
                {
                    _log.LogWarning(cex, "Concurrency conflict applying payment {PaymentId}. Will mark NeedsReview and continue.", p.Id);
                    try
                    {
                        db.ChangeTracker.Clear();
                    }
                    catch { }
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Error matching payment {CheckNo}", p.CheckNumber);
                }
            }

            await tx.CommitAsync();
            _log.LogInformation("Auto-apply finished for batch {BatchId}", batchId);
        }

        private async Task<string?> ResolveCustomerAsync(AppDbContext db, Payment p)
        {
            // Use lookups from the CustomerLookups table
            // Prefer composite BankNumber|AccountNumber
            var composite = (p.BankNumber ?? string.Empty) + "|" + (p.AccountNumber ?? p.BankAccount ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(composite.Trim('|')))
            {
                var viaComposite = await db.CustomerLookups.FirstOrDefaultAsync(l => l.KeyType == "BankRouteAcct" && l.KeyValue == composite);
                if (viaComposite != null) return viaComposite.CustomerId;
            }
            var viaAcct = await db.CustomerLookups.FirstOrDefaultAsync(l => l.KeyType == "BankAcct" && l.KeyValue == p.BankAccount);
            if (viaAcct != null) return viaAcct.CustomerId;
            var viaAddr = await db.CustomerLookups.FirstOrDefaultAsync(l => l.KeyType == "AddrHash" && l.KeyValue == p.RemitAddressHash);
            return viaAddr?.CustomerId;
        }

        private sealed record InvoiceInfo(string InvoiceNo, decimal AmountRemaining, decimal FreightAllowedAmt, DateTime? NetDueDate);

        private async Task<IEnumerable<InvoiceInfo>> GetOpenInvoicesAsync(string customerId)
        {
            // Prophet21 (ReadDb) stored procedure: jbi_sp_cash_batch_open_invoices
            var connStr = _cfg.GetConnectionString("ReadDb")!;
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            _log.LogInformation("Calling SP jbi_sp_cash_batch_open_invoices for CustomerId={CustomerId}", customerId);

            async Task<IEnumerable<dynamic>> CallWithParamAsync(string paramName)
            {
                var dp = new DynamicParameters();
                // Dapper accepts names with or without @, standardize without
                dp.Add(paramName.TrimStart('@'), customerId, System.Data.DbType.String);
                return await conn.QueryAsync(
                    "jbi_sp_cash_batch_open_invoices",
                    dp,
                    commandType: System.Data.CommandType.StoredProcedure);
            }

            var paramCandidates = new[] { "CustomerId", "CustomerNo", "Customer", "CustomerNumber", "cust_no", "custnum", "cust_id", "customer_id" };
            IEnumerable<dynamic> rows = Array.Empty<dynamic>();
            Exception? lastEx = null;
            foreach (var name in paramCandidates)
            {
                try
                {
                    rows = await CallWithParamAsync(name);
                    _log.LogInformation("SP call succeeded with parameter name '{ParamName}'", name);
                    break;
                }
                catch (SqlException ex)
                {
                    lastEx = ex;
                    // Try next candidate if the error indicates missing or mismatched parameter
                    _log.LogDebug(ex, "SP call failed with parameter name '{ParamName}', trying next candidate...", name);
                    continue;
                }
            }
            if (!rows.Any() && lastEx != null)
            {
                _log.LogWarning(lastEx, "SP jbi_sp_cash_batch_open_invoices returned no rows for CustomerId={CustomerId} using tried parameter names: {Names}", customerId, string.Join(", ", paramCandidates));
            }

            // Robust field extraction (case-insensitive, supports common aliases)
            var list = new List<InvoiceInfo>();

            string? TryGetString(IDictionary<string, object> d, params string[] names)
            {
                foreach (var n in names)
                {
                    var kv = d.FirstOrDefault(k => string.Equals(k.Key, n, StringComparison.OrdinalIgnoreCase));
                    if (!kv.Equals(default(KeyValuePair<string, object>)))
                        return Convert.ToString(kv.Value);
                }
                return null;
            }

            bool TryGetDecimal(IDictionary<string, object> d, out decimal value, params string[] names)
            {
                foreach (var n in names)
                {
                    var kv = d.FirstOrDefault(k => string.Equals(k.Key, n, StringComparison.OrdinalIgnoreCase));
                    if (!kv.Equals(default(KeyValuePair<string, object>)))
                    {
                        try
                        {
                            value = kv.Value switch
                            {
                                decimal dec => dec,
                                double dbl => Convert.ToDecimal(dbl),
                                float flt => Convert.ToDecimal(flt),
                                int i => i,
                                long l => l,
                                string s => Convert.ToDecimal(s, System.Globalization.CultureInfo.InvariantCulture),
                                _ => Convert.ToDecimal(kv.Value)
                            };
                            // Normalize to 2 decimals to compare with payment amounts
                            value = Math.Round(value, 2, MidpointRounding.AwayFromZero);
                            return true;
                        }
                        catch { }
                    }
                }
                value = 0m; return false;
            }

            DateTime? TryGetDate(IDictionary<string, object> d, params string[] names)
            {
                foreach (var n in names)
                {
                    var kv = d.FirstOrDefault(k => string.Equals(k.Key, n, StringComparison.OrdinalIgnoreCase));
                    if (!kv.Equals(default(KeyValuePair<string, object>)))
                    {
                        try
                        {
                            return kv.Value switch
                            {
                                DateTime dt => dt,
                                string s when DateTime.TryParse(s, out var dt2) => dt2,
                                _ => Convert.ToDateTime(kv.Value)
                            };
                        }
                        catch { }
                    }
                }
                return null;
            }

            foreach (var row in rows)
            {
                if (row is IDictionary<string, object> dict)
                {
                    var inv = TryGetString(dict, "invoice_no", "invoice", "invoice_num", "invoice_number", "inv_no", "InvoiceNo", "InvoiceNumber");
                    if (TryGetDecimal(dict, out var amt, "amount_remaining", "open_amount", "balance", "amount_due", "balance_remaining", "amount_open", "AmountRemaining"))
                    {
                        if (!string.IsNullOrWhiteSpace(inv))
                        {
                            TryGetDecimal(dict, out var freight, "freight_allowed_amt", "freight_allowed", "freight", "FreightAllowedAmt");
                            var due = TryGetDate(dict, "net_due_date", "net_due", "due_date", "NetDueDate");
                            list.Add(new InvoiceInfo(inv!, amt, Math.Max(freight, 0m), due));
                        }
                    }
                }
                else
                {
                    // Fallback: dynamic property access with common aliases
                    try
                    {
                        dynamic d = row;
                        string inv = (d.invoice_no ?? d.invoice ?? d.invoice_num ?? d.invoice_number ?? d.inv_no ?? d.InvoiceNo ?? d.InvoiceNumber).ToString();
                        decimal amt = (decimal)(d.amount_remaining ?? d.open_amount ?? d.balance ?? d.amount_due ?? d.balance_remaining ?? d.amount_open ?? d.AmountRemaining);
                        amt = Math.Round(amt, 2, MidpointRounding.AwayFromZero);
                        decimal freight = 0m;
                        try { freight = (decimal)(d.freight_allowed_amt ?? d.freight_allowed ?? d.freight ?? d.FreightAllowedAmt ?? 0m); } catch { }
                        freight = Math.Max(Math.Round(freight, 2, MidpointRounding.AwayFromZero), 0m);
                        DateTime? due = null;
                        try { due = (DateTime?)(d.net_due_date ?? d.net_due ?? d.due_date ?? d.NetDueDate); } catch { }
                        list.Add(new InvoiceInfo(inv, amt, freight, due));
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "Unable to parse SP row for customer {CustomerId}", customerId);
                    }
                }
            }

            if (list.Count == 0)
            {
                _log.LogWarning("SP jbi_sp_cash_batch_open_invoices returned no parseable rows for CustomerId={CustomerId}", customerId);
            }
            else
            {
                // Log a few examples for diagnostics
                foreach (var exRow in list.Take(5))
                    _log.LogInformation("Invoice candidate: {Inv} AmountRemaining={Amt} FreightAllowed={Freight}", exRow.InvoiceNo, exRow.AmountRemaining, exRow.FreightAllowedAmt);
            }

            return list;
        }

        // Try to find exact match allowing each invoice to be applied as either AmountRemaining OR AmountRemaining - FreightAllowedAmt.
        // This supports mixed combinations per-invoice.
        private List<(InvoiceInfo Info, decimal AppliedAmount)>? TryFindExactMatch(
            IEnumerable<InvoiceInfo> invoices, decimal amount)
        {
            const decimal epsilon = 0.01m; // accept 1-cent differences
            amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);

            var items = invoices
                .Select(x => new
                {
                    Info = x,
                    Full = Math.Round(x.AmountRemaining, 2, MidpointRounding.AwayFromZero),
                    Less = Math.Round(Math.Max(x.AmountRemaining - x.FreightAllowedAmt, 0m), 2, MidpointRounding.AwayFromZero)
                })
                .Select(x => new
                {
                    x.Info,
                    Full = x.Full,
                    Less = x.Less,
                    HasLess = x.Less > 0m && Math.Abs(x.Less - x.Full) > epsilon,
                    Max = Math.Max(x.Full, x.Less),
                    Due = x.Info.NetDueDate ?? DateTime.MaxValue
                })
                .OrderByDescending(i => i.Max)
                .ThenBy(i => i.Due)
                .ThenBy(i => i.Info.InvoiceNo)
                .ToList();

            if (items.Count == 0) return null;

            // 1) Single-invoice check (either choice)
            foreach (var it in items)
            {
                if (Math.Abs(it.Full - amount) <= epsilon) return new() { (it.Info, it.Full) };
                if (it.HasLess && Math.Abs(it.Less - amount) <= epsilon) return new() { (it.Info, it.Less) };
            }

            // Cap the search set for combinatorial steps
            var capped = items.Take(28).ToList();

            // 2) Pair scan (mixing choices per-invoice)
            for (int i = 0; i < capped.Count; i++)
            {
                var a = capped[i];
                var aVals = a.HasLess ? new[] { a.Full, a.Less } : new[] { a.Full };
                for (int j = i + 1; j < capped.Count; j++)
                {
                    var b = capped[j];
                    var bVals = b.HasLess ? new[] { b.Full, b.Less } : new[] { b.Full };
                    foreach (var av in aVals)
                    foreach (var bv in bVals)
                    {
                        var sum2 = av + bv;
                        if (Math.Abs(sum2 - amount) <= epsilon)
                            return new() { (a.Info, av), (b.Info, bv) };
                    }
                }
            }

            // 3) Greedy heuristic: choose per-invoice value closer to remaining target, include if doesn't overshoot
            {
                var sorted = capped
                    .OrderByDescending(i => i.Max)
                    .ThenBy(i => i.Due)
                    .ThenBy(i => i.Info.InvoiceNo)
                    .ToList();
                var remaining = amount;
                var res = new List<(InvoiceInfo, decimal)>();
                foreach (var it in sorted)
                {
                    var choice = it.Full;
                    if (it.HasLess)
                    {
                        var dFull = Math.Abs(remaining - it.Full);
                        var dLess = Math.Abs(remaining - it.Less);
                        if (Math.Abs(dLess - dFull) <= 0.00001m)
                        {
                            // tie-breaker: prefer the smaller choice to avoid overshoot and keep room for others
                            choice = Math.Min(it.Full, it.Less);
                        }
                        else
                        {
                            choice = dLess < dFull ? it.Less : it.Full;
                        }
                    }
                    if (choice <= remaining + epsilon && choice > 0)
                    {
                        res.Add((it.Info, choice));
                        remaining -= choice;
                        if (Math.Abs(remaining) <= epsilon)
                            return res;
                    }
                }
            }

            // 4) Bounded DFS with pruning by optimistic bound
            int n = capped.Count;
            var order = capped; // already sorted by Max desc
            var suffixMax = new decimal[n + 1];
            for (int i = n - 1; i >= 0; i--) suffixMax[i] = suffixMax[i + 1] + order[i].Max;

            List<(InvoiceInfo, decimal)>? best = null;
            var current = new List<(InvoiceInfo, decimal)>();

            void Dfs(int idx, decimal sum)
            {
                if (best != null) return; // stop at first exact match
                if (Math.Abs(sum - amount) <= epsilon) { best = new(current); return; }
                if (idx == n) return;
                if (sum > amount + epsilon) return;
                if (sum + suffixMax[idx] < amount - epsilon) return; // even with all remaining max we can't reach target

                var it = order[idx];

                // Option 1: include full
                if (it.Full > 0)
                {
                    current.Add((it.Info, it.Full));
                    Dfs(idx + 1, sum + it.Full);
                    current.RemoveAt(current.Count - 1);
                    if (best != null) return;
                }

                // Option 2: include less (if available and different)
                if (it.HasLess)
                {
                    current.Add((it.Info, it.Less));
                    Dfs(idx + 1, sum + it.Less);
                    current.RemoveAt(current.Count - 1);
                    if (best != null) return;
                }

                // Option 3: skip
                Dfs(idx + 1, sum);
            }

            // Only run DFS when the set isn't huge
            if (n <= 24)
            {
                Dfs(0, 0m);
                if (best != null) return best;
            }

            return null;
        }
    }
}
