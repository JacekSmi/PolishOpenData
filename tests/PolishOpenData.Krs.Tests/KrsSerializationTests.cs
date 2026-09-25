using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using PolishOpenData.Krs;
using PolishOpenData.Krs.Serialization;
#if NET
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using PolishOpenData.Tests.Shared;
#endif

namespace PolishOpenData.Krs.Tests;

public class KrsPublicConverterTests
{
    // This test assembly sees Krs internals (InternalsVisibleTo), so the generator here would accept an internal
    // converter; a consumer's would not (SYSLIB1220). Hence this check, which also runs on .NET Framework. The build-time
    // check is the consumer context in PolishOpenData.Mcp.Tests, which has no InternalsVisibleTo from Krs.
    [Fact]
    public void Every_converter_on_a_public_krs_type_is_usable_by_a_consumer_source_generator()
    {
        var offenders = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var type in typeof(KrsClient).Assembly.GetExportedTypes())
        {
            Check(type.GetCustomAttribute<JsonConverterAttribute>(), type.FullName!);

            // inherited properties too: a consumer's generator serialises every public property of a model, including
            // one declared on a base type outside this assembly, which the exported-type loop would not reach
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            {
                Check(property.GetCustomAttribute<JsonConverterAttribute>(), type.FullName + "." + property.Name);
            }
        }

        Assert.Empty(offenders);

        void Check(JsonConverterAttribute? attribute, string member)
        {
            if (attribute?.ConverterType is { } converter &&
                (!converter.IsVisible || converter.GetConstructor(Type.EmptyTypes) is not { IsPublic: true }))
            {
                offenders.Add(member + " -> " + converter.FullName);
            }
        }
    }

    [Fact]
    public void Public_converters_read_and_write_the_registry_formats()
    {
        var options = new JsonSerializerOptions { Converters = { new KrsDateJsonConverter(), new KrsTimestampJsonConverter() } };

        Assert.Equal(new DateOnly(2026, 9, 17), JsonSerializer.Deserialize<DateOnly>("\"17.09.2026\"", options));
        Assert.Equal("\"17.09.2026\"", JsonSerializer.Serialize(new DateOnly(2026, 9, 17), options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DateOnly>("\"2026-09-17\"", options));

        // Warsaw wall-clock time: +02:00 in summer, +01:00 in winter
        var summer = JsonSerializer.Deserialize<DateTimeOffset>("\"24.09.2026 02:32:16\"", options);
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 0, 32, 16, TimeSpan.Zero), summer);
        Assert.Equal(TimeSpan.FromHours(2), summer.Offset);
        var winter = JsonSerializer.Deserialize<DateTimeOffset>("\"15.01.2026 10:00:00\"", options);
        Assert.Equal(TimeSpan.FromHours(1), winter.Offset);
        Assert.Equal("\"24.09.2026 02:32:16\"", JsonSerializer.Serialize(new DateTimeOffset(2026, 9, 24, 0, 32, 16, TimeSpan.Zero), options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DateTimeOffset>("\"2026-09-24T02:32:16\"", options));

        // well formed, but with the Warsaw offset applied the moment falls before DateTimeOffset.MinValue
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DateTimeOffset>("\"01.01.0001 00:00:00\"", options));
    }
}

#if NET

// Stands in for a consumer's own source-generated context over the public KRS models (the generator cannot handle
// the netstandard2.0 DateOnly polyfill, hence .NET only). Whether a consumer's generator accepts the converters is
// checked by KrsPublicConverterTests above; these tests check that the generated code reads and writes the same values
// as the reflection path.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(KrsCurrentExtract))]
[JsonSerializable(typeof(KrsFullExtract))]
[JsonSerializable(typeof(ConsumerCurrentEnvelope))]
[JsonSerializable(typeof(ConsumerFullEnvelope))]
internal sealed partial class ConsumerKrsJsonContext : JsonSerializerContext;

internal sealed class ConsumerCurrentEnvelope
{
    public KrsCurrentExtract? Odpis { get; set; }
}

internal sealed class ConsumerFullEnvelope
{
    public KrsFullExtract? Odpis { get; set; }
}

