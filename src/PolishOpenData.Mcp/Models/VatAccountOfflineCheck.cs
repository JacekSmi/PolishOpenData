using System;
using System.Collections.Generic;

namespace PolishOpenData.Mcp;

/// <summary>One element of the <c>pairs</c> input of check_vat_bank_account_offline.</summary>
internal sealed record OfflineVatPair(string? Nip, string? BankAccount);

internal sealed record VatAccountOfflineCheck
{
    public DateOnly FileDate { get; init; }

    public DateOnly GeneratedOn { get; init; }

    public required IReadOnlyList<VatAccountOfflineResult> Results { get; init; }

    public required SourceInfo Source { get; init; }
}

internal sealed record VatAccountOfflineResult
{
    public required string Nip { get; init; }

    public required string BankAccount { get; init; }

    /// <summary>"active", "exempt" or "not_found" (not found in the file; never "absent from the whitelist").</summary>
    public required string Status { get; init; }

    public bool ViaVirtualAccountMask { get; init; }

    public required string Meaning { get; init; }
}
