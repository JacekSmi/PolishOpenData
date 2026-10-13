using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.BialaLista.Offline.Tests;

public sealed class WhitelistIndexTests : IDisposable
{
    private const string ActiveNip = "5261040828";
    private const string ActiveAccount = "06160011271843983820000034";
    private const string ExemptNip = "7171642051";
    private const string ExemptAccount = "61109010140000071219812874";
    private const string VirtualAccount = "60102010260000042270201111";
    private const string SameBankMiss = "62102010260000042270209999";
    private const string UnknownAccount = "36124000011111222233334444";

    private static readonly DateOnly FileDate = new(2026, 10, 13);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public WhitelistIndexTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
        }
    }

    private static ParsedFlatFile ParseFixture()
    {
        using var json = new MemoryStream(Fixture.ReadBytes("bialalista-offline/20261013.json"));
        return FlatFileParser.Parse(json, FileDate, CancellationToken.None);
    }

    private string Build(ParsedFlatFile? parsed = null, string name = "index.idx")
    {
        var path = Path.Combine(_dir, name);
        WhitelistIndex.Write(path, FileDate, parsed ?? ParseFixture());
        return path;
    }

    private WhitelistIndex OpenFixture() => WhitelistIndex.Open(Build());

    [Theory]
    [InlineData(ActiveNip, ActiveAccount, VatWhitelistOfflineStatus.Active, false)]
    [InlineData(ExemptNip, ExemptAccount, VatWhitelistOfflineStatus.Exempt, false)]
    [InlineData(ActiveNip, VirtualAccount, VatWhitelistOfflineStatus.Active, true)]
    [InlineData(ActiveNip, SameBankMiss, VatWhitelistOfflineStatus.NotFound, false)]
    [InlineData(ActiveNip, UnknownAccount, VatWhitelistOfflineStatus.NotFound, false)]
    [InlineData(ExemptNip, ActiveAccount, VatWhitelistOfflineStatus.NotFound, false)]
    public void Lookup_answers_for_fixture_pairs(string nip, string account, VatWhitelistOfflineStatus status, bool viaMask)
    {
        using var index = OpenFixture();

        Assert.Equal((status, viaMask), index.Lookup(nip, account));
    }

    [Fact]
    public void Info_matches_fixture()
    {
        using var index = OpenFixture();

        Assert.Equal(22, index.Info.ActiveCount);
        Assert.Equal(21, index.Info.ExemptCount);
        Assert.Equal(3, index.Info.MaskCount);
        Assert.Equal(3, index.Info.Transformations);
        Assert.Equal(FileDate, index.Info.FileDate);
        Assert.Equal(new DateOnly(2026, 10, 13), index.Info.GeneratedOn);
    }

    [Fact]
    public void Lookup_hashes_with_header_date()
    {
        var header = new DateOnly(2026, 10, 12);
        var digest = FlatFileHash.Compute(FlatFileHash.Input(header, ActiveNip, ActiveAccount), 3);
        var active = new byte[16];
        Array.Copy(digest, active, 16);
        using var index = WhitelistIndex.Open(Build(new ParsedFlatFile(header, 3, active, [], []), "header.idx"));

        Assert.Equal((VatWhitelistOfflineStatus.Active, false), index.Lookup(ActiveNip, ActiveAccount));
        Assert.Equal(FileDate, index.Info.FileDate);
        Assert.Equal(header, index.Info.GeneratedOn);
    }

    [Fact]
    public void Corrupt_magic_throws_unexpected_format()
    {
        var path = Build();
        var bytes = File.ReadAllBytes(path);
        bytes[0] ^= 0xFF;
        File.WriteAllBytes(path, bytes);

        var ex = Assert.Throws<VatWhitelistDataUnavailableException>(() => WhitelistIndex.Open(path));

        Assert.Equal(VatWhitelistDataUnavailableReason.UnexpectedFormat, ex.Reason);
    }

    [Theory]
    [InlineData("last-byte-missing")]
    [InlineData("extra-byte")]
    [InlineData("header-only")]
    public void Wrong_file_length_throws_unexpected_format(string damage)
    {
        var path = Build();
        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, damage switch
        {
            "last-byte-missing" => bytes.Take(bytes.Length - 1).ToArray(),
            "extra-byte" => bytes.Append((byte)0).ToArray(),
            _ => bytes.Take(40).ToArray(),
        });

        var ex = Assert.Throws<VatWhitelistDataUnavailableException>(() => WhitelistIndex.Open(path));

        Assert.Equal(VatWhitelistDataUnavailableReason.UnexpectedFormat, ex.Reason);
    }

    [Fact]
    public void Wrong_version_throws_unexpected_format()
    {
        var path = Build();
        var bytes = File.ReadAllBytes(path);
        bytes[8] = 2;
        File.WriteAllBytes(path, bytes);

        var ex = Assert.Throws<VatWhitelistDataUnavailableException>(() => WhitelistIndex.Open(path));

        Assert.Equal(VatWhitelistDataUnavailableReason.UnexpectedFormat, ex.Reason);
    }

    [Fact]
    public void Open_index_can_be_deleted()
    {
        var path = Build();
        using var index = WhitelistIndex.Open(path);

        File.Delete(path);

        Assert.Equal((VatWhitelistOfflineStatus.Active, false), index.Lookup(ActiveNip, ActiveAccount));
    }

    [Theory]
    [InlineData("526104082", ActiveAccount)]
    [InlineData(ActiveNip, "0616001127184398382000003")]
    public void Wrong_length_arguments_throw(string nip, string account)
    {
        using var index = OpenFixture();

        Assert.Throws<ArgumentException>(() => index.Lookup(nip, account));
    }

    [Fact]
    public void Parallel_lookups_agree()
    {
        using var index = OpenFixture();
        (string Nip, string Account, VatWhitelistOfflineStatus Status, bool ViaMask)[] cases =
        [
            (ActiveNip, ActiveAccount, VatWhitelistOfflineStatus.Active, false),
            (ExemptNip, ExemptAccount, VatWhitelistOfflineStatus.Exempt, false),
            (ActiveNip, VirtualAccount, VatWhitelistOfflineStatus.Active, true),
            (ActiveNip, SameBankMiss, VatWhitelistOfflineStatus.NotFound, false),
            (ActiveNip, UnknownAccount, VatWhitelistOfflineStatus.NotFound, false),
        ];

        Parallel.For(0, 1000, i =>
        {
            var c = cases[i % cases.Length];
            Assert.Equal((c.Status, c.ViaMask), index.Lookup(c.Nip, c.Account));
        });
    }
}
