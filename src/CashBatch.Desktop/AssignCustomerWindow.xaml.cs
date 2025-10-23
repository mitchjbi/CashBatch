using System.Windows;

namespace CashBatch.Desktop;

public partial class AssignCustomerWindow : Window
{
    public string? BankNumber { get; set; }
    public string? AccountNumber { get; set; }
    public string? PossibleCustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? MailCity { get; set; }

    public string? CustomerId { get; set; }

    public AssignCustomerWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
