using System;
using System.Collections.Generic;

namespace PolishOpenData.Mcp;

/// <summary>An act in a search result.</summary>
internal sealed record EliActView(string Eli, string DisplayAddress, string Title, string? Type, string? Status, bool? InForce, DateOnly? AnnouncementDate, DateOnly? EntryIntoForce, bool HasHtml);

/// <summary>search_legal_acts result.</summary>
internal sealed record EliSearchResult
{
    public required string Query { get; init; }

    /// <summary>All matches the API reports; <see cref="Acts"/> holds the first 20.</summary>
    public required int TotalMatches { get; init; }

    public required IReadOnlyList<EliActView> Acts { get; init; }

    public required string Source { get; init; }
}

/// <summary>A text (consolidated or original) of an act.</summary>
internal sealed record EliTextView(string Eli, string DisplayAddress, DateOnly? AnnouncementDate, bool? HasHtml, bool? InForce = null);

internal sealed record EliAmendmentView(string Eli, DateOnly? EffectiveDate);

/// <summary>Amendments: how many, and the first ones.</summary>
internal sealed record EliAmendmentList(int Count, IReadOnlyList<EliAmendmentView> Items);

/// <summary>How current the text of an act is.</summary>
internal sealed record EliCurrencyView
{
    public EliTextView? NewestConsolidated { get; init; }

    public EliTextView? NewestWithHtml { get; init; }

    /// <summary>True only when the newest text with HTML is the newest consolidated text and nothing has taken effect since.</summary>
    public required bool IsHtmlTextCurrent { get; init; }

    /// <summary>Amendments that took effect after the newest text with HTML was announced.</summary>
    public required EliAmendmentList AmendmentsInForceSince { get; init; }

    /// <summary>Amendments that take effect in the future.</summary>
    public required EliAmendmentList FutureAmendments { get; init; }

    public required int ConsolidatedTextsChecked { get; init; }
}

/// <summary>get_legal_act result.</summary>
internal sealed record EliActDetails
{
    public required string Eli { get; init; }

    public required string DisplayAddress { get; init; }

    public required string Title { get; init; }

    public string? Type { get; init; }

    public string? Status { get; init; }

    public bool? InForce { get; init; }

    public DateOnly? AnnouncementDate { get; init; }

    public DateOnly? EntryIntoForce { get; init; }

    public required bool HasHtml { get; init; }

    public required bool HasPdf { get; init; }

    public IReadOnlyList<string> Keywords { get; init; } = [];

    /// <summary>For a consolidated text: the base act it consolidates.</summary>
    public required IReadOnlyList<string> BaseActs { get; init; }

    /// <summary>Currency of the text (base acts only); null for a consolidated text.</summary>
    public EliCurrencyView? Currency { get; init; }

    public string? PdfLink { get; init; }

    public string? Warning { get; init; }

    public required string Source { get; init; }
}

/// <summary>get_legal_article result.</summary>
internal sealed record EliArticleResult
{
    /// <summary>The act the caller named.</summary>
    public required string Eli { get; init; }

    public required string Article { get; init; }

    /// <summary>Whether the act named by the caller is in force (false for a repealed act or a superseded consolidated text), or null when unknown.</summary>
    public bool? ActInForce { get; init; }

    /// <summary>Status of the act named by the caller, for example "uchylony" (repealed).</summary>
    public string? ActStatus { get; init; }

    /// <summary>The article as plain text; untrusted text from the register.</summary>
    public required string Text { get; init; }

    public required bool Truncated { get; init; }

    /// <summary>The text the quote was read from; it may be older than the law in force.</summary>
    public required EliTextView TextSource { get; init; }

    /// <summary>False whenever a newer consolidated text exists or an amendment took effect after the quoted text.</summary>
    public required bool IsCurrent { get; init; }

    public required IReadOnlyList<EliTextView> NewerConsolidatedTexts { get; init; }

    public required EliAmendmentList AmendmentsSince { get; init; }

    public required EliAmendmentList FutureAmendments { get; init; }

    /// <summary>Plain-language warning, set whenever <see cref="IsCurrent"/> is false.</summary>
    public string? Warning { get; init; }

    public string? PdfLinkOfNewest { get; init; }

    public required string Source { get; init; }
}
