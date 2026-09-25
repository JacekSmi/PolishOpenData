using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using PolishOpenData.Krs;

namespace PolishOpenData.Mcp.Tests;

// A consumer's own source-generated context over the public KRS models. It lives here, not in the Krs tests, because
// Krs grants InternalsVisibleTo only to its own test project: this project sees Krs as a consumer does. If a public KRS
// model pointed a [JsonConverter] at an internal converter again, the generator here would report SYSLIB1220 and
// TreatWarningsAsErrors would fail the build. The web defaults are what reading the registry's own JSON needs
// (camelCase, case-insensitive: the registry writes "dataRejestracjiWKRS" for DataRejestracjiWKrs).
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(KrsCurrentExtract))]
[JsonSerializable(typeof(KrsFullExtract))]
internal sealed partial class ConsumerKrsContext : JsonSerializerContext;

public class KrsConsumerContextTests
{
    [Fact]
    public void A_consumer_context_reads_and_writes_krs_dates_with_the_public_converters()
    {
        var header = JsonSerializer.Deserialize(
            """{"naglowekA":{"numerKRS":"0000028860","dataCzasOdpisu":"24.09.2026 02:32:16","stanZDnia":"17.09.2026","dataRejestracjiWKRS":"19.07.2001"}}""",
            ConsumerKrsContext.Default.KrsCurrentExtract)!.NaglowekA!;
        Assert.Equal("0000028860", header.NumerKrs);
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 2, 32, 16, TimeSpan.FromHours(2)), header.DataCzasOdpisu);
        Assert.Equal(TimeSpan.FromHours(2), header.DataCzasOdpisu!.Value.Offset);
        Assert.Equal(new DateOnly(2026, 9, 17), header.StanZDnia);
        Assert.Equal(new DateOnly(2001, 7, 19), header.DataRejestracjiWKrs);

        var full = JsonSerializer.Deserialize(
            """{"naglowekP":{"wpis":[{"dataWpisu":"12.08.2022","dataUprawomocnienia":"02.09.2022"}]}}""",
            ConsumerKrsContext.Default.KrsFullExtract)!;
        var entry = Assert.Single(full.NaglowekP!.Wpis!);
        Assert.Equal(new DateOnly(2022, 8, 12), entry.DataWpisu);
        Assert.Equal(new DateOnly(2022, 9, 2), entry.DataUprawomocnienia);

        var written = JsonSerializer.Serialize(full, ConsumerKrsContext.Default.KrsFullExtract);
        Assert.Contains("\"dataWpisu\":\"12.08.2022\"", written, StringComparison.Ordinal);
        Assert.Contains("\"dataUprawomocnienia\":\"02.09.2022\"", written, StringComparison.Ordinal);
    }
}
