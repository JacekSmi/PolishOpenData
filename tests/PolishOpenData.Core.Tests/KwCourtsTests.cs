using System;
using System.Linq;
using PolishOpenData;

namespace PolishOpenData.Core.Tests;

public class KwCourtsTests
{
    [Fact]
    public void Table_has_342_current_and_7_historical_codes()
    {
        Assert.Equal(349, KwCourts.All.Count);
        Assert.Equal(342, KwCourts.All.Count(c => c.IsCurrent));
        Assert.Equal(
            new[] { "CZ2C", "GD2W", "KR2I", "KR3I", "RA2Z", "RZ2Z", "SW2K" },
            KwCourts.All.Where(c => !c.IsCurrent).Select(c => c.Code).OrderBy(c => c, StringComparer.Ordinal));
    }

    [Fact]
    public void Every_code_matches_the_official_pattern()
    {
        const string letters = "XABCDEFGHIJKLMNOPRSTUWYZ";
        foreach (var court in KwCourts.All)
        {
            Assert.Equal(4, court.Code.Length);
            Assert.Contains(court.Code[0], letters);
            Assert.Contains(court.Code[1], letters);
            Assert.InRange(court.Code[2], '1', '9');
            Assert.Contains(court.Code[3], letters);
            Assert.False(string.IsNullOrWhiteSpace(court.Name));
        }
    }

    [Theory]
    [InlineData("WA1M", "Warszawy-Mokotowa")]
    [InlineData("wa1m", "Warszawy-Mokotowa")]
    [InlineData("OS1U", "Pułtusku")]
    [InlineData("GL1X", "Żorach")]
    [InlineData("SO1C", "Czeladzi")]
    public void Finds_current_courts(string code, string nameFragment)
    {
        var court = KwCourts.Find(code);
        Assert.NotNull(court);
        Assert.True(court!.IsCurrent);
        Assert.Null(court.LastValidOn);
        Assert.Contains(nameFragment, court.Name, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("KR2I", 2026, 6, 30)]
    [InlineData("GD2W", 2023, 4, 4)]
    [InlineData("RA2Z", 2017, 8, 7)]
    public void Finds_historical_courts(string code, int year, int month, int day)
    {
        var court = KwCourts.Find(code);
        Assert.NotNull(court);
        Assert.False(court!.IsCurrent);
        Assert.Equal(new DateOnly(year, month, day), court.LastValidOn);
    }

    [Theory]
    [InlineData("CIKW")]    // in pyekw's list but not a court
    [InlineData("DIRS")]
    [InlineData("ZZ9Z")]
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_codes_return_null(string? code)
    {
        Assert.Null(KwCourts.Find(code));
    }
}
