using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace PolishOpenData.Uug;

// Wire DTOs for services.gugik.gov.pl/uug. Internal: the public surface is UugMatch.

/// <summary>
/// The answer to GetAddress and GetAddressReverse. <c>results</c> is null (or an empty array) when nothing matched,
/// else an object keyed "1", "2", …; it stays a <see cref="JsonElement"/> so that either shape reads.
/// </summary>
internal sealed class UugResponse
{
    public string? Type { get; set; }

    public JsonElement Results { get; set; }
}

/// <summary>
/// One result. The service writes numbers as strings (<c>"x":"637659.46"</c>); AllowReadingFromString reads them
/// with the invariant culture and also accepts real JSON numbers. For GetAddress <c>x</c>/<c>y</c> are PL-1992
/// easting/northing; for GetAddressReverse (srid 4326) they are longitude/latitude.
/// </summary>
internal sealed class UugRow
{
    public string? City { get; set; }

    public string? Citypart { get; set; }

    public string? Street { get; set; }

    public string? Number { get; set; }

    public string? Teryt { get; set; }

    public string? Simc { get; set; }

    public string? Ulic { get; set; }

    public string? Code { get; set; }

    public double? X { get; set; }

    public double? Y { get; set; }

    public double? Accuracy { get; set; }

    public double? Distance { get; set; }
}

#if NET
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(UugResponse))]
[JsonSerializable(typeof(UugRow))]
internal sealed partial class UugJsonContext : JsonSerializerContext;
#endif

/// <summary>Type info: source-generated on .NET, reflection on netstandard2.0.</summary>
internal static class UugJson
{
#if NET
    public static JsonTypeInfo<UugResponse> Response => UugJsonContext.Default.UugResponse;

    public static JsonTypeInfo<UugRow> Row => UugJsonContext.Default.UugRow;
#else
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    public static JsonTypeInfo<UugResponse> Response { get; } = (JsonTypeInfo<UugResponse>)Options.GetTypeInfo(typeof(UugResponse));

    public static JsonTypeInfo<UugRow> Row { get; } = (JsonTypeInfo<UugRow>)Options.GetTypeInfo(typeof(UugRow));
#endif
}
