using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Moneta.Core;
using Moneta.Core.Presentation;

namespace Moneta.App;

public partial class AccountWindow : Window
{
    private readonly MainViewModel _model;
    private readonly Account? _editing;

    public AccountWindow(MainViewModel model, Account? editing = null)
    {
        _model = model;
        _editing = editing;
        InitializeComponent();
        DataContext = model;
        Title = model.IsDemo ? L.Get("AccountWindow.AddADemoAccount") : L.Get("Common.AddAccount");
        Heading.Text = Title;
        ModeNotice.Text = model.IsDemo
            ? L.Get("AccountWindow.InMemoryOnlyThisAccountWillDisappearUpon")
            : L.Get("AccountWindow.TheAccountWillBeAddedToTheDocument");
        ConfirmAccount.Content = model.IsDemo ? L.Get("AccountWindow.AddInMemory") : L.Get("AccountWindow.AddToDocument");
        if (model.IsDemo) BankFields.Visibility = Visibility.Collapsed;
        if (editing is not null)
        {
            Heading.Text = Title = L.Get("Common.AccountProperties");
            ConfirmAccount.Content = L.Get("Common.Apply");
            ModeNotice.Text = L.Get("AccountWindow.TheNativeCurrencyAndTheInitialBalanceAre");
            AccountName.Text = editing.Name;
            AccountType.Text = editing.Type ?? string.Empty;
            NativeCurrency.SelectedItem = editing.Currency;
            NativeCurrency.IsEnabled = false;
            OpeningBalance.Text = MoneyInput.FormatForEdit(editing.OpeningBalance, editing.Currency.DecimalPlaces);
            OpeningBalance.IsReadOnly = true;
            BankName.Text = editing.BankName ?? string.Empty;
            BankReference.Text = editing.BankReference ?? string.Empty;
            Holder.Text = editing.Holder ?? string.Empty;
            Notes.Text = editing.Notes ?? string.Empty;
        }
        AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => ErrorText.Text = string.Empty));
        Loaded += (_, _) =>
        {
            AccountName.Focus();
        };
    }
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var input = new AccountProperties(AccountName.Text, AccountType.Text, Notes.Text, BankName.Text, BankReference.Text, Holder.Text);
            if (_editing is { } account)
                _model.EditAccount(account.Id, input);
            else
            {
                if (NativeCurrency.SelectedItem is not Currency currency) throw new ArgumentException(L.Get("AccountWindow.ChooseANativeCurrency"));
                var amount = MoneyInput.ParseLocalized(OpeningBalance.Text, currency, allowNegative: true);
                if (_model.IsDemo) _model.AddDemoAccount(input.Name, currency, input.Type, amount, input.Notes);
                else _model.AddAccount(input, currency, amount);
            }
            DialogResult = true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or DocumentException)
        {
            ErrorText.Text = error.Message;
        }
    }
}
