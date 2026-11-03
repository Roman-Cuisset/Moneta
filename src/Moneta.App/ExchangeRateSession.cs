using Moneta.Core;

namespace Moneta.App;

public sealed record RateSessionState(RateProvider Provider, DateOnly Date, OfficialRateSet? Rates,
    bool IsRefreshing, bool FromCache, string Message)
{
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
        _state = new(RateProvider.Nbrb, Today, null, false, true, "Cours non chargés.");
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
        if (date == default) throw new ArgumentException("Choisissez une date valide.", nameof(date));
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
        Publish(generation, new(provider, date, previous, true, true, "Lecture du cache local…") { IsPersistent = previousPersistent });
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
                    CacheMessage(cached, cacheWarning, cachedOnDisk)) { IsPersistent = cachedOnDisk });
                return;
            }
            Publish(generation, new(provider, date, cached, true, true,
                "Actualisation HTTPS — aucun compte, solde ou mouvement n’est transmis.") { IsPersistent = cachedOnDisk });
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(_deadline);
            RateDownload? response = null;
            try
            {
                if (!_sources.TryGetValue(provider, out var source))
                    throw new RateException(RateFailure.Unavailable, "Fournisseur non disponible.");
                for (var days = 0; days <= 7 && date.DayNumber > days; days++)
                {
                    try { response = await source.FetchAsync(date.AddDays(-days), deadline.Token); break; }
                    catch (RateException error) when (error.Failure == RateFailure.Unavailable && days < 7) { }
                }
                if (response is null) throw new RateException(RateFailure.Unavailable, "Aucune publication dans les huit dates interrogées.");
                if (response.Rates.Provider != provider)
                    throw new RateException(RateFailure.InvalidResponse, "Le fournisseur de la réponse ne correspond pas au choix explicite.");
                RateRules.Validate(response.Rates);
                try
                {
                    await Task.Run(() => _cache.Store(response, deadline.Token), deadline.Token);
                    downloadedPersistent = true;
                }
                catch (RateException error) { cacheWarning = "Cours reçus, mais cache non enregistré : " + error.Message; }
                deadline.Token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new RateException(RateFailure.Timeout, "Délai global d’actualisation dépassé.");
            }
            var rates = response.Rates;
            if (rates.EffectiveDate > date || date.DayNumber - rates.EffectiveDate.DayNumber > RateRules.MaximumAgeDays)
            {
                Publish(generation, new(provider, date, cached, false, true,
                    $"Publication reçue pour le {rates.EffectiveDate:dd/MM/yyyy}, non applicable au {date:dd/MM/yyyy}. " + CacheMessage(cached, cacheWarning, cachedOnDisk)) { IsPersistent = cachedOnDisk });
                return;
            }
            Publish(generation, new(provider, date, rates, false, false,
                "Réponse officielle reçue et validée. " + cacheWarning) { IsPersistent = downloadedPersistent });
        }
        catch (OperationCanceledException)
        {
            Publish(generation, new(provider, date, cached, false, true,
                "Actualisation annulée. " + CacheMessage(cached, cacheWarning, cachedOnDisk)) { IsPersistent = cachedOnDisk });
        }
        catch (RateException error)
        {
            var prefix = error.Failure is RateFailure.Network or RateFailure.Timeout
                ? "Hors ligne ou fournisseur inaccessible. " : "Actualisation impossible. ";
            Publish(generation, new(provider, date, cached, false, true,
                prefix + error.Message + " " + CacheMessage(cached, cacheWarning, cachedOnDisk)) { IsPersistent = cachedOnDisk });
        }
        finally
        {
            lock (_gate) if (ReferenceEquals(_operation, operation)) _operation = null;
            operation.Dispose();
        }
    }

    private static string CacheMessage(OfficialRateSet? rates, string warning, bool persistent) =>
        (rates is null ? "Cours indisponible : aucun cache applicable dans les 31 jours précédents."
            : persistent ? "Cache disque utilisé ; connexion non vérifiée. La date d’effet réelle est conservée."
            : "Cours précédemment validés en mémoire uniquement ; persistance non confirmée pour le redémarrage.") +
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
