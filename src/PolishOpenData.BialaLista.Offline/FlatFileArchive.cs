using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
}
