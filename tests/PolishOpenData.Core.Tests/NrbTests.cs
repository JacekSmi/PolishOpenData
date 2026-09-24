using System;
using System.Text.Json;
using PolishOpenData;

namespace PolishOpenData.Core.Tests;

public class NrbTests
{
    [Theory]
    [InlineData("06160011271843983820000034")]           // an ORLEN account from the VAT whitelist
    [InlineData("06 1600 1127 1843 9838 2000 0034")]
    [InlineData("PL06160011271843983820000034")]
    [InlineData("PL 06 1600 1127 1843 9838 2000 0034")]
    [InlineData("16160011271234567890123456")]           // synthetic, valid checksum
    public void Parses_valid_input(string input)
    {
        Assert.True(Nrb.TryParse(input, out var nrb));
        Assert.Equal(26, nrb.ToString().Length);
    }

    [Fact]
    public void Formats_canonical_and_iban()
    {
        var nrb = Nrb.Parse("PL 06 1600 1127 1843 9838 2000 0034");
        Assert.Equal("06160011271843983820000034", nrb.ToString());
        Assert.Equal("PL06160011271843983820000034", nrb.ToIban());
    }

    [Theory]
    [InlineData("06160011271843983820000035")]   // one digit changed
    [InlineData("0616001127184398382000003")]    // 25 digits
    [InlineData("DE06160011271843983820000034")]
    [InlineData("0616001127184398382000003X")]
    [InlineData("")]
    public void Rejects_invalid_input(string input)
    {
        Assert.False(Nrb.TryParse(input, out var nrb));
        Assert.True(nrb.IsEmpty);
        Assert.Throws<FormatException>(() => Nrb.Parse(input));
    }

    [Fact]
    public void Serializes_as_26_digits()
    {
        Assert.Equal("\"06160011271843983820000034\"", JsonSerializer.Serialize(Nrb.Parse("PL06160011271843983820000034")));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Nrb>("\"06160011271843983820000035\""));
    }
}
