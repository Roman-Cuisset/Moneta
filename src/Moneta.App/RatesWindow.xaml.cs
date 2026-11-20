using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Moneta.Core;
using Moneta.Core.Presentation;

namespace Moneta.App;

public partial class RatesWindow : Window
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    private readonly ExchangeRateSession _session;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _updating;
    private long _historyGeneration;

    public RatesWindow(ExchangeRateSession session, IEnumerable<Currency>? currencies = null)
    {
        _session = session;
        InitializeComponent();
        var choices = (currencies ?? new CurrencyRegistry().Currencies).ToArray();
        ConvertSource.ItemsSource = ConvertTarget.ItemsSource = choices;
        ConvertSource.SelectedItem = choices.FirstOrDefault(currency => currency.Code == "BYN") ?? choices.FirstOrDefault();
        ConvertTarget.SelectedItem = choices.FirstOrDefault(currency => currency.Code == "EUR") ?? choices.FirstOrDefault();
        _session.Changed += Session_Changed;
        Closed += (_, _) =>
        {
            _session.Changed -= Session_Changed;
            _lifetime.Cancel();
            _lifetime.Dispose();
        };
        Loaded += (_, _) =>
        {
            ProviderChoice.Focus();
        };
        RefreshView();
    }

    private void Session_Changed(object? sender, EventArgs e)
    {
        if (!_lifetime.IsCancellationRequested && !Dispatcher.HasShutdownStarted)
            Dispatcher.BeginInvoke(() => { if (!_lifetime.IsCancellationRequested) RefreshView(); });
    }

    private void RefreshView()
    {
        var state = _session.State;
        if (state.IsRefreshing) HistoryGrid.ItemsSource = Array.Empty<HistoryRow>();
        _updating = true;
        try
        {
            ProviderChoice.SelectedIndex = state.Provider == RateProvider.Cbr ? 1 : 0;
            QueryDate.Text = state.Date.ToString("dd/MM/yyyy", French);
            QueryDate.IsEnabled = !state.IsRefreshing;
            RefreshButton.IsEnabled = !state.IsRefreshing;
            CancelRefreshButton.IsEnabled = state.IsRefreshing;
            RateStatus.Text = RateRules.ProviderName(state.Provider) + " — " + state.Message;
            ConversionResult.Text = string.Empty;
            ErrorText.Text = string.Empty;
            if (state.Rates is not { } rates)
            {
                QuotesGrid.ItemsSource = Array.Empty<QuoteRow>();
                RateDates.Text = $"Date de valorisation demandée : {state.Date:dd/MM/yyyy}. Aucun cours applicable chargé.";
                TraceInformation.Text = "Aucun montant estimé sans publication officielle valide. Cache indépendant des fichiers .moneta.";
            }
            else
            {
                var rows = rates.Quotes.Select(quote => new QuoteRow(quote.CurrencyCode, quote.Name,
                    quote.Nominal.ToString("N0", French), quote.OfficialValue.ToString("G29", French), rates.ReferenceCurrency,
                    quote.SourceId.Length == 0 ? quote.NumericCode : quote.SourceId)).ToList();
                rows.Add(new(rates.ReferenceCurrency, "Devise de référence", "1", "1", rates.ReferenceCurrency,
                    "Identité mathématique, pas un cours publié"));
                QuotesGrid.ItemsSource = rows.OrderBy(row => row.Code, StringComparer.Ordinal).ToArray();
                RateDates.Text = $"Demandé : {state.Date:dd/MM/yyyy} · effet réel : {rates.EffectiveDate:dd/MM/yyyy} · requête fournisseur : {rates.RequestedDate:dd/MM/yyyy}\nRécupéré : {rates.RetrievedUtc.ToLocalTime():dd/MM/yyyy HH:mm:ss zzz} · " +
                    (state.FromCache ? state.IsPersistent ? "cache disque" : "mémoire uniquement, persistance non confirmée" : state.IsPersistent ? "réponse HTTPS conservée" : "réponse HTTPS, cache non enregistré") +
                    ". Valeur de référence = montant coté × valeur officielle / nominal.";
                TraceInformation.Text = rates.Endpoint;
                TraceInformation.ToolTip = "SHA-256 du contenu original : " + rates.PayloadSha256;
            }
        }
        finally { _updating = false; }
        if (!state.IsRefreshing) _ = LoadHistoryAsync(state.Provider);
    }

    private async Task LoadHistoryAsync(RateProvider provider)
    {
        var generation = ++_historyGeneration;
        try
        {
            var rows = await _session.HistoryAsync(_lifetime.Token);
            if (_lifetime.IsCancellationRequested || generation != _historyGeneration || _session.State.Provider != provider) return;
            HistoryGrid.ItemsSource = rows.Select(row => new HistoryRow(
                row.EffectiveDate.ToString("dd/MM/yyyy", French), row.RequestedDate.ToString("dd/MM/yyyy", French),
                row.RetrievedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", French), row.PayloadSha256)).ToArray();
        }
        catch (OperationCanceledException) { }
        catch (RateException error)
        {
            if (!_lifetime.IsCancellationRequested && generation == _historyGeneration) ErrorText.Text = error.Message;
        }
    }

    private void Provider_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || ProviderChoice.SelectedItem is not ComboBoxItem { Tag: string source }) return;
        if (source == App.Settings.Current.RateSource) return;
        if (!App.SaveSettings(App.Settings.Current with { RateSource = source }, this)) RefreshView();
    }

    private bool TryDate(out DateOnly date)
    {
        if (DateOnly.TryParseExact(QueryDate.Text, "dd/MM/yyyy", French, DateTimeStyles.None, out date) && date != default) return true;
        ErrorText.Text = "Date invalide : utilisez jj/mm/aaaa.";
        QueryDate.Focus();
        return false;
    }

    private async void LoadDate_Click(object sender, RoutedEventArgs e)
    {
        if (TryDate(out var date)) await _session.SelectAsync(_session.State.Provider, date, _lifetime.Token);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (!TryDate(out var date)) return;
        await _session.RefreshAsync(_session.State.Provider, date, _lifetime.Token);
    }

    private void CancelRefresh_Click(object sender, RoutedEventArgs e) => _session.Cancel();

    private void Convert_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ErrorText.Text = string.Empty;
            ConversionResult.Text = string.Empty;
            if (ConvertSource.SelectedItem is not Currency source || ConvertTarget.SelectedItem is not Currency target)
                throw new ArgumentException("Choisissez deux devises de présentation.");
            var amount = MoneyInput.ParseFrench(ConvertInput.Text, source, allowNegative: true);
            var state = _session.State;
            decimal estimate;
            if (source.Code == target.Code) estimate = amount;
            else
            {
                if (state.Rates is null) throw new RateException(RateFailure.Unavailable, "Cours indisponible.");
                estimate = RateConverter.ConvertAmount(amount, source.Code, target, state.Rates);
            }
            ConversionResult.Text = MoneyFormatter.Format(amount, source) + " → " + MoneyFormatter.Format(estimate, target) +
                (source.Code == target.Code ? " · identité, sans change." : $" · estimation {RateRules.ProviderName(state.Provider)}, cours du {state.Rates!.EffectiveDate:dd/MM/yyyy}.");
        }
        catch (Exception error) when (error is ArgumentException or OverflowException or RateException)
        {
            ErrorText.Text = error.Message;
        }
    }

    private sealed record QuoteRow(string Code, string Name, string Nominal, string Value, string Reference, string Note);
    private sealed record HistoryRow(string Effective, string Requested, string Retrieved, string Hash);
}
