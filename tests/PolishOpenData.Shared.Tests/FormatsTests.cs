using System;
using System.Text.Json;
using PolishOpenData.Internal;

namespace PolishOpenData.Shared.Tests;

public class FormatsTests
{
    [Theory]
    [InlineData("1451177561,25", "1451177561.25")]
    [InlineData("10000,00", "10000.00")]
    [InlineData("1 000,50", "1000.50")]
    [InlineData("1,25", "1.25")]
    public void Parses_polish_decimals(string input, string expected)
    {
        Assert.True(PolishFormats.TryParseDecimal(input, out var value));
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), value);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("abc")]
    public void Rejects_non_numbers(string? input)
    {
        Assert.False(PolishFormats.TryParseDecimal(input, out _));
    }

    [Fact]
    public void Parses_dates_and_warsaw_timestamps()
    {
        Assert.Equal(new DateOnly(2001, 7, 19), PolishFormats.ParseDate("19.07.2001", "dd.MM.yyyy"));
        Assert.Equal(
            new DateTimeOffset(2026, 9, 24, 0, 36, 1, TimeSpan.Zero),
            PolishFormats.ParseWarsawDateTime("24-09-2026 02:36:01", "dd-MM-yyyy HH:mm:ss").ToUniversalTime());
    }

    [Fact]
    public void Converters_read()
    {
        var sample = JsonSerializer.Deserialize<ConverterSample>(
            """{"Dotted":"19.07.2001","Iso":"1993-07-05","Stamp":"24.09.2026 02:32:16"}""")!;
        Assert.Equal(new DateOnly(2001, 7, 19), sample.Dotted);
        Assert.Equal(new DateOnly(1993, 7, 5), sample.Iso);
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 0, 32, 16, TimeSpan.Zero), sample.Stamp!.Value.ToUniversalTime());
    }

    [Fact]
    public void Converters_write()
    {
        var sample = new ConverterSample
        {
            Dotted = new DateOnly(2001, 7, 19),
            Iso = new DateOnly(1993, 7, 5),
            Stamp = new DateTimeOffset(2026, 9, 24, 0, 32, 16, TimeSpan.Zero),
        };
        Assert.Equal(
            """{"Dotted":"19.07.2001","Iso":"1993-07-05","Stamp":"24.09.2026 02:32:16"}""",
            JsonSerializer.Serialize(sample));
    }

    [Fact]
    public void Timestamp_converter_rejects_a_moment_that_has_no_utc_value()
    {
        // 01.01.0001 00:00:00 in Warsaw is before 0001-01-01 00:00 UTC, which DateTimeOffset cannot hold
        var ex = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ConverterSample>("""{"Stamp":"01.01.0001 00:00:00"}"""));
        Assert.Contains("01.01.0001 00:00:00", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Converters_accept_null_for_nullable_properties()
    {
        var sample = JsonSerializer.Deserialize<ConverterSample>("""{"Dotted":null,"Iso":null,"Stamp":null}""")!;
        Assert.Null(sample.Dotted);
        Assert.Null(sample.Iso);
        Assert.Null(sample.Stamp);
    }
}

internal sealed class ConverterSample
{
    [System.Text.Json.Serialization.JsonConverter(typeof(DottedDateOnlyConverter))]
    public DateOnly? Dotted { get; set; }

    [System.Text.Json.Serialization.JsonConverter(typeof(IsoDateOnlyConverter))]
    public DateOnly? Iso { get; set; }

    [System.Text.Json.Serialization.JsonConverter(typeof(DottedWarsawDateTimeConverter))]
    public DateTimeOffset? Stamp { get; set; }
}
