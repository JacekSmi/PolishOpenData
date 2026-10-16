using System.Threading;
using System.Threading.Tasks;

namespace PolishOpenData.Rcn;

/// <summary>Client for RCN, the GUGiK register of real-estate transaction prices (WFS). The library never retries.</summary>
public interface IRcnClient
{
    /// <summary>
    /// Gets the transactions that match the query, newest first, at most <see cref="RcnQuery.MaxRecords"/> of them
    /// (<see cref="RcnTransactions.Truncated"/> says whether more exist). Pages are fetched with <c>count</c> and <c>startIndex</c> only.
    /// </summary>
    /// <exception cref="System.ArgumentException">The query is not bounded, has a malformed powiat code, <see cref="RcnQuery.From"/> after <see cref="RcnQuery.To"/>, or a premises function on another layer.</exception>
    /// <exception cref="System.ArgumentOutOfRangeException">The area is above 25 km² or <see cref="RcnQuery.MaxRecords"/> is outside 1 to 5,000.</exception>
    /// <exception cref="PolishOpenDataApiException">
    /// RCN answered with a 4xx error (an OGC exception report gives <see cref="PolishOpenDataApiException.ErrorCode"/> its <c>exceptionCode</c>),
    /// with an HTML block page (<c>blocked</c>, transient), or with a success response that could not be read
    /// (<see cref="PolishOpenDataApiException.StatusCode"/> 200). <see cref="PolishOpenDataApiException.ResponseSnippet"/> holds the start of the answer.
    /// </exception>
    /// <exception cref="QuotaExceededException">RCN answered HTTP 429.</exception>
    /// <exception cref="System.Net.Http.HttpRequestException">RCN answered 5xx, or the request failed.</exception>
    Task<RcnTransactions> GetTransactionsAsync(RcnQuery query, CancellationToken cancellationToken = default);
}
