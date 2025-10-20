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

namespace CashBatch.Integration;

public class ERPExportService : IERPExportService
{
    private readonly AppDbContext _db;
    private readonly ILogger<ERPExportService> _log;
    private readonly IConfiguration _cfg;

    public ERPExportService(AppDbContext db, ILogger<ERPExportService> log, IConfiguration cfg)
    { _db = db; _log = log; _cfg = cfg; }

    public async Task<int> ExportAutoAppliedAsync(Guid batchId, string pickupShare)
    {
        var payments = await _db.Payments
            .Where(p => p.BatchId == batchId && p.Status == PaymentStatus.AutoApplied)
            .Include(p => p.Lines)
            .ToListAsync();

        Directory.CreateDirectory(pickupShare);

        int count = 0;
        foreach (var p in payments)
        {
            var file = Path.Combine(pickupShare, $"PAY_{p.CheckNumber}_{p.Id:N}.txt");
            await File.WriteAllTextAsync(file, RenderPayment(p));
            p.Status = PaymentStatus.Exported;
            count++;
        }
        await _db.SaveChangesAsync();
        return count;
    }

    private static string RenderPayment(Payment p)
    {
        // TODO: shape to ERP’s exact spec
        var lines = new List<string> {
            $"CHECK|{p.CheckNumber}|{p.CustomerId}|{p.Amount}"
        };
        lines.AddRange(p.Lines.Select(l => $"APPLY|{l.InvoiceNo}|{l.AppliedAmount}"));
        return string.Join(Environment.NewLine, lines);
    }
}
