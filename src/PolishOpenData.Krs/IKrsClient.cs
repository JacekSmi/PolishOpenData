using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PolishOpenData.Krs;

/// <summary>Client for the KRS Open API of the Ministry of Justice (no key, CC0 data).</summary>
public interface IKrsClient
{
    /// <summary>Gets the current extract (<i>odpis aktualny</i>). Without <paramref name="register"/>, tries P then S.</summary>
    Task<KrsResult<KrsCurrentExtract>> GetCurrentExtractAsync(KrsNumber krs, KrsRegister? register = null, CancellationToken cancellationToken = default);

    /// <summary>Gets the full extract (<i>odpis pełny</i>) with the entry history. Without <paramref name="register"/>, tries P then S.</summary>
    Task<KrsResult<KrsFullExtract>> GetFullExtractAsync(KrsNumber krs, KrsRegister? register = null, CancellationToken cancellationToken = default);

    /// <summary>Streams the KRS numbers that changed on a day (<i>Biuletyn</i>, available from 2021-12-08), de-duplicated.</summary>
    IAsyncEnumerable<KrsNumber> GetChangedAsync(DateOnly day, CancellationToken cancellationToken = default);

    /// <summary>Streams the KRS numbers that changed between two hours of a day (<i>BiuletynGodzinowy</i>), de-duplicated.</summary>
    IAsyncEnumerable<KrsNumber> GetChangedAsync(DateOnly day, int fromHour, int toHour, CancellationToken cancellationToken = default);
}
