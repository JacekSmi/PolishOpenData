using PolishOpenData.Uldk;

namespace PolishOpenData.Demo.Search;

/// <summary>The parcel at a picked point and the GeoJSON of its outline.</summary>
/// <param name="Parcel">The parcel, or <c>null</c> when ULDK knows none there.</param>
/// <param name="OutlineGeoJson">A GeoJSON geometry for the map, or <c>null</c> when there is no parcel or its outline could not be built.</param>
public sealed record ParcelResult(UldkParcel? Parcel, string? OutlineGeoJson);
