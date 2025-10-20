using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CashBatch.Application;
using Microsoft.Win32;

namespace CashBatch.Desktop;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly IBatchService _batches;
    private readonly IImportService _import;
    private readonly IMatchingService _match;
    private readonly IERPExportService _export;
    private readonly ILookupService _lookup;

    public ObservableCollection<BatchDto> Batches { get; } = new();
    public ObservableCollection<PaymentDto> Payments { get; } = new();
    public ObservableCollection<PaymentDto> NeedsReviewPayments { get; } = new();
    public ObservableCollection<LookupDto> Lookups { get; } = new();
    public ObservableCollection<AppliedLineDto> AppliedLines { get; } = new();
    public ObservableCollection<LogDto> Logs { get; } = new();
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set { _selectedTabIndex = value; OnPropertyChanged(); _ = OnTabChangedAsync(); }
    }
    private int _selectedTabIndex;

    public ICommand ImportCommand { get; }
    public ICommand AutoApplyCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand PrintCommand { get; }
    public ICommand AssignCustomerCommand { get; }
    public ICommand OpenExportSettingsCommand { get; }

    private PaymentDto? _selectedPayment;
    public PaymentDto? SelectedPayment
    {
        get => _selectedPayment;
        set
        {
            _selectedPayment = value;
            OnPropertyChanged();
            // Load applied lines whenever the selection changes
            _ = LoadAppliedAsync();
        }
    }

    private BatchDto? _selectedBatch;
    public BatchDto? SelectedBatch
    {
        get => _selectedBatch;
        set { _selectedBatch = value; OnPropertyChanged(); _ = RefreshSelectedBatchAsync(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set { _isBusy = value; OnPropertyChanged(); } }

    public MainViewModel(IBatchService batches, IImportService import, IMatchingService match, IERPExportService export, ILookupService lookup)
    {
        _batches = batches; _import = import; _match = match; _export = export; _lookup = lookup;

        ImportCommand = new RelayCommand(async _ => await ImportFile());
        AutoApplyCommand = new RelayCommand(async _ => await AutoApply());
        ExportCommand = new RelayCommand(async _ => await Export());
        PrintCommand = new RelayCommand(async _ => await Print());
        var assignCmd = new RelayCommand(async _ => await AssignCustomer(), _ => SelectedPayment != null);
        AssignCustomerCommand = assignCmd;
        OpenExportSettingsCommand = new RelayCommand(_ => { OpenExportSettings(); return Task.CompletedTask; });

        _ = LoadRecentBatchesAsync();
    }

    private async Task LoadRecentBatchesAsync()
    {
        var list = await _batches.GetRecentAsync();
        Batches.Clear();
        foreach (var b in list) Batches.Add(b);
        if (SelectedBatch == null && Batches.Count > 0)
            SelectedBatch = Batches[0];
    }

    private async Task RefreshSelectedBatchAsync()
    {
        if (SelectedBatch == null) { Payments.Clear(); NeedsReviewPayments.Clear(); AppliedLines.Clear(); return; }
        var batchId = SelectedBatch.Id;
        var all = await _batches.GetPaymentsAsync(batchId);
        var needs = await _batches.GetNeedsReviewAsync(batchId);
        Payments.Clear();
        foreach (var p in all) Payments.Add(p);
        NeedsReviewPayments.Clear();
        foreach (var p in needs) NeedsReviewPayments.Add(p);
        await LoadAppliedAsync();
        OnPropertyChanged(nameof(SelectedBatch));
    }

    private async Task ImportFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Import Bank File",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        var ok = dlg.ShowDialog() == true;
        if (!ok) return;

        var result = await _import.ImportAsync(dlg.FileName, Environment.UserName);

        // Refresh batches and select the newly imported one
        await LoadRecentBatchesAsync();
        var match = Batches.FirstOrDefault(b => b.Id == result.Id);
        if (match != null) SelectedBatch = match;
    }

    private async Task AutoApply()
    {
        if (SelectedBatch == null) return;
        try
        {
            IsBusy = true;
            await _match.AutoApplyAsync(SelectedBatch.Id);
            await RefreshSelectedBatchAsync();
            if (Payments.Count == 0)
            {
                System.Windows.MessageBox.Show("No payments to process in this batch.", "Auto-Apply", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            var firstAuto = Payments.FirstOrDefault(p => p.Status == nameof(Domain.PaymentStatus.AutoApplied));
            if (firstAuto != null)
            {
                SelectedPayment = firstAuto;
                await LoadAppliedAsync();
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "Auto-Apply Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }
    private Task Export() => Task.CompletedTask;
    private Task Print() => Task.CompletedTask;

    // Export settings captured from the settings window
    public int ExportFiscalYear { get => _exportFiscalYear; set { _exportFiscalYear = value; OnPropertyChanged(); } }
    private int _exportFiscalYear = DateTime.Now.Year;
    public int ExportPeriod { get => _exportPeriod; set { _exportPeriod = value; OnPropertyChanged(); } }
    private int _exportPeriod = 1;

    private void OpenExportSettings()
    {
        var win = new ExportSettingsWindow
        {
            Owner = System.Windows.Application.Current?.MainWindow,
            FiscalYear = ExportFiscalYear,
            Period = ExportPeriod
        };
        var result = win.ShowDialog();
        if (result == true)
        {
            ExportFiscalYear = win.FiscalYear;
            ExportPeriod = win.Period;
        }
    }

    private async Task LoadAppliedAsync()
    {
        var sel = SelectedPayment;
        if (sel == null) { AppliedLines.Clear(); return; }

        var paymentId = sel.Id;
        var lines = await _batches.GetAppliedAsync(paymentId);

        // marshal back to UI thread for collection updates
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            // If selection changed since we queried, ignore
            if (SelectedPayment == null || SelectedPayment.Id != paymentId) return;
            AppliedLines.Clear();
            foreach (var l in lines) AppliedLines.Add(l);
        });
    }

    private async Task AssignCustomer()
    {
        var sel = SelectedPayment;
        if (sel == null) return;
        try
        {
            // Simple prompt for customer id
            string? current = sel.CustomerId;
            var bank = sel.BankNumber ?? "";
            var acct = sel.AccountNumber ?? sel.BankAccount ?? "";
            string prompt = $"Assign Customer for{Environment.NewLine}Bank Number: {bank}{Environment.NewLine}Account Number: {acct}";
            string title = "Assign Customer";
            string input = Microsoft.VisualBasic.Interaction.InputBox(prompt, title, current ?? string.Empty);
            if (string.IsNullOrWhiteSpace(input)) return;
            if (input == "0") { System.Windows.MessageBox.Show("'0' is not a valid customer id."); return; }

            await _batches.AssignCustomerAsync(sel.Id, input.Trim());

            // Refresh current batch lists
            await RefreshSelectedBatchAsync();

            // Restore selection to edited payment if still present
            var found = Payments.FirstOrDefault(p => p.Id == sel.Id);
            if (found != null) SelectedPayment = found;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "Assign Customer Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private async Task OnTabChangedAsync()
    {
        // Tab order: 0 Batches, 1 Payments, 2 Applied Detail, 3 Needs Review, 4 Logs, 5 Customer Lookups
        if (SelectedTabIndex == 5)
        {
            try
            {
                var rows = await _lookup.GetAllAsync();
                Lookups.Clear();
                foreach (var r in rows) Lookups.Add(r);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "Load Lookups Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
    }
}
