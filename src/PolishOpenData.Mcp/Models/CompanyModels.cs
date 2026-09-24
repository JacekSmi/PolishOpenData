using System;
using System.Collections.Generic;
using PolishOpenData.Krs;

namespace PolishOpenData.Mcp;

internal sealed record SourceInfo(string Name, string Endpoint, DateTimeOffset RetrievedAt, string? RequestId);

internal sealed record CompanyOverview
{
    public required string Query { get; init; }

    public bool Found { get; init; }

    public string? Name { get; init; }

    public string? Nip { get; init; }

    public string? Regon { get; init; }

    public string? Krs { get; init; }

    public VatInfo? Vat { get; init; }

    public KrsInfo? KrsRegistry { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public IReadOnlyList<SourceInfo> Sources { get; init; } = [];
}

internal sealed record VatInfo
{
    public required string Status { get; init; }

    public required string StatusExplanation { get; init; }

    public string? Address { get; init; }

    public int BankAccountCount { get; init; }

    public IReadOnlyList<string> BankAccounts { get; init; } = [];

    public bool HasVirtualAccounts { get; init; }

    public DateOnly? RegisteredForVatOn { get; init; }

    public DateOnly? RemovedOn { get; init; }

    public string? RemovalBasis { get; init; }
}

internal sealed record KrsInfo
{
    public required string Status { get; init; }

    public string? Register { get; init; }

    public string? LegalForm { get; init; }

    public string? Address { get; init; }

    public decimal? ShareCapital { get; init; }

    public string? ShareCapitalCurrency { get; init; }

    public DateOnly? RegisteredOn { get; init; }

    public DateOnly? RemovedOn { get; init; }

    public string? MainActivity { get; init; }
}

internal sealed record VatAccountCheck
{
    public required string BankAccount { get; init; }

    public string? Nip { get; init; }

    public string? Regon { get; init; }

    public bool AssignedToActiveVatPayer { get; init; }

    public required string Meaning { get; init; }

    public required string RequestId { get; init; }

    public DateTimeOffset RequestDateTime { get; init; }

    public required SourceInfo Source { get; init; }
}

internal sealed record KrsExtractView
{
    public required string Status { get; init; }

    public KrsCompanySummary? Summary { get; init; }

    public DateOnly? RemovedOn { get; init; }

    public string? Note { get; init; }

    public IReadOnlyList<SourceInfo> Sources { get; init; } = [];
}
