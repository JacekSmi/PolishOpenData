using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using PolishOpenData.Internal;

namespace PolishOpenData.BialaLista;

/// <summary>Maps wire DTOs to the public model without ever failing on one bad value.</summary>
internal static class VatMapper
{
    private const string RequestTimeFormat = "dd-MM-yyyy HH:mm:ss";

    public static VatSubject MapSubject(WlSubject subject)
    {
        var unknown = new List<string>();
        if (subject.Extra is { } extra)
        {
            unknown.AddRange(extra.Keys);
        }

        return new VatSubject
        {
            Name = subject.Name ?? string.Empty,
            Nip = Nip.TryParse(subject.Nip, out var nip) ? nip : null,
            VatStatus = ParseStatus(subject.StatusVat),
            VatStatusRaw = subject.StatusVat,
            Regon = Regon.TryParse(subject.Regon, out var regon) ? regon : null,
            Pesel = subject.Pesel,
            Krs = KrsNumber.TryParse(subject.Krs, out var krs) ? krs : null,
            ResidenceAddress = subject.ResidenceAddress,
            WorkingAddress = subject.WorkingAddress,
            Representatives = MapPeople(subject.Representatives, "representatives", unknown),
            AuthorizedClerks = MapPeople(subject.AuthorizedClerks, "authorizedClerks", unknown),
            Partners = MapPeople(subject.Partners, "partners", unknown),
            RegistrationLegalDate = subject.RegistrationLegalDate,
            RegistrationDenialDate = subject.RegistrationDenialDate,
            RegistrationDenialBasis = subject.RegistrationDenialBasis,
            RestorationDate = subject.RestorationDate,
            RestorationBasis = subject.RestorationBasis,
            RemovalDate = subject.RemovalDate,
            RemovalBasis = subject.RemovalBasis,
            ExemptionSmeDate = subject.ExemptionSmeDate,
            AccountNumbers = MapAccounts(subject.AccountNumbers, unknown),
            HasVirtualAccounts = subject.HasVirtualAccounts ?? false,
            UnknownFields = unknown,
        };
    }

    public static List<VatBatchEntry> MapEntries(List<WlEntry>? entries)
    {
        var result = new List<VatBatchEntry>(entries?.Count ?? 0);
        foreach (var entry in entries ?? [])
        {
            var subjects = new List<VatSubject>();
            foreach (var subject in entry.Subjects ?? [])
            {
                subjects.Add(MapSubject(subject));
            }

            var error = entry.Error is null ? null : new BialaListaError(entry.Error.Code ?? string.Empty, entry.Error.Message ?? string.Empty);
            result.Add(new VatBatchEntry(entry.Identifier ?? string.Empty, subjects, error));
        }

        return result;
    }

    public static List<VatSubject> MapSubjects(List<WlSubject>? subjects)
    {
        var result = new List<VatSubject>(subjects?.Count ?? 0);
        foreach (var subject in subjects ?? [])
        {
            result.Add(MapSubject(subject));
        }

        return result;
    }

    public static List<string> UnknownKeys(Dictionary<string, JsonElement>? extra, List<WlEntry>? entries = null)
    {
        var keys = new List<string>();
        if (extra is not null)
        {
            keys.AddRange(extra.Keys);
        }

        foreach (var entry in entries ?? [])
        {
            if (entry.Extra is null)
            {
                continue;
            }

            foreach (var key in entry.Extra.Keys)
            {
                var name = "entries[]." + key;
                if (!keys.Contains(name))
                {
                    keys.Add(name);
                }
            }
        }

        return keys;
    }

    public static DateTimeOffset ParseRequestTime(string? value, TimeProvider timeProvider)
    {
        if (value is not null &&
            DateTime.TryParseExact(value, RequestTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local) &&
            WarsawTime.TryFromLocal(local, out var time))
        {
            return time;
        }

        return timeProvider.GetUtcNow();
    }

    private static VatStatus ParseStatus(string? value) => value switch
    {
        "Czynny" => VatStatus.Active,
        "Zwolniony" => VatStatus.Exempt,
        "Niezarejestrowany" => VatStatus.NotRegistered,
        _ => VatStatus.Unknown,
    };

    private static List<VatPerson> MapPeople(List<WlPerson>? people, string field, List<string> unknown)
    {
        var result = new List<VatPerson>(people?.Count ?? 0);
        foreach (var person in people ?? [])
        {
            if (person.Extra is { } extra)
            {
                foreach (var key in extra.Keys)
                {
                    // one entry per list and key, however many people carry it (as UnknownKeys does for entries[])
                    var name = field + "[]." + key;
                    if (!unknown.Contains(name))
                    {
                        unknown.Add(name);
                    }
                }
            }

            result.Add(new VatPerson
            {
                CompanyName = person.CompanyName,
                FirstName = person.FirstName,
                LastName = person.LastName,
                Nip = person.Nip,
                Pesel = person.Pesel,
            });
        }

        return result;
    }

    private static List<Nrb> MapAccounts(List<string>? accounts, List<string> unknown)
    {
        var result = new List<Nrb>(accounts?.Count ?? 0);
        for (var i = 0; i < (accounts?.Count ?? 0); i++)
        {
            if (Nrb.TryParse(accounts![i], out var nrb))
            {
                result.Add(nrb);
            }
            else
            {
                unknown.Add("accountNumbers[" + i.ToString(CultureInfo.InvariantCulture) + "]");
            }
        }

        return result;
    }
}
