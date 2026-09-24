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
