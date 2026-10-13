using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Readers;

namespace PolishOpenData.BialaLista.Offline;

/// <summary>Reads the 7z archive of the Ministry of Finance flat file (a JSON entry and a SHA-512 sum entry).</summary>
internal static class FlatFileArchive
{
    // The caller owns the archive stream (7z keeps its header at the end, so it must be seekable) and keeps it open
    // while it reads the entry stream.
    private static readonly ReaderOptions KeepStreamOpen = new() { LeaveStreamOpen = true };

    /// <summary>The names of the files in the archive.</summary>
    public static IReadOnlyList<string> EntryNames(Stream archive) =>
        SevenZipArchive.Open(archive, KeepStreamOpen).Entries.Where(e => !e.IsDirectory).Select(e => e.Key!).ToList();

    /// <summary>Opens one file of the archive for sequential reading.</summary>
    /// <exception cref="InvalidDataException">The archive has no such file.</exception>
    public static Stream OpenEntry(Stream archive, string entryName)
    {
        var entry = SevenZipArchive.Open(archive, KeepStreamOpen).Entries
            .FirstOrDefault(e => !e.IsDirectory && string.Equals(e.Key, entryName, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"The archive has no entry '{entryName}'.");
        return entry.OpenEntryStream();
    }

    /// <summary>
    /// Reads and verifies the flat file in one forward pass over the archive. The JSON is the first entry of a solid 7z, so
    /// reading the checksum entry through a second <see cref="OpenEntry"/> would decode the whole JSON again; the sequential
    /// reader visits each entry once, in archive order, whichever order the two entries have.
    /// </summary>
    /// <exception cref="VatWhitelistDataUnavailableException">An entry is missing or malformed, or the checksum does not match.</exception>
    public static ParsedFlatFile Read(Stream archive, DateOnly fileDate, CancellationToken ct)
    {
        var jsonName = fileDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".json";
        var sumName = jsonName + ".sha512sum";
        ParsedFlatFile? parsed = null;
        string? computed = null;
        string? expected = null;
        try
        {
            using var reader = SevenZipArchive.Open(archive, KeepStreamOpen).ExtractAllEntries();
            while (reader.MoveToNextEntry())
            {
                ct.ThrowIfCancellationRequested();
                if (reader.Entry.IsDirectory)
                {
                    continue;
                }

                if (parsed is null && string.Equals(reader.Entry.Key, jsonName, StringComparison.Ordinal))
                {
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
                    using var entry = reader.OpenEntryStream();
                    parsed = FlatFileParser.Parse(entry, fileDate, ct, hash);
                    computed = Convert.ToHexString(hash.GetHashAndReset());
                }
                else if (expected is null && string.Equals(reader.Entry.Key, sumName, StringComparison.Ordinal))
                {
                    using var entry = reader.OpenEntryStream();
                    expected = ReadChecksum(entry);
                }
            }
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or VatWhitelistDataUnavailableException))
        // Trust boundary: the LZMA/7z decoder reports corruption with many exception types (even IndexOutOfRangeException).
        {
            throw new VatWhitelistDataUnavailableException(
                $"The flat file archive of {fileDate:yyyy-MM-dd} cannot be read: {ex.Message}", fileDate, VatWhitelistDataUnavailableReason.UnexpectedFormat, ex);
        }

        if (parsed is null || computed is null || expected is null)
        {
            throw new VatWhitelistDataUnavailableException(
                $"The flat file archive of {fileDate:yyyy-MM-dd} lacks {jsonName} or {sumName}.", fileDate, VatWhitelistDataUnavailableReason.UnexpectedFormat);
        }
        if (!string.Equals(computed, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new VatWhitelistDataUnavailableException(
                $"The flat file of {fileDate:yyyy-MM-dd} does not match its SHA-512 checksum.", fileDate, VatWhitelistDataUnavailableReason.ChecksumMismatch);
        }
        return parsed;
    }

    // "<hex>  <name>": only the first token matters; a real checksum file is far below the 4 KiB read here.
    private static string ReadChecksum(Stream entry)
    {
        var buffer = new byte[4096];
        var length = 0;
        int read;
        while (length < buffer.Length && (read = entry.Read(buffer, length, buffer.Length - length)) > 0)
        {
            length += read;
        }

        var token = Encoding.ASCII.GetString(buffer, 0, length).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return token ?? string.Empty;
    }
}
