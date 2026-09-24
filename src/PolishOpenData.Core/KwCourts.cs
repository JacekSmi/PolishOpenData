using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PolishOpenData;

/// <summary>
/// The official table of KW court codes: annex 1 to the regulation on keeping land registers in the IT system
/// (Dz.U. 2016 poz. 312, annex as replaced by Dz.U. 2026 poz. 740, in force 2026-07-01), plus codes that earlier
/// versions of the annex listed. Books keep the code of the court where they were founded, so historical codes still
/// appear in valid KW numbers.
/// </summary>
public static class KwCourts
{
    private const string ResourceName = "PolishOpenData.kw-courts.tsv";
    private static readonly Lazy<Dictionary<string, KwCourt>> Table = new(Load);

    /// <summary>All known codes, current and historical.</summary>
    public static IReadOnlyCollection<KwCourt> All => Table.Value.Values;

    /// <summary>Finds a court by code (case-insensitive); <c>null</c> when unknown.</summary>
    public static KwCourt? Find(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return null;
        }

        return Table.Value.TryGetValue(code!.ToUpperInvariant(), out var court) ? court : null;
    }

    private static Dictionary<string, KwCourt> Load()
    {
        var table = new Dictionary<string, KwCourt>(StringComparer.Ordinal);
        using var stream = typeof(KwCourts).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            line = line.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var parts = line.Split('\t');
            if (parts.Length != 3)
            {
                throw new InvalidOperationException("Malformed court table line: " + line);
            }

            DateOnly? lastValidOn = parts[1].Length == 0
                ? null
                : DateOnly.ParseExact(parts[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None);
            table[parts[0]] = new KwCourt(parts[0], parts[2], lastValidOn);
        }

        return table;
    }
}
