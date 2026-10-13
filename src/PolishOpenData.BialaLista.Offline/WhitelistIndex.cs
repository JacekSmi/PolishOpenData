using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace PolishOpenData.BialaLista.Offline;

/// <summary>
/// A compact on-disk index of one flat file: sorted 16-byte hash prefixes bucketed by their first two bytes, plus the masks.
/// Lookups read only one bucket from the file with positional reads and are safe from any number of threads.
/// </summary>
internal sealed class WhitelistIndex : IDisposable
{
    private const int Version = 1;
    private const int EntryLength = 16;
    private const int MaskLength = 26;
    private const int Buckets = 65536;
    private const int TableLength = Buckets + 1;
    private const int HeaderLength = 8 + (7 * 4);
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("PODWLIX1");

    private readonly int[] _activeBuckets;
    private readonly int[] _exemptBuckets;
    private readonly string[] _masks;
    private readonly long _activeOffset;
    private readonly long _exemptOffset;
    private readonly string _path;
    private volatile bool _disposed;
    // Kept referenced so its finalizer cannot close the handle. Never reopened by path: the file may have been renamed or deleted since Open (FileShare.Delete).
    private readonly FileStream _stream;

    private WhitelistIndex(string path, VatWhitelistFileInfo info, int[] activeBuckets, int[] exemptBuckets, string[] masks, long activeOffset, long exemptOffset, FileStream stream)
    {
        _path = path;
        Info = info;
        _activeBuckets = activeBuckets;
        _exemptBuckets = exemptBuckets;
        _masks = masks;
        _activeOffset = activeOffset;
        _exemptOffset = exemptOffset;
        _stream = stream;
    }

    public VatWhitelistFileInfo Info { get; }

