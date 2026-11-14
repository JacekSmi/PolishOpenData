using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PolishOpenData.Sejm;

/// <summary>
/// Client for the Sejm API (api.sejm.gov.pl/sejm). No key is needed and no limits are published; the client neither limits itself nor
/// retries. The term is always explicit: <see cref="GetCurrentTermAsync"/> finds the running one. Personal data is off by default: individual
/// votes and club or committee members are returned only when the caller asks for them.
/// </summary>
public interface ISejmClient
{
    /// <summary>Lists all terms (one request, about 1 KB).</summary>
    Task<IReadOnlyList<SejmTerm>> GetTermsAsync(CancellationToken cancellationToken = default);

    /// <summary>The running term (one request).</summary>
    /// <exception cref="PolishOpenDataApiException">The API reports no current term.</exception>
    Task<SejmTerm> GetCurrentTermAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds legislative processes (one request). A passed process is not the same as a law: read <see cref="SejmProcessSummary.Eli"/>.
    /// The API ignores sort options and returns ascending by number; the total comes from <c>X-Total-Count</c>.
    /// </summary>
    /// <exception cref="System.ArgumentOutOfRangeException">Term, limit or offset is out of range.</exception>
    Task<SejmPage<SejmProcessSummary>> SearchProcessesAsync(SejmProcessSearch search, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one process with its stages. The API answers only for the initiating print: a report print (for example 401 of process 318)
    /// gives null; follow <see cref="SejmPrint.ProcessNumber"/> from <see cref="GetPrintAsync"/> instead.
    /// </summary>
    /// <exception cref="System.ArgumentException">The number is not a print number (digits with optional -suffixes).</exception>
    Task<SejmProcess?> GetProcessAsync(int term, string number, CancellationToken cancellationToken = default);

    /// <summary>Reads one print; null when it does not exist.</summary>
    /// <exception cref="System.ArgumentException">The number is not a print number (digits with optional -suffixes).</exception>
    Task<SejmPrint?> GetPrintAsync(int term, string number, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the bill record of a print (one request: the first item whose print equals the argument); null when there is none.
    /// Use it for who submitted the bill. Its status is not offered because the API's status does not follow the outcome.
    /// </summary>
    /// <exception cref="System.ArgumentException">The number is not a print number.</exception>
    Task<SejmBill?> GetBillForPrintAsync(int term, string print, CancellationToken cancellationToken = default);

    /// <summary>Lists the days with votings in a term (one request, about 9 KB for a term).</summary>
    Task<IReadOnlyList<SejmVotingDay>> GetVotingDaysAsync(int term, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds votings (one request). The API has no total and no cap and lists oldest first; the client asks for one extra item so
    /// <see cref="SejmPage{T}.HasMore"/> is exact. <see cref="SejmVotingSearch.Title"/> matches a substring of title and topic, so "1344" also finds "13440".
    /// </summary>
    /// <exception cref="System.ArgumentException">No sitting, title or date was given.</exception>
    /// <exception cref="System.ArgumentOutOfRangeException">Term, limit or offset is out of range.</exception>
    Task<SejmPage<SejmVoting>> SearchVotingsAsync(SejmVotingSearch search, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one voting (one request, 45 to 90 KB) and sums the individual votes into club tallies. Individual votes with names are
    /// personal data: <see cref="SejmVotingDetails.MpVotes"/> is null unless <paramref name="includeMpVotes"/> is true. Null when the voting does not exist.
    /// </summary>
    Task<SejmVotingDetails?> GetVotingAsync(int term, int sitting, int number, bool includeMpVotes = false, CancellationToken cancellationToken = default);
}
