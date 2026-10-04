using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PolishOpenData.Bir;

/// <summary>
/// Client for the GUS REGON register (the BIR1.2 web service): search by NIP, REGON or KRS number, batches of up to 20
/// identifiers, full reports of legal persons, sole traders and local units, and the date of the data.
/// Unofficial; not affiliated with GUS or the Polish government.
/// </summary>
/// <remarks>
/// <para>
/// You need your own free GUS key (<see cref="RegonClientOptions.UserKey"/>). The public test environment
/// (<see cref="RegonEnvironment.Test"/>) works without one but serves an old, anonymised copy of the register;
/// every result says where it came from (<see cref="RegonResult{T}.Environment"/>).
/// </para>
/// <para>
/// GUS publishes limits per second, per minute and per hour, and
/// forbids enumerating consecutive identifiers. The client throttles itself (<see cref="RegonClientOptions.RateLimit"/>):
/// it counts calls per second (a batch of up to 20 identifiers is one call) and identifiers per minute and per hour. Whether GUS also counts every identifier of a batch
/// toward its per-second limit is not verified; if batches of 20 are throttled, send smaller ones.
/// "Not found" is data, not an error: a single search returns an empty list, a batch search returns one entry per
/// requested identifier, empty when GUS did not answer for it.
/// </para>
/// <para>
/// A key that GUS rejects makes the first call throw <see cref="RegonAuthenticationException"/>, and so does every later call
/// (each costs one login request): stop on that exception instead of retrying.
/// </para>
/// </remarks>
public interface IRegonClient
{
    /// <summary>
    /// Finds the register rows for a NIP. One NIP can match several rows: one per activity type ("silo") of a sole
    /// trader, and local units. The list is empty when GUS has no entity with this NIP.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="nip"/> is empty (<c>default(Nip)</c>).</exception>
    /// <exception cref="RegonAuthenticationException">GUS did not accept the configured key. This is never retried.</exception>
    /// <exception cref="PolishOpenDataApiException">
    /// GUS reported an error (its code is in <see cref="PolishOpenDataApiException.ErrorCode"/>), answered with
    /// something that is not readable, or rejected the request itself (a SOAP sender fault, or HTTP 400 or 415, usually
    /// a wrong <see cref="RegonClientOptions.BaseAddress"/>).
    /// </exception>
    /// <exception cref="QuotaExceededException">The local request limiter refuses the call (see <see cref="RegonClientOptions.RateLimit"/>).</exception>
    /// <exception cref="System.Net.Http.HttpRequestException">A transport failure, or a 5xx answer from GUS.</exception>
    Task<RegonResult<IReadOnlyList<RegonEntity>>> FindByNipAsync(Nip nip, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the register rows for a REGON (9 digits for an entity, 14 digits for a local unit). The list is empty when
    /// GUS has no entity with this REGON.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="regon"/> is empty (<c>default(Regon)</c>).</exception>
    /// <exception cref="RegonAuthenticationException">GUS did not accept the configured key. This is never retried.</exception>
    /// <exception cref="PolishOpenDataApiException">
    /// GUS reported an error (its code is in <see cref="PolishOpenDataApiException.ErrorCode"/>), answered with
    /// something that is not readable, or rejected the request itself (a SOAP sender fault, or HTTP 400 or 415, usually
    /// a wrong <see cref="RegonClientOptions.BaseAddress"/>).
    /// </exception>
    /// <exception cref="QuotaExceededException">The local request limiter refuses the call (see <see cref="RegonClientOptions.RateLimit"/>).</exception>
    /// <exception cref="System.Net.Http.HttpRequestException">A transport failure, or a 5xx answer from GUS.</exception>
    Task<RegonResult<IReadOnlyList<RegonEntity>>> FindByRegonAsync(Regon regon, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the register rows for a KRS number. The list is empty when GUS has no entity with this KRS number.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="krs"/> is empty (<c>default(KrsNumber)</c>).</exception>
    /// <exception cref="RegonAuthenticationException">GUS did not accept the configured key. This is never retried.</exception>
    /// <exception cref="PolishOpenDataApiException">
    /// GUS reported an error (its code is in <see cref="PolishOpenDataApiException.ErrorCode"/>), answered with
    /// something that is not readable, or rejected the request itself (a SOAP sender fault, or HTTP 400 or 415, usually
    /// a wrong <see cref="RegonClientOptions.BaseAddress"/>).
    /// </exception>
    /// <exception cref="QuotaExceededException">The local request limiter refuses the call (see <see cref="RegonClientOptions.RateLimit"/>).</exception>
    /// <exception cref="System.Net.Http.HttpRequestException">A transport failure, or a 5xx answer from GUS.</exception>
    Task<RegonResult<IReadOnlyList<RegonEntity>>> FindByKrsAsync(KrsNumber krs, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the register rows for any number of NIPs, sent to GUS in batches of at most 20 identifiers. The result has one
    /// entry per distinct requested NIP, in request order (duplicates are dropped); an entry is empty when GUS did not
    /// answer for it, because GUS silently leaves unknown identifiers out; rows that match none of the requested NIPs are
    /// ignored. Every identifier counts toward the per-minute and per-hour limits; a batch counts as one call toward the
    /// per-second limit.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="nips"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="nips"/> is empty or contains an empty identifier.</exception>
    /// <exception cref="RegonAuthenticationException">GUS did not accept the configured key. This is never retried.</exception>
    /// <exception cref="PolishOpenDataApiException">
    /// GUS reported an error (its code is in <see cref="PolishOpenDataApiException.ErrorCode"/>), answered with
    /// something that is not readable, or rejected the request itself (a SOAP sender fault, or HTTP 400 or 415, usually
    /// a wrong <see cref="RegonClientOptions.BaseAddress"/>).
    /// </exception>
    /// <exception cref="QuotaExceededException">The local request limiter refuses the call (see <see cref="RegonClientOptions.RateLimit"/>).</exception>
    /// <exception cref="System.Net.Http.HttpRequestException">A transport failure, or a 5xx answer from GUS.</exception>
    Task<RegonResult<IReadOnlyList<RegonBatchEntry>>> FindByNipsAsync(IReadOnlyCollection<Nip> nips, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the register rows for any number of REGONs. The 9-digit and the 14-digit numbers are sent separately, each in
    /// batches of at most 20 identifiers. The result has one entry per distinct requested REGON, in request order
    /// (duplicates are dropped); an entry is empty when GUS did not answer for it, because GUS silently leaves unknown
    /// identifiers out; rows that match none of the requested REGONs are ignored. Every identifier counts toward the per-minute
    /// and per-hour limits; a batch counts as one call toward the per-second limit.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="regons"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="regons"/> is empty or contains an empty identifier.</exception>
    /// <exception cref="RegonAuthenticationException">GUS did not accept the configured key. This is never retried.</exception>
    /// <exception cref="PolishOpenDataApiException">
    /// GUS reported an error (its code is in <see cref="PolishOpenDataApiException.ErrorCode"/>), answered with
    /// something that is not readable, or rejected the request itself (a SOAP sender fault, or HTTP 400 or 415, usually
    /// a wrong <see cref="RegonClientOptions.BaseAddress"/>).
    /// </exception>
    /// <exception cref="QuotaExceededException">The local request limiter refuses the call (see <see cref="RegonClientOptions.RateLimit"/>).</exception>
    /// <exception cref="System.Net.Http.HttpRequestException">A transport failure, or a 5xx answer from GUS.</exception>
    Task<RegonResult<IReadOnlyList<RegonBatchEntry>>> FindByRegonsAsync(IReadOnlyCollection<Regon> regons, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the register rows for any number of KRS numbers, with one search per distinct number, one after the other: a
    /// search row carries no KRS number, so a batch search could not be matched back to the requested numbers. GUS counts
    /// each identifier of a batch like a single request, so this costs the same quota as a batch. The result has one entry
    /// per distinct requested KRS number, in request order; an entry is empty when GUS has no entity with that number.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="krsNumbers"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="krsNumbers"/> is empty or contains an empty identifier.</exception>
    /// <exception cref="RegonAuthenticationException">GUS did not accept the configured key. This is never retried.</exception>
    /// <exception cref="PolishOpenDataApiException">
    /// GUS reported an error (its code is in <see cref="PolishOpenDataApiException.ErrorCode"/>), answered with
    /// something that is not readable, or rejected the request itself (a SOAP sender fault, or HTTP 400 or 415, usually
    /// a wrong <see cref="RegonClientOptions.BaseAddress"/>).
    /// </exception>
    /// <exception cref="QuotaExceededException">The local request limiter refuses the call (see <see cref="RegonClientOptions.RateLimit"/>).</exception>
    /// <exception cref="System.Net.Http.HttpRequestException">A transport failure, or a 5xx answer from GUS.</exception>
    Task<RegonResult<IReadOnlyList<RegonBatchEntry>>> FindByKrsNumbersAsync(IReadOnlyCollection<KrsNumber> krsNumbers, CancellationToken cancellationToken = default);
}
