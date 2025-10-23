using System.Diagnostics;
using System.IO;
using System.Printing;
using System.Windows.Xps;
using System.Windows.Xps.Packaging;
using System.Threading.Tasks;

namespace CashBatch.Desktop.Services;

public static class PdfPrintService
{
    // Writes the PDF to a temp file and opens it with the default PDF reader for user preview.
    // No automatic printing; the user can choose Print from the reader.
    public static void PrintPdfBytes(byte[] pdfData)
    {
        try
        {
            CleanupOldTempPdfs(daysOld: 3);
        }
        catch { /* best-effort cleanup */ }

        var fileName = $"CashBatch_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.pdf";
        var tempPath = Path.Combine(Path.GetTempPath(), fileName);
        File.WriteAllBytes(tempPath, pdfData);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = tempPath,
                // Use default "open" action to show preview instead of auto-print
                Verb = "open",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = true
            };
            using var proc = Process.Start(psi);
            // Optional: try to delete on exit; many readers spawn new processes, so this may not trigger.
            if (proc != null)
            {
                proc.EnableRaisingEvents = true;
                proc.Exited += (_, __) =>
                {
                    try { File.Delete(tempPath); } catch { }
                };
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Failed to send PDF to printer: {ex.Message}", "Print", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return;
        }
    }

    private static void CleanupOldTempPdfs(int daysOld)
    {
        var dir = new DirectoryInfo(Path.GetTempPath());
        var cutoff = DateTime.Now.AddDays(-daysOld);
        foreach (var f in dir.EnumerateFiles("CashBatch_*.pdf"))
        {
            try { if (f.CreationTime < cutoff) f.Delete(); } catch { }
        }
    }
}