    /// <summary>Writes the index to exactly <paramref name="path"/> and flushes it to disk; the caller chooses a temporary name and moves the file into place.</summary>
    public static void Write(string path, DateOnly fileDate, ParsedFlatFile parsed)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        var active = Sorted(parsed.Active);
        var exempt = Sorted(parsed.Exempt);
        var masks = (string[])parsed.Masks.Clone();
        foreach (var mask in masks)
        {
            if (mask.Length != MaskLength)
            {
                throw new ArgumentException($"A mask must have {MaskLength} characters.", nameof(parsed));
            }
        }
        Array.Sort(masks, static (a, b) =>
        {
            var byBank = string.CompareOrdinal(a, 2, b, 2, 8);
            return byBank != 0 ? byBank : string.CompareOrdinal(a, b);
        });

        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024);
        using var writer = new BinaryWriter(file, Encoding.ASCII, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(Version);
        writer.Write(ToInt(fileDate));
        writer.Write(ToInt(parsed.GeneratedOn));
        writer.Write(parsed.Transformations);
        writer.Write(active.Length);
        writer.Write(exempt.Length);
        writer.Write(masks.Length);
        WriteBucketTable(writer, active);
        WriteBucketTable(writer, exempt);
        writer.Flush();
        WriteEntries(file, active);
        WriteEntries(file, exempt);
        foreach (var mask in masks)
        {
            writer.Write(Encoding.ASCII.GetBytes(mask));
        }
        writer.Flush();
        file.Flush(true);
    }

    /// <summary>Opens an index for reading; others may read or delete the file while it is open.</summary>
    /// <exception cref="VatWhitelistDataUnavailableException">The file is not an index of this version or is damaged (<see cref="VatWhitelistDataUnavailableReason.UnexpectedFormat"/>).</exception>
    public static WhitelistIndex Open(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 1);
        try
        {
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
            if (stream.Length < HeaderLength || !reader.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic))
            {
                throw Corrupt("is not a whitelist index");
            }
            if (reader.ReadInt32() != Version)
            {
                throw Corrupt("has an unsupported version");
            }

            var fileDate = ToDate(reader.ReadInt32());
            var generatedOn = ToDate(reader.ReadInt32());
            var transformations = reader.ReadInt32();
            var activeCount = reader.ReadInt32();
            var exemptCount = reader.ReadInt32();
            var maskCount = reader.ReadInt32();
            if (transformations < 1 || activeCount < 0 || exemptCount < 0 || maskCount < 0)
            {
                throw Corrupt("has an invalid header");
            }

            var activeOffset = HeaderLength + (2 * TableLength * 4L);
            var exemptOffset = activeOffset + (activeCount * (long)EntryLength);
            var masksOffset = exemptOffset + (exemptCount * (long)EntryLength);
            if (masksOffset + (maskCount * (long)MaskLength) != stream.Length)
            {
                throw Corrupt("has a size that does not match its header");
            }

            var activeBuckets = ReadBucketTable(reader, activeCount, path);
            var exemptBuckets = ReadBucketTable(reader, exemptCount, path);
            stream.Position = masksOffset;
            var masks = new string[maskCount];
            for (var i = 0; i < maskCount; i++)
            {
                masks[i] = Encoding.ASCII.GetString(reader.ReadBytes(MaskLength));
            }

            var info = new VatWhitelistFileInfo(fileDate, generatedOn, transformations, activeCount, exemptCount, maskCount);
            return new WhitelistIndex(path, info, activeBuckets, exemptBuckets, masks, activeOffset, exemptOffset, stream);
        }
        catch (EndOfStreamException ex)
        {
            stream.Dispose();
            throw Corrupt("is truncated", ex);
        }
        catch
        {
            stream.Dispose();
            throw;
        }

        VatWhitelistDataUnavailableException Corrupt(string what, Exception? inner = null) =>
            new($"The index file '{path}' {what}.", default, VatWhitelistDataUnavailableReason.UnexpectedFormat, inner);
    }

    /// <summary>Looks the pair up: the plain account first, then each mask of the account's bank in turn; for each candidate an active hit wins over an exempt one, and the first candidate found decides.</summary>
    /// <param name="nip">Ten digits.</param>
    /// <param name="nrb">Twenty-six digits.</param>
    public (VatWhitelistOfflineStatus Status, bool ViaMask) Lookup(string nip, string nrb)
    {
        ArgumentNullException.ThrowIfNull(nip);
        ArgumentNullException.ThrowIfNull(nrb);
        if (nip.Length != 10)
        {
            throw new ArgumentException("A NIP has 10 digits.", nameof(nip));
        }
        if (nrb.Length != MaskLength)
        {
            throw new ArgumentException("An NRB has 26 digits.", nameof(nrb));
        }
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Masks are hashed only after the plain account missed both lists, one at a time: each costs N SHA-512 rounds.
        if (Find(nip, nrb) is { } plain)
        {
            return (plain, false);
        }
        for (var i = FirstMaskOfBank(nrb); i < _masks.Length && string.CompareOrdinal(_masks[i], 2, nrb, 2, 8) == 0; i++)
        {
            if (FlatFileHash.ApplyMask(nrb, _masks[i]) is { } account && Find(nip, account) is { } masked)
            {
                return (masked, true);
            }
        }
        return (VatWhitelistOfflineStatus.NotFound, false);
    }

    public void Dispose()
    {
        _disposed = true;
        _stream.Dispose();
    }

    private int FirstMaskOfBank(string nrb)
    {
        int lo = 0, hi = _masks.Length;
        while (lo < hi)
        {
            var mid = lo + ((hi - lo) / 2);
            if (string.CompareOrdinal(_masks[mid], 2, nrb, 2, 8) < 0)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }
        return lo;
    }

    // Active wins over exempt for one account.
    private VatWhitelistOfflineStatus? Find(string nip, string account)
    {
        var digest = FlatFileHash.Compute(FlatFileHash.Input(Info.GeneratedOn, nip, account), Info.Transformations);
        var key = (BinaryPrimitives.ReadUInt64BigEndian(digest), BinaryPrimitives.ReadUInt64BigEndian(digest.AsSpan(8)));
        return Contains(key, _activeBuckets, _activeOffset) ? VatWhitelistOfflineStatus.Active
            : Contains(key, _exemptBuckets, _exemptOffset) ? VatWhitelistOfflineStatus.Exempt
            : null;
    }

    /// <summary>Whether the active (or exempt) list holds exactly this 16-byte prefix, given as two big-endian halves.</summary>
    internal bool Contains(bool exempt, ulong hi, ulong lo) =>
        exempt ? Contains((hi, lo), _exemptBuckets, _exemptOffset) : Contains((hi, lo), _activeBuckets, _activeOffset);

    private bool Contains((ulong Hi, ulong Lo) key, int[] buckets, long entriesOffset)
    {
        var bucket = (int)(key.Hi >> 48);
        int lo = buckets[bucket], hi = buckets[bucket + 1];
        var count = hi - lo;
        if (count == 0)
        {
            return false;
        }

        var buffer = ArrayPool<byte>.Shared.Rent(count * EntryLength);
        try
        {
            ReadAt(entriesOffset + (lo * (long)EntryLength), buffer, count * EntryLength);
            lo = 0;
            hi = count;
            while (lo < hi)
            {
                var mid = lo + ((hi - lo) / 2);
                var at = buffer.AsSpan(mid * EntryLength, EntryLength);
                var entry = (Hi: BinaryPrimitives.ReadUInt64BigEndian(at), Lo: BinaryPrimitives.ReadUInt64BigEndian(at.Slice(8)));
                var order = entry.CompareTo(key);
                if (order == 0)
                {
                    return true;
                }
                if (order < 0)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }
            return false;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void ReadAt(long offset, byte[] buffer, int count)
    {
        var read = 0;
#if NET
        while (read < count)
        {
            var n = RandomAccess.Read(_stream.SafeFileHandle, buffer.AsSpan(read, count - read), offset + read);
            if (n == 0)
            {
                throw Truncated();
            }
            read += n;
        }
#else
        // netstandard2.0 has no positional read; one stream under a lock (lookups are dominated by the SHA-512 rounds, so this is not a bottleneck).
        lock (_stream)
        {
            _stream.Position = offset;
            while (read < count)
            {
                var n = _stream.Read(buffer, read, count - read);
                if (n == 0)
                {
                    throw Truncated();
                }
                read += n;
            }
        }
#endif
    }

    private VatWhitelistDataUnavailableException Truncated() =>
        new($"The index file '{_path}' ended unexpectedly.", Info.FileDate, VatWhitelistDataUnavailableReason.UnexpectedFormat);

    private static (ulong Hi, ulong Lo)[] Sorted(byte[] concatenated)
    {
        if (concatenated.Length % EntryLength != 0)
        {
            throw new ArgumentException($"Entries must be {EntryLength} bytes each.", nameof(concatenated));
        }

        var entries = new (ulong, ulong)[concatenated.Length / EntryLength];
        for (var i = 0; i < entries.Length; i++)
        {
            var at = concatenated.AsSpan(i * EntryLength, EntryLength);
            entries[i] = (BinaryPrimitives.ReadUInt64BigEndian(at), BinaryPrimitives.ReadUInt64BigEndian(at.Slice(8)));
        }
        Array.Sort(entries);
        return entries;
    }

    private static void WriteBucketTable(BinaryWriter writer, (ulong Hi, ulong Lo)[] sorted)
    {
        // start[b] = index of the first entry whose two-byte prefix is >= b; start[65536] = count.
        var next = 0;
        for (var bucket = 0; bucket < Buckets; bucket++)
        {
            while (next < sorted.Length && (int)(sorted[next].Hi >> 48) < bucket)
            {
                next++;
            }
            writer.Write(next);
        }
        writer.Write(sorted.Length);
    }

    private static void WriteEntries(FileStream file, (ulong Hi, ulong Lo)[] sorted)
    {
        var chunk = new byte[EntryLength * 4096];
        for (var i = 0; i < sorted.Length;)
        {
            var n = Math.Min(4096, sorted.Length - i);
            for (var j = 0; j < n; j++, i++)
            {
                BinaryPrimitives.WriteUInt64BigEndian(chunk.AsSpan(j * EntryLength), sorted[i].Hi);
                BinaryPrimitives.WriteUInt64BigEndian(chunk.AsSpan((j * EntryLength) + 8), sorted[i].Lo);
            }
            file.Write(chunk, 0, n * EntryLength);
        }
    }

    private static int[] ReadBucketTable(BinaryReader reader, int count, string path)
    {
        var table = new int[TableLength];
        var previous = 0;
        for (var i = 0; i < TableLength; i++)
        {
            table[i] = reader.ReadInt32();
            if (table[i] < previous || table[i] > count)
            {
                throw InconsistentTable(path);
            }
            previous = table[i];
        }
        return table[0] == 0 && table[Buckets] == count ? table : throw InconsistentTable(path);
    }

    private static VatWhitelistDataUnavailableException InconsistentTable(string path) =>
        new($"The index file '{path}' has an inconsistent bucket table.", default, VatWhitelistDataUnavailableReason.UnexpectedFormat);

    private static int ToInt(DateOnly date) => (date.Year * 10000) + (date.Month * 100) + date.Day;

    private static DateOnly ToDate(int yyyymmdd)
    {
        try
        {
            return new DateOnly(yyyymmdd / 10000, yyyymmdd / 100 % 100, yyyymmdd % 100);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new VatWhitelistDataUnavailableException("The index has an invalid date.", default, VatWhitelistDataUnavailableReason.UnexpectedFormat, ex);
        }
    }
}
