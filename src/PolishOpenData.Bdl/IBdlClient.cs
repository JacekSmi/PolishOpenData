using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PolishOpenData.Bdl;

/// <summary>
/// Client for the GUS Bank Danych Lokalnych (BDL) API. Anonymous use allows 5 requests per second, 100 per 15 minutes, 1,000
/// per 12 hours and 10,000 per week; going over throws <see cref="QuotaExceededException"/>. Data licence: CC BY 4.0, cite
/// "Główny Urząd Statystyczny, Bank Danych Lokalnych". An id that does not exist is not an error for the data calls: the result
/// is simply empty.
/// </summary>
public interface IBdlClient
{
    /// <summary>
    /// Finds units whose name contains <paramref name="name"/> (the first 100 plus the total, one request). The same name can match several
    /// levels (a subregion, a powiat and a gmina all called "Kraków"): look at <see cref="BdlUnit.Level"/>.
    /// </summary>
    /// <exception cref="System.ArgumentException"><paramref name="name"/> is shorter than 2 characters.</exception>
    /// <exception cref="System.ArgumentOutOfRangeException"><paramref name="level"/> is <see cref="BdlLevel.Locality"/>: statistical localities are not supported.</exception>
    Task<BdlPage<BdlUnit>> SearchUnitsAsync(string name, BdlLevel? level = null, CancellationToken cancellationToken = default);

    /// <summary>Reads one unit by its 12-digit BDL id; null when it does not exist.</summary>
    Task<BdlUnit?> GetUnitAsync(string unitId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches variables by text in their dimension names (the first 100 matches plus the total). A variable's dimension names
    /// mean little without its subject: read it with <see cref="GetSubjectAsync"/>.
    /// </summary>
    Task<BdlPage<BdlVariable>> SearchVariablesAsync(string text, string? subjectId = null, CancellationToken cancellationToken = default);

    /// <summary>Reads one variable (with the years it has data for); null when it does not exist.</summary>
    Task<BdlVariable?> GetVariableAsync(int variableId, CancellationToken cancellationToken = default);

    /// <summary>Reads one subject (topic) with its name and dimensions; null when it does not exist.</summary>
    Task<BdlSubject?> GetSubjectAsync(string subjectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads 1 to 100 variables for one unit in one request, optionally limited to some years. An unknown unit gives empty
    /// <see cref="BdlUnitData.Series"/>. Duplicate ids are sent once.
    /// </summary>
    Task<BdlUnitData> GetDataByUnitAsync(string unitId, IReadOnlyCollection<int> variableIds, IReadOnlyCollection<int>? years = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one variable for many units of a level (optionally below one parent unit). One request returns 100 units; the call
    /// reads at most <paramref name="maxPages"/> pages, starting at page <paramref name="startPage"/>, and sets
    /// <see cref="BdlVariableData.Truncated"/> with <see cref="BdlVariableData.NextPage"/> when more exist. Every page is one request
    /// against the rate limit: all gminas of Poland are about 25 pages. When GUS's rate limit stops the read after the first page, the
    /// units already read are returned with <see cref="BdlVariableData.RateLimited"/> set (continue from <c>NextPage</c> later);
    /// a rate limit on the first page throws <see cref="QuotaExceededException"/>.
    /// </summary>
    Task<BdlVariableData> GetDataByVariableAsync(int variableId, BdlLevel level, string? parentUnitId = null, IReadOnlyCollection<int>? years = null, int maxPages = 1, int startPage = 0, CancellationToken cancellationToken = default);
}
