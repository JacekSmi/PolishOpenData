using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PolishOpenData.Bir.Wire;

namespace PolishOpenData.Bir;

/// <summary>HttpClient-based implementation of <see cref="IRegonClient"/> for the GUS BIR1.2 web service.</summary>
/// <remarks>
/// <para>
/// The login session and the request limiter are shared state: GUS counts requests per application instance, and a
/// session id is valid only from the IP address that created it. Register the client once per process with
/// <c>AddRegonClient</c>, which keeps one session and one limiter in singletons (the typed client itself is transient).
/// A client created with the public constructor gets a private session and a private limiter that never see requests
/// made by other instances.
/// </para>
/// <para>
/// The client never logs out by itself; call <c>LogoutAsync</c> when the process stops. It sets no HTTP timeout: the
/// timeout of the <see cref="HttpClient"/> you pass in applies.
/// </para>
/// </remarks>
public sealed class RegonClient : IRegonClient
{
    // research-gus-bir/api-facts.md 5.3 (parameter table): a batch search takes at most 20 identifiers.
    private const int MaxBatchSize = 20;
    private readonly BirService _service;
    private readonly BirSession _session;
    private readonly TimeProvider _timeProvider;
    private readonly RegonEnvironment _environment;

    /// <summary>Creates a client with a private session and a private request limiter. The <paramref name="httpClient"/> is not disposed by this class.</summary>
    /// <param name="httpClient">Transport.</param>
    /// <param name="options">Options; the defaults when null, which means the production environment and therefore needs <see cref="RegonClientOptions.UserKey"/>.</param>
    /// <param name="timeProvider">Clock for the session lifetime, the limiter and <see cref="RegonResult{T}.RetrievedAt"/>; the system clock when null.</param>
    /// <exception cref="ArgumentNullException"><paramref name="httpClient"/> is null.</exception>
    /// <exception cref="ArgumentException">An option has an invalid value (a non-positive session lifetime, a negative wait, an undefined enum value, or a base address that is not absolute https).</exception>
    /// <exception cref="InvalidOperationException">The environment is production and <see cref="RegonClientOptions.UserKey"/> is not set, or <see cref="RegonClientOptions.BaseAddress"/> points at the GUS service that the environment does not name (the test address with production, the production address with test).</exception>
    public RegonClient(HttpClient httpClient, RegonClientOptions? options = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        var resolved = options ?? new RegonClientOptions();
        resolved.Validate();
        var environment = resolved.ResolveEnvironment();
        var clock = timeProvider ?? TimeProvider.System;
        // Private state: this client shares its session and its limiter with no other client.
        var session = new BirSession(clock, resolved.SessionLifetime);
        _timeProvider = clock;
        _environment = environment;
        _session = session;
        _service = CreateService(httpClient, resolved, session, new RegonRateLimiter(clock, resolved.RateLimit, resolved.MaxRateLimitWait));
    }

