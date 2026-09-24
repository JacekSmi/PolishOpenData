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
/// 100-per-day IP limit; KRS extracts are cached for 1 h. Failures are never cached.
/// </summary>
internal sealed class CachedRegistries(IKrsClient krs, IBialaListaClient vat, IMemoryCache cache)
{
    private static readonly TimeSpan VatLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan KrsLifetime = TimeSpan.FromHours(1);

    public Task<BialaListaResult<VatSubject?>> FindByNipAsync(Nip nip, DateOnly? date, CancellationToken cancellationToken) =>
        GetOrAddAsync("vat:nip:" + nip.ToString() + ":" + Key(date), VatLifetime, () => vat.FindByNipAsync(nip, date, cancellationToken));

    public Task<BialaListaResult<VatSubject?>> FindByRegonAsync(Regon regon, DateOnly? date, CancellationToken cancellationToken) =>
        GetOrAddAsync("vat:regon:" + regon.ToString() + ":" + Key(date), VatLifetime, () => vat.FindByRegonAsync(regon, date, cancellationToken));

    public Task<BialaListaResult<bool>> CheckAsync(Nip nip, Nrb account, DateOnly? date, CancellationToken cancellationToken) =>
        GetOrAddAsync("vat:check:nip:" + nip.ToString() + ":" + account.ToString() + ":" + Key(date), VatLifetime, () => vat.CheckBankAccountAsync(nip, account, date, cancellationToken));

    public Task<BialaListaResult<bool>> CheckAsync(Regon regon, Nrb account, DateOnly? date, CancellationToken cancellationToken) =>
        GetOrAddAsync("vat:check:regon:" + regon.ToString() + ":" + account.ToString() + ":" + Key(date), VatLifetime, () => vat.CheckBankAccountAsync(regon, account, date, cancellationToken));

    public Task<KrsResult<KrsCurrentExtract>> GetCurrentExtractAsync(KrsNumber number, CancellationToken cancellationToken) =>
        GetOrAddAsync("krs:current:" + number.ToString(), KrsLifetime, () => krs.GetCurrentExtractAsync(number, cancellationToken: cancellationToken));

    public Task<KrsResult<KrsFullExtract>> GetFullExtractAsync(KrsNumber number, CancellationToken cancellationToken) =>
        GetOrAddAsync("krs:full:" + number.ToString(), KrsLifetime, () => krs.GetFullExtractAsync(number, cancellationToken: cancellationToken));

    private async Task<T> GetOrAddAsync<T>(string key, TimeSpan lifetime, Func<Task<T>> factory)
        where T : class
    {
        if (cache.TryGetValue(key, out T? cached) && cached is not null)
        {
            return cached;
        }

        var value = await factory().ConfigureAwait(false);
        cache.Set(key, value, lifetime);
        return value;
    }

    private static string Key(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "today";
}
