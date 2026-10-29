using System.Globalization;
using System.Windows;
using Moneta.Core;
using Moneta.Core.Presentation;

namespace Moneta.App;

public partial class DemoAccountWindow : Window
{
    private readonly MainViewModel _model;
    public DemoAccountWindow(MainViewModel model)
    {
        _model = model;
        InitializeComponent();
        DataContext = model;
        Loaded += (_, _) => AccountName.Focus();
    }
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(OpeningBalance.Text, NumberStyles.Number, CultureInfo.GetCultureInfo("fr-FR"), out var amount))
        {
            ErrorText.Text = "Montant invalide : utilisez le format français, par exemple 1 234,56.";
            OpeningBalance.Focus();
            return;
        }
        if (NativeCurrency.SelectedItem is not Currency currency)
        {
            ErrorText.Text = "Choisissez une devise native.";
            return;
        }
        try
        {
            _model.AddDemoAccount(AccountName.Text, currency, AccountType.Text, amount, Notes.Text);
            DialogResult = true;
        }
        catch (ArgumentException error)
        {
            ErrorText.Text = error.Message;
        }
    }
}
