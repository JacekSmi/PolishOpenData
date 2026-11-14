using System;
using System.Collections.Generic;

namespace PolishOpenData.Eli;

/// <summary>An act in a search result or in the details of an act.</summary>
/// <param name="Id">Publisher, year and position.</param>
/// <param name="DisplayAddress">For example "Dz.U. 2024 poz. 1061".</param>
/// <param name="Title">Official title.</param>
/// <param name="Type">Kind of document (Ustawa, Rozporządzenie, Obwieszczenie...).</param>
/// <param name="Status">Status text as the API writes it (for example "akt posiada tekst jednolity").</param>
/// <param name="InForce">True when the act is in force, false when not, null when unknown.</param>
/// <param name="AnnouncementDate">Date of the announcement (the date in the act's title for a consolidated text), or null.</param>
/// <param name="EntryIntoForce">Date the act takes effect, or null.</param>
/// <param name="HasHtml">True when the API serves the text as HTML and a structure. Acts from 2025 on are PDF-only.</param>
/// <param name="HasPdf">True when the API serves a PDF.</param>
public sealed record EliActSummary(
    EliActId Id,
    string DisplayAddress,
    string Title,
    string? Type,
    string? Status,
    bool? InForce,
    DateOnly? AnnouncementDate,
    DateOnly? EntryIntoForce,
    bool HasHtml,
    bool HasPdf);

/// <summary>An amending act and when it takes effect (possibly in the future).</summary>
public sealed record EliAmendment(EliActId Id, DateOnly? EffectiveDate);

/// <summary>An act with its references.</summary>
/// <param name="Summary">The act's own data.</param>
/// <param name="Keywords">Subject keywords.</param>
/// <param name="ReleasedBy">Issuing bodies.</param>
/// <param name="ConsolidatedTexts">Consolidated texts (teksty jednolite) of this act, newest first as the API lists them.</param>
/// <param name="Amendments">Acts that amend this one, with their entry-into-force dates.</param>
/// <param name="References">Every other reference group, by the API's Polish group name.</param>
public sealed record EliAct(
    EliActSummary Summary,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> ReleasedBy,
    IReadOnlyList<EliActId> ConsolidatedTexts,
    IReadOnlyList<EliAmendment> Amendments,
    IReadOnlyDictionary<string, IReadOnlyList<EliActId>> References);

/// <summary>A unit (part, book, title, chapter, article, paragraph...) in an act's structure.</summary>
/// <param name="Id">Structure id, for example <c>book_TRZECIA-titl_VI-arti_415</c>.</param>
/// <param name="Type">Unit type (<c>part</c>, <c>book</c>, <c>titl</c>, <c>chpt</c>, <c>arti</c>, <c>pass</c>, <c>pint</c>...).</param>
/// <param name="Name">Short name (for an article its number, for example <c>446_1</c>), or null.</param>
/// <param name="Title">Heading as printed (for example "Art. 415."), or null.</param>
/// <param name="Children">Nested units.</param>
public sealed record EliUnit(string Id, string Type, string? Name, string? Title, IReadOnlyList<EliUnit> Children);

/// <summary>The text of one article.</summary>
/// <param name="Act">The act the text was read from (not necessarily the law in force).</param>
/// <param name="Article">The normalised article name, for example <c>415</c> or <c>446_1</c>.</param>
/// <param name="UnitId">The unit id in the act's structure.</param>
/// <param name="Text">Plain text, paragraphs separated by line breaks.</param>
/// <param name="Truncated">True when the text was cut at the length cap.</param>
public sealed record EliArticleText(EliActId Act, string Article, string UnitId, string Text, bool Truncated);

/// <summary>
/// What is known about the text of a base act: the newest consolidated text, the newest one the API serves as HTML, and what
/// changed after that text.
/// </summary>
/// <param name="BaseAct">The base act asked about.</param>
/// <param name="NewestConsolidated">Newest consolidated text found (may be PDF-only), or null when there is none.</param>
/// <param name="NewestWithHtml">Newest text with HTML (a consolidated text, or the base act itself when it has no consolidated text), or null.</param>
/// <param name="InForceSince">Amendments that took effect after <paramref name="NewestWithHtml"/> was announced.</param>
/// <param name="FutureSince">Amendments that take effect later than today.</param>
/// <param name="IsHtmlTextCurrent">True only when the HTML text is the newest consolidated text and nothing has taken effect since.</param>
/// <param name="ConsolidatedTextsChecked">How many consolidated texts were looked at (at most 5).</param>
public sealed record EliCurrentText(
    EliActId BaseAct,
    EliActSummary? NewestConsolidated,
    EliActSummary? NewestWithHtml,
    IReadOnlyList<EliAmendment> InForceSince,
    IReadOnlyList<EliAmendment> FutureSince,
    bool IsHtmlTextCurrent,
    int ConsolidatedTextsChecked);

/// <summary>A list result with the total the API knows about.</summary>
public sealed record EliPage<T>(IReadOnlyList<T> Items, int TotalRecords);
