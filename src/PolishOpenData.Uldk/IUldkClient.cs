using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PolishOpenData.Geo;

namespace PolishOpenData.Uldk;

/// <summary>Client for ULDK, the GUGiK cadastral parcel location service. A parcel that does not exist is <c>null</c>.</summary>
public interface IUldkClient
{
    /// <summary>Gets the parcel with the given identifier, or null when there is none.</summary>
    /// <exception cref="System.ArgumentException"><paramref name="id"/> is empty.</exception>
    /// <exception cref="PolishOpenDataApiException">
    /// ULDK answered with a 4xx error, with an error line (<c>-1 …</c>, <see cref="PolishOpenDataApiException.ErrorCode"/>
    /// <c>uldk_error</c>), with an HTML block page (<c>blocked</c>, transient), or with a success response that could not be read
    /// (<see cref="PolishOpenDataApiException.StatusCode"/> 200). <see cref="PolishOpenDataApiException.ResponseSnippet"/> holds the start of the answer.
    /// </exception>
    /// <exception cref="QuotaExceededException">ULDK answered HTTP 429.</exception>
    Task<UldkParcel?> GetParcelByIdAsync(ParcelId id, ParcelGeometryKind geometry = ParcelGeometryKind.None, CancellationToken cancellationToken = default);

    /// <summary>Gets the parcel that contains the point, or null when there is none.</summary>
    /// <exception cref="System.ArgumentOutOfRangeException">The point is outside Poland, e.g. <c>default(Pl1992Point)</c>.</exception>
    /// <exception cref="PolishOpenDataApiException">
    /// ULDK answered with a 4xx error, with an error line (<c>-1 …</c>, <see cref="PolishOpenDataApiException.ErrorCode"/>
    /// <c>uldk_error</c>), with an HTML block page (<c>blocked</c>, transient), or with a success response that could not be read
    /// (<see cref="PolishOpenDataApiException.StatusCode"/> 200). <see cref="PolishOpenDataApiException.ResponseSnippet"/> holds the start of the answer.
    /// </exception>
    /// <exception cref="QuotaExceededException">ULDK answered HTTP 429.</exception>
    Task<UldkParcel?> GetParcelAtAsync(Pl1992Point point, ParcelGeometryKind geometry = ParcelGeometryKind.None, CancellationToken cancellationToken = default);

    /// <summary>Gets the parcel that contains the point, or null when there is none.</summary>
    /// <exception cref="System.ArgumentOutOfRangeException">The point is outside Poland, e.g. <c>default(Wgs84Point)</c>.</exception>
    /// <exception cref="PolishOpenDataApiException">
    /// ULDK answered with a 4xx error, with an error line (<c>-1 …</c>, <see cref="PolishOpenDataApiException.ErrorCode"/>
    /// <c>uldk_error</c>), with an HTML block page (<c>blocked</c>, transient), or with a success response that could not be read
    /// (<see cref="PolishOpenDataApiException.StatusCode"/> 200). <see cref="PolishOpenDataApiException.ResponseSnippet"/> holds the start of the answer.
    /// </exception>
    /// <exception cref="QuotaExceededException">ULDK answered HTTP 429.</exception>
    Task<UldkParcel?> GetParcelAtAsync(Wgs84Point point, ParcelGeometryKind geometry = ParcelGeometryKind.None, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds parcels by region name and number ("Krzewina 134") or by a full parcel identifier; an empty list when none match.
    /// A name can match many parcels, so there is no geometry option.
    /// </summary>
    /// <exception cref="System.ArgumentException"><paramref name="regionAndNumber"/> is null, empty or white space.</exception>
    /// <exception cref="PolishOpenDataApiException">
    /// ULDK answered with a 4xx error, with an error line (<c>-1 …</c>, <see cref="PolishOpenDataApiException.ErrorCode"/>
    /// <c>uldk_error</c>), with an HTML block page (<c>blocked</c>, transient), or with a success response that could not be read
    /// (<see cref="PolishOpenDataApiException.StatusCode"/> 200). <see cref="PolishOpenDataApiException.ResponseSnippet"/> holds the start of the answer.
    /// </exception>
    /// <exception cref="QuotaExceededException">ULDK answered HTTP 429.</exception>
    Task<IReadOnlyList<UldkParcel>> FindParcelsAsync(string regionAndNumber, CancellationToken cancellationToken = default);
}
