using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PolishOpenData.Geo;

namespace PolishOpenData.Uug;

/// <summary>Client for UUG, the GUGiK address geocoder. No match is an empty list (geocoding) or <c>null</c> (reverse).</summary>
public interface IUugClient
{
    /// <summary>
    /// Finds the places that match a free-text address such as <c>Warszawa, Krucza 50</c>, best match first as the service orders them.
    /// A partial address ("Warszawa, Krucza") gives a <see cref="UugMatchKind.Street"/> match, not an address.
    /// </summary>
    /// <exception cref="System.ArgumentException"><paramref name="address"/> is null, empty or white space.</exception>
    /// <exception cref="PolishOpenDataApiException">
    /// UUG answered with a 4xx error, with an HTML block page (<see cref="PolishOpenDataApiException.ErrorCode"/> <c>blocked</c>,
    /// transient), or with a success response that could not be used (malformed JSON, a result without coordinates or with
    /// coordinates outside Poland; <see cref="PolishOpenDataApiException.StatusCode"/> 200).
    /// </exception>
    /// <exception cref="QuotaExceededException">UUG answered HTTP 429.</exception>
    Task<IReadOnlyList<UugMatch>> GeocodeAsync(string address, CancellationToken cancellationToken = default);

    /// <summary>Finds the address nearest to the point (within the service's search radius), or null when there is none.</summary>
    /// <exception cref="System.ArgumentOutOfRangeException">The point is outside Poland, e.g. <c>default(Wgs84Point)</c>.</exception>
    /// <exception cref="PolishOpenDataApiException">
    /// UUG answered with a 4xx error, with an HTML block page (<see cref="PolishOpenDataApiException.ErrorCode"/> <c>blocked</c>,
    /// transient), or with a success response that could not be used (malformed JSON, a result without coordinates or with
    /// coordinates outside Poland; <see cref="PolishOpenDataApiException.StatusCode"/> 200).
    /// </exception>
    /// <exception cref="QuotaExceededException">UUG answered HTTP 429.</exception>
    Task<UugMatch?> ReverseGeocodeAsync(Wgs84Point point, CancellationToken cancellationToken = default);
}
