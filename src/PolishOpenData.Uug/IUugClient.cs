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
    Task<IReadOnlyList<UugMatch>> GeocodeAsync(string address, CancellationToken cancellationToken = default);

    /// <summary>Finds the address nearest to the point (within the service's search radius), or null when there is none.</summary>
    Task<UugMatch?> ReverseGeocodeAsync(Wgs84Point point, CancellationToken cancellationToken = default);
}
