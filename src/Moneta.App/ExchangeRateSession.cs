using Moneta.Core;

namespace Moneta.App;

public sealed record RateSessionState(RateProvider Provider, DateOnly Date, OfficialRateSet? Rates,
    bool IsRefreshing, bool FromCache, Func<string> Describe)
{
    public string Message => Describe();
    public bool IsPersistent { get; init; }
}

// Nonfinancial session. No account or amount crosses this boundary.
public sealed class ExchangeRateSession : IDisposable
{
    private readonly IRateCache _cache;
    private readonly IReadOnlyDictionary<RateProvider, IOfficialRateSource> _sources;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _deadline;
    private readonly object _gate = new();
    private CancellationTokenSource? _operation;
    private long _generation;
    private bool _disposed;
    private RateSessionState _state;

    public ExchangeRateSession(IRateCache cache, IEnumerable<IOfficialRateSource> sources,
        TimeProvider? clock = null, TimeSpan? deadline = null)
    {
        _cache = cache;
        _sources = sources.ToDictionary(source => source.Provider);
        _clock = clock ?? TimeProvider.System;
        _deadline = deadline ?? TimeSpan.FromSeconds(20);
        if (_deadline <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(deadline));
        _state = new(RateProvider.Nbrb, Today, null, false, true, () => L.Get("ExchangeRateSession.UnloadedRates"));
    }

