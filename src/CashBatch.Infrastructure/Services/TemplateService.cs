using CashBatch.Application;
using CashBatch.Domain;
using Microsoft.EntityFrameworkCore;

namespace CashBatch.Infrastructure.Services;

public class TemplateService : ITemplateService
{
    private readonly AppDbContext _db;
    public TemplateService(AppDbContext db) { _db = db; }

    public async Task<IReadOnlyList<CashTemplateDto>> GetAllAsync(bool onlyActive = true)
    {
        var query = _db.CashTemplates.Include(t => t.Details).AsNoTracking();
        if (onlyActive) query = query.Where(t => t.IsActive);
        var list = await query.OrderBy(t => t.Name).ToListAsync();
        return list.Select(Map).ToList();
    }

    public async Task<CashTemplateDto?> GetByIdAsync(int templateId)
    {
        var entity = await _db.CashTemplates.AsNoTracking().Include(t => t.Details).FirstOrDefaultAsync(t => t.TemplateId == templateId);
        return entity == null ? null : Map(entity);
    }

    private static CashTemplateDto Map(CashTemplate t)
    {
        return new CashTemplateDto(
            t.TemplateId,
            t.Name,
            t.Description,
            t.FileType,
            t.HasHeaders,
            t.HeaderRowIndex,
            t.DataStartRowIndex,
            t.Delimiter,
            t.QuoteChar,
            t.EscapeChar,
            t.Culture,
            t.DateFormats,
            t.Encoding,
            t.WorksheetName,
            t.IsActive,
            t.CreatedBy,
            t.CreatedAtUtc,
            t.ModifiedBy,
            t.ModifiedAtUtc,
            t.Details.OrderBy(d => d.TargetField).Select(d => new CashTemplateDetailDto(
                d.DetailId,
                d.TemplateId,
                d.TargetField,
                d.SourceHeader,
                d.SourceColumnIndex,
                d.FixedWidthStart,
                d.FixedWidthLength,
                d.IsRequired,
                d.DefaultValue,
                d.Transform,
                d.ValidationRule,
                d.Notes
            )).ToList()
        );
    }
}
