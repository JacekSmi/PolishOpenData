using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PolishOpenData.Bir;

/// <summary>
/// The BIR1.2 summary reports (<c>DanePobierzRaportZbiorczy</c>): the REGON numbers that GUS entered, changed or removed on
/// one day. <see cref="RegonClient"/> implements it, and <see cref="RegonServiceCollectionExtensions.AddRegonClient"/>
/// registers it next to <see cref="IRegonClient"/> with the same session and request limiter.
/// </summary>
public interface IRegonSummaryReports
{
    /// <summary>
    /// Returns the REGON numbers of one summary report for one day, in the order GUS sends them and with any duplicates it
    /// sends. The list is empty when GUS has no data for that day (ErrorCode 4, also on weekends). Cost: 1 GUS request,
    /// plus 1 login when there is no session or the answer was empty. Nothing is retried.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The entity reports return 9-digit numbers, the local-unit reports 14-digit numbers; match a local unit to the
    /// 9-digit numbers you hold with <see cref="Regon.BaseRegon"/>.
    /// </para>
    /// <para>
    /// GUS's caveats: an "Aktualizowane" report lists an entity with any change, also in data this service does not show;
    /// for a natural person it does not say which activity changed, so re-fetch all of them; a "Nowe" report also lists a
    /// person who adds another kind of activity or registers again under the same REGON.
    /// </para>
    /// <para>
    /// GUS serves the last 7 days only (yesterday and six days before; today is refused locally). After a longer outage,
    /// re-check your whole list. A listed REGON may answer "not found" later, more so after the BIR changes of 17-18 December 2026.
    /// Older dates are not refused locally: GUS answers them with an error (ErrorCode 104) or, on the test environment, with
    /// no data, and each costs one request. Do not ask for yesterday right after midnight: GUS may not have built the report
    /// yet, and an empty list or error 102 can mean "not ready yet"; the window is 7 days, so ask again later in the morning
    /// or re-ask the previous day too.
    /// </para>
    /// <para>
    /// Do not run summary calls in parallel, and after a transient error wait minutes, not seconds. ErrorCode 101 ("too many
    /// summary reports at once") is not tied to your session: another session of the same key got it for the same day and
    /// report, and it lasted at least 2.5 minutes. A first call can take several seconds and a hung one more than 30; a
    /// hung call can end with an empty answer, after which the session is renewed and the call throws a transient error
    /// instead of sending the report again. GUS closes every session at 03:20 Polish time, so a job that crosses it gets
    /// one such error where searches and full reports recover silently.
    /// </para>
    /// <para>
    /// Responsible use: this is meant for refreshing records you already hold. Take "Aktualizowane" or "Skreslone",
    /// intersect it with your own set and re-fetch only the matches. The entity lists mix companies and sole traders, and a
    /// 9-digit number does not show which. Fetching details for a whole "Nowe" list builds a database of natural persons
    /// and brings the information duty of GDPR article 14. GUS forbids copying the register by automatic lookups and logs
    /// every request.
    /// </para>
    /// </remarks>
    /// <param name="day">The day of the report, in Polish time; it must be before today in Warsaw.</param>
    /// <param name="report">The report.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="day"/> is today or later in Warsaw, or <paramref name="report"/> is not a defined value. Nothing is sent.</exception>
    /// <exception cref="RegonAuthenticationException">GUS did not accept the configured key. This is never retried.</exception>
    /// <exception cref="PolishOpenDataApiException">
    /// GUS reported an error (its code is in <see cref="PolishOpenDataApiException.ErrorCode"/>; 101 is
    /// <see cref="PolishOpenDataApiException.IsTransient"/>, and 105 means GUS no longer accepts the report name, so update
    /// the library), answered with an empty result (transient: the session was renewed), answered with something that is
    /// not readable (a row without a valid REGON, or an answer with no rows and no error code, included), or rejected the request itself (a SOAP sender fault, or HTTP
    /// 400 or 415, usually a wrong <see cref="RegonClientOptions.BaseAddress"/>).
    /// </exception>
    /// <exception cref="QuotaExceededException">The local request limiter refuses the call (see <see cref="RegonClientOptions.RateLimit"/>).</exception>
    /// <exception cref="System.Net.Http.HttpRequestException">A transport failure, a 5xx answer or a SOAP receiver fault from GUS.</exception>
    Task<RegonResult<IReadOnlyList<Regon>>> GetSummaryReportAsync(DateOnly day, RegonSummaryReport report, CancellationToken cancellationToken = default);
}
