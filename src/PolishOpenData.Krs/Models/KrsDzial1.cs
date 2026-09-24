using System.Collections.Generic;
using System.Text.Json;

#pragma warning disable CS1591 // wire model: properties mirror the KRS JSON field names one to one

namespace PolishOpenData.Krs;

/// <summary>Section 1: entity data, seat, capital, shareholders.</summary>
public sealed class KrsDzial1 : KrsNode
{
    public KrsDanePodmiotu? DanePodmiotu { get; set; }

    public KrsSiedzibaIAdres? SiedzibaIAdres { get; set; }

    public KrsKapital? Kapital { get; set; }

    /// <summary>Shareholders of a sp. z o.o.; natural persons are masked by the API.</summary>
    public IReadOnlyList<KrsOsoba>? WspolnicySpzoo { get; set; }

    public JsonElement? JednostkiTerenoweOddzialy { get; set; }

    public JsonElement? UmowaStatut { get; set; }

    public JsonElement? PozostaleInformacje { get; set; }

    public JsonElement? SposobPowstaniaPodmiotu { get; set; }

    public JsonElement? EmisjeAkcji { get; set; }

    public JsonElement? WzmiankaOPodjeciuUchwalyOEmisjachObligacjiZamiennych { get; set; }

    public JsonElement? WzmiankaOUpowaznieniuDoEmisjiWarrantowSubskrypcyjnych { get; set; }

    public JsonElement? OrganSprawujacyNadzor { get; set; }
}

/// <summary>Entity data (<c>danePodmiotu</c>).</summary>
public sealed class KrsDanePodmiotu : KrsNode
{
    public string? FormaPrawna { get; set; }

    public KrsIdentyfikatory? Identyfikatory { get; set; }

    public string? Nazwa { get; set; }

    public JsonElement? DaneOWczesniejszejRejestracji { get; set; }

    public bool? CzyProwadziDzialalnoscZInnymiPodmiotami { get; set; }

    public bool? CzyPosiadaStatusOpp { get; set; }
}

/// <summary>NIP and REGON of the entity. KRS writes a 9-digit REGON padded with <c>00000</c>.</summary>
public sealed class KrsIdentyfikatory : KrsNode
{
    public string? Regon { get; set; }

    public string? Nip { get; set; }
}

/// <summary>Seat, address and electronic contact data (<c>siedzibaIAdres</c>).</summary>
public sealed class KrsSiedzibaIAdres : KrsNode
{
    public KrsSiedziba? Siedziba { get; set; }

    public KrsAdres? Adres { get; set; }

    public string? AdresPocztyElektronicznej { get; set; }

    public string? AdresStronyInternetowej { get; set; }

    public string? AdresDoDoreczenElektronicznychWpisanyDoBae { get; set; }
}

/// <summary>Seat (<c>siedziba</c>).</summary>
public sealed class KrsSiedziba : KrsNode
{
    public string? Kraj { get; set; }

    public string? Wojewodztwo { get; set; }

    public string? Powiat { get; set; }

    public string? Gmina { get; set; }

    public string? Miejscowosc { get; set; }
}

/// <summary>Postal address (<c>adres</c>).</summary>
public sealed class KrsAdres : KrsNode
{
    public string? Ulica { get; set; }

    public string? NrDomu { get; set; }

    public string? NrLokalu { get; set; }

    public string? Miejscowosc { get; set; }

    public string? KodPocztowy { get; set; }

    public string? Poczta { get; set; }

    public string? Kraj { get; set; }
}

/// <summary>Capital (<c>kapital</c>); absent for register S.</summary>
public sealed class KrsKapital : KrsNode
{
    public KrsKwota? WysokoscKapitaluZakladowego { get; set; }

    public string? LacznaLiczbaAkcjiUdzialow { get; set; }

    public KrsKwota? WartoscJednejAkcji { get; set; }

    public KrsKwota? CzescKapitaluWplaconegoPokrytego { get; set; }

    public JsonElement? WniesioneAporty { get; set; }
}

/// <summary>An amount with a comma decimal separator, e.g. <c>1451177561,25</c> PLN.</summary>
public sealed class KrsKwota : KrsNode
{
    public string? Wartosc { get; set; }

    public string? Waluta { get; set; }
}
