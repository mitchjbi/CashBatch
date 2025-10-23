using System.Windows;

namespace CashBatch.Desktop
{
    public partial class ExportAutoAppliedWindow : Window
    {
        public ExportAutoAppliedWindow()
        {
            InitializeComponent();
            DataContext = this;
        }

        public string? BatchName { get; set; }
        public int Period { get; set; }
        public int FiscalYear { get; set; }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
