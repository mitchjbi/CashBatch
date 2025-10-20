using System;
using System.Text.RegularExpressions;
using System.Windows;

namespace CashBatch.Desktop;

public partial class ExportSettingsWindow : Window
{
    public int FiscalYear { get; set; }
    public int Period { get; set; }

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
            MessageBox.Show("Fiscal Year must be a 4-digit year.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        // Period must be 1-12
        if (Period < 1 || Period > 12)
        {
            MessageBox.Show("Period must be between 1 and 12.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        return true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!Validate()) return;
        DialogResult = true;
    }
}
