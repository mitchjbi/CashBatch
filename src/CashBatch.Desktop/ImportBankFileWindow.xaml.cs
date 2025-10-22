using System.Windows;

namespace CashBatch.Desktop
{
    public partial class ImportBankFileWindow : Window
    {
        public ImportBankFileWindow()
        {
            InitializeComponent();
            DataContext = this;
        }

        public string? DepositNumber { get; set; }
        public string? FilePath { get; set; }

        private void OnBrowse(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Bank CSV",
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dlg.ShowDialog(this) == true)
            {
                FilePath = dlg.FileName;
                // Notify binding update
                this.DataContext = null;
                this.DataContext = this;
            }
        }

        private void OnOk(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(FilePath))
            {
                System.Windows.MessageBox.Show(this, "Please choose a file to import.", "Import", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(DepositNumber))
            {
                if (System.Windows.MessageBox.Show(this, "Deposit Number is empty. Continue?", "Import", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;
            }
            DialogResult = true;
        }
    }
}
