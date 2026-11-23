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
    private static CultureInfo DisplayCulture => L.Culture;
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
        DateInput.Text = DateOnly.FromDateTime(DateTime.Today).ToString(L.DatePattern, DisplayCulture);
        Heading.Text = flow is not null ? L.Get("HistoricalValuationWindow.TransactionValuation") : balance is not null ? L.Get("Common.ValorizationOfBalanceToDate") : "Convertisseur historique";
        Scope.Text = flow is not null ? L.Get("HistoricalValuationWindow.TheDateAndAmountAreDerivedFromThe")
            : balance is not null ? L.Get("HistoricalValuationWindow.TheBalanceIncludesTransactionsRecordedUpToAnd")
            : L.Get("HistoricalValuationWindow.TheSupplierAndTheDateDetermineTheRate");
        if (flow is not null || balance is not null) SourceFields.Visibility = AmountFields.Visibility = Visibility.Collapsed;
        if (flow is not null) DateFields.Visibility = Visibility.Collapsed;
        Loaded += (_, _) =>
        {
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
        Status.Text = L.Get("HistoricalValuationWindow.SearchForApplicableRating");
        try
        {
            var target = (Currency)TargetChoice.SelectedItem;
            var provider = ProviderChoice.SelectedIndex == 0 ? RateProvider.Nbrb : RateProvider.Cbr;
            var access = Offline.IsChecked == true ? HistoricalRateAccess.CacheOnly : ReferenceEquals(sender, Refresh)
                ? HistoricalRateAccess.Refresh : HistoricalRateAccess.FetchMissing;
            var input = await ReadInput(operation.Token);
            var result = await _engine.ValueAsync(input, target, provider, access, operation.Token);
            if (_closed) return;
            if (result.Proof is { } proof) ShowProof(proof, result.Detail is { } detail ? L.Diagnostic(detail) : L.Get("HistoricalValuationWindow.CalculatedValue"));
            else Status.Text = result.Detail is { } detail ? L.Diagnostic(detail) : L.Get("Rates.ValuationUnavailable");
        }
        catch (OperationCanceledException) { if (!_closed) Status.Text = L.Get("Common.CalculationCanceled"); }
        catch (Exception error) when (error is ArgumentException or DocumentException or RateException or OverflowException)
        { if (!_closed) Status.Text = L.Diagnostic(error.Message); }
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
        var amount = MoneyInput.ParseLocalized(AmountInput.Text, currency, allowNegative: true);
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
        ValueText.Text = $"{proof.Value.ToString($"N{proof.TargetDecimals}", DisplayCulture)} {proof.TargetCode}";
        var lines = new List<string> { L.Format("HistoricalValuationWindow.01DateRequested2MmDdYyyy", (input.NativeAmount.ToString(DisplayCulture)), (input.SourceCode), (input.Date)) };
        if (input.BalanceOperationCount is { } count) lines.Add(L.Format("HistoricalValuationWindow.OpeningBalanceAnd0SavedTransactions", (count)));
        if (input.FlowKind is HistoricalFlowKind.TransferDebit or HistoricalFlowKind.TransferCredit)
            lines.Add(L.Get("HistoricalValuationWindow.LegOfAnInternalTransferExcludingTotalRevenue"));
        if (input.FlowKind == HistoricalFlowKind.Fee) lines.Add(L.Get("HistoricalValuationWindow.TransferChargesCountedAsACharge"));
        if (proof.Quotation is { } quote)
        {
            var path = proof.Method == HistoricalMethod.Cross
                ? $"{input.SourceCode} → {quote.ReferenceCurrency} → {proof.TargetCode}"
                : $"{input.SourceCode} → {proof.TargetCode}";
            lines.Add(L.Format("Rates.ValuationPublication", RateRules.ProviderName(quote.Provider),
                quote.EffectiveDate.ToString(L.DatePattern, L.Culture),
                L.Get(quote.FromCache ? "Rates.FromLocalCache" : "Rates.FromDownload")));
            lines.Add(L.Format("HistoricalValuationWindow.Path0RecoveredOn1MmDdYyyy", (path), (quote.RetrievedUtc)));
            foreach (var leg in new[] { quote.Source, quote.Target }.OfType<OfficialRateQuote>())
                lines.Add(L.Format("HistoricalValuationWindow.0123Reference4", (leg.Nominal), (leg.CurrencyCode), (leg.OfficialValue.ToString(DisplayCulture)), (quote.ReferenceCurrency), (leg.SourceId)));
        }
        else lines.Add(L.Get("Common.SameCurrency"));
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
        var dialog = new SaveFileDialog { Filter = L.Get("HistoricalValuationWindow.ProofOfRecoveryFxproofJsonFxproofJson"), DefaultExt = ".fxproof.json",
            FileName = "valorisation.fxproof.json", OverwritePrompt = false, AddExtension = true, Title = L.Get("HistoricalValuationWindow.RecordProofOfValorization") };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            HistoricalProofFile.WriteNew(dialog.FileName, proof);
            Status.Text = L.Get("HistoricalValuationWindow.EvidenceSavedInANewFile");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or RateException or OverflowException)
        { Status.Text = error.Message; }
    }

    private void Replay_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = L.Get("HistoricalValuationWindow.ProofOfRecoveryFxproofJsonFxproofJson"), CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            ShowProof(HistoricalProofFile.ReadVerified(dialog.FileName), L.Get("HistoricalValuationWindow.ResultReproducedFromTheLocalEvidence"));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or RateException or OverflowException)
        { ClearResult(); Status.Text = error.Message; }
    }
}