    // Used by AddRegonClient and by the tests: the session and the limiter come from outside, so they can be shared.
    internal RegonClient(HttpClient httpClient, RegonClientOptions options, TimeProvider timeProvider, BirSession session, RegonRateLimiter limiter)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(limiter);
        options.Validate();
        _timeProvider = timeProvider;
        _environment = options.ResolveEnvironment();
        _session = session;
        _service = CreateService(httpClient, options, session, limiter);
    }

    /// <inheritdoc/>
    public async Task<RegonResult<IReadOnlyList<RegonEntity>>> FindByNipAsync(Nip nip, CancellationToken cancellationToken = default)
    {
        RequireValue(nip.IsEmpty, nameof(nip));
        return await SearchOneAsync(BirSearchField.Nip, nip.ToString(), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<RegonResult<IReadOnlyList<RegonEntity>>> FindByRegonAsync(Regon regon, CancellationToken cancellationToken = default)
    {
        RequireValue(regon.IsEmpty, nameof(regon));
        return await SearchOneAsync(BirSearchField.Regon, regon.ToString(), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<RegonResult<IReadOnlyList<RegonEntity>>> FindByKrsAsync(KrsNumber krs, CancellationToken cancellationToken = default)
    {
        RequireValue(krs.IsEmpty, nameof(krs));
        return await SearchOneAsync(BirSearchField.Krs, krs.ToString(), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<RegonResult<IReadOnlyList<RegonBatchEntry>>> FindByNipsAsync(IReadOnlyCollection<Nip> nips, CancellationToken cancellationToken = default)
    {
        var ids = DistinctIdentifiers(nips, static n => n.IsEmpty, static n => n.ToString(), nameof(nips));
        var found = new Dictionary<string, List<RegonEntity>>(StringComparer.Ordinal);
        await SearchInChunksAsync(ids, BirSearchField.Nipy, static e => e.Nip?.ToString(), found, cancellationToken).ConfigureAwait(false);
        return Wrap<IReadOnlyList<RegonBatchEntry>>(Entries(ids, found));
    }

    /// <inheritdoc/>
    public async Task<RegonResult<IReadOnlyList<RegonBatchEntry>>> FindByRegonsAsync(IReadOnlyCollection<Regon> regons, CancellationToken cancellationToken = default)
    {
        var ids = DistinctIdentifiers(regons, static r => r.IsEmpty, static r => r.ToString(), nameof(regons));
        var entityIds = new List<string>();
        var localUnitIds = new List<string>();
        foreach (var id in ids)
        {
            (id.Length == 14 ? localUnitIds : entityIds).Add(id);
        }

        var found = new Dictionary<string, List<RegonEntity>>(StringComparer.Ordinal);
        await SearchInChunksAsync(entityIds, BirSearchField.Regony9zn, static e => e.Regon?.ToString(), found, cancellationToken).ConfigureAwait(false);
        await SearchInChunksAsync(localUnitIds, BirSearchField.Regony14zn, static e => e.Regon?.ToString(), found, cancellationToken).ConfigureAwait(false);
        return Wrap<IReadOnlyList<RegonBatchEntry>>(Entries(ids, found));
    }

    /// <inheritdoc/>
    public async Task<RegonResult<IReadOnlyList<RegonBatchEntry>>> FindByKrsNumbersAsync(IReadOnlyCollection<KrsNumber> krsNumbers, CancellationToken cancellationToken = default)
    {
        var ids = DistinctIdentifiers(krsNumbers, static k => k.IsEmpty, static k => k.ToString(), nameof(krsNumbers));
        var entries = new List<RegonBatchEntry>(ids.Count);
        foreach (var id in ids)
        {
            // api-facts.md 5.3 (row fields): a search row has no KRS field, so a Krsy batch could not be matched back to the requested numbers; one Krs search per number.
            var rows = await _service.SearchAsync(BirSearchField.Krs, id, 1, cancellationToken).ConfigureAwait(false);
            entries.Add(new RegonBatchEntry(id, MapEntities(rows)));
        }

        return Wrap<IReadOnlyList<RegonBatchEntry>>(entries);
    }

    private static BirService CreateService(HttpClient httpClient, RegonClientOptions options, BirSession session, RegonRateLimiter limiter) =>
        new(new BirTransport(httpClient), options.ResolveAddress(), options.ResolveUserKey(), session, limiter);

    private async Task<RegonResult<IReadOnlyList<RegonEntity>>> SearchOneAsync(BirSearchField field, string value, CancellationToken cancellationToken)
    {
        var rows = await _service.SearchAsync(field, value, 1, cancellationToken).ConfigureAwait(false);
        return Wrap<IReadOnlyList<RegonEntity>>(MapEntities(rows));
    }

    // Sends the identifiers in chunks of at most 20 (every id counts toward the minute and hour limits) and files each
    // answered row under the identifier it carries; GUS leaves unknown identifiers out, so some ids get no row (api-facts.md 5.3, [OBS call 7]).
    private async Task SearchInChunksAsync(
        List<string> ids,
        BirSearchField field,
        Func<RegonEntity, string?> identifierOf,
        Dictionary<string, List<RegonEntity>> found,
        CancellationToken cancellationToken)
    {
        for (var offset = 0; offset < ids.Count; offset += MaxBatchSize)
        {
            var count = Math.Min(MaxBatchSize, ids.Count - offset);
            var chunk = new string[count];
            for (var i = 0; i < count; i++)
            {
                chunk[i] = ids[offset + i];
            }

            var rows = await _service.SearchAsync(field, string.Join(",", chunk), count, cancellationToken).ConfigureAwait(false);
            foreach (var row in rows)
            {
                var entity = RegonMapper.MapEntity(row);
                if (identifierOf(entity) is not { } id)
                {
                    continue;   // a row without a valid identifier cannot be matched to a request
                }

                if (!found.TryGetValue(id, out var list))
                {
                    list = [];
                    found[id] = list;
                }

                list.Add(entity);
            }
        }
    }

    private static List<RegonEntity> MapEntities(IReadOnlyList<BirRow> rows)
    {
        var entities = new List<RegonEntity>(rows.Count);
        foreach (var row in rows)
        {
            entities.Add(RegonMapper.MapEntity(row));
        }

        return entities;
    }

    // One entry per requested identifier, in request order; rows that match no requested identifier are dropped.
    private static List<RegonBatchEntry> Entries(List<string> ids, Dictionary<string, List<RegonEntity>> found)
    {
        var entries = new List<RegonBatchEntry>(ids.Count);
        foreach (var id in ids)
        {
            entries.Add(new RegonBatchEntry(id, found.TryGetValue(id, out var list) ? list : []));
        }

        return entries;
    }

    private static List<string> DistinctIdentifiers<T>(IReadOnlyCollection<T> items, Func<T, bool> isEmpty, Func<T, string> format, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(items, parameterName);
        if (items.Count == 0)
        {
            throw new ArgumentException("At least one identifier is required.", parameterName);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ids = new List<string>(items.Count);
        foreach (var item in items)
        {
            if (isEmpty(item))
            {
                throw new ArgumentException("The batch contains an empty identifier.", parameterName);
            }

            var id = format(item);
            if (seen.Add(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    private RegonResult<T> Wrap<T>(T value) => new(value, _environment, _timeProvider.GetUtcNow(), _session.DataAsOf);

    private static void RequireValue(bool isEmpty, string parameterName)
    {
        if (isEmpty)
        {
            throw new ArgumentException("The identifier is empty.", parameterName);
        }
    }
}
