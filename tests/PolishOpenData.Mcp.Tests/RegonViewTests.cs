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
