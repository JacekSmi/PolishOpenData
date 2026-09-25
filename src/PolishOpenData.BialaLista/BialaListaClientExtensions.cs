using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace PolishOpenData.BialaLista;

/// <summary>Convenience operations over <see cref="IBialaListaClient"/>.</summary>
public static class BialaListaClientExtensions
{
    private const int MaxBatchSize = 30;

    /// <summary>
    /// Looks up any number of NIPs: removes duplicates and sends requests of at most 30 NIPs. Yields one result per
    /// upstream request so that every <see cref="BialaListaResult{T}.RequestId"/> is kept. Each request counts as one
    /// search against the daily limit of 100.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="nips"/> is null.</exception>
    /// <exception cref="PolishOpenDataApiException">
    /// Thrown while enumerating: Biała Lista rejected a request (its code, such as <c>WL-115</c>, is in
    /// <see cref="PolishOpenDataApiException.ErrorCode"/>), or answered with a success response that is not readable
    /// (malformed JSON, a value of the wrong type or format, or no <c>result</c>; then
    /// <see cref="PolishOpenDataApiException.StatusCode"/> and <see cref="PolishOpenDataApiException.ResponseSnippet"/>
    /// describe the response). The results already yielded stay valid.
    /// </exception>
    /// <exception cref="QuotaExceededException">
    /// Thrown while enumerating: the daily limit is reached (reported by the API or by the local quota tracker). The
    /// results already yielded stay valid.
    /// </exception>
    public static IAsyncEnumerable<BialaListaResult<IReadOnlyList<VatBatchEntry>>> FindByNipsChunkedAsync(
        this IBialaListaClient client,
        IEnumerable<Nip> nips,
        DateOnly? date = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(nips);
        return ChunkedCoreAsync(client, nips, date, cancellationToken);
    }

    private static async IAsyncEnumerable<BialaListaResult<IReadOnlyList<VatBatchEntry>>> ChunkedCoreAsync(
        IBialaListaClient client,
        IEnumerable<Nip> nips,
        DateOnly? date,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var chunk = new List<Nip>(MaxBatchSize);
        foreach (var nip in nips.Distinct())
        {
            chunk.Add(nip);
            if (chunk.Count == MaxBatchSize)
            {
                yield return await client.FindByNipsAsync(chunk.ToArray(), date, cancellationToken).ConfigureAwait(false);
                chunk.Clear();
            }
        }

        if (chunk.Count > 0)
        {
            yield return await client.FindByNipsAsync(chunk.ToArray(), date, cancellationToken).ConfigureAwait(false);
        }
    }
}
