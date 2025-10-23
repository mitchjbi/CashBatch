using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CashBatch.Application;

namespace CashBatch.Desktop
{
    public partial class ImportBankFileWindow : Window
    {
        private readonly ITemplateService _templates;
        public ImportBankFileWindow()
        {
            InitializeComponent();
            // Resolve service from App host (dialog is not DI-created)
            _templates = App.HostApp.Services.GetService(typeof(ITemplateService)) as ITemplateService
                ?? throw new InvalidOperationException("ITemplateService not registered");
            DataContext = this;
            _ = LoadTemplatesAsync();
        }

        public string? BatchName { get; set; }
        public string? FilePath { get; set; }
        public int? SelectedTemplateId { get; set; }
        public ObservableCollection<CashTemplateDto> Templates { get; } = new();

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

        private async Task LoadTemplatesAsync()
        {
            var list = await _templates.GetAllAsync(onlyActive: true);
            Templates.Clear();
            foreach (var t in list) Templates.Add(t);
            // Do not set a default template; require explicit user selection
            // refresh bindings
            this.DataContext = null;
            this.DataContext = this;
        }

        private void OnOk(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(FilePath))
            {
                System.Windows.MessageBox.Show(this, "Please choose a file to import.", "Import", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (SelectedTemplateId is null)
            {
                System.Windows.MessageBox.Show(this, "Please choose a template.", "Import", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(BatchName))
            {
                if (System.Windows.MessageBox.Show(this, "Batch Name is empty. Continue?", "Import", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;
            }
            DialogResult = true;
        }
    }
}
