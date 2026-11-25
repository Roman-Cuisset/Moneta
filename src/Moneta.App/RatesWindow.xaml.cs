using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Moneta.Core;
using Moneta.Core.Presentation;

namespace Moneta.App;

public partial class RatesWindow : Window
{
    private static CultureInfo DisplayCulture => L.Culture;
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
            QueryDate.Text = state.Date.ToString(L.DatePattern, DisplayCulture);
            QueryDate.IsEnabled = !state.IsRefreshing;
            RefreshButton.IsEnabled = !state.IsRefreshing;
            CancelRefreshButton.IsEnabled = state.IsRefreshing;
            RateStatus.Text = RateRules.ProviderName(state.Provider) + " — " + state.Message;
            ConversionResult.Text = string.Empty;
            ErrorText.Text = string.Empty;
            if (state.Rates is not { } rates)
            {
                QuotesGrid.ItemsSource = Array.Empty<QuoteRow>();
                RateDates.Text = L.Format("RatesWindow.DateOfValuationRequested0MmDdYyyy", (state.Date));
                TraceInformation.Text = L.Get("RatesWindow.NoEstimatedAmountWithoutValidOfficialPublicationIndependent");
            }
            else
            {
                var rows = rates.Quotes.Select(quote => new QuoteRow(quote.CurrencyCode, quote.Name,
                    quote.Nominal.ToString("N0", DisplayCulture), quote.OfficialValue.ToString("G29", DisplayCulture), rates.ReferenceCurrency,
                    quote.SourceId.Length == 0 ? quote.NumericCode : quote.SourceId)).ToList();
                rows.Add(new(rates.ReferenceCurrency, L.Get("RatesWindow.ReferenceCurrency"), "1", "1", rates.ReferenceCurrency,
                    L.Get("RatesWindow.MathematicalIdentityNotAPublishedRate")));
                QuotesGrid.ItemsSource = rows.OrderBy(row => row.Code, StringComparer.Ordinal).ToArray();
                RateDates.Text = L.Format("RatesWindow.Requested0MmDdYyyyActualEffect1", (state.Date), (rates.EffectiveDate), (rates.RequestedDate), (rates.RetrievedUtc.ToLocalTime())) +
                    (state.FromCache ? state.IsPersistent ? L.Get("RateDisplay.DiskCache") : L.Get("RatesWindow.MemoryOnlyPersistenceNotConfirmed") : state.IsPersistent ? L.Get("RatesWindow.HttpsResponseStored") : L.Get("RatesWindow.HttpsResponseCacheNotSaved")) +
                    L.Get("RatesWindow.ReferenceValueQuotedAmountOfficialNominalValue");
                TraceInformation.Text = rates.Endpoint;
                TraceInformation.ToolTip = L.Get("RatesWindow.Sha256OfTheOriginalContent") + rates.PayloadSha256;
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
                row.EffectiveDate.ToString(L.DatePattern, DisplayCulture), row.RequestedDate.ToString(L.DatePattern, DisplayCulture),
                row.RetrievedUtc.ToLocalTime().ToString(L.DatePattern + " HH:mm:ss", DisplayCulture), row.PayloadSha256)).ToArray();
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
        if (source != App.Settings.Current.RateSource && !App.SaveSettings(App.Settings.Current with { RateSource = source }, this))
        {
            RefreshView();
            return;
        }
        if (_session.State.Provider != App.SelectedRateProvider)
            _ = _session.SelectAsync(App.SelectedRateProvider, _session.State.Date, _lifetime.Token);
    }

    private bool TryDate(out DateOnly date)
    {
        if (DateOnly.TryParseExact(QueryDate.Text, L.DatePattern, DisplayCulture, DateTimeStyles.None, out date) && date != default) return true;
        ErrorText.Text = L.Get("RatesWindow.InvalidDateUseMmDdYyyy");
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
                throw new ArgumentException(L.Get("RatesWindow.ChooseTwoCurrenciesOfPresentation"));
            var amount = MoneyInput.ParseLocalized(ConvertInput.Text, source, allowNegative: true);
            var state = _session.State;
            decimal estimate;
            if (source.Code == target.Code) estimate = amount;
            else
            {
                if (state.Rates is null) throw new RateException(RateFailure.Unavailable, L.Get("RatesWindow.ExchangeRateNotAvailable"));
                estimate = RateConverter.ConvertAmount(amount, source.Code, target, state.Rates);
            }
            ConversionResult.Text = MoneyFormatter.Format(amount, source) + " → " + MoneyFormatter.Format(estimate, target) +
                (source.Code == target.Code ? L.Get("RatesWindow.IdentityUnchanged") : L.Format("RatesWindow.Estimate0PriceOf1MmDdYyyy", (RateRules.ProviderName(state.Provider)), (state.Rates!.EffectiveDate)));
        }
        catch (Exception error) when (error is ArgumentException or OverflowException or RateException)
        {
            ErrorText.Text = error.Message;
        }
    }

    private sealed record QuoteRow(string Code, string Name, string Nominal, string Value, string Reference, string Note);
    private sealed record HistoryRow(string Effective, string Requested, string Retrieved, string Hash);
}
