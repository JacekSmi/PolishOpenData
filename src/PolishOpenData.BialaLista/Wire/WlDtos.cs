using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using PolishOpenData.Internal;

namespace PolishOpenData.BialaLista;

// Wire DTOs for wl-api.mf.gov.pl (OpenAPI v1.6.0). Internal: the public surface is VatSubject and friends.

internal sealed class WlError
{
    public string? Code { get; set; }

    public string? Message { get; set; }
}

internal sealed class WlPerson
{
    public string? CompanyName { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? Pesel { get; set; }

    public string? Nip { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

internal sealed class WlSubject
{
    public string? Name { get; set; }

    public string? Nip { get; set; }

    public string? StatusVat { get; set; }

    public string? Regon { get; set; }

    public string? Pesel { get; set; }

    public string? Krs { get; set; }

    public string? ResidenceAddress { get; set; }

    public string? WorkingAddress { get; set; }

    public List<WlPerson>? Representatives { get; set; }

    public List<WlPerson>? AuthorizedClerks { get; set; }

    public List<WlPerson>? Partners { get; set; }

    [JsonConverter(typeof(IsoDateOnlyConverter))]
    public DateOnly? RegistrationLegalDate { get; set; }

    [JsonConverter(typeof(IsoDateOnlyConverter))]
    public DateOnly? RegistrationDenialDate { get; set; }

    public string? RegistrationDenialBasis { get; set; }

    [JsonConverter(typeof(IsoDateOnlyConverter))]
    public DateOnly? RestorationDate { get; set; }

    public string? RestorationBasis { get; set; }

    [JsonConverter(typeof(IsoDateOnlyConverter))]
    public DateOnly? RemovalDate { get; set; }

    public string? RemovalBasis { get; set; }

    [JsonConverter(typeof(IsoDateOnlyConverter))]
    public DateOnly? ExemptionSmeDate { get; set; }

    public List<string>? AccountNumbers { get; set; }

    public bool? HasVirtualAccounts { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

internal sealed class WlEntityItem
{
    public WlSubject? Subject { get; set; }

    public string? RequestId { get; set; }

    public string? RequestDateTime { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

internal sealed class WlEntityResponse
{
    public WlEntityItem? Result { get; set; }
}

internal sealed class WlEntityList
{
    public List<WlSubject>? Subjects { get; set; }

    public string? RequestId { get; set; }

    public string? RequestDateTime { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

internal sealed class WlEntityListResponse
{
    public WlEntityList? Result { get; set; }
}

internal sealed class WlEntry
{
    public string? Identifier { get; set; }

    public List<WlSubject>? Subjects { get; set; }

    public WlError? Error { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

internal sealed class WlEntryList
{
    public List<WlEntry>? Entries { get; set; }

    public string? RequestId { get; set; }

    public string? RequestDateTime { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

internal sealed class WlEntryListResponse
{
    public WlEntryList? Result { get; set; }
}

internal sealed class WlCheck
{
    public string? AccountAssigned { get; set; }

    public string? RequestId { get; set; }

    public string? RequestDateTime { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

internal sealed class WlCheckResponse
{
    public WlCheck? Result { get; set; }
}
