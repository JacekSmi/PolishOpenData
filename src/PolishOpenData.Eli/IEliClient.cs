using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PolishOpenData.Eli;

/// <summary>Search criteria for <see cref="IEliClient.SearchActsAsync"/>. At least a title, a type or a year is required.</summary>
public sealed class EliSearch
{
    /// <summary>Words to find in the title (all of them, any order, case-insensitive), for example "kodeks cywilny".</summary>
    public string? Title { get; set; }

    /// <summary>Kind of document as the API names it: Ustawa, Rozporządzenie, Obwieszczenie... (see the API's /types).</summary>
    public string? Type { get; set; }

    /// <summary>Publisher code: <c>DU</c> (Dziennik Ustaw) or <c>MP</c> (Monitor Polski).</summary>
    public string? Publisher { get; set; }

    /// <summary>Year of publication.</summary>
    public int? Year { get; set; }

    /// <summary>When true, only acts in force.</summary>
    public bool InForceOnly { get; set; }

    /// <summary>Page size, 1 to 500. Default 20.</summary>
    public int Limit { get; set; } = 20;

    /// <summary>Index of the first result, 0 or more.</summary>
    public int Offset { get; set; }
}

/// <summary>
/// Client for the Sejm ELI API (Polish legal acts, api.sejm.gov.pl/eli). No key is needed and no limits are published; the client
/// neither limits itself nor retries. Texts are served as HTML with a structure only for acts up to 2024: newer acts, consolidated
/// texts included, have a PDF only, so an article read here may come from a text older than the law in force.
/// </summary>
public interface IEliClient
{
    /// <summary>Finds acts (one request). The base act of a code ranks among its many amending acts: filter by <see cref="EliSearch.Type"/> and status.</summary>
    /// <exception cref="System.ArgumentException">No title, type or year was given.</exception>
    /// <exception cref="System.ArgumentOutOfRangeException">Limit or offset is out of range.</exception>
    Task<EliPage<EliActSummary>> SearchActsAsync(EliSearch search, CancellationToken cancellationToken = default);

    /// <summary>Reads one act with its consolidated texts and amendments; null when it does not exist.</summary>
    Task<EliAct?> GetActAsync(EliActId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the structure of an act's text (parts, books, articles...); null when the API has none (a PDF-only act). Cached in memory
    /// per client instance for the last 16 acts that had one.
    /// </summary>
    Task<IReadOnlyList<EliUnit>?> GetStructureAsync(EliActId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one article as plain text from exactly the act given (this does not look for the law in force). The article may be written
    /// "415", "art. 415", "446^1", "446¹" or "446_1". Returns null when the act has no HTML, has no such article, or the text is empty.
    /// One request for the structure (cached afterwards) and one for the text.
    /// </summary>
    Task<EliArticleText?> GetArticleAsync(EliActId id, string article, CancellationToken cancellationToken = default);

    /// <summary>
    /// Works out how current the text of a base act is: its newest consolidated text (tekst jednolity), the newest one the API serves
    /// as HTML, and the amendments that took effect after that text was announced (or take effect in the future). Looks at the five
    /// newest consolidated texts, so costs one request for the act plus at most five. The base act's own HTML is its original text and
    /// is offered only when the act has no consolidated text at all. Use the result to say, next to any quoted article, whether
    /// newer law exists.
    /// </summary>
    /// <exception cref="System.ArgumentException">The act does not exist.</exception>
    Task<EliCurrentText> GetCurrentTextAsync(EliActId baseId, CancellationToken cancellationToken = default);

    /// <summary>Like <see cref="GetCurrentTextAsync(EliActId, CancellationToken)"/> for an act you have already read: saves the request for the base act (at most five requests).</summary>
    Task<EliCurrentText> GetCurrentTextAsync(EliAct baseAct, CancellationToken cancellationToken = default);
}
