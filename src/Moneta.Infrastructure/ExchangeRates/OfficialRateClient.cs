using System.Globalization;
using System.Net;
using Moneta.Core;

namespace Moneta.Infrastructure.ExchangeRates;

public sealed class OfficialRateClient : IOfficialRateSource, IDisposable
{
    private readonly HttpClient client;
    private readonly TimeSpan timeout;

    public RateProvider Provider { get; }

    public OfficialRateClient(RateProvider provider, HttpMessageHandler? handler = null, TimeSpan? timeout = null)
    {
        if (provider is not (RateProvider.Nbrb or RateProvider.Cbr))
            throw new ArgumentOutOfRangeException(nameof(provider));
        this.timeout = timeout ?? TimeSpan.FromSeconds(10);
        if (this.timeout <= TimeSpan.Zero || this.timeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        Provider = provider;
        client = new HttpClient(handler ?? new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = true,
            Credentials = null,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        }, disposeHandler: true)
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan
        };
    }

    public async Task<RateDownload> FetchAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        if (date == default) throw new ArgumentOutOfRangeException(nameof(date));
        cancellationToken.ThrowIfCancellationRequested();
        var endpoint = Provider switch
        {
            RateProvider.Nbrb => "https://api.nbrb.by/exrates/rates?ondate=" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "&periodicity=0",
            RateProvider.Cbr => "https://www.cbr.ru/scripts/XML_daily.asp?date_req=" + date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            _ => throw new ArgumentOutOfRangeException(nameof(Provider))
        };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            // Redirects are HTTP failures; never contact a supplied Location or another provider.
            if (!response.IsSuccessStatusCode)
                throw new RateException(RateFailure.Http, L.FormatCanonical("OfficialRateClient.TheOfficialServiceRepliedHttp0", ((int)response.StatusCode)));
            if (response.Content.Headers.ContentLength > OfficialRateParsers.MaximumPayloadBytes)
                throw new RateException(RateFailure.InvalidResponse, L.GetCanonical("OfficialRateClient.TheRateResponseExceedsTheAuthorizedSize"));
            await using var body = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var block = new byte[8192];
            while (true)
            {
                var read = await body.ReadAsync(block, deadline.Token).ConfigureAwait(false);
                if (read == 0) break;
                if (buffer.Length + read > OfficialRateParsers.MaximumPayloadBytes)
                    throw new RateException(RateFailure.InvalidResponse, L.GetCanonical("OfficialRateClient.TheRateResponseExceedsTheAuthorizedSize"));
                buffer.Write(block, 0, read);
            }
            deadline.Token.ThrowIfCancellationRequested();
            var download = OfficialRateParsers.Parse(Provider, date, DateTimeOffset.UtcNow, endpoint,
                buffer.GetBuffer().AsMemory(0, checked((int)buffer.Length)));
            deadline.Token.ThrowIfCancellationRequested();
            return download;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (OperationCanceledException ex) when (deadline.IsCancellationRequested)
        {
            throw new RateException(RateFailure.Timeout, L.GetCanonical("OfficialRateClient.TheResponseTimeOfTheOfficialServiceIs"), ex);
        }
        catch (HttpRequestException ex)
        {
            throw new RateException(RateFailure.Network, L.GetCanonical("OfficialRateClient.TheOfficialServiceIsInaccessible"), ex);
        }
        catch (IOException ex)
        {
            throw new RateException(RateFailure.Network, L.GetCanonical("OfficialRateClient.TheResponseOfTheOfficialServiceWasInterrupted"), ex);
        }
    }

    public void Dispose() => client.Dispose();
}
