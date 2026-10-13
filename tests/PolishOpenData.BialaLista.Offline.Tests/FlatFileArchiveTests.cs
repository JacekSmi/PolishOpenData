using System;
using System.IO;
using System.Linq;
using System.Threading;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.BialaLista.Offline.Tests;

public sealed class FlatFileArchiveTests
{
    private static FileStream OpenFixture(string name = "20261013.7z") => File.OpenRead(Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "bialalista-offline", name));

    private static VatWhitelistDataUnavailableException ReadFails(Stream archive, DateOnly date) =>
        Assert.Throws<VatWhitelistDataUnavailableException>(() => FlatFileArchive.Read(archive, date, CancellationToken.None));

    [Fact]
    public void Lists_entries_of_fixture()
    {
        using var archive = OpenFixture();

        var names = FlatFileArchive.EntryNames(archive);

        Assert.Equal(["20261013.json", "20261013.json.sha512sum"], names.OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void Opens_json_entry_byte_identical()
    {
        using var archive = OpenFixture();
        using var entry = FlatFileArchive.OpenEntry(archive, "20261013.json");
        using var copy = new MemoryStream();

        entry.CopyTo(copy);

        Assert.Equal(Fixture.ReadBytes("bialalista-offline/20261013.json"), copy.ToArray());
    }

    [Fact]
    public void Missing_entry_is_unexpected_format()
    {
        using var archive = OpenFixture();

        var ex = ReadFails(archive, new DateOnly(2026, 10, 15));

        Assert.Equal(VatWhitelistDataUnavailableReason.UnexpectedFormat, ex.Reason);
        Assert.Equal(new DateOnly(2026, 10, 15), ex.Date);
    }

    [Fact]
    public void Reads_and_verifies_fixture()
    {
        using var archive = OpenFixture();

        var parsed = FlatFileArchive.Read(archive, new DateOnly(2026, 10, 13), CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 10, 13), parsed.GeneratedOn);
        Assert.Equal(22 * 16, parsed.Active.Length);
        Assert.Equal(21 * 16, parsed.Exempt.Length);
    }

    [Fact]
    public void Reads_archive_whose_checksum_entry_comes_first()
    {
        using var archive = OpenFixture("20261015-sumfirst.7z");

        var parsed = FlatFileArchive.Read(archive, new DateOnly(2026, 10, 15), CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 10, 15), parsed.GeneratedOn);
    }

    [Fact]
    public void Archive_with_wrong_checksum_is_checksum_mismatch()
    {
        using var archive = OpenFixture("20261014-badsum.7z");

        var ex = ReadFails(archive, new DateOnly(2026, 10, 14));

        Assert.Equal(VatWhitelistDataUnavailableReason.ChecksumMismatch, ex.Reason);
        Assert.Equal(new DateOnly(2026, 10, 14), ex.Date);
    }

    [Fact]
    public void Garbage_archive_is_unexpected_format()
    {
        using var archive = new MemoryStream(new byte[100]);

        var ex = ReadFails(archive, new DateOnly(2026, 10, 13));

        Assert.Equal(VatWhitelistDataUnavailableReason.UnexpectedFormat, ex.Reason);
    }

    [Theory]
    [InlineData(40)]
    [InlineData(100)]
    [InlineData(300)]
    [InlineData(323)]
    [InlineData(420)]
    [InlineData(1000)]
    [InlineData(1293)]
    [InlineData(2000)]
    [InlineData(3000)]
    [InlineData(3330)]
    [InlineData(3427)]
    public void Corrupted_archive_only_throws_the_public_exception(int offset)
    {
        var bytes = Fixture.ReadBytes("bialalista-offline/20261013.7z");
        bytes[offset] ^= 0xFF;
        using var archive = new MemoryStream(bytes);

        var ex = ReadFails(archive, new DateOnly(2026, 10, 13));

        Assert.True(ex.Reason is VatWhitelistDataUnavailableReason.UnexpectedFormat or VatWhitelistDataUnavailableReason.ChecksumMismatch, ex.Reason.ToString());
    }
}
