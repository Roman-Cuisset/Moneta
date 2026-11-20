using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using Moneta.Core;
using Moneta.Infrastructure.ExchangeRates;

namespace Moneta.App;

public partial class HistoricalValuationWindow : Window
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    private readonly HistoricalValuation _engine;
    private readonly Func<CancellationToken, HistoricalInput>? _flow;
    private readonly Func<DateOnly, CancellationToken, HistoricalInput>? _balance;
    private CancellationTokenSource? _operation;
    private HistoricalProof? _proof;
    private bool _closed;

    public HistoricalValuationWindow(HistoricalValuation engine, IEnumerable<Currency> currencies, RateProvider provider,
        Func<CancellationToken, HistoricalInput>? flow = null, Func<DateOnly, CancellationToken, HistoricalInput>? balance = null)
    {
        _engine = engine;
        _flow = flow;
        _balance = balance;
        InitializeComponent();
        var choices = currencies.ToArray();
        SourceChoice.ItemsSource = TargetChoice.ItemsSource = choices;
        SourceChoice.SelectedItem = choices.FirstOrDefault(item => item.Code == "BYN") ?? choices.FirstOrDefault();
        TargetChoice.SelectedItem = choices.FirstOrDefault(item => item.Code == "EUR") ?? choices.FirstOrDefault();
        ProviderChoice.SelectedIndex = provider == RateProvider.Cbr ? 1 : 0;
        DateInput.Text = DateOnly.FromDateTime(DateTime.Today).ToString("dd/MM/yyyy", French);
        Heading.Text = flow is not null ? "Valorisation de l’opération" : balance is not null ? "Valorisation du solde à date" : "Convertisseur historique";
        Scope.Text = flow is not null ? "La date et le montant proviennent de l’opération enregistrée."
            : balance is not null ? "Le solde inclut les opérations enregistrées jusqu’à la date d’arrêté, jour inclus."
            : "Le fournisseur et la date déterminent le cours utilisé.";
        if (flow is not null || balance is not null) SourceFields.Visibility = AmountFields.Visibility = Visibility.Collapsed;
        if (flow is not null) DateFields.Visibility = Visibility.Collapsed;
        Loaded += (_, _) =>
        {
            MaxHeight = SystemParameters.WorkArea.Height;
            MaxWidth = SystemParameters.WorkArea.Width;
            if (flow is not null) TargetChoice.Focus();
            else if (balance is not null) DateInput.Focus();
            else AmountInput.Focus();
        };
        Closed += (_, _) => { _closed = true; _operation?.Cancel(); };
    }

    private async void Calculate_Click(object sender, RoutedEventArgs e)
    {
        if (_operation is not null) return;
        using var operation = new CancellationTokenSource();
        _operation = operation;
        SetBusy(true);
        ClearResult();
        Status.Text = "Recherche de la cotation applicable…";
        try
        {
            var target = (Currency)TargetChoice.SelectedItem;
            var provider = ProviderChoice.SelectedIndex == 0 ? RateProvider.Nbrb : RateProvider.Cbr;
            var access = Offline.IsChecked == true ? HistoricalRateAccess.CacheOnly : ReferenceEquals(sender, Refresh)
                ? HistoricalRateAccess.Refresh : HistoricalRateAccess.FetchMissing;
            var input = await ReadInput(operation.Token);
            var result = await _engine.ValueAsync(input, target, provider, access, operation.Token);
            if (_closed) return;
            if (result.Proof is { } proof) ShowProof(proof, result.Detail ?? "Valorisation calculée.");
            else Status.Text = result.Detail ?? "Valorisation indisponible.";
        }
        catch (OperationCanceledException) { if (!_closed) Status.Text = "Calcul annulé."; }
        catch (Exception error) when (error is ArgumentException or DocumentException or RateException or OverflowException)
        { if (!_closed) Status.Text = error.Message; }
        finally
        {
            _operation = null;
            if (!_closed) SetBusy(false);
        }
    }

    private Task<HistoricalInput> ReadInput(CancellationToken token)
    {
        if (_flow is not null) return Task.Run(() => _flow(token), token);
        var date = ScheduleEditorWindow.ReadDate(DateInput.Text);
        if (_balance is not null) return Task.Run(() => _balance(date, token), token);
        var currency = (Currency)SourceChoice.SelectedItem;
        var amount = MoneyInput.ParseFrench(AmountInput.Text, currency, allowNegative: true);
        return Task.FromResult(new HistoricalInput(HistoricalUse.Conversion, date, amount, currency.Code));
    }

    private void ClearResult()
    {
        _proof = null;
        ValueText.Text = Evidence.Text = "";
        Export.IsEnabled = false;
    }

    private void ShowProof(HistoricalProof proof, string status)
    {
        _proof = proof;
        var input = proof.Input;
        ValueText.Text = $"{proof.Value.ToString($"N{proof.TargetDecimals}", French)} {proof.TargetCode}";
        var lines = new List<string> { $"{input.NativeAmount.ToString(French)} {input.SourceCode} · date demandée {input.Date:dd/MM/yyyy}" };
        if (input.BalanceOperationCount is { } count) lines.Add($"Solde initial et {count} opération(s) enregistrée(s)");
        if (input.FlowKind is HistoricalFlowKind.TransferDebit or HistoricalFlowKind.TransferCredit)
            lines.Add("Jambe d’un virement interne, exclue des totaux de recettes et dépenses.");
        if (input.FlowKind == HistoricalFlowKind.Fee) lines.Add("Frais du virement, comptés comme charge.");
        if (proof.Quotation is { } quote)
        {
            var path = proof.Method == HistoricalMethod.Cross
                ? $"{input.SourceCode} → {quote.ReferenceCurrency} → {proof.TargetCode}"
                : $"{input.SourceCode} → {proof.TargetCode}";
            lines.Add($"{RateRules.ProviderName(quote.Provider)} · date effective {quote.EffectiveDate:dd/MM/yyyy} · {(quote.FromCache ? "cache local" : "réponse téléchargée")}");
            lines.Add($"Chemin {path} · récupéré le {quote.RetrievedUtc:dd/MM/yyyy HH:mm} UTC");
            foreach (var leg in new[] { quote.Source, quote.Target }.OfType<OfficialRateQuote>())
                lines.Add($"{leg.Nominal} {leg.CurrencyCode} = {leg.OfficialValue.ToString(French)} {quote.ReferenceCurrency} · référence {leg.SourceId}");
        }
        else lines.Add("Même devise");
        Evidence.Text = string.Join(Environment.NewLine, lines);
        Status.Text = status;
        Export.IsEnabled = _operation is null;
    }

    private void SetBusy(bool busy)
    {
        Inputs.IsEnabled = Offline.IsEnabled = Calculate.IsEnabled = Replay.IsEnabled = !busy;
        Refresh.IsEnabled = !busy && Offline.IsChecked != true;
        Export.IsEnabled = !busy && _proof is not null;
        Cancel.IsEnabled = busy;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _operation?.Cancel();
    private void Offline_Changed(object sender, RoutedEventArgs e) { if (Refresh is not null) Refresh.IsEnabled = Offline.IsChecked != true; }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_proof is not { } proof) return;
        var dialog = new SaveFileDialog { Filter = "Preuve de valorisation (*.fxproof.json)|*.fxproof.json", DefaultExt = ".fxproof.json",
            FileName = "valorisation.fxproof.json", OverwritePrompt = false, AddExtension = true, Title = "Enregistrer une preuve de valorisation" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            HistoricalProofFile.WriteNew(dialog.FileName, proof);
            Status.Text = "Preuve enregistrée dans un nouveau fichier.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or RateException or OverflowException)
        { Status.Text = error.Message; }
    }

    private void Replay_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Preuve de valorisation (*.fxproof.json)|*.fxproof.json", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            ShowProof(HistoricalProofFile.ReadVerified(dialog.FileName), "Résultat reproduit depuis la preuve locale.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or RateException or OverflowException)
        { ClearResult(); Status.Text = error.Message; }
    }
}
