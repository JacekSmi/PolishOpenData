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
}
