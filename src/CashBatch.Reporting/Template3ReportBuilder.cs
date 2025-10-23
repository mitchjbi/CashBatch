using CashBatch.Application;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;

namespace CashBatch.Reporting;

public static class Template3ReportBuilder
{
    // Template 3: Based on Template 1 with these changes:
    // - Replace City with TransactionType titled "Type"
    // - Insert a new column after Type for AccountNumber titled "Account"
    // - Keep Amount title and values left-aligned
    public static byte[] Build(BatchDto batch, IReadOnlyList<PaymentDto> payments, string? templateName = null)
    {
        using var doc = new PdfDocument();
        doc.Info.Title = $"Batch Summary - {batch.BatchName}";

        const double inch = 72.0; // points per inch
        const double margin = 0.5 * inch; // 0.5 inch margin

        var page = doc.AddPage();
        page.Size = PdfSharpCore.PageSize.Letter;
        var gfx = XGraphics.FromPdfPage(page);

        var font = new XFont("Segoe UI", 8, XFontStyle.Regular);
        var bold = new XFont("Segoe UI", 8, XFontStyle.Bold);
        var titleBoldLarge = new XFont("Segoe UI", 14, XFontStyle.Bold);

        double y = margin;

        // Header labels
        gfx.DrawString("Batch Name:", bold, XBrushes.Black, new XPoint(margin, y));
        gfx.DrawString(batch.BatchName ?? string.Empty, font, XBrushes.Black, new XPoint(margin + 100, y));
        if (!string.IsNullOrWhiteSpace(templateName))
        {
            var tn = templateName!.Trim();
            var tsize = gfx.MeasureString(tn, titleBoldLarge);
            gfx.DrawString(tn, titleBoldLarge, XBrushes.Black, new XPoint(page.Width - margin - tsize.Width, y));
        }
        y += 18;
        gfx.DrawString("Date Created:", bold, XBrushes.Black, new XPoint(margin, y));
        gfx.DrawString(batch.ImportedAt.ToString(), font, XBrushes.Black, new XPoint(margin + 100, y));
        y += 24;

        // Table header function (repeat on new pages)
        var tableLeft = margin;
        var tableRight = page.Width - margin;
        var tableWidth = tableRight - tableLeft;

        // Column widths tuned to fit within the page
        var colWidths = new double[]
        {
            60,   // CustomerId
            120,  // RemitterName (wrap)
            60,   // Amount (left aligned)
            50,   // Invoice No
            80,   // Type (TransactionType)
            50,   // Account (AccountNumber)
            50,   // Detail ID
            60    // Status
        };
        var colStarts = new double[colWidths.Length];
        double x = tableLeft;
        for (int i = 0; i < colWidths.Length; i++) { colStarts[i] = x; x += colWidths[i]; }

        double rowHeight = 18; // base row height; will expand for wrap
        double headerHeight = 20;

        void DrawHeader()
        {
            // Background
            var rect = new XRect(tableLeft, y, tableWidth, headerHeight);
            gfx.DrawRectangle(XBrushes.WhiteSmoke, rect);
            // Lines
            gfx.DrawRectangle(XPens.LightGray, rect);

            // Text
            gfx.DrawString("Customer Id", bold, XBrushes.Black, new XRect(colStarts[0], y + 4, colWidths[0], headerHeight), XStringFormats.TopLeft);
            gfx.DrawString("Name", bold, XBrushes.Black, new XRect(colStarts[1], y + 4, colWidths[1], headerHeight), XStringFormats.TopLeft);
            gfx.DrawString("Amount", bold, XBrushes.Black, new XRect(colStarts[2], y + 4, colWidths[2], headerHeight), XStringFormats.TopLeft);
            gfx.DrawString("Invoice No", bold, XBrushes.Black, new XRect(colStarts[3], y + 4, colWidths[3], headerHeight), XStringFormats.TopLeft);
            gfx.DrawString("Type", bold, XBrushes.Black, new XRect(colStarts[4], y + 4, colWidths[4], headerHeight), XStringFormats.TopLeft);
            gfx.DrawString("Account", bold, XBrushes.Black, new XRect(colStarts[5], y + 4, colWidths[5], headerHeight), XStringFormats.TopLeft);
            gfx.DrawString("Detail ID", bold, XBrushes.Black, new XRect(colStarts[6], y + 4, colWidths[6], headerHeight), XStringFormats.TopLeft);
            gfx.DrawString("Status", bold, XBrushes.Black, new XRect(colStarts[7], y + 4, colWidths[7], headerHeight), XStringFormats.TopLeft);

            y += headerHeight;
        }

        DrawHeader();

        double footerHeight = 24;
        double bottomLimit() => page.Height - margin - footerHeight - 2; // leave space for footer

        // Util for measuring/wrapping text in a given width; returns lines and total height
        (string[] lines, double height) Wrap(string? text, double width)
        {
            text ??= string.Empty;
            var words = text.Split(' ');
            var lines = new List<string>();
            var current = "";
            foreach (var word in words)
            {
                var next = string.IsNullOrEmpty(current) ? word : current + " " + word;
                var size = gfx.MeasureString(next, font);
                if (size.Width <= width)
                {
                    current = next;
                }
                else
                {
                    if (!string.IsNullOrEmpty(current)) lines.Add(current);
                    current = word;
                }
            }
            if (!string.IsNullOrEmpty(current)) lines.Add(current);
            var lineHeight = rowHeight; // same baseline height
            return (lines.ToArray(), lines.Count * lineHeight);
        }

        void NewPage()
        {
            // Dispose current graphics before starting a new page
            gfx?.Dispose();
            page = doc.AddPage();
            page.Size = PdfSharpCore.PageSize.Letter;
            // new gfx for new page
            var newGfx = XGraphics.FromPdfPage(page);
            // reassign
            gfx = newGfx;
            y = margin;
            DrawHeader();
        }

        // rows
        foreach (var p in payments.OrderByDescending(p => p.Status).ThenBy(p => p.Id))
        {
            // Determine required height (wrap name)
            var wrapped = Wrap(p.RemitterName, colWidths[1]);
            var height = Math.Max(rowHeight, wrapped.height);

            if (y + height > bottomLimit())
            {
                NewPage();
            }

            // Row border
            gfx.DrawRectangle(XPens.LightGray, new XRect(tableLeft, y, tableWidth, height));

            // Cells
            gfx.DrawString(p.CustomerId ?? string.Empty, font, XBrushes.Black, new XRect(colStarts[0] + 2, y + 2, colWidths[0] - 4, height), XStringFormats.TopLeft);

            // Name with wrapping
            double ny = y + 2;
            foreach (var line in wrapped.lines)
            {
                gfx.DrawString(line, font, XBrushes.Black, new XPoint(colStarts[1] + 2, ny + 12));
                ny += rowHeight;
            }

            // Amount left aligned and bold
            var amountStr = p.Amount.ToString();
            gfx.DrawString(amountStr, bold, XBrushes.Black, new XRect(colStarts[2], y + 2, colWidths[2] - 2, height), XStringFormats.TopLeft);

            gfx.DrawString(p.InvoiceNumber ?? string.Empty, font, XBrushes.Black, new XRect(colStarts[3] + 2, y + 2, colWidths[3] - 4, height), XStringFormats.TopLeft);
            gfx.DrawString(p.TransactionType ?? string.Empty, font, XBrushes.Black, new XRect(colStarts[4] + 2, y + 2, colWidths[4] - 4, height), XStringFormats.TopLeft);
            gfx.DrawString(p.AccountNumber ?? string.Empty, font, XBrushes.Black, new XRect(colStarts[5] + 2, y + 2, colWidths[5] - 4, height), XStringFormats.TopLeft);
            gfx.DrawString(p.CheckNumber ?? string.Empty, font, XBrushes.Black, new XRect(colStarts[6] + 2, y + 2, colWidths[6] - 4, height), XStringFormats.TopLeft);
            gfx.DrawString(p.Status ?? string.Empty, font, XBrushes.Black, new XRect(colStarts[7] + 2, y + 2, colWidths[7] - 4, height), XStringFormats.TopLeft);

            y += height;
        }

        // Totals section
        var totalCount = payments.Count;
        var totalAmount = payments.Sum(p => p.Amount);
        if (y + 2 * rowHeight > bottomLimit())
        {
            NewPage();
        }
        y += 12;
        string label1 = "Transaction Count:";
        var label1Size = gfx.MeasureString(label1 + " ", bold);
        gfx.DrawString(label1, bold, XBrushes.Black, new XPoint(margin, y));
        gfx.DrawString(totalCount.ToString(), font, XBrushes.Black, new XPoint(margin + label1Size.Width, y));
        y += 18;
        string label2 = "Total Transaction Amount:";
        var label2Size = gfx.MeasureString(label2 + " ", bold);
        gfx.DrawString(label2, bold, XBrushes.Black, new XPoint(margin, y));
        gfx.DrawString(totalAmount.ToString(), font, XBrushes.Black, new XPoint(margin + label2Size.Width, y));

        // Finish drawing content on the last page
        gfx.Dispose();

        // Draw footers on all pages with total count
        var totalPages = doc.PageCount;
        for (int i = 0; i < totalPages; i++)
        {
            DrawFooter(doc.Pages[i], i + 1, totalPages);
        }

        using var ms = new MemoryStream();
        doc.Save(ms, false);
        return ms.ToArray();
    }

    private static void DrawFooter(PdfPage page, int pageNumber, int totalPages)
    {
        using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        var font = new XFont("Segoe UI", 11, XFontStyle.Regular);
        const double inch = 72.0;
        const double margin = 0.5 * inch;
        var y = page.Height - margin + 2;
        var text = $"Page {pageNumber} of {totalPages}";
        var size = gfx.MeasureString(text, font);
        gfx.DrawString(text, font, XBrushes.Black, new XPoint(page.Width - margin - size.Width, y));
    }
}
