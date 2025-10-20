using CashBatch.Application;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CashBatch.Reporting;

public class BatchSummaryDocument : IDocument
{
    private readonly BatchDto _batch;
    private readonly IReadOnlyList<PaymentDto> _payments;

    public BatchSummaryDocument(BatchDto batch, IReadOnlyList<PaymentDto> payments)
    { _batch = batch; _payments = payments; }

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Margin(20);
            page.Header().Text($"CashBatch Summary - {_batch.SourceFilename}").SemiBold().FontSize(18);
            page.Content().Table(t =>
            {
                t.ColumnsDefinition(c => { c.ConstantColumn(140); c.RelativeColumn(); c.ConstantColumn(90); c.ConstantColumn(120); });
                t.Header(h =>
                {
                    h.Cell().Text("Check #").Bold();
                    h.Cell().Text("Customer");
                    h.Cell().Text("Amount");
                    h.Cell().Text("Status");
                });
                foreach (var p in _payments)
                {
                    t.Cell().Text(p.CheckNumber);
                    t.Cell().Text(p.CustomerId ?? "(unknown)");
                    t.Cell().Text(p.Amount.ToString("C"));
                    t.Cell().Text(p.Status);
                }
            });
            page.Footer().AlignRight().Text(x => x.Span($"Printed {DateTime.Now:g}"));
        });
    }
}