    public DateOnly Today => DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
    public RateSessionState State { get { lock (_gate) return _state; } }
    public event EventHandler? Changed;
    public Task InitializeAsync(RateProvider provider, bool automatic, CancellationToken cancellationToken = default) =>
        Begin(provider, Today, automatic, false, cancellationToken);
    public Task SelectAsync(RateProvider provider, DateOnly date, CancellationToken cancellationToken = default) =>
        Begin(provider, date, false, false, cancellationToken);
    public Task RefreshAsync(RateProvider provider, DateOnly date, CancellationToken cancellationToken = default) =>
        Begin(provider, date, true, true, cancellationToken);
    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var state = State;
        return Begin(state.Provider, state.Date, true, true, cancellationToken);
    }
    public void Cancel() { lock (_gate) _operation?.Cancel(); }
    public Task<IReadOnlyList<RateHistoryEntry>> HistoryAsync(CancellationToken cancellationToken = default)
    {
        var provider = State.Provider;
        return Task.Run(() => _cache.ReadHistory(provider, cancellationToken: cancellationToken), cancellationToken);
    }

    private Task Begin(RateProvider provider, DateOnly date, bool download, bool force, CancellationToken token)
    {
        _ = RateRules.ReferenceCurrency(provider);
        if (date == default) throw new ArgumentException(L.Get("ExchangeRateSession.ChooseAValidDate"), nameof(date));
        CancellationTokenSource operation;
        long generation;
        OfficialRateSet? previous;
        bool previousPersistent;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _operation?.Cancel();
            operation = CancellationTokenSource.CreateLinkedTokenSource(token);
            _operation = operation;
            generation = ++_generation;
            previous = _state.Provider == provider && _state.Date == date ? _state.Rates : null;
            previousPersistent = previous is not null && _state.IsPersistent;
        }
        Publish(generation, new(provider, date, previous, true, true, () => L.Get("ExchangeRateSession.ReadingTheLocalCache")) { IsPersistent = previousPersistent });
        return RunAsync(generation, operation, provider, date, previous, previousPersistent, download, force);
    }

    private async Task RunAsync(long generation, CancellationTokenSource operation, RateProvider provider,
        DateOnly date, OfficialRateSet? previous, bool previousPersistent, bool download, bool force)
    {
        var token = operation.Token;
        var cached = previous;
        var cachedOnDisk = previousPersistent;
        var downloadedPersistent = false;
        var cacheWarning = string.Empty;
        var storeFailed = false;
        string CacheWarning() => (storeFailed ? L.Get("ExchangeRateSession.RatesReceivedButCacheNotRecorded") : string.Empty)
            + L.Diagnostic(cacheWarning);
        try
        {
            try
            {
                var disk = await Task.Run(() => _cache.Find(provider, date, cancellationToken: token), token);
                if (disk is not null && (cached is null || disk.EffectiveDate > cached.EffectiveDate ||
                    disk.EffectiveDate == cached.EffectiveDate && disk.RetrievedUtc >= cached.RetrievedUtc))
                {
                    cached = disk;
                    cachedOnDisk = true;
                }
                else cachedOnDisk = disk is not null && cached?.PayloadSha256 == disk.PayloadSha256;
            }
            catch (RateException error) { cachedOnDisk = false; cacheWarning = error.Message; }
            token.ThrowIfCancellationRequested();
            var fresh = cached is not null && (date < Today || cached.RequestedDate == date &&
                _clock.GetUtcNow() >= cached.RetrievedUtc && _clock.GetUtcNow() - cached.RetrievedUtc <= TimeSpan.FromHours(6));
            if (!download || !force && fresh)
            {
                Publish(generation, new(provider, date, cached, false, true,
                    () => CacheMessage(cached, CacheWarning(), cachedOnDisk)) { IsPersistent = cachedOnDisk });
                return;
            }
            Publish(generation, new(provider, date, cached, true, true,
                () => L.Get("ExchangeRateSession.HttpsUpdateNoAccountBalanceOrTransactionIs")) { IsPersistent = cachedOnDisk });
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(_deadline);
            RateDownload? response = null;
            try
            {
                if (!_sources.TryGetValue(provider, out var source))
                    throw new RateException(RateFailure.Unavailable, L.GetCanonical("ExchangeRateSession.SupplierNotAvailable"));
                for (var days = 0; days <= 7 && date.DayNumber > days; days++)
                {
                    try { response = await source.FetchAsync(date.AddDays(-days), deadline.Token); break; }
                    catch (RateException error) when (error.Failure == RateFailure.Unavailable && days < 7) { }
                }
                if (response is null) throw new RateException(RateFailure.Unavailable, L.GetCanonical("ExchangeRateSession.NoPublicationInTheEightDatesInterviewed"));
                if (response.Rates.Provider != provider)
                    throw new RateException(RateFailure.InvalidResponse, L.GetCanonical("ExchangeRateSession.TheProviderOfTheAnswerDoesNotCorrespond"));
                RateRules.Validate(response.Rates);
                try
                {
                    await Task.Run(() => _cache.Store(response, deadline.Token), deadline.Token);
                    downloadedPersistent = true;
                }
                catch (RateException error) { cacheWarning = error.Message; storeFailed = true; }
                deadline.Token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new RateException(RateFailure.Timeout, L.GetCanonical("ExchangeRateSession.OverallDiscountTimeExceeded"));
            }
            var rates = response.Rates;
            if (rates.EffectiveDate > date || date.DayNumber - rates.EffectiveDate.DayNumber > RateRules.MaximumAgeDays)
            {
                Publish(generation, new(provider, date, cached, false, true,
                    () => L.Format("ExchangeRateSession.PublicationReceivedFor0MmDdYyyyNot", rates.EffectiveDate, date)
                        + CacheMessage(cached, CacheWarning(), cachedOnDisk)) { IsPersistent = cachedOnDisk });
                return;
            }
            if (cached is not null && rates.EffectiveDate < cached.EffectiveDate)
            {
                Publish(generation, new(provider, date, cached, false, true,
                    () => L.Format("RateDisplay.KeptNewerCache", rates.EffectiveDate, cached.EffectiveDate)
                        + CacheMessage(cached, CacheWarning(), cachedOnDisk)) { IsPersistent = cachedOnDisk });
                return;
            }
            Publish(generation, new(provider, date, rates, false, false,
                () => L.Get("ExchangeRateSession.OfficialResponseReceivedAndValidated") + CacheWarning()) { IsPersistent = downloadedPersistent });
        }
        catch (OperationCanceledException)
        {
            Publish(generation, new(provider, date, cached, false, true,
                () => L.Get("ExchangeRateSession.UpdateCancelled") + CacheMessage(cached, CacheWarning(), cachedOnDisk)) { IsPersistent = cachedOnDisk });
        }
        catch (RateException error)
        {
            var key = error.Failure switch
            {
                RateFailure.Network or RateFailure.Timeout => "ExchangeRateSession.OfflineOrInaccessibleSupplier",
                RateFailure.Http => "RateDisplay.HttpError",
                RateFailure.InvalidResponse => "RateDisplay.InvalidResponse",
                _ => "ExchangeRateSession.RefreshFailed"
            };
            var message = error.Message;
            Publish(generation, new(provider, date, cached, false, true,
                () => L.Get(key) + L.Diagnostic(message) + " " + CacheMessage(cached, CacheWarning(), cachedOnDisk)) { IsPersistent = cachedOnDisk });
        }
        finally
        {
            lock (_gate) if (ReferenceEquals(_operation, operation)) _operation = null;
            operation.Dispose();
        }
    }

    private static string CacheMessage(OfficialRateSet? rates, string warning, bool persistent) =>
        (rates is null ? L.Get("ExchangeRateSession.RatesNotAvailableNoCacheApplicableInThe")
            : persistent ? L.Get("ExchangeRateSession.DiskCacheUsedConnectionNotVerifiedTheActual")
            : L.Get("ExchangeRateSession.RatesPreviouslyValidatedInMemoryOnlyPersistenceNot")) +
        (warning.Length == 0 ? string.Empty : " " + warning);

    private void Publish(long generation, RateSessionState state)
    {
        lock (_gate)
        {
            if (_disposed || generation != _generation) return;
            _state = state;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _generation++;
            _operation?.Cancel();
        }
    }
}
