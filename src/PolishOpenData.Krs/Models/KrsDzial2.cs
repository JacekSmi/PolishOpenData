using System.Collections.Generic;

#pragma warning disable CS1591 // wire model: properties mirror the KRS JSON field names one to one

namespace PolishOpenData.Krs;

/// <summary>Section 2: representation, supervisory bodies, proxies.</summary>
public sealed class KrsDzial2 : KrsNode
{
    public KrsOrgan? Reprezentacja { get; set; }

    public IReadOnlyList<KrsOrgan>? OrganNadzoru { get; set; }

    /// <summary>Proxies. Warning: <see cref="KrsOsoba.RodzajProkury"/> is free text that can contain unmasked names and PESEL numbers.</summary>
    public IReadOnlyList<KrsOsoba>? Prokurenci { get; set; }
}

/// <summary>A body such as the management board (<c>nazwaOrganu</c>) or a supervisory board (<c>nazwa</c>).</summary>
public sealed class KrsOrgan : KrsNode
{
    public string? NazwaOrganu { get; set; }

    public string? Nazwa { get; set; }

    public string? SposobReprezentacji { get; set; }

    public IReadOnlyList<KrsOsoba>? Sklad { get; set; }
}

/// <summary>A person or a legal entity (board member, proxy or shareholder). Natural persons are masked by the API.</summary>
public sealed class KrsOsoba : KrsNode
{
    public KrsNazwisko? Nazwisko { get; set; }

    public KrsImiona? Imiona { get; set; }

    public KrsIdentyfikator? Identyfikator { get; set; }

    public string? FunkcjaWOrganie { get; set; }

    public bool? CzyZawieszona { get; set; }

    /// <summary>Free text; may contain unmasked personal data. Never display it without review.</summary>
    public string? RodzajProkury { get; set; }

    /// <summary>Name of a legal-entity shareholder.</summary>
    public string? Nazwa { get; set; }

    public KrsNumerRef? Krs { get; set; }

    public string? PosiadaneUdzialy { get; set; }

    public bool? CzyPosiadaCaloscUdzialow { get; set; }
}

/// <summary>Surname parts (masked, e.g. <c>M****</c>).</summary>
public sealed class KrsNazwisko : KrsNode
{
    public string? NazwiskoICzlon { get; set; }

    public string? NazwiskoIICzlon { get; set; }
}

/// <summary>First names (masked).</summary>
public sealed class KrsImiona : KrsNode
{
    public string? Imie { get; set; }

    public string? ImieDrugie { get; set; }
}

/// <summary>Identifier of a person (masked PESEL) or entity (REGON/NIP).</summary>
public sealed class KrsIdentyfikator : KrsNode
{
    public string? Pesel { get; set; }

    public string? Regon { get; set; }

    public string? Nip { get; set; }
}

/// <summary>Reference to another KRS entity.</summary>
public sealed class KrsNumerRef : KrsNode
{
    public string? Krs { get; set; }
}