public class KrsSourceGenerationTests
{
    private static readonly JsonSerializerOptions Reflection = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    [Fact]
    public void Current_extract_dates_round_trip_through_a_consumer_context_like_reflection()
    {
        var json = Fixture.Read("krs/current-P-0000028860-orlen.json");
        var generated = JsonSerializer.Deserialize(json, ConsumerKrsJsonContext.Default.ConsumerCurrentEnvelope)!.Odpis!.NaglowekA!;
        var reflected = JsonSerializer.Deserialize<ConsumerCurrentEnvelope>(json, Reflection)!.Odpis!.NaglowekA!;

        Assert.NotNull(generated.DataCzasOdpisu);
        Assert.NotNull(generated.StanZDnia);
        Assert.NotNull(generated.DataRejestracjiWKrs);
        Assert.NotNull(generated.DataOstatniegoWpisu);
        Assert.Equal(reflected.DataCzasOdpisu, generated.DataCzasOdpisu);
        Assert.Equal(reflected.DataCzasOdpisu!.Value.Offset, generated.DataCzasOdpisu!.Value.Offset);
        Assert.Equal(reflected.StanZDnia, generated.StanZDnia);
        Assert.Equal(reflected.DataRejestracjiWKrs, generated.DataRejestracjiWKrs);
        Assert.Equal(reflected.DataOstatniegoWpisu, generated.DataOstatniegoWpisu);

        // written back in the registry's own formats, identically by both paths
        var source = JsonNode.Parse(json)!["odpis"]!["naglowekA"]!;
        var written = JsonNode.Parse(JsonSerializer.Serialize(generated, ConsumerKrsJsonContext.Default.KrsCurrentHeader))!;
        var writtenByReflection = JsonNode.Parse(JsonSerializer.Serialize(reflected, Reflection))!;
        // (field name in the KRS JSON, name written by the camelCase web defaults)
        foreach (var (read, name) in new[]
        {
            ("dataCzasOdpisu", "dataCzasOdpisu"),
            ("stanZDnia", "stanZDnia"),
            ("dataRejestracjiWKRS", "dataRejestracjiWKrs"),
            ("dataOstatniegoWpisu", "dataOstatniegoWpisu"),
        })
        {
            Assert.Equal(source[read]!.GetValue<string>(), written[name]!.GetValue<string>());
            Assert.Equal(writtenByReflection[name]!.GetValue<string>(), written[name]!.GetValue<string>());
        }

        // the whole extract, written and read back through the consumer context, keeps the same dates
        var extract = JsonSerializer.Deserialize(json, ConsumerKrsJsonContext.Default.ConsumerCurrentEnvelope)!.Odpis!;
        var again = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(extract, ConsumerKrsJsonContext.Default.KrsCurrentExtract),
            ConsumerKrsJsonContext.Default.KrsCurrentExtract)!.NaglowekA!;
        Assert.Equal(generated.DataCzasOdpisu, again.DataCzasOdpisu);
        Assert.Equal(generated.DataCzasOdpisu!.Value.Offset, again.DataCzasOdpisu!.Value.Offset);
        Assert.Equal(generated.StanZDnia, again.StanZDnia);
        Assert.Equal(generated.DataRejestracjiWKrs, again.DataRejestracjiWKrs);
        Assert.Equal(generated.DataOstatniegoWpisu, again.DataOstatniegoWpisu);
    }

    [Fact]
    public void Full_extract_dates_round_trip_through_a_consumer_context_like_reflection()
    {
        var json = Fixture.Read("krs/full-P-0000106150-removed-trimmed.json");
        var generated = JsonSerializer.Deserialize(json, ConsumerKrsJsonContext.Default.ConsumerFullEnvelope)!.Odpis!;
        var reflected = JsonSerializer.Deserialize<ConsumerFullEnvelope>(json, Reflection)!.Odpis!;

        Assert.NotNull(generated.NaglowekP!.DataCzasOdpisu);
        Assert.Equal(reflected.NaglowekP!.DataCzasOdpisu, generated.NaglowekP.DataCzasOdpisu);
        Assert.Equal(reflected.NaglowekP.StanZDnia, generated.NaglowekP.StanZDnia);
        Assert.Equal(reflected.NaglowekP.Wpis!.Count, generated.NaglowekP.Wpis!.Count);
        for (var i = 0; i < generated.NaglowekP.Wpis.Count; i++)
        {
            Assert.Equal(reflected.NaglowekP.Wpis[i].DataWpisu, generated.NaglowekP.Wpis[i].DataWpisu);
            Assert.Equal(reflected.NaglowekP.Wpis[i].DataUprawomocnienia, generated.NaglowekP.Wpis[i].DataUprawomocnienia);
        }

        Assert.Equal(new DateOnly(2022, 8, 12), generated.RemovedOn);

        var last = generated.NaglowekP.Wpis[generated.NaglowekP.Wpis.Count - 1];
        var written = JsonNode.Parse(JsonSerializer.Serialize(last, ConsumerKrsJsonContext.Default.KrsWpis))!;
        Assert.Equal("12.08.2022", written["dataWpisu"]!.GetValue<string>());
        Assert.Equal("02.09.2022", written["dataUprawomocnienia"]!.GetValue<string>());
    }
}
#endif
