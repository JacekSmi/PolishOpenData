using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Eli.Tests;

public class EliMapperTests
{
    private static T Read<T>(string fixture, JsonTypeInfo<T> info) => JsonSerializer.Deserialize(Fixture.Read(fixture), info)!;

    [Fact]
    public void Act_KC_MapsTheSummary()
    {
        var act = EliMapper.ToAct(Read("eli/act-DU-1964-93.json", EliJson.Act));

        Assert.Equal("DU/1964/93", act.Summary.Id.ToString());
        Assert.Equal("Dz.U. 1964 nr 16 poz. 93", act.Summary.DisplayAddress);
        Assert.Equal("Ustawa", act.Summary.Type);
        Assert.Equal("akt posiada tekst jednolity", act.Summary.Status);
        Assert.True(act.Summary.InForce);
        Assert.True(act.Summary.HasHtml);
        Assert.True(act.Summary.HasPdf);
        Assert.Equal(new DateOnly(1964, 4, 23), act.Summary.AnnouncementDate);
        Assert.Equal(new DateOnly(1965, 1, 1), act.Summary.EntryIntoForce);
    }

    [Fact]
    public void Act_KC_MapsConsolidatedTextsNewestFirst()
    {
        var act = EliMapper.ToAct(Read("eli/act-DU-1964-93.json", EliJson.Act));

        Assert.Equal(11, act.ConsolidatedTexts.Count);
        Assert.Equal("DU/2026/795", act.ConsolidatedTexts[0].ToString());
        Assert.Equal("DU/2025/1071", act.ConsolidatedTexts[1].ToString());
    }

    [Fact]
    public void Act_KC_AmendmentsKeepFutureDates()
    {
        var act = EliMapper.ToAct(Read("eli/act-DU-1964-93.json", EliJson.Act));

        var future = act.Amendments.Single(a => a.Id.ToString() == "DU/2026/507");
        Assert.Equal(new DateOnly(2028, 11, 1), future.EffectiveDate);
        Assert.Equal(113, act.Amendments.Count);
    }

    [Fact]
    public void Act_KC_KeepsOtherReferenceGroupsRaw()
    {
        var act = EliMapper.ToAct(Read("eli/act-DU-1964-93.json", EliJson.Act));

        Assert.True(act.References.ContainsKey("Akty wykonawcze"));
        Assert.True(act.References.ContainsKey("Orzeczenie TK"));
        Assert.False(act.References.ContainsKey("Inf. o tekście jednolitym"));
        Assert.False(act.References.ContainsKey("Akty zmieniające"));
    }

    [Fact]
    public void Act_ConsolidatedText_PointsBackToTheBaseAct()
    {
        var act = EliMapper.ToAct(Read("eli/act-DU-2026-795.json", EliJson.Act));

        Assert.Equal("DU/1964/93", Assert.Single(act.BaseActs).ToString());
        Assert.False(act.Summary.HasHtml);
        Assert.True(act.Summary.HasPdf);
        Assert.Equal(new DateOnly(2026, 5, 27), act.Summary.AnnouncementDate);
    }

    [Fact]
    public void Act_ExpiredConsolidatedText_IsNotInForce()
    {
        var act = EliMapper.ToAct(Read("eli/act-DU-2024-1061.json", EliJson.Act));

        Assert.False(act.Summary.InForce);
        Assert.True(act.Summary.HasHtml);
    }

    [Fact]
    public void Search_MapsItemsAndTotal()
    {
        var dto = Read("eli/search-kodeks-cywilny.json", EliJson.Search);
        var items = dto.Items!.Select(EliMapper.ToSummary).ToList();

        Assert.Equal(48, dto.TotalCount);
        Assert.Equal(5, items.Count);
        Assert.Equal("DU/2026/184", items[0].Id.ToString());
    }

    [Fact]
    public void Struct_FindsArticle446_1_AndKeepsNestedUnits()
    {
        var units = EliMapper.ToUnits(Read("eli/struct-DU-2024-1061.json", EliJson.Struct));

        var arti = Flatten(units).Where(u => u.Type == "arti").ToList();
        var sup = arti.Single(u => u.Name == "446_1");
        Assert.Equal("book_TRZECIA-titl_VI-arti_446_1", sup.Id);
        Assert.Equal(6, arti.Count);
        Assert.NotEmpty(arti.Single(u => u.Name == "417").Children);
    }

    private static System.Collections.Generic.IEnumerable<EliUnit> Flatten(System.Collections.Generic.IEnumerable<EliUnit> units) =>
        units.SelectMany(u => new[] { u }.Concat(Flatten(u.Children)));

    [Fact]
    public void BadDate_Throws()
    {
        var json = """{"publisher":"DU","year":2024,"pos":1,"displayAddress":"x","title":"t","announcementDate":"24-01-02"}""";
        Assert.Throws<FormatException>(() => EliMapper.ToSummary(JsonSerializer.Deserialize(json, EliJson.Act)!));
    }

    [Fact]
    public void UnreadableConsolidatedTextId_Throws()
    {
        var json = """{"publisher":"DU","year":2024,"pos":1,"displayAddress":"x","title":"t","references":{"Inf. o tekście jednolitym":[{"id":"nonsense"}]}}""";
        Assert.Throws<FormatException>(() => EliMapper.ToAct(JsonSerializer.Deserialize(json, EliJson.Act)!));
    }

    [Fact]
    public void UnreadableIdInAnOtherGroup_IsSkipped()
    {
        var json = """{"publisher":"DU","year":2024,"pos":1,"displayAddress":"x","title":"t","references":{"Orzeczenie TK":[{"id":"nonsense"},{"id":"DU/2020/5"}]}}""";

        var act = EliMapper.ToAct(JsonSerializer.Deserialize(json, EliJson.Act)!);

        Assert.Equal("DU/2020/5", Assert.Single(act.References["Orzeczenie TK"]).ToString());
    }

    [Fact]
    public void MissingPosition_Throws()
    {
        var json = """{"publisher":"DU","year":2024,"displayAddress":"x","title":"t"}""";
        Assert.Throws<FormatException>(() => EliMapper.ToSummary(JsonSerializer.Deserialize(json, EliJson.Act)!));
    }

    [Fact]
    public void UnknownFields_AreIgnored()
    {
        var json = """{"publisher":"DU","year":2024,"pos":1,"displayAddress":"x","title":"t","somethingNew":{"a":1},"inForce":"SOMETHING_ELSE"}""";

        var summary = EliMapper.ToSummary(JsonSerializer.Deserialize(json, EliJson.Act)!);

        Assert.Null(summary.InForce);
    }
}
