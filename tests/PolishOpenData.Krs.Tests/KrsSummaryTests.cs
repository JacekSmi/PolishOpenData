using System;
using System.Linq;
using PolishOpenData.Krs;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Krs.Tests;

public class KrsSummaryTests
{
    private static KrsCompanySummary Summary(string file) => KrsJson.DeserializeCurrent(Fixture.Read("krs/" + file))!.ToSummary();

    [Fact]
    public void Small_limited_company()
    {
        var s = Summary("current-P-0001268296.json");

        Assert.Equal(KrsNumber.Parse("1268296"), s.Krs);
        Assert.Equal(KrsRegister.Entrepreneurs, s.Register);
        Assert.Equal("GYM CENTER 8.0 SPÓŁKA Z OGRANICZONĄ ODPOWIEDZIALNOŚCIĄ", s.Name);
        Assert.Equal("SPÓŁKA Z OGRANICZONĄ ODPOWIEDZIALNOŚCIĄ", s.LegalForm);
        Assert.Equal(Nip.Parse("9452336418"), s.Nip);
        Assert.Equal(Regon.Parse("545772924"), s.Regon);            // KRS sends 54577292400000
        Assert.Equal("UL. OSTATNIA 1D, 31-444 KRAKÓW", s.Address);
        Assert.Equal("KRAKÓW", s.Locality);
        Assert.Equal("MAŁOPOLSKIE", s.Voivodeship);
        Assert.Equal(10000.00m, s.ShareCapital);
        Assert.Equal("PLN", s.ShareCapitalCurrency);
        Assert.Equal(new DateOnly(2026, 9, 22), s.RegisteredOn);
        Assert.Equal(3, s.LastEntryNumber);
        Assert.False(s.IsPublicBenefitOrganization);

        Assert.Equal("ZARZĄD", s.Representation!.Name);
        var member = Assert.Single(s.Representation.Members);
        Assert.Equal("D***** K******** M****", member.MaskedName);
        Assert.Equal("PREZES ZARZĄDU", member.Function);
        Assert.False(member.IsSuspended);

        Assert.Equal(2, s.Shareholders.Count);
        Assert.True(s.Shareholders[0].IsLegalEntity);
        Assert.Equal("MAJKA GROUP SPÓŁKA Z OGRANICZONĄ ODPOWIEDZIALNOŚCIĄ", s.Shareholders[0].Name);
        Assert.Equal(KrsNumber.Parse("1223769"), s.Shareholders[0].Krs);
        Assert.Equal(Regon.Parse("543978082"), s.Shareholders[0].Regon);
        Assert.Equal("102 UDZIAŁY O WARTOŚCI 5100 ZŁOTYCH", s.Shareholders[0].Shares);
        Assert.False(s.Shareholders[1].IsLegalEntity);
        Assert.Equal("M****** C*****", s.Shareholders[1].Name);

        Assert.Equal(new KrsActivity("93.13.Z", "DZIAŁALNOŚĆ KLUBÓW FITNESS"), s.MainActivity);
        Assert.Equal(5, s.OtherActivities.Count);
        Assert.Equal("85.51.Z", s.OtherActivities[0].Code);
    }

    [Fact]
    public void Joint_stock_company()
    {
        var s = Summary("current-P-0000028860-orlen.json");

        Assert.Equal("ORLEN SPÓŁKA AKCYJNA", s.Name);
        Assert.Equal(Nip.Parse("7740001454"), s.Nip);
        Assert.Equal(Regon.Parse("610188201"), s.Regon);
        Assert.Equal(1451177561.25m, s.ShareCapital);
        Assert.Equal("CHEMIKÓW 7, 09-411 PŁOCK-BIAŁA", s.Address);
        Assert.Equal("ZARZAD@ORLEN.PL", s.Email);
        Assert.Equal("WWW.ORLEN.PL", s.Website);
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 0, 32, 16, TimeSpan.Zero), s.ExtractedAt!.Value.ToUniversalTime());
        Assert.Equal(9, s.Representation!.Members.Count);
        Assert.Equal("I******* F*****", s.Representation.Members[0].MaskedName);
        var board = Assert.Single(s.SupervisoryBodies);
        Assert.Equal("RADA NADZORCZA", board.Name);
        Assert.Equal(10, board.Members.Count);
        Assert.Equal(5, s.Proxies.Count);
        Assert.All(s.Proxies, p => Assert.DoesNotContain("REDACTED", p.MaskedName, StringComparison.Ordinal));
        Assert.Equal("19.20.Z", s.MainActivity!.Code);
        Assert.Equal(9, s.OtherActivities.Count);
    }

    [Fact]
    public void Foundation_in_register_s()
    {
        var s = Summary("current-S-0000030897-wosp.json");

        Assert.Equal(KrsRegister.Associations, s.Register);
        Assert.Equal("FUNDACJA", s.LegalForm);
        Assert.Equal(Nip.Parse("5213003700"), s.Nip);
        Assert.Null(s.Regon);
        Assert.Null(s.ShareCapital);
        Assert.True(s.IsPublicBenefitOrganization);
        Assert.Equal("DOMINIKAŃSKA 19 C, 02-738 WARSZAWA", s.Address);
        Assert.Equal(4, s.Representation!.Members.Count);
        Assert.Equal("RADA FUNDACJI", Assert.Single(s.SupervisoryBodies).Name);
        Assert.Null(s.MainActivity);
        Assert.Equal(16, s.OtherActivities.Count);
        Assert.Empty(s.Shareholders);
    }

    [Theory]
    [InlineData("61018820100000", "610188201")]   // padded form fails the 14-digit checksum
    [InlineData("10000004300000", "100000043")]   // padded form happens to pass the 14-digit checksum
    public void Padded_regon_is_normalised_to_nine_digits(string krsValue, string expected)
    {
        var extract = new KrsCurrentExtract
        {
            Dane = new KrsCurrentData
            {
                Dzial1 = new KrsDzial1
                {
                    DanePodmiotu = new KrsDanePodmiotu { Identyfikatory = new KrsIdentyfikatory { Regon = krsValue } },
                },
            },
        };

        Assert.Equal(Regon.Parse(expected), extract.ToSummary().Regon);
    }

    [Fact]
    public void Empty_extract_does_not_throw()
    {
        var s = new KrsCurrentExtract().ToSummary();
        Assert.Equal(string.Empty, s.Name);
        Assert.True(s.Krs.IsEmpty);
        Assert.Null(s.Representation);
        Assert.Empty(s.OtherActivities);
    }
}
