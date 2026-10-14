using System;

namespace PolishOpenData.BialaLista.Offline;

/// <summary>What the flat file an index was built from contains.</summary>
public sealed class VatWhitelistFileInfo
{
    internal VatWhitelistFileInfo(DateOnly fileDate, DateOnly generatedOn, int transformations, int activeCount, int exemptCount, int maskCount)
    {
        FileDate = fileDate;
        GeneratedOn = generatedOn;
        Transformations = transformations;
        ActiveCount = activeCount;
        ExemptCount = exemptCount;
        MaskCount = maskCount;
    }

    /// <summary>The date the file was requested for (the date in its name).</summary>
    public DateOnly FileDate { get; }

    /// <summary>The generation date from the file's header; the date the entries' hashes are computed with.</summary>
    public DateOnly GeneratedOn { get; }

    /// <summary>How many SHA-512 rounds the file's hashes went through.</summary>
    public int Transformations { get; }

    /// <summary>The number of active-taxpayer entries.</summary>
    public int ActiveCount { get; }

    /// <summary>The number of exempt-taxpayer entries.</summary>
    public int ExemptCount { get; }

    /// <summary>The number of virtual-account masks.</summary>
    public int MaskCount { get; }
}
