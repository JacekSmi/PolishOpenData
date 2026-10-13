using System;

namespace PolishOpenData.BialaLista.Offline;

/// <summary>The answer of the flat file for one pair of NIP and bank account.</summary>
public sealed class VatWhitelistOfflineCheck
{
    internal VatWhitelistOfflineCheck(Nip nip, Nrb account, VatWhitelistOfflineStatus status, bool matchedVirtualAccountMask, DateOnly fileDate, DateOnly generatedOn)
    {
        Nip = nip;
        Account = account;
        Status = status;
        MatchedVirtualAccountMask = matchedVirtualAccountMask;
        FileDate = fileDate;
        GeneratedOn = generatedOn;
    }

    /// <summary>The NIP that was checked.</summary>
    public Nip Nip { get; }

    /// <summary>The bank account that was checked.</summary>
    public Nrb Account { get; }

    /// <summary>What the file says. <see cref="VatWhitelistOfflineStatus.NotFound"/> means not found in the file, not that the taxpayer is unregistered.</summary>
    public VatWhitelistOfflineStatus Status { get; }

    /// <summary>True when the pair was found through a virtual-account mask of the file rather than as a plain entry.</summary>
    public bool MatchedVirtualAccountMask { get; }

    /// <summary>The date the answer is for (the date of the file that was used).</summary>
    public DateOnly FileDate { get; }

    /// <summary>The generation date from the file's header.</summary>
    public DateOnly GeneratedOn { get; }
}
