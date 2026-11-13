using System;
using System.Linq;
using System.Text.Json;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Bdl.Tests;

public class BdlMapperTests
{
    private static T Read<T>(string fixture, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info) =>
        JsonSerializer.Deserialize(Fixture.Read(fixture), info)!;

    [Fact]
    public void UnitData_MapsFractionalValuesAndYears()
    {
        var data = BdlMapper.ToUnitData(Read("bdl/data-by-unit-krakow.json", BdlJson.UnitData));

        Assert.Equal("011212161011", data.UnitId);
        Assert.Equal("Kraków", data.UnitName);
        Assert.Equal(3, data.Series.Count);
        Assert.Equal(2498.4, data.Series.Single(s => s.VariableId == 60559).Values.Single(v => v.Year == 2025).Value);
        Assert.Equal(816614, data.Series.Single(s => s.VariableId == 72305).Values.Single(v => v.Year == 2025).Value);
        Assert.NotNull(data.Series[0].LastUpdate);
    }

    [Fact]
    public void UnitData_NoValueBecomesNull_RealZeroStays()
    {
        var data = BdlMapper.ToUnitData(Read("bdl/data-by-unit-novalue.json", BdlJson.UnitData));

        var first = data.Series.Single(s => s.VariableId == 111).Values;
        Assert.Null(first.Single(v => v.Year == 2024).Value);
        Assert.Equal("x", first.Single(v => v.Year == 2024).Flag.Symbol);
        Assert.Null(first.Single(v => v.Year == 2025).Value);
        Assert.Equal(BdlValueKind.NoValue, first.Single(v => v.Year == 2025).Flag.Kind);

        var real = data.Series.Single(s => s.VariableId == 222).Values.Single();
        Assert.Equal(0, real.Value);
        Assert.Equal("a", real.Flag.Symbol);
    }

    [Fact]
    public void UnitData_UnknownUnit_HasNoSeries()
    {
        var data = BdlMapper.ToUnitData(Read("bdl/empty-unit.json", BdlJson.UnitData));
        Assert.Empty(data.Series);
    }

    [Fact]
    public void MissingAttrId_KeepsTheValueAsUnknown()
    {
        var json = """{"unitId":"1","unitName":"x","results":[{"id":1,"measureUnitId":1,"values":[{"year":"2025","val":816614},{"year":"2024","val":5,"attrId":null}]}]}""";

        var values = BdlMapper.ToUnitData(JsonSerializer.Deserialize(json, BdlJson.UnitData)!).Series.Single().Values;

        Assert.All(values, v => Assert.Equal(BdlValueKind.Unknown, v.Flag.Kind));
        Assert.Equal(816614, values.Single(v => v.Year == 2025).Value);
        Assert.Equal(5, values.Single(v => v.Year == 2024).Value);
    }

    [Fact]
    public void Year_NonNumeric_ThrowsFormatException()
    {
        var json = """{"unitId":"1","unitName":"x","results":[{"id":1,"measureUnitId":1,"values":[{"year":"20x5","val":1,"attrId":1}]}]}""";
        var dto = JsonSerializer.Deserialize(json, BdlJson.UnitData)!;
        Assert.Throws<FormatException>(() => BdlMapper.ToUnitData(dto));
    }

    [Fact]
    public void Units_MapIdLevelKindAndParent()
    {
        var page = Read("bdl/units-search-krakow.json", BdlJson.Units);
        var units = page.Results!.Select(BdlMapper.ToUnit).ToList();

        Assert.Equal(3, units.Count);
        var gmina = units.Single(u => u.Level == BdlLevel.Gmina);
        Assert.Equal("011212161011", gmina.Id);
        Assert.Equal("011212161000", gmina.ParentId);
        Assert.Equal("1", gmina.Kind);
        Assert.Equal("1261011", gmina.Teryt);
    }

    [Fact]
    public void Variable_DimensionsSkipEmpty()
    {
        var json = """{"id":72305,"subjectId":"P2137","n1":"ogółem","n2":"ogółem","level":6,"measureUnitId":26,"measureUnitName":"osoba","years":[2024,2025]}""";
        var v = BdlMapper.ToVariable(JsonSerializer.Deserialize(json, BdlJson.Variable)!);

        Assert.Equal(2, v.Dimensions.Count);
        Assert.Equal("osoba", v.MeasureUnitName);
        Assert.Equal(new[] { 2024, 2025 }, v.Years);
        Assert.Equal(BdlLevel.Gmina, v.Level);
    }

    [Fact]
    public void Variable_UnknownLevel_ThrowsFormatException()
    {
        var json = """{"id":1,"subjectId":"P1","n1":"a","level":99}""";
        Assert.Throws<FormatException>(() => BdlMapper.ToVariable(JsonSerializer.Deserialize(json, BdlJson.Variable)!));
    }

    [Fact]
    public void Subject_MapsNameAndDimensions()
    {
        var s = BdlMapper.ToSubject(Read("bdl/subject-P2137.json", BdlJson.Subject));

        Assert.Equal("P2137", s.Id);
        Assert.Equal("G7", s.ParentId);
        Assert.Equal("Ludność wg grup wieku i płci", s.Name);
        Assert.Equal(new[] { "Wiek", "Płeć" }, s.Dimensions);
    }

    [Fact]
    public void VariableData_MapsUnitsAndTruncation()
    {
        var d = BdlMapper.ToVariableData(Read("bdl/data-by-variable-72305.json", BdlJson.VariableData), truncated: true);

        Assert.Equal(72305, d.VariableId);
        Assert.True(d.Truncated);
        var unit = Assert.Single(d.Units);
        Assert.Equal("Kraków", unit.Name);
        Assert.Equal(816614, unit.Values.Single().Value);
    }

    [Fact]
    public void Wire_IgnoresUnknownFields()
    {
        var json = """{"unitId":"1","unitName":"x","somethingNew":{"a":1},"results":[]}""";
        Assert.NotNull(JsonSerializer.Deserialize(json, BdlJson.UnitData));
    }
}
