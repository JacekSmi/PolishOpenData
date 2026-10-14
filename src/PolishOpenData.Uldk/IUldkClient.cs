using System.Threading;
using System.Threading.Tasks;
using PolishOpenData.Geo;

namespace PolishOpenData.Uldk;

/// <summary>Client for ULDK, the GUGiK cadastral parcel location service. A parcel that does not exist is <c>null</c>.</summary>
public interface IUldkClient
{
    /// <summary>Gets the parcel with the given identifier, or null when there is none.</summary>
    Task<UldkParcel?> GetParcelByIdAsync(ParcelId id, ParcelGeometryKind geometry = ParcelGeometryKind.None, CancellationToken cancellationToken = default);

    /// <summary>Gets the parcel that contains the point, or null when there is none.</summary>
    Task<UldkParcel?> GetParcelAtAsync(Pl1992Point point, ParcelGeometryKind geometry = ParcelGeometryKind.None, CancellationToken cancellationToken = default);

    /// <summary>Gets the parcel that contains the point, or null when there is none.</summary>
    Task<UldkParcel?> GetParcelAtAsync(Wgs84Point point, ParcelGeometryKind geometry = ParcelGeometryKind.None, CancellationToken cancellationToken = default);
}
