using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using PolishOpenData.Internal;

namespace PolishOpenData.BialaLista.Offline;

/// <summary>HttpClient-based implementation of <see cref="IVatWhitelistOffline"/>.</summary>
public sealed class VatWhitelistOffline : IVatWhitelistOffline, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly Uri _baseAddress;
    private readonly TimeProvider _timeProvider;
    private readonly OfflineIndexStore _store;
    private readonly bool _ownsStore;

    /// <summary>
    /// Creates a client with its own store of prepared indexes. The <paramref name="httpClient"/> is not disposed by this
    /// class; this client is, and it closes the store's open index files.
    /// </summary>
    /// <param name="httpClient">
    /// Transport. Set its <see cref="HttpClient.Timeout"/> to <see cref="Timeout.InfiniteTimeSpan"/> (or a very long time):
    /// the flat file is about 250 MB and the server can be slow, so the default 100 seconds would abort a download that is
    /// progressing; cancel through the call's <see cref="CancellationToken"/> instead.
    /// </param>
    /// <param name="options">Options; defaults when null.</param>
    /// <param name="timeProvider">Clock for "today" and the retention of prepared dates; system clock when null.</param>
    /// <remarks>
    /// Clients created this way do not share downloads or open files with each other, so create one and keep it, or use
    /// <see cref="VatWhitelistOfflineServiceCollectionExtensions.AddVatWhitelistOffline"/>, which shares one store between all
    /// resolved clients.
    /// </remarks>
    public VatWhitelistOffline(HttpClient httpClient, VatWhitelistOfflineOptions? options = null, TimeProvider? timeProvider = null)
        : this(httpClient, options ?? new VatWhitelistOfflineOptions(), timeProvider ?? TimeProvider.System, null)
    {
    }

    // store == null: this client owns a private store; otherwise the store is the container's singleton and is never disposed here.
    internal VatWhitelistOffline(HttpClient httpClient, VatWhitelistOfflineOptions options, TimeProvider timeProvider, OfflineIndexStore? store)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _baseAddress = options.BaseAddress;
        _timeProvider = timeProvider;
        _ownsStore = store is null;
        _store = store ?? new OfflineIndexStore(options.ResolveDataDirectory(), options.DatesToKeep, options.LockTimeout, timeProvider);
    }

    /// <inheritdoc/>
    public async Task<VatWhitelistOfflineCheck> CheckAsync(Nip nip, Nrb account, DateOnly? date = null, CancellationToken cancellationToken = default)
    {
        RequireValue(nip.IsEmpty, nameof(nip));
        RequireValue(account.IsEmpty, nameof(account));
        var index = await GetIndexAsync(date, null, cancellationToken).ConfigureAwait(false);
        return Lookup(index, nip, account);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<VatWhitelistOfflineCheck>> CheckManyAsync(IEnumerable<(Nip Nip, Nrb Account)> pairs, DateOnly? date = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        var list = pairs.ToArray();
        foreach (var (nip, account) in list)
        {
            RequireValue(nip.IsEmpty, nameof(pairs));
            RequireValue(account.IsEmpty, nameof(pairs));
        }

        var day = ResolveDate(date);
        if (list.Length == 0)
        {
            return Array.Empty<VatWhitelistOfflineCheck>();
        }

        var index = await GetIndexAsync(day, null, cancellationToken).ConfigureAwait(false);
        var results = new VatWhitelistOfflineCheck[list.Length];
        // The hashing is CPU-bound (SHA-512 rounds per candidate account); keep it off the caller's thread.
        try
        {
            await Task.Run(
                () => Parallel.For(
                    0,
                    list.Length,
                    new ParallelOptions { CancellationToken = cancellationToken },
                    i => results[i] = Lookup(index, list[i].Nip, list[i].Account)),
                cancellationToken).ConfigureAwait(false);
        }
        catch (AggregateException ex)
        {
            // Callers see the same exception as from CheckAsync, not Parallel.For's wrapper; when several lookups fail
            // at once they fail for the same reason (a broken or closed index), so the first one stands for all.
            ExceptionDispatchInfo.Capture(ex.InnerExceptions[0]).Throw();
        }
        return results;
    }

    /// <inheritdoc/>
    public async Task<VatWhitelistFileInfo> PrepareAsync(DateOnly? date = null, IProgress<VatWhitelistPreparationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var index = await GetIndexAsync(date, progress, cancellationToken).ConfigureAwait(false);
        return index.Info;
    }

    /// <summary>Closes the index files of a client created with the public constructor; does nothing for a client from dependency injection, whose store belongs to the container.</summary>
    public void Dispose()
    {
        if (_ownsStore)
        {
            _store.Dispose();
        }
    }

    private static void RequireValue(bool isEmpty, string name)
    {
        if (isEmpty)
        {
            throw new ArgumentException("The value is empty (default).", name);
        }
    }

    private static VatWhitelistOfflineCheck Lookup(WhitelistIndex index, Nip nip, Nrb account)
    {
        var (status, viaMask) = index.Lookup(nip.ToString(), account.ToString());
        return new VatWhitelistOfflineCheck(nip, account, status, viaMask, index.Info.FileDate, index.Info.GeneratedOn);
    }

    // The window is checked before any I/O.
    private DateOnly ResolveDate(DateOnly? date)
    {
        var today = WarsawTime.Today(_timeProvider);
        var day = date ?? today;
        OfflineDateWindow.Check(day, today);
        return day;
    }

    private Task<WhitelistIndex> GetIndexAsync(DateOnly? date, IProgress<VatWhitelistPreparationProgress>? progress, CancellationToken cancellationToken)
    {
        var day = ResolveDate(date);
        var today = WarsawTime.Today(_timeProvider);
        return _store.GetAsync(
            day,
            (path, p, ct) => FlatFileDownloader.DownloadAsync(_httpClient, _baseAddress, day, today, path, p, ct),
            progress,
            cancellationToken);
    }
}
