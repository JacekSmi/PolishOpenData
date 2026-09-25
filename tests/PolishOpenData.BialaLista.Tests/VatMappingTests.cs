using System;
using System.Linq;
using Microsoft.Extensions.Time.Testing;
using PolishOpenData.BialaLista;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.BialaLista.Tests;

public class VatMappingTests
{
    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 9, 24, 8, 0, 0, TimeSpan.Zero));

    private static WlEntityResponse Entity(string file) =>
        BialaListaJson.Deserialize(Fixture.Read("bialalista/" + file), BialaListaJson.EntityResponse)!;

    [Fact]
    public void Maps_orlen()
    {
        var item = Entity("search-nip-orlen.json").Result!;
        var subject = VatMapper.MapSubject(item.Subject!);

        Assert.Equal("ORLEN SPÓŁKA AKCYJNA", subject.Name);
        Assert.Equal(Nip.Parse("7740001454"), subject.Nip);
        Assert.Equal(VatStatus.Active, subject.VatStatus);
        Assert.Equal("Czynny", subject.VatStatusRaw);
        Assert.Equal(Regon.Parse("610188201"), subject.Regon);
        Assert.Equal(KrsNumber.Parse("28860"), subject.Krs);
        Assert.Null(subject.ResidenceAddress);
        Assert.Equal("CHEMIKÓW 7, 09-411 PŁOCK", subject.WorkingAddress);
        Assert.Equal(new DateOnly(1993, 7, 5), subject.RegistrationLegalDate);
        Assert.Equal(236, subject.AccountNumbers.Count);
        Assert.Equal(Nrb.Parse("06160011271843983820000034"), subject.AccountNumbers[0]);
        Assert.True(subject.HasVirtualAccounts);
        Assert.Empty(subject.Representatives);
        Assert.Empty(subject.UnknownFields);

        Assert.Equal("MVRqg-98jk3i1", item.RequestId);
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 0, 36, 1, TimeSpan.Zero), VatMapper.ParseRequestTime(item.RequestDateTime, Clock).ToUniversalTime());
    }

    [Fact]
    public void Not_found_has_no_subject_but_keeps_request_id()
    {
        var item = Entity("search-nip-notfound.json").Result!;
        Assert.Null(item.Subject);
        Assert.Equal("h1zOr-98jk3jj", item.RequestId);
    }

    [Fact]
    public void Maps_batch_entries_by_identifier()
    {
        var list = BialaListaJson.Deserialize(Fixture.Read("bialalista/search-nips-mixed.json"), BialaListaJson.EntryListResponse)!.Result!;
        var entries = VatMapper.MapEntries(list.Entries);

        Assert.Equal(4, entries.Count);
        var notFound = entries.Single(e => e.Identifier == "9999999982");
        Assert.Empty(notFound.Subjects);
        Assert.Null(notFound.Error);

        var invalid = entries.Single(e => e.Identifier == "7740001455");
        Assert.Equal("WL-115", invalid.Error!.Code);
        Assert.Equal("Nieprawidłowy NIP.", invalid.Error.Message);

        var pzu = Assert.Single(entries.Single(e => e.Identifier == "5260251049").Subjects);
        Assert.Equal(VatStatus.NotRegistered, pzu.VatStatus);
        Assert.Equal(new DateOnly(2026, 3, 31), pzu.RemovalDate);
        Assert.Equal("Art. 96 ust. 7ba", pzu.RemovalBasis);
        Assert.Equal(7, pzu.Representatives.Count);
        Assert.Equal("JAN", pzu.Representatives[0].FirstName);
        Assert.Empty(pzu.AccountNumbers);

        Assert.Equal("ORLEN SPÓŁKA AKCYJNA", Assert.Single(entries.Single(e => e.Identifier == "7740001454").Subjects).Name);
    }

    [Fact]
    public void Null_arrays_and_unknown_fields_are_tolerated()
    {
        var subject = VatMapper.MapSubject(Entity("synthetic-search-nip-nullarrays-unknownfield.json").Result!.Subject!);

        Assert.Equal(VatStatus.Exempt, subject.VatStatus);
        Assert.Equal(new DateOnly(2025, 1, 1), subject.ExemptionSmeDate);
        Assert.Empty(subject.AccountNumbers);
        Assert.Empty(subject.Representatives);
        Assert.False(subject.HasVirtualAccounts);
        Assert.Equal(new[] { "brandNewField" }, subject.UnknownFields);
    }

    [Fact]
    public void Unexpected_values_do_not_break_mapping()
    {
        var subject = VatMapper.MapSubject(new WlSubject
        {
            Name = "X",
            StatusVat = "Nowy status",
            Nip = "123",
            AccountNumbers = ["06160011271843983820000034", "not-an-account"],
        });

        Assert.Equal(VatStatus.Unknown, subject.VatStatus);
        Assert.Equal("Nowy status", subject.VatStatusRaw);
        Assert.Null(subject.Nip);
        Assert.Single(subject.AccountNumbers);
        Assert.Equal(new[] { "accountNumbers[1]" }, subject.UnknownFields);
    }

    [Fact]
    public void Unknown_person_fields_are_listed_once_per_list()
    {
        var subject = VatMapper.MapSubject(BialaListaJson.Deserialize(
            """
            {"result":{"subject":{"name":"X",
              "representatives":[{"firstName":"JAN","newKey":1},{"firstName":"ANNA","newKey":2,"otherKey":3},{"firstName":"EWA","newKey":4}],
              "partners":[{"companyName":"Y","newKey":5}]}}}
            """,
            BialaListaJson.EntityResponse)!.Result!.Subject!);

        Assert.Equal(3, subject.Representatives.Count);
        Assert.Equal(new[] { "representatives[].newKey", "representatives[].otherKey", "partners[].newKey" }, subject.UnknownFields);
    }

    [Fact]
    public void Missing_request_time_falls_back_to_the_clock()
    {
        Assert.Equal(Clock.GetUtcNow(), VatMapper.ParseRequestTime(null, Clock));
        Assert.Equal(Clock.GetUtcNow(), VatMapper.ParseRequestTime("garbage", Clock));
    }

    [Fact]
    public void Check_response_deserializes()
    {
        var check = BialaListaJson.Deserialize(Fixture.Read("bialalista/check-nip-tak.json"), BialaListaJson.CheckResponse)!.Result!;
        Assert.Equal("TAK", check.AccountAssigned);
        Assert.Equal("NiR01-98jk3mi", check.RequestId);
    }
}
