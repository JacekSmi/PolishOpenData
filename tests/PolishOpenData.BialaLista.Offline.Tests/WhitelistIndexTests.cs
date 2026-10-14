using System;
using System.Buffers.Binary;
using System.Collections.Generic;
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
    public void Plain_hit_returns_without_trying_masks()
    {
        // plain hash in the exempt list, its masked form in the active list: the plain account decides (spec §4 order)
        const string mask = "XX10201026XXXXXXXXYYYYYYXX";
        static byte[] Prefix(string account) => FlatFileHash.Compute(FlatFileHash.Input(FileDate, ActiveNip, account), 3).Take(16).ToArray();
        var parsed = new ParsedFlatFile(FileDate, 3, Prefix(FlatFileHash.ApplyMask(VirtualAccount, mask)!), Prefix(VirtualAccount), [mask]);
        using var index = WhitelistIndex.Open(Build(parsed, "order.idx"));

        Assert.Equal((VatWhitelistOfflineStatus.Exempt, false), index.Lookup(ActiveNip, VirtualAccount));
    }

    [Fact]
    public void Index_opened_under_temporary_name_can_be_moved_into_place()
    {
        var temp = Build(name: "index.tmp");
        var final = Path.Combine(_dir, "index.idx");
        using var index = WhitelistIndex.Open(temp);

        File.Move(temp, final);   // needs FileShare.Delete on the open handle (Windows)

        Assert.True(File.Exists(final));
        AssertParallelLookups(index);
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
    public void Open_index_can_be_deleted_and_still_answers_in_parallel()
    {
        var path = Build();
        using var index = WhitelistIndex.Open(path);

        File.Delete(path);

        AssertParallelLookups(index);
    }

    [Fact]
    public void Open_index_renamed_and_replaced_still_answers_from_the_original()
    {
        var path = Build();
        using var index = WhitelistIndex.Open(path);

        File.Move(path, path + ".old");
        File.WriteAllBytes(path, new byte[4096]);

        AssertParallelLookups(index);
    }

    private static void AssertParallelLookups(WhitelistIndex index) =>
        Parallel.For(0, 500, i =>
        {
            Assert.Equal((VatWhitelistOfflineStatus.Active, false), index.Lookup(ActiveNip, ActiveAccount));
            Assert.Equal((VatWhitelistOfflineStatus.Exempt, false), index.Lookup(ExemptNip, ExemptAccount));
            Assert.Equal((VatWhitelistOfflineStatus.Active, true), index.Lookup(ActiveNip, VirtualAccount));
            Assert.Equal((VatWhitelistOfflineStatus.NotFound, false), index.Lookup(ActiveNip, UnknownAccount));
        });

    private static byte[] Entries(params (ulong Hi, ulong Lo)[] entries)
    {
        var bytes = new byte[entries.Length * 16];
        for (var i = 0; i < entries.Length; i++)
        {
            BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(i * 16), entries[i].Hi);
            BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan((i * 16) + 8), entries[i].Lo);
        }
        return bytes;
    }

    private static ulong Prefixed(ulong prefix, ulong rest) => (prefix << 48) | rest;

    private static void AssertContainsExactly(bool exempt, WhitelistIndex index, (ulong Hi, ulong Lo)[] present)
    {
        var set = present.ToHashSet();
        var probes = new HashSet<(ulong, ulong)>(present);
        foreach (var (hi, lo) in present)
        {
            probes.Add((hi, lo + 1));
            probes.Add((hi, lo - 1));
            probes.Add((hi + 1, lo));
            probes.Add((hi - 1, lo));
            probes.Add((hi, 0));
            probes.Add((hi, ulong.MaxValue));
            probes.Add((hi & 0xFFFF_0000_0000_0000UL, 0));
            probes.Add((hi | 0x0000_FFFF_FFFF_FFFFUL, ulong.MaxValue));
        }
        foreach (var neighbour in new[] { 0x1233UL, 0x1235UL, 0x0002UL, 0xFFFEUL })
        {
            probes.Add((Prefixed(neighbour, 5), 5));
        }

        foreach (var key in probes)
        {
            Assert.Equal(set.Contains(key), index.Contains(exempt, key.Item1, key.Item2));
        }
    }

    [Fact]
    public void Contains_finds_exactly_the_entries_of_a_synthetic_index()
    {
        // Prefixes at both ends of the table, one bucket with 7 entries given unsorted, a lone entry after the empty bucket 0x1235, duplicates of none.
        var active = new (ulong Hi, ulong Lo)[]
        {
            (Prefixed(0xFFFF, 9), 1), (Prefixed(0x0000, 0), 0), (Prefixed(0x1234, 50), 7), (Prefixed(0x0000, 0), 1),
            (Prefixed(0x1234, 10), 99), (Prefixed(0x1236, 3), 3), (Prefixed(0x1234, 50), 2), (Prefixed(0x1234, 10), 98),
            (Prefixed(0xFFFF, 0xFFFF_FFFF_FFFF), ulong.MaxValue), (Prefixed(0x1234, 0), 0), (Prefixed(0x1234, 0xFFFF_FFFF_FFFF), ulong.MaxValue),
            (Prefixed(0x0000, 0), ulong.MaxValue), (Prefixed(0x1234, 20), 5),
        };
        var exempt = new (ulong Hi, ulong Lo)[] { (Prefixed(0x0001, 1), 1), (Prefixed(0xFFFF, 1), 1), (Prefixed(0x1234, 7), 7) };
        var path = Build(new ParsedFlatFile(FileDate, 3, Entries(active), Entries(exempt), []), "synthetic.idx");
        using var index = WhitelistIndex.Open(path);

        AssertContainsExactly(false, index, active);
        AssertContainsExactly(true, index, exempt);
        Assert.False(index.Contains(true, active[0].Hi, active[0].Lo));
        Assert.False(index.Contains(false, exempt[0].Hi, exempt[0].Lo));
    }

    [Fact]
    public void Empty_index_contains_nothing()
    {
        using var index = WhitelistIndex.Open(Build(new ParsedFlatFile(FileDate, 3, [], [], []), "empty.idx"));

        foreach (var exempt in new[] { false, true })
        {
            Assert.False(index.Contains(exempt, 0, 0));
            Assert.False(index.Contains(exempt, ulong.MaxValue, ulong.MaxValue));
        }
        Assert.Equal((VatWhitelistOfflineStatus.NotFound, false), index.Lookup(ActiveNip, ActiveAccount));
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
