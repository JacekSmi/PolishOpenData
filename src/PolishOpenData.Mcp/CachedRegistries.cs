using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using PolishOpenData.BialaLista;
using PolishOpenData.Krs;

namespace PolishOpenData.Mcp;

/// <summary>
/// Registry calls with an in-memory cache: Biała Lista data changes daily (24 h) and every search counts against a
/// 100-per-day IP limit; KRS extracts are cached for 1 h. Failures are never cached. Concurrent identical requests
/// share one upstream call (<see cref="InFlightCalls"/>).
/// </summary>
internal sealed class CachedRegistries(IKrsClient krs, IBialaListaClient vat, IMemoryCache cache, InFlightCalls inFlight)
{
    private static readonly TimeSpan VatLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan KrsLifetime = TimeSpan.FromHours(1);

    public Task<BialaListaResult<VatSubject?>> FindByNipAsync(Nip nip, DateOnly? date, CancellationToken cancellationToken) =>
        GetOrAddAsync("vat:nip:" + nip.ToString() + ":" + Key(date), VatLifetime, ct => vat.FindByNipAsync(nip, date, ct), cancellationToken);

    public Task<BialaListaResult<VatSubject?>> FindByRegonAsync(Regon regon, DateOnly? date, CancellationToken cancellationToken) =>
        GetOrAddAsync("vat:regon:" + regon.ToString() + ":" + Key(date), VatLifetime, ct => vat.FindByRegonAsync(regon, date, ct), cancellationToken);

    public Task<BialaListaResult<bool>> CheckAsync(Nip nip, Nrb account, DateOnly? date, CancellationToken cancellationToken) =>
        GetOrAddAsync("vat:check:nip:" + nip.ToString() + ":" + account.ToString() + ":" + Key(date), VatLifetime, ct => vat.CheckBankAccountAsync(nip, account, date, ct), cancellationToken);

    public Task<BialaListaResult<bool>> CheckAsync(Regon regon, Nrb account, DateOnly? date, CancellationToken cancellationToken) =>
        GetOrAddAsync("vat:check:regon:" + regon.ToString() + ":" + account.ToString() + ":" + Key(date), VatLifetime, ct => vat.CheckBankAccountAsync(regon, account, date, ct), cancellationToken);

    public Task<KrsResult<KrsCurrentExtract>> GetCurrentExtractAsync(KrsNumber number, CancellationToken cancellationToken) =>
        GetOrAddAsync("krs:current:" + number.ToString(), KrsLifetime, ct => krs.GetCurrentExtractAsync(number, cancellationToken: ct), cancellationToken);

    public Task<KrsResult<KrsFullExtract>> GetFullExtractAsync(KrsNumber number, CancellationToken cancellationToken) =>
        GetOrAddAsync("krs:full:" + number.ToString(), KrsLifetime, ct => krs.GetFullExtractAsync(number, cancellationToken: ct), cancellationToken);

    private async Task<T> GetOrAddAsync<T>(string key, TimeSpan lifetime, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken)
        where T : class
    {
        if (cache.TryGetValue(key, out T? cached) && cached is not null)
        {
            return cached;
        }

        // The shared call runs without this caller's token, so a caller that stops waiting does not cancel it for
        // the others; HttpClient's timeout and the resilience handler's timeouts bound it instead, and it is
        // cancelled when the server shuts down.
        var shared = inFlight.GetOrStart(key, async stopping =>
        {
            if (cache.TryGetValue(key, out T? hit) && hit is not null)
            {
                return hit;   // another call finished and cached it after this caller's lookup above
            }

            var value = await factory(stopping).ConfigureAwait(false);
            cache.Set(key, value, lifetime);   // before the call leaves the in-flight map, so no caller misses both
            return value;
        });
        return await shared.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string Key(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "today";
}
