using System;
using PolishOpenData.Bir;

namespace PolishOpenData.Mcp.Tests;

/// <summary>The pure parts of lookup_regon: what reaches the model from GUS free text, and the address line.</summary>
public sealed class RegonViewTests
{
    [Fact]
    public void Clean_removes_control_characters_and_caps_length()
    {
        Assert.Equal("AB C", RegonTools.Clean("A\u0007B\r\nC"));
        Assert.Equal(new string('x', 199) + "…", RegonTools.Clean(new string('x', 250)));
        Assert.Equal(new string('x', 200), RegonTools.Clean(new string('x', 200)));
        Assert.Null(RegonTools.Clean("  \t "));
        Assert.Null(RegonTools.Clean(null));
    }

    [Theory]
    [InlineData(null, null, null, null, "active")]
    [InlineData(null, null, "2012-05-01", null, "suspended")]
    [InlineData(null, null, "2012-05-01", "2012-09-01", "active")]
    [InlineData(null, null, "2012-05-01", "2011-01-01", "suspended")]
    [InlineData("2020-12-31", null, null, null, "ended")]
    [InlineData(null, "2021-01-15", null, null, "ended")]
    [InlineData("2020-12-31", null, "2012-05-01", null, "ended")]
    public void Status_is_derived_from_the_row_dates(string? end, string? removal, string? suspension, string? resumption, string expected)
    {
        var details = new RegonDetails
        {
            EndDate = Date(end),
            RegonRemovalDate = Date(removal),
            SuspensionDate = Date(suspension),
            ResumptionDate = Date(resumption),
        };

        Assert.Equal(expected, RegonTools.Status(details, null));
    }

    [Fact]
    public void Status_uses_the_search_rows_end_date_when_the_report_has_none()
    {
        Assert.Equal("ended", RegonTools.Status(new RegonDetails(), new DateOnly(2020, 12, 31)));
    }

    private static DateOnly? Date(string? text) => text is null ? null : DateOnly.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void FormatAddress_skips_empty_parts()
    {
        var street = new RegonAddress { Street = "ul. Test-Prosta", BuildingNumber = "1", ApartmentNumber = "2", PostalCode = "00-001", PostTown = "Warszawa", City = "Warszawa" };
        var village = new RegonAddress { City = "Piaseczno", BuildingNumber = "7", PostalCode = "05-500" };

        Assert.Equal("ul. Test-Prosta 1/2, 00-001 Warszawa", RegonTools.FormatAddress(street));
        Assert.Equal("Piaseczno 7, 05-500 Piaseczno", RegonTools.FormatAddress(village));
        Assert.Null(RegonTools.FormatAddress(new RegonAddress()));
    }
}
