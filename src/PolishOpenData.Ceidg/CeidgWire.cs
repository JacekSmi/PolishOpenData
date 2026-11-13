using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace PolishOpenData.Ceidg;

internal sealed class CeidgFirmaResponse
{
    public List<CeidgFirmaDto>? Firma { get; set; }
}

internal sealed class CeidgFirmaDto
{
    public string? Id { get; set; }

    public string? Nazwa { get; set; }

    public CeidgAdresDto? AdresDzialalnosci { get; set; }

    public CeidgOsobaDto? Wlasciciel { get; set; }

    public List<CeidgPkdDto>? Pkd { get; set; }

    public CeidgPkdDto? PkdGlowny { get; set; }

    public string? DataRozpoczecia { get; set; }

    public string? DataZawieszenia { get; set; }

    public string? DataWznowienia { get; set; }

    public string? DataZakonczenia { get; set; }

    public string? Status { get; set; }

    public string? Link { get; set; }
}

internal sealed class CeidgAdresDto
{
    public string? Ulica { get; set; }

    public string? Budynek { get; set; }

    public string? Lokal { get; set; }

    public string? Miasto { get; set; }

    public string? Wojewodztwo { get; set; }

    public string? Powiat { get; set; }

    public string? Gmina { get; set; }

    public string? Kraj { get; set; }

    public string? Kod { get; set; }

    public string? Terc { get; set; }

    public string? Simc { get; set; }

    public string? Ulic { get; set; }
}

internal sealed class CeidgOsobaDto
{
    public string? Imie { get; set; }

    public string? Nazwisko { get; set; }

    public string? Nip { get; set; }

    public string? Regon { get; set; }
}

internal sealed class CeidgPkdDto
{
    public string? Kod { get; set; }

    public string? Nazwa { get; set; }
}

internal sealed class CeidgErrorDto
{
    public string? Code { get; set; }

    public string? Message { get; set; }
}

#if NET
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(CeidgFirmaResponse))]
[JsonSerializable(typeof(CeidgErrorDto))]
internal sealed partial class CeidgJsonContext : JsonSerializerContext;
#endif

/// <summary>Type infos: source-generated on .NET, reflection on netstandard2.0.</summary>
internal static class CeidgJson
{
#if NET
    public static JsonTypeInfo<CeidgFirmaResponse> Response => CeidgJsonContext.Default.CeidgFirmaResponse;

    public static JsonTypeInfo<CeidgErrorDto> Error => CeidgJsonContext.Default.CeidgErrorDto;
#else
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    public static JsonTypeInfo<CeidgFirmaResponse> Response { get; } = (JsonTypeInfo<CeidgFirmaResponse>)Options.GetTypeInfo(typeof(CeidgFirmaResponse));

    public static JsonTypeInfo<CeidgErrorDto> Error { get; } = (JsonTypeInfo<CeidgErrorDto>)Options.GetTypeInfo(typeof(CeidgErrorDto));
#endif
}
