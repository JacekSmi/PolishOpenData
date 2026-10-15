using System;
using System.Text.Json;
using PolishOpenData.Geo;

namespace PolishOpenData.Geo.Core.Tests;

public class ParcelIdTests
{
    [Fact]
    public void ParcelId_parses_plain()
    {
        var id = ParcelId.Parse("141201_1.0001.6509");
        Assert.Equal("141201_1", id.Unit);
        Assert.Equal("0001", id.Region);
        Assert.Null(id.Sheet);
        Assert.Equal("6509", id.Number);
    }

    [Fact]
    public void ParcelId_parses_sheet_and_slash()
    {
        var id = ParcelId.Parse(" 146510_8.0507.AR_2.41/1 ");
        Assert.Equal("146510_8", id.Unit);
        Assert.Equal("0507", id.Region);
        Assert.Equal("AR_2", id.Sheet);
        Assert.Equal("41/1", id.Number);
    }

    [Theory]
    [InlineData("141201_1.0001.6509")]
    [InlineData("146510_8.0507.AR_2.41/1")]
    [InlineData("146510_8.0507.41/1")]
    public void ParcelId_round_trips_ToString(string text)
    {
        var id = ParcelId.Parse(text);
        Assert.Equal(text, id.ToString());
        Assert.Equal(id, ParcelId.Parse(id.ToString()));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("146510_8.0507")]
    [InlineData("146510.0507.41")]
    [InlineData("146510_8.0507.41 1")]
    [InlineData("Krzewina 134")]
    [InlineData("146510_8.0507.AR_.41")]
    [InlineData("146510_8.0507.41/")]
    [InlineData("146510_8.0507.AR_2.")]
    [InlineData("146510_8.507.41")]
    [InlineData("146510_8.0507.ar_2.41/1")]
    [InlineData("146510_8.0507.٤١/1")]
    [InlineData("١٤٦٥١٠_8.0507.41/1")]
    [InlineData("146510_8.0507.41//1")]
    public void ParcelId_rejects(string? text)
    {
        Assert.False(ParcelId.TryParse(text, out var id));
        Assert.Equal(default, id);
        if (text is not null)
        {
            Assert.Throws<FormatException>(() => ParcelId.Parse(text));
        }
    }

    [Fact]
    public void Default_ParcelId_is_empty_with_empty_parts()
    {
        var id = default(ParcelId);
        Assert.True(id.IsEmpty);
        Assert.Equal(string.Empty, id.Unit);
        Assert.Equal(string.Empty, id.Region);
        Assert.Null(id.Sheet);
        Assert.Equal(string.Empty, id.Number);
        Assert.Equal(string.Empty, id.ToString());
        Assert.False(ParcelId.Parse("146510_8.0507.41/1").IsEmpty);
    }

    [Fact]
    public void ParcelId_json_round_trip()
    {
        var id = ParcelId.Parse("146510_8.0507.AR_2.41/1");
        var json = JsonSerializer.Serialize(id);
        Assert.Equal("\"146510_8.0507.AR_2.41/1\"", json);
        Assert.Equal(id, JsonSerializer.Deserialize<ParcelId>(json));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ParcelId>("\"nope\""));
    }
}
