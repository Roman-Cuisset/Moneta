using System.Windows;
using Moneta.Core;
using Moneta.Core.Presentation;

namespace Moneta.App;

public partial class AccountWindow : Window
{
    private readonly MainViewModel _model;
    public AccountWindow(MainViewModel model)
    {
        _model = model;
        InitializeComponent();
        DataContext = model;
        Title = model.IsDemo ? "Ajouter un compte de démonstration" : "Ajouter un compte";
        Heading.Text = Title;
        ModeNotice.Text = model.IsDemo
            ? "En mémoire uniquement. Ce compte disparaîtra à la fermeture. Ne saisissez aucune donnée bancaire personnelle."
            : "Le compte sera ajouté au dossier. Utilisez Enregistrer (Ctrl+S) pour conserver les modifications sur disque.";
        ConfirmAccount.Content = model.IsDemo ? "Ajouter en mémoire" : "Ajouter au dossier";
        Loaded += (_, _) => AccountName.Focus();
    }
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (NativeCurrency.SelectedItem is not Currency currency)
        {
            ErrorText.Text = "Choisissez une devise native.";
            return;
        }
        try
        {
            var amount = MoneyInput.ParseFrench(OpeningBalance.Text, currency, allowNegative: true);
            if (_model.IsDemo) _model.AddDemoAccount(AccountName.Text, currency, AccountType.Text, amount, Notes.Text);
            else _model.AddAccount(AccountName.Text, currency, AccountType.Text, amount, Notes.Text);
            DialogResult = true;
        }
        catch (ArgumentException error)
        {
            ErrorText.Text = error.Message;
        }
    }
}
