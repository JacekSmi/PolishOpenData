using System;

namespace PolishOpenData.BialaLista.Offline;

/// <summary>The content of a flat file, reduced to what lookups need.</summary>
internal sealed class ParsedFlatFile
{
    public ParsedFlatFile(DateOnly generatedOn, int transformations, byte[] active, byte[] exempt, string[] masks)
    {
        GeneratedOn = generatedOn;
        Transformations = transformations;
        Active = active;
        Exempt = exempt;
        Masks = masks;
    }

    /// <summary>The generation date from the file header, as written there.</summary>
    public DateOnly GeneratedOn { get; }

    /// <summary>How many SHA-512 rounds the file's hashes went through.</summary>
    public int Transformations { get; }

    /// <summary>The first 16 bytes of each active-taxpayer digest, concatenated, in file order.</summary>
    public byte[] Active { get; }

    /// <summary>The first 16 bytes of each exempt-taxpayer digest, concatenated, in file order.</summary>
    public byte[] Exempt { get; }

    /// <summary>The masks for virtual accounts.</summary>
    public string[] Masks { get; }
}
