using CashBatch.Application;
using Microsoft.EntityFrameworkCore;

namespace CashBatch.Infrastructure.Services;

public class LookupService : ILookupService
{
    private readonly AppDbContext _db;

    public LookupService(AppDbContext db) => _db = db;

    public async Task UpsertAsync(string keyType, string keyValue, string customerId, double confidence = 1.0)
    {
        var cur = await _db.CustomerLookups.FirstOrDefaultAsync(x => x.KeyType == keyType && x.KeyValue == keyValue);
        if (cur == null)
        {
            _db.CustomerLookups.Add(new()
            {
                Id = Guid.NewGuid(),
                KeyType = keyType,
                KeyValue = keyValue,
                CustomerId = customerId,
                Confidence = confidence,
                CreatedAt = DateTime.UtcNow
            });
        }
        else
        {
            cur.CustomerId = customerId;
            cur.Confidence = confidence;
        }
        await _db.SaveChangesAsync();
    }

    public async Task<string?> ResolveCustomerAsync(string? bankAcct, string? addrHash)
    {
        if (!string.IsNullOrEmpty(bankAcct))
        {
            var viaAcct = await _db.CustomerLookups.FirstOrDefaultAsync(l => l.KeyType == "BankAcct" && l.KeyValue == bankAcct);
            if (viaAcct != null) return viaAcct.CustomerId;
        }
        if (!string.IsNullOrEmpty(addrHash))
        {
            var viaAddr = await _db.CustomerLookups.FirstOrDefaultAsync(l => l.KeyType == "AddrHash" && l.KeyValue == addrHash);
            if (viaAddr != null) return viaAddr.CustomerId;
        }
        return null;
    }

    public async Task<string?> ResolveCustomerAsync(string? bankNumber, string? accountNumber, string? bankAcct, string? addrHash)
    {
        var composite = string.IsNullOrWhiteSpace(bankNumber) && string.IsNullOrWhiteSpace(accountNumber)
            ? null
            : (bankNumber ?? "") + "|" + (accountNumber ?? "");
        if (!string.IsNullOrWhiteSpace(composite))
        {
            var viaComposite = await _db.CustomerLookups.FirstOrDefaultAsync(l => l.KeyType == "BankRouteAcct" && l.KeyValue == composite);
            if (viaComposite != null) return viaComposite.CustomerId;
        }
        return await ResolveCustomerAsync(bankAcct, addrHash);
    }

    public async Task<IReadOnlyList<LookupDto>> GetAllAsync()
    {
        return await _db.CustomerLookups
            .AsNoTracking()
            .OrderBy(l => l.KeyType)
            .ThenBy(l => l.KeyValue)
            .Select(l => new LookupDto(l.Id, l.KeyType, l.KeyValue, l.CustomerId, l.Confidence))
            .ToListAsync();
    }
}
