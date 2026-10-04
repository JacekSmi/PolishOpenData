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

    /// <summary>
    /// Finds the entity with this REGON and returns its full report, one <see cref="RegonDetails"/> per register row: a
    /// legal person or a local unit gives one, a sole trader gives one per activity type ("silo"), each with its own
    /// address; the list is empty when GUS has no such entity. <paramref name="include"/> adds the PKD codes, the local
    /// units and the partners of a civil partnership.
    /// </summary>
    /// <remarks>
    /// The report to fetch depends on the type and silo of the entity, so this call first searches. Cost in requests (each
    /// counts toward the limits): a legal person needs a login (once per session), the search and one report, plus one for
    /// every included list; a sole trader with one silo needs the search, the general report and one activity report, and
    /// each further silo adds one activity report (the general report is fetched once).
    /// </remarks>
    /// <param name="regon">The REGON of an entity (9 digits) or of a local unit (14 digits).</param>
    /// <param name="include">The optional lists to fetch; each costs one more request. Included lists that do not apply to the entity (partners of a sole trader, local units of a local unit) stay <c>null</c>.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <exception cref="ArgumentException"><paramref name="regon"/> is empty (<c>default(Regon)</c>).</exception>
    /// <exception cref="RegonAuthenticationException">GUS did not accept the configured key. This is never retried.</exception>
    /// <exception cref="PolishOpenDataApiException">
    /// GUS reported an error (its code is in <see cref="PolishOpenDataApiException.ErrorCode"/>), answered with
    /// something that is not readable, or rejected the request itself (a SOAP sender fault, or HTTP 400 or 415, usually
    /// a wrong <see cref="RegonClientOptions.BaseAddress"/>).
    /// </exception>
    /// <exception cref="QuotaExceededException">The local request limiter refuses the call (see <see cref="RegonClientOptions.RateLimit"/>).</exception>
    /// <exception cref="System.Net.Http.HttpRequestException">A transport failure, or a 5xx answer from GUS.</exception>
    Task<RegonResult<IReadOnlyList<RegonDetails>>> GetDetailsAsync(Regon regon, RegonInclude include = RegonInclude.None, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the full report of an entity that a search already found, without searching again (one request less than
    /// the overload that takes a REGON). The list holds one <see cref="RegonDetails"/>, or none when GUS has no report for it.
    /// </summary>
    /// <param name="entity">A row returned by one of the search methods.</param>
    /// <param name="include">The optional lists to fetch; each costs one more request.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="entity"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="entity"/> has no REGON or an unknown type.</exception>
    /// <exception cref="RegonAuthenticationException">GUS did not accept the configured key. This is never retried.</exception>
    /// <exception cref="PolishOpenDataApiException">
    /// GUS reported an error (its code is in <see cref="PolishOpenDataApiException.ErrorCode"/>), answered with
    /// something that is not readable, or rejected the request itself (a SOAP sender fault, or HTTP 400 or 415, usually
    /// a wrong <see cref="RegonClientOptions.BaseAddress"/>).
    /// </exception>
    /// <exception cref="QuotaExceededException">The local request limiter refuses the call (see <see cref="RegonClientOptions.RateLimit"/>).</exception>
    /// <exception cref="System.Net.Http.HttpRequestException">A transport failure, or a 5xx answer from GUS.</exception>
    Task<RegonResult<IReadOnlyList<RegonDetails>>> GetDetailsAsync(RegonEntity entity, RegonInclude include = RegonInclude.None, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one of the 17 BIR1.2 full reports as raw text fields, one <see cref="RegonRecord"/> per row of the report
    /// (a PKD or a list report has many). Use it for fields that <see cref="RegonDetails"/> does not type. The list is
    /// empty when GUS has no data for this report (unknown REGON, a removed entity's PKD, a civil partnership report
    /// of an entity that is not one).
    /// </summary>
    /// <param name="regon">The REGON the report needs: 9 digits for most reports, 14 for the local-unit reports.</param>
    /// <param name="report">The report.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <exception cref="ArgumentException"><paramref name="regon"/> is empty or has the wrong length for <paramref name="report"/>.</exception>
    /// <exception cref="RegonAuthenticationException">GUS did not accept the configured key. This is never retried.</exception>
    /// <exception cref="PolishOpenDataApiException">
    /// GUS reported an error (its code is in <see cref="PolishOpenDataApiException.ErrorCode"/>), answered with
    /// something that is not readable, or rejected the request itself (a SOAP sender fault, or HTTP 400 or 415, usually
    /// a wrong <see cref="RegonClientOptions.BaseAddress"/>).
    /// </exception>
    /// <exception cref="QuotaExceededException">The local request limiter refuses the call (see <see cref="RegonClientOptions.RateLimit"/>).</exception>
    /// <exception cref="System.Net.Http.HttpRequestException">A transport failure, or a 5xx answer from GUS.</exception>
    Task<RegonResult<IReadOnlyList<RegonRecord>>> GetReportAsync(Regon regon, RegonReport report, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks GUS for the date of the data (<c>StanDanych</c>), one request. The date is cached with the session: results
    /// of later calls carry it in <see cref="RegonResult{T}.DataAsOf"/>. The value is <c>null</c> when GUS answers with
    /// text in an unknown date format.
    /// </summary>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <exception cref="RegonAuthenticationException">GUS did not accept the configured key. This is never retried.</exception>
    /// <exception cref="PolishOpenDataApiException">
    /// GUS reported an error (its code is in <see cref="PolishOpenDataApiException.ErrorCode"/>), answered with
    /// something that is not readable, or rejected the request itself (a SOAP sender fault, or HTTP 400 or 415, usually
    /// a wrong <see cref="RegonClientOptions.BaseAddress"/>).
    /// </exception>
    /// <exception cref="QuotaExceededException">The local request limiter refuses the call (see <see cref="RegonClientOptions.RateLimit"/>).</exception>
    /// <exception cref="System.Net.Http.HttpRequestException">A transport failure, or a 5xx answer from GUS.</exception>
    Task<RegonResult<DateOnly?>> GetDataDateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends the GUS session, if there is one. Best effort: transport and GUS errors are swallowed (cancellation is not),
    /// and the next call logs in again. The client never logs out by itself, because it cannot know when you are done;
    /// call this when the process stops. Sessions that are not ended expire at GUS after about an hour.
    /// </summary>
    /// <param name="cancellationToken">Cancellation.</param>
    Task LogoutAsync(CancellationToken cancellationToken = default);
}
