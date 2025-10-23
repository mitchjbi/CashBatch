namespace CashBatch.Domain;

public class CashTemplate
{
    public int TemplateId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    // File parsing parameters
    public string FileType { get; set; } = "CSV"; // CSV | FixedWidth | XLSX
    public bool HasHeaders { get; set; } = true;
    public int HeaderRowIndex { get; set; } = 1;     // 1-based
    public int DataStartRowIndex { get; set; } = 2;  // 1-based
    public string? Delimiter { get; set; } = ",";   // CSV
    public string? QuoteChar { get; set; } = "\""; // CSV
    public string? EscapeChar { get; set; } = "\\"; // CSV

    public string? Culture { get; set; }
    public string? DateFormats { get; set; }
    public string? Encoding { get; set; }
    public string? WorksheetName { get; set; }

    public bool IsActive { get; set; } = true;
    public string? CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedAtUtc { get; set; }

    public ICollection<CashTemplateDetail> Details { get; set; } = new List<CashTemplateDetail>();
}

public class CashTemplateDetail
{
    public int DetailId { get; set; }
    public int TemplateId { get; set; }
    public CashTemplate Template { get; set; } = null!;

    // Maps to cash_payments column/property name
    public string TargetField { get; set; } = string.Empty;

    // Source information (one of these or default value)
    public string? SourceHeader { get; set; }
    public int? SourceColumnIndex { get; set; } // 1-based when set

    // For FixedWidth files (future-safe)
    public int? FixedWidthStart { get; set; } // 1-based
    public int? FixedWidthLength { get; set; }

    public bool IsRequired { get; set; }
    public string? DefaultValue { get; set; }
    public string? Transform { get; set; }
    public string? ValidationRule { get; set; }
    public string? Notes { get; set; }
}
