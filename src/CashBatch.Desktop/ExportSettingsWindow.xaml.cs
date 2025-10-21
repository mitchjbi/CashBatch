using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Forms;

namespace CashBatch.Desktop;

public partial class ExportSettingsWindow : Window
{
    public int FiscalYear { get; set; }
    public int Period { get; set; }
    public string? BankNumber { get; set; }
    public string? GLBankAccountNumber { get; set; }
    public string? ARAccountNumber { get; set; }
    public string? TermsAccountNumber { get; set; }
    public string? AllowedAccountNumber { get; set; }
    public string? ExportDirectory { get; set; }

    public ExportSettingsWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    private bool Validate()
    {
        // Fiscal year must be 4 digits between 1900 and 9999
        if (FiscalYear < 1900 || FiscalYear > 9999)
        {
            System.Windows.MessageBox.Show("Fiscal Year must be a 4-digit year.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        // Period must be 1-12
        if (Period < 1 || Period > 12)
        {
            System.Windows.MessageBox.Show("Period must be between 1 and 12.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        if (string.IsNullOrWhiteSpace(ExportDirectory))
        {
            System.Windows.MessageBox.Show("Please select an Export Directory.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        return true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!Validate()) return;
        DialogResult = true;
    }

    private void BrowseExportDir_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Select export directory",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        if (!string.IsNullOrWhiteSpace(ExportDirectory))
        {
            try { dlg.SelectedPath = ExportDirectory; } catch { }
        }
        var result = dlg.ShowDialog();
        if (result == System.Windows.Forms.DialogResult.OK)
        {
            ExportDirectory = dlg.SelectedPath;
            // Update textbox bound via DataContext
            DataContext = null;
            DataContext = this;
        }
    }
}
