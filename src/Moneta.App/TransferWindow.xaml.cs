using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Moneta.Core;
using Moneta.Core.Presentation;

namespace Moneta.App;

public partial class TransferWindow : Window
{
    private const string DefaultRateSource = "Montants saisis par l’utilisateur";
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    private readonly MainViewModel _model;
    private readonly TransferDetails? _editing;
    private readonly bool _readOnly;
    private bool _ready;
    private bool _updating;
    private bool _submitting;
    private bool _sameCurrency;
    private bool _exchangeDateFollowsTransfer;
    private string _independentCredit = string.Empty;

    public TransferWindow(MainViewModel model, TransferDetails? editing = null, bool readOnly = false)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.IsDemo)
            throw new InvalidOperationException("Les virements ne sont pas disponibles dans la démonstration.");
        var archived = editing is not null && model.HasArchivedTransferAccount(editing);
        var consultation = editing is not null && (readOnly || archived || editing.HasReconciledLeg || !model.PendingReconciliations.IsEmpty);
        if (!consultation && !model.PendingReconciliations.IsEmpty)
            throw new InvalidOperationException("Enregistrez le rapprochement ou sa réouverture (Ctrl+S) avant de modifier un virement.");
        if (editing?.Value.IsDeleted == true)
            throw new ArgumentException("Un virement annulé ne peut pas être modifié.", nameof(editing));
        if (readOnly && editing is null)
            throw new ArgumentException("La consultation nécessite un virement existant.", nameof(editing));

        _model = model;
        _editing = editing;
        _readOnly = consultation;
        var accounts = model.Accounts.Where(item => consultation || !item.Account.IsArchived).ToArray();
        if (accounts.Length < 2)
            throw new InvalidOperationException("Créez au moins deux comptes actifs dans le dossier pour effectuer un virement.");

        InitializeComponent();
        Title = consultation ? "Consulter le virement" : editing is null ? "Nouveau virement" : "Modifier le virement";
        Heading.Text = Title;
        SourceAccount.ItemsSource = accounts;
        DestinationAccount.ItemsSource = accounts;
        FeeAccount.ItemsSource = accounts;
        SourceAccount.SelectedItem = accounts.FirstOrDefault(item => item.Account.Id == (editing?.Debit.AccountId ?? model.SelectedAccountId)) ?? accounts[0];
        DestinationAccount.SelectedItem = editing is null
            ? accounts.First(item => item != SourceAccount.SelectedItem)
            : accounts.Single(item => item.Account.Id == editing.Credit.AccountId);
        FeeAccount.SelectedItem = editing?.Fee is { } fee
            ? accounts.Single(item => item.Account.Id == fee.AccountId) : null;
        SourceAccount.IsEnabled = DestinationAccount.IsEnabled = editing is null;
        EntryDate.Text = FormatDate(editing?.Value.Date ?? DateOnly.FromDateTime(DateTime.Today));
        ExchangeDate.Text = FormatDate(editing?.Value.ExchangeDate ?? editing?.Value.Date ?? DateOnly.FromDateTime(DateTime.Today));
        _exchangeDateFollowsTransfer = editing is null;
        RateSource.Text = editing?.Value.RateSource ?? DefaultRateSource;
        DebitAmount.Text = editing is null ? string.Empty : FormatAmount(editing.Debit);
        CreditAmount.Text = editing is null ? string.Empty : FormatAmount(editing.Credit);
        _independentCredit = CreditAmount.Text;
        HasFee.IsChecked = editing?.Fee is not null;
        HasFee.IsEnabled = editing?.Fee is null;
        FeeAmount.Text = editing?.Fee is { } existingFee ? FormatAmount(existingFee) : string.Empty;
        EntryReference.Text = editing?.Value.Reference ?? string.Empty;
        EntryNotes.Text = editing?.Value.Notes ?? string.Empty;
        AccountNotice.Text = editing is null
            ? "Deux mouvements distincts : débit et crédit. Leur état NP / P / R reste indépendant."
            : "Comptes source, destinataire et frais existants fixes. Pour corriger un compte, annulez tout le virement puis recréez-le. Les états NP / P / R restent indépendants.";
        _ready = true;
        RefreshAccounts();
        RefreshFee();
        if (consultation)
        {
            DraftNotice.Text = "Consultation — aucun mouvement n’est modifié depuis cette fenêtre.";
            SourceAccount.IsEnabled = DestinationAccount.IsEnabled = FeeAccount.IsEnabled = HasFee.IsEnabled = false;
            foreach (var field in new[] { EntryDate, DebitAmount, CreditAmount, ExchangeDate, RateSource, FeeAmount, EntryReference, EntryNotes })
                field.IsReadOnly = true;
            ConfirmTransfer.IsEnabled = false;
            CancelTransfer.Content = "Fermer";
            ErrorText.Text = archived ? "Réactivez les comptes du virement, frais compris, pour retrouver ses commandes de modification."
                : editing!.HasReconciledLeg ? ReconciledMessage
                : !model.PendingReconciliations.IsEmpty
                    ? "Consultation seule : enregistrez le rapprochement ou sa réouverture (Ctrl+S) avant de modifier ce virement."
                    : "Consultation seule : la modification du virement est indisponible dans ce contexte.";
        }
        Loaded += (_, _) =>
        {
            // WorkArea is in WPF units, including the current desktop scaling.
            MaxHeight = SystemParameters.WorkArea.Height;
            MaxWidth = SystemParameters.WorkArea.Width;
            if (editing is null) SourceAccount.Focus();
            else { EntryDate.Focus(); EntryDate.SelectAll(); }
        };
    }

    private const string ReconciledMessage = "Une jambe du virement (débit, crédit ou frais) est rapprochée R. Rouvrez le rapprochement ou libérez le rapprochement hérité, puis enregistrez (Ctrl+S) avant de modifier le virement entier.";

    private static string FormatDate(DateOnly date) => date.ToString("dd/MM/yyyy", French);
    private static string FormatAmount(Transaction value) => value.Amount.ToString($"F{value.Currency.DecimalPlaces}", French);
    private static bool TryDate(string text, out DateOnly date) => DateOnly.TryParseExact(text.Trim(), "dd/MM/yyyy", French, DateTimeStyles.None, out date) && date != default;

    private void Account_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ready) RefreshAccounts();
    }

    private void RefreshAccounts()
    {
        var source = (SourceAccount.SelectedItem as AccountSummary)?.Account;
        var destination = (DestinationAccount.SelectedItem as AccountSummary)?.Account;
        var same = source is not null && destination is not null && source.Currency.Code == destination.Currency.Code;
        _updating = true;
        try
        {
            if (same && !_sameCurrency) _independentCredit = CreditAmount.Text;
            if (!same && _sameCurrency) CreditAmount.Text = _independentCredit;
            _sameCurrency = same;
            CreditAmount.IsReadOnly = same;
            if (same) CreditAmount.Text = DebitAmount.Text;
            DebitCurrency.Text = source?.Currency.Code ?? "Devise source";
            CreditCurrency.Text = destination?.Currency.Code ?? "Devise destinataire";
            ExchangeFields.Visibility = same ? Visibility.Collapsed : Visibility.Visible;
            AmountHint.Text = same
                ? "Même devise : le crédit recopie exactement le débit et n’est pas modifiable. Les frais sont séparés."
                : "Devises différentes : saisissez librement les deux montants natifs, sans conversion automatique. Aucun cours réseau.";
            if (source is not null && destination is not null)
                AmountHint.Text += $" Précision : {source.Currency.Code} {source.Currency.DecimalPlaces}, {destination.Currency.Code} {destination.Currency.DecimalPlaces} décimale(s), sans arrondi.";
        }
        finally { _updating = false; }
        RefreshPreview();
    }

    private void Amount_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _updating) return;
        if (_sameCurrency && ReferenceEquals(sender, DebitAmount))
        {
            _updating = true;
            try { CreditAmount.Text = DebitAmount.Text; }
            finally { _updating = false; }
        }
        else if (!_sameCurrency && ReferenceEquals(sender, CreditAmount))
            _independentCredit = CreditAmount.Text;
        RefreshPreview();
    }

    private void Date_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _updating) return;
        if (_exchangeDateFollowsTransfer)
        {
            _updating = true;
            try { ExchangeDate.Text = EntryDate.Text; }
            finally { _updating = false; }
        }
        RefreshPreview();
    }

    private void ExchangeDate_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _updating) return;
        _exchangeDateFollowsTransfer = string.IsNullOrWhiteSpace(ExchangeDate.Text);
        RefreshPreview();
    }

    private void Preview_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_ready && !_updating) RefreshPreview();
    }

    private void Fee_Changed(object sender, RoutedEventArgs e)
    {
        if (_ready) RefreshFee();
    }

    private void FeeAccount_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ready) RefreshFee();
    }

    private void RefreshFee()
    {
        var included = HasFee.IsChecked == true;
        FeeFields.IsEnabled = included;
        FeeAccount.IsEnabled = included && _editing?.Fee is null;
        var account = (FeeAccount.SelectedItem as AccountSummary)?.Account;
        FeeCurrency.Text = account?.Currency.Code ?? "Devise du compte de frais";
        FeeHint.Text = _editing?.Fee is not null
            ? "Frais existants : compte fixe et mouvement conservé. Saisissez explicitement 0 pour annuler ces frais."
            : "Frais facultatifs, strictement positifs à l’ajout. Choisissez le compte débité dans ce dossier ; sa devise native s’applique.";
        if (included && account is not null)
            FeeHint.Text += $" Précision : {account.Currency.DecimalPlaces} décimale(s), sans arrondi. Frais exclus du rapport de change.";
    }

    private void RefreshPreview()
    {
        if (SourceAccount.SelectedItem is not AccountSummary { Account: var source } ||
            DestinationAccount.SelectedItem is not AccountSummary { Account: var destination })
        {
            RateDescription.Text = "Choisissez les deux comptes pour afficher le rapport de change.";
            return;
        }
        if (source.Id == destination.Id)
        {
            RateDescription.Text = "Le compte source et le compte destinataire doivent être distincts.";
            return;
        }
        try
        {
            var debit = MoneyInput.ParseFrench(DebitAmount.Text, source.Currency);
            var credit = MoneyInput.ParseFrench(CreditAmount.Text, destination.Currency);
            if (debit <= 0 || credit <= 0)
            {
                RateDescription.Text = "Les deux montants doivent être strictement positifs pour afficher le rapport exact.";
                return;
            }
            if (!TryDate(EntryDate.Text, out var date))
            {
                RateDescription.Text = "Saisissez une date de virement valide (jj/mm/aaaa).";
                return;
            }
            DateOnly? exchangeDate = null;
            if (!_sameCurrency)
            {
                if (string.IsNullOrWhiteSpace(ExchangeDate.Text)) exchangeDate = date;
                else if (TryDate(ExchangeDate.Text, out var entered)) exchangeDate = entered;
                else
                {
                    RateDescription.Text = "Saisissez une date du change valide (jj/mm/aaaa).";
                    return;
                }
            }
            RateDescription.Text = TransferRules.FormatEffectiveRate(debit, source.Currency, credit, destination.Currency)
                + (_sameCurrency ? "\nFrais séparés, exclus du rapport."
                    : $"\nDate du change : {FormatDate(exchangeDate!.Value)}. Source déclarée / non vérifiée : {ReadRateSource()}. Frais exclus. Aucun cours réseau.");
        }
        catch (ArgumentException error)
        {
            RateDescription.Text = "Aperçu en attente : " + error.Message;
        }
    }

    private string ReadRateSource() => string.IsNullOrWhiteSpace(RateSource.Text) ? DefaultRateSource : RateSource.Text.Trim();

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (_submitting || _readOnly) return;
        _submitting = true;
        ConfirmTransfer.IsEnabled = false;
        ErrorText.Text = string.Empty;
        var staged = false;
        try
        {
            if (!_model.PendingReconciliations.IsEmpty)
                throw new InvalidOperationException("Enregistrez le rapprochement ou sa réouverture (Ctrl+S) avant de modifier un virement.");
            var current = _editing is null ? null : _model.GetTransfer(_editing.Value.Id);
            if (current?.HasReconciledLeg == true) throw new InvalidOperationException(ReconciledMessage);
            if (!TryBuildDraft(out var draft)) return;
            if (current is not null && ChangesMarkedFinancialData(current, draft)
                && MessageBox.Show(this,
                    "Une jambe pointée P va changer de date ou de montant. Ses état et date de pointage seront conservés ; les soldes pointés seront recalculés. Les autres jambes ne seront pas pointées automatiquement. Confirmer la correction du virement ?",
                    "Moneta — corriger un virement pointé", MessageBoxButton.YesNo,
                    MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            if (_editing is null) _model.CreateTransfer(draft);
            else _model.EditTransfer(_editing.Value.Id, _editing.Value.Revision, draft);
            staged = true;
            DialogResult = true;
        }
        catch (Exception error)
        {
            ErrorText.Text = "Le brouillon n’a pas pu être validé. Vos champs sont conservés.\n" + error.Message;
            ErrorText.BringIntoView();
        }
        finally
        {
            if (!staged)
            {
                _submitting = false;
                ConfirmTransfer.IsEnabled = true;
            }
        }
    }

    private static bool ChangesMarkedFinancialData(TransferDetails current, TransferDraft draft) =>
        current.Debit.Status == ReconciliationStatus.Marked && (current.Debit.Date != draft.Date || current.Debit.Amount != draft.DebitedAmount)
        || current.Credit.Status == ReconciliationStatus.Marked && (current.Credit.Date != draft.Date || current.Credit.Amount != draft.CreditedAmount)
        || current.Fee is { Status: ReconciliationStatus.Marked } fee && (fee.Date != draft.Date || fee.Amount != draft.FeeAmount);

    private bool TryBuildDraft(out TransferDraft draft)
    {
        draft = null!;
        if (SourceAccount.SelectedItem is not AccountSummary { Account: var source })
            return ShowError("Choisissez un compte source actif.", SourceAccount);
        if (DestinationAccount.SelectedItem is not AccountSummary { Account: var destination })
            return ShowError("Choisissez un compte destinataire actif.", DestinationAccount);
        if (source.Id == destination.Id)
            return ShowError("Le compte source et le compte destinataire doivent être distincts.", DestinationAccount);
        if (!TryDate(EntryDate.Text, out var date))
            return ShowError("Date invalide : saisissez une date au format jj/mm/aaaa.", EntryDate);
        if (!TryAmount(DebitAmount, source.Currency, false, out var debit)) return false;
        if (!TryAmount(CreditAmount, destination.Currency, false, out var credit)) return false;
        if (_sameCurrency && debit != credit)
            return ShowError("Dans une même devise, le crédit doit être exactement égal au débit, hors frais.", DebitAmount);
        DateOnly? exchangeDate = null;
        if (!_sameCurrency)
        {
            if (string.IsNullOrWhiteSpace(ExchangeDate.Text)) exchangeDate = date;
            else if (TryDate(ExchangeDate.Text, out var entered)) exchangeDate = entered;
            else return ShowError("Date du change invalide : utilisez jj/mm/aaaa.", ExchangeDate);
        }
        Guid? feeAccountId = null;
        decimal feeAmount = 0;
        if (HasFee.IsChecked == true)
        {
            if (FeeAccount.SelectedItem is not AccountSummary { Account: var feeAccount })
                return ShowError("Choisissez explicitement le compte actif débité pour les frais.", FeeAccount);
            feeAccountId = feeAccount.Id;
            if (!TryAmount(FeeAmount, feeAccount.Currency, _editing?.Fee is not null, out feeAmount)) return false;
        }
        if (EntryReference.Text.Length > 120)
            return ShowError("La référence ne doit pas dépasser 120 caractères.", EntryReference);
        if (EntryNotes.Text.Length > 2000)
            return ShowError("Les notes ne doivent pas dépasser 2 000 caractères.", EntryNotes);
        draft = new TransferDraft(source.Id, destination.Id, date, debit, credit,
            EntryReference.Text.Trim(), EntryNotes.Text, exchangeDate, _sameCurrency ? null : ReadRateSource(), feeAccountId, feeAmount);
        return true;
    }

    private bool TryAmount(TextBox field, Currency currency, bool allowZero, out decimal amount)
    {
        amount = 0;
        try
        {
            amount = MoneyInput.ParseFrench(field.Text, currency);
            if (amount == 0 && !allowZero)
                return ShowError("Saisissez un montant strictement positif. Zéro n’est autorisé que pour annuler des frais existants.", field);
            return true;
        }
        catch (ArgumentException error) { return ShowError(error.Message, field); }
    }

    private bool ShowError(string message, Control field)
    {
        ErrorText.Text = message;
        field.BringIntoView();
        field.Focus();
        if (field is TextBox text) text.SelectAll();
        return false;
    }
}
