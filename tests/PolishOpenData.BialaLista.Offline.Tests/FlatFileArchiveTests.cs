using System.IO;
using System.Linq;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.BialaLista.Offline.Tests;

public sealed class FlatFileArchiveTests
{
    private static FileStream OpenFixture() => File.OpenRead(Path.Combine(
        System.AppContext.BaseDirectory, "Fixtures", "bialalista-offline", "20261013.7z"));

    [Fact]
    public void Lists_entries_of_fixture()
    {
        using var archive = OpenFixture();

        var names = FlatFileArchive.EntryNames(archive);

        Assert.Equal(["20261013.json", "20261013.json.sha512sum"], names.OrderBy(n => n, System.StringComparer.Ordinal));
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
    public void Missing_entry_throws_unexpected_format()
    {
        using var archive = OpenFixture();

        Assert.ThrowsAny<InvalidDataException>(() => FlatFileArchive.OpenEntry(archive, "20990101.json"));
    }
}
