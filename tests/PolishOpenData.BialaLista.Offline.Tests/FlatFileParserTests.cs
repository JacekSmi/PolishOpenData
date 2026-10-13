using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.BialaLista.Offline.Tests;

public sealed class FlatFileParserTests
{
    private static readonly DateOnly FileDate = new(2026, 10, 13);
    private static readonly string Hash = new('a', 128);
    private const string Mask = "XX10201026XXXXXXXXYYYYYYXX";

    private static ParsedFlatFile ParseFixture(Func<Stream, Stream>? wrap = null)
    {
        using var json = new MemoryStream(Fixture.ReadBytes("bialalista-offline/20261013.json"));
        return FlatFileParser.Parse(wrap is null ? json : wrap(json), FileDate, CancellationToken.None);
    }

    private static ParsedFlatFile ParseText(string json) =>
        FlatFileParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(json)), FileDate, CancellationToken.None);

    private static string Document(
        string header = "{\"dataGenerowaniaDanych\":\"20261013\",\"liczbaTransformacji\":\"3\"}",
        string? hash = null,
        string mask = Mask,
        string extra = "") =>
        "{" + extra + "\"naglowek\":" + header + ",\"skrotyPodatnikowCzynnych\":[\"" + (hash ?? Hash) + "\"],"
        + "\"skrotyPodatnikowZwolnionych\":[],\"maski\":[\"" + mask + "\"]}";

    private static string Header(string date, string rounds) =>
        "{\"dataGenerowaniaDanych\":\"" + date + "\",\"liczbaTransformacji\":" + rounds + "}";

    private static void AssertUnexpectedFormat(string json)
    {
        var ex = Assert.Throws<VatWhitelistDataUnavailableException>(() => ParseText(json));

        Assert.Equal(VatWhitelistDataUnavailableReason.UnexpectedFormat, ex.Reason);
        Assert.Equal(FileDate, ex.Date);
    }

    [Fact]
    public void Parses_fixture_header_and_counts()
    {
        var parsed = ParseFixture();

        Assert.Equal(new DateOnly(2026, 10, 13), parsed.GeneratedOn);
        Assert.Equal(3, parsed.Transformations);
        Assert.Equal(22 * 16, parsed.Active.Length);
        Assert.Equal(21 * 16, parsed.Exempt.Length);
        Assert.Equal(3, parsed.Masks.Length);
    }

    [Fact]
    public void Active_contains_fixture_pair_prefix()
    {
        var parsed = ParseFixture();
        var digest = FlatFileHash.Compute(FlatFileHash.Input(FileDate, "5261040828", "06160011271843983820000034"), 3);

        var found = Enumerable.Range(0, parsed.Active.Length / 16)
            .Any(i => parsed.Active.AsSpan(i * 16, 16).SequenceEqual(digest.AsSpan(0, 16)));

        Assert.True(found);
    }

    [Fact]
    public void Parses_with_tiny_read_buffer()
    {
        var whole = ParseFixture();

        var trickled = ParseFixture(s => new TrickleStream(s, 7));

        Assert.Equal(whole.Active, trickled.Active);
        Assert.Equal(whole.Exempt, trickled.Exempt);
        Assert.Equal(whole.Masks, trickled.Masks);
        Assert.Equal(whole.GeneratedOn, trickled.GeneratedOn);
        Assert.Equal(whole.Transformations, trickled.Transformations);
    }

    [Theory]
    [InlineData(1)]   // the BOM split across reads
    [InlineData(4096)]
    public void Leading_utf8_bom_is_skipped(int readSize)
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(Document())).ToArray();

        var parsed = FlatFileParser.Parse(new TrickleStream(new MemoryStream(bytes), readSize), FileDate, CancellationToken.None);

        Assert.Equal(3, parsed.Transformations);
        Assert.Equal(16, parsed.Active.Length);
        Assert.Single(parsed.Masks);
    }

    [Fact]
    public void Header_date_may_differ_from_file_date()
    {
        var parsed = ParseText(Document(header: Header("20261012", "\"3\"")));

        Assert.Equal(new DateOnly(2026, 10, 12), parsed.GeneratedOn);
    }

    [Fact]
    public void Unknown_top_level_keys_ignored()
    {
        var parsed = ParseText(Document(
            extra: "\"zbedne\":{\"a\":[1,2,{\"b\":null}],\"c\":\"d\"},\"inne\":7,",
            header: "{\"schemat\":\"dlugi opis\",\"dataGenerowaniaDanych\":\"20261013\",\"liczbaTransformacji\":3}"));

        Assert.Equal(3, parsed.Transformations);
        Assert.Equal(16, parsed.Active.Length);
    }

    [Fact]
    public void Missing_header_is_unexpected_format() =>
        AssertUnexpectedFormat("{\"skrotyPodatnikowCzynnych\":[],\"skrotyPodatnikowZwolnionych\":[],\"maski\":[]}");

    [Fact]
    public void Missing_transformations_is_unexpected_format() =>
        AssertUnexpectedFormat(Document(header: "{\"dataGenerowaniaDanych\":\"20261013\"}"));

    [Theory]
    [InlineData("\"abc\"")]
    [InlineData("\"0\"")]
    [InlineData("\"-1\"")]
    [InlineData("true")]
    public void Non_numeric_transformations_is_unexpected_format(string value) =>
        AssertUnexpectedFormat(Document(header: Header("20261013", value)));

    [Fact]
    public void Bad_header_date_is_unexpected_format() =>
        AssertUnexpectedFormat(Document(header: Header("2026-10-13", "\"3\"")));

    [Theory]
    [InlineData(127)]
    [InlineData(129)]
    public void Bad_hash_length_is_unexpected_format(int length) =>
        AssertUnexpectedFormat(Document(hash: new string('a', length)));

    [Fact]
    public void Non_hex_hash_is_unexpected_format() =>
        AssertUnexpectedFormat(Document(hash: new string('a', 127) + "g"));

    [Fact]
    public void Bad_mask_character_is_unexpected_format() =>
        AssertUnexpectedFormat(Document(mask: "XX10201026XXXXXXXXYYYYYYXZ"));

    [Fact]
    public void Truncated_json_is_unexpected_format() =>
        AssertUnexpectedFormat(Document().Substring(0, Document().Length - 10));

    [Fact]
    public void Cancelled_token_stops_the_parse()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            FlatFileParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(Document())), FileDate, cts.Token));
    }

    /// <summary>Returns at most <c>max</c> bytes per read, to put a buffer boundary inside every token.</summary>
    private sealed class TrickleStream(Stream inner, int max) : Stream
    {
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, Math.Min(count, max));

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
