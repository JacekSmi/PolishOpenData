using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PolishOpenData.Internal;

namespace PolishOpenData.Eli;

/// <summary>HttpClient-based implementation of <see cref="IEliClient"/>.</summary>
public sealed class EliClient : IEliClient
{
    private const string Service = "ELI";
    private const string JsonAccept = "application/json";
    private const string HtmlAccept = "text/html";
    private const int MaxJsonBytes = 8_000_000;
    private const int MaxFragmentBytes = 1_000_000;
    private const int MaxArticleChars = 20_000;
    private const int StructureCacheSize = 16;
    private const int MaxConsolidatedChecked = 5;

    private static readonly Regex SegmentKey = new(@"^[A-Za-z]{1,12}$", RegexOptions.CultureInvariant);
    // unit ids contain Polish capitals in some codes (part_OGÓLNA, book_PIĄTA): letters and digits of any script are fine, the value is escaped for the URL
    private static readonly Regex SegmentValue = new(@"^[\p{L}\p{N}_]{1,40}$", RegexOptions.CultureInvariant);

    private readonly HttpClient _httpClient;
    private readonly EliClientOptions _options;
    private readonly TimeProvider _time;
    private readonly object _cacheLock = new();
    private readonly Dictionary<EliActId, IReadOnlyList<EliUnit>> _structures = new();
    private readonly Queue<EliActId> _structureOrder = new();

    /// <summary>Creates a client. The <paramref name="httpClient"/> is not disposed by this class.</summary>
    /// <exception cref="ArgumentException"><see cref="EliClientOptions.BaseAddress"/> is not an absolute https URI.</exception>
    public EliClient(HttpClient httpClient, EliClientOptions? options = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _options = options ?? new EliClientOptions();
        _time = timeProvider ?? TimeProvider.System;
        _options.Validate();
    }

    /// <inheritdoc/>
    public async Task<EliPage<EliActSummary>> SearchActsAsync(EliSearch search, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        var hasTitle = !string.IsNullOrWhiteSpace(search.Title);
        if (!hasTitle && string.IsNullOrWhiteSpace(search.Type) && search.Year is null)
        {
            throw new ArgumentException("Give a title, a type or a year to search for.", nameof(search));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(search.Limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(search.Limit, 500);
        ArgumentOutOfRangeException.ThrowIfLessThan(search.Offset, 0);

        var query = new Query();
        if (hasTitle)
        {
            query.Add("title", search.Title!.Trim());
        }

        if (!string.IsNullOrWhiteSpace(search.Type))
        {
            query.Add("type", search.Type!.Trim());
        }

        if (!string.IsNullOrWhiteSpace(search.Publisher))
        {
            query.Add("publisher", search.Publisher!.Trim());
        }

        if (search.Year is { } year)
        {
            query.Add("year", Number(year));
        }

        if (search.InForceOnly)
        {
            query.Add("inForce", "1");
        }

        query.Add("limit", Number(search.Limit));
        if (search.Offset > 0)
        {
            query.Add("offset", Number(search.Offset));
        }

        var page = await RequestAsync("acts/search" + query, MaxJsonBytes, JsonAccept,
            body => Map(Parse(body, EliJson.Search), static dto => new EliPage<EliActSummary>(dto.Items is null ? [] : dto.Items.Where(static i => i is not null).Select(EliMapper.ToSummary).ToList(), dto.TotalCount ?? dto.Items?.Count ?? 0)),
            cancellationToken).ConfigureAwait(false);
        return page ?? new EliPage<EliActSummary>([], 0);
    }

    /// <inheritdoc/>
    public Task<EliAct?> GetActAsync(EliActId id, CancellationToken cancellationToken = default)
    {
        RequireId(id);
        return RequestAsync("acts/" + id.Path, MaxJsonBytes, JsonAccept, body => Map(Parse(body, EliJson.Act), EliMapper.ToAct), cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<EliUnit>?> GetStructureAsync(EliActId id, CancellationToken cancellationToken = default)
    {
        RequireId(id);
        lock (_cacheLock)
        {
            if (_structures.TryGetValue(id, out var cached))
            {
                return cached;
            }
        }

        var units = await RequestAsync("acts/" + id.Path + "/struct", MaxJsonBytes, JsonAccept, body => Map(Parse(body, EliJson.Struct), EliMapper.ToUnits), cancellationToken).ConfigureAwait(false);
        if (units is null)
        {
            return null;   // not cached: a structure that appears later must be seen
        }

        lock (_cacheLock)
        {
            if (_structures.TryAdd(id, units))
            {
                _structureOrder.Enqueue(id);
                while (_structureOrder.Count > StructureCacheSize)
                {
                    _ = _structures.Remove(_structureOrder.Dequeue());
                }
            }
        }

        return units;
    }

    /// <inheritdoc/>
    public async Task<EliArticleText?> GetArticleAsync(EliActId id, string article, CancellationToken cancellationToken = default)
    {
        RequireId(id);
        if (EliArticle.ParseUnit(article) is not { } want)
        {
            return null;
        }

        var units = await GetStructureAsync(id, cancellationToken).ConfigureAwait(false);
        var unit = units is null ? null : FindUnit(units, want.Type, want.Name);
        if (unit is null || TreePath(unit.Id) is not { } tree)
        {
            return null;
        }

        var html = await RequestAsync("acts/" + id.Path + "/text.html/" + tree, MaxFragmentBytes, HtmlAccept, static body => Encoding.UTF8.GetString(body), cancellationToken).ConfigureAwait(false);
        if (html is null)
        {
            return null;
        }

        string text;
        bool truncated;
        try
        {
            text = EliHtmlText.ToPlainText(html, MaxArticleChars, out truncated);
        }
        catch (RegexMatchTimeoutException ex)
        {
            // hostile or pathological markup: an error about the response, never a bare TimeoutException (which other layers read as something else)
            throw new PolishOpenDataApiException(Service + " returned markup this library could not read in time.", HttpStatusCode.OK, null, null, ex);
        }

        return text.Length == 0 ? null : new EliArticleText(id, want.Name, want.Type, unit.Id, text, truncated);
    }

    /// <inheritdoc/>
    public async Task<EliCurrentText> GetCurrentTextAsync(EliActId baseId, CancellationToken cancellationToken = default)
    {
        RequireId(baseId);
        var act = await GetActAsync(baseId, cancellationToken).ConfigureAwait(false)
            ?? throw new ArgumentException("No legal act " + baseId + " in the ELI API.", nameof(baseId));
        return await GetCurrentTextAsync(act, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<EliCurrentText> GetCurrentTextAsync(EliAct baseAct, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseAct);
        var act = baseAct;
        var baseId = act.Summary.Id;

        // Newest first by publication order (year, then position), not by the order the API lists them in.
        var candidates = act.ConsolidatedTexts
            .OrderByDescending(static c => c.Year)
            .ThenByDescending(static c => c.Position)
            .Take(MaxConsolidatedChecked)
            .ToList();

        EliActSummary? newest = null;
        EliActSummary? withHtml = null;
        var newestMissing = false;
        var checkedCount = 0;
        foreach (var id in candidates)
        {
            checkedCount++;
            var text = await GetActAsync(id, cancellationToken).ConfigureAwait(false);
            if (text is null)
            {
                // listed but not served: skip it, but then the newest text cannot be confirmed, so the answer is never "current"
                newestMissing |= newest is null;
                continue;
            }

            newest ??= text.Summary;
            if (text.Summary.HasHtml)
            {
                withHtml = text.Summary;
                break;
            }
        }

        // The base act's own HTML is the ORIGINAL text. It is offered only when no consolidated text exists at all.
        if (act.ConsolidatedTexts.Count == 0 && act.Summary.HasHtml)
        {
            withHtml = act.Summary;
        }

        var since = (withHtml ?? newest)?.AnnouncementDate ?? act.Summary.AnnouncementDate ?? DateOnly.MinValue;
        var today = WarsawTime.Today(_time);   // the law changes at midnight in Warsaw, not in UTC
        var inForce = new List<EliAmendment>();
        var future = new List<EliAmendment>();
        foreach (var a in act.Amendments)
        {
            if (a.EffectiveDate is { } date)
            {
                if (date <= since)
                {
                    continue;   // already part of the text
                }

                (date > today ? future : inForce).Add(a);
            }
            else
            {
                inForce.Add(a);   // no date: assume it may already apply rather than call the text current
            }
        }

        // Cross-checks that do not depend on the reference lists being complete: a repealed act, a text that is no longer in force, or a status
        // that promises a consolidated text none of whose links we got, can never be called current.
        var repealed = act.Summary.InForce == false || withHtml?.InForce == false;
        var promisedTextNotListed = act.ConsolidatedTexts.Count == 0
            && string.Equals(act.Summary.Status, "akt posiada tekst jednolity", StringComparison.Ordinal);
        var isCurrent = withHtml is not null && !newestMissing && inForce.Count == 0 && !repealed && !promisedTextNotListed
            && (newest is null || newest.Id == withHtml.Id);
        return new EliCurrentText(baseId, newest, withHtml, inForce, future, isCurrent, checkedCount, act.Summary.InForce, act.Summary.Status);
    }

    private static EliUnit? FindUnit(IReadOnlyList<EliUnit> units, string type, string name)
    {
        foreach (var u in units)
        {
            if (u.Type == type && string.Equals(u.Name, name, StringComparison.Ordinal))
            {
                return u;
            }

            if (FindUnit(u.Children, type, name) is { } inner)
            {
                return inner;
            }
        }

        return null;
    }

    // "book_TRZECIA-titl_VI-arti_415" -> "book=TRZECIA/titl=VI/arti=415"; null when an id has anything unexpected (it goes into a URL path).
    private static string? TreePath(string unitId)
    {
        var parts = new List<string>();
        foreach (var segment in unitId.Split('-'))
        {
            var cut = segment.IndexOf('_', StringComparison.Ordinal);
            if (cut <= 0)
            {
                return null;
            }

            var key = segment.Substring(0, cut);
            var value = segment.Substring(cut + 1);
            if (!SegmentKey.IsMatch(key) || !SegmentValue.IsMatch(value))
            {
                return null;
            }

            parts.Add(key + "=" + Uri.EscapeDataString(value));
        }

        return string.Join("/", parts);
    }

    private static void RequireId(EliActId id)
    {
        if (id.IsEmpty)
        {
            throw new ArgumentException("The act id is empty.", nameof(id));
        }
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static T Parse<T>(byte[] body, JsonTypeInfo<T> typeInfo)
    {
        using var stream = new MemoryStream(body, writable: false);
        return JsonSerializer.Deserialize(stream, typeInfo) ?? throw new JsonException("The response is null.");
    }

    private static TResult Map<TDto, TResult>(TDto dto, Func<TDto, TResult> map) => map(dto);

    // One GET. 404 and an empty 200 body are both "nothing here" (null): for text.html the API answers an empty 200 for an act without
    // HTML. A mapper or JSON failure becomes an unreadable-response error.
    private async Task<T?> RequestAsync<T>(string relative, int maxBytes, string accept, Func<byte[], T> handle, CancellationToken cancellationToken)
        where T : class
    {
        _options.Validate();   // the options are mutable: BaseAddress may have changed since construction
        using var request = HttpHelpers.CreateGet(new Uri(_options.ResolveBaseAddress(), relative));
        // each endpoint serves one media type and answers 406 for any other: the JSON endpoints want JSON, the text endpoint wants HTML
        request.Headers.Remove("Accept");
        request.Headers.TryAddWithoutValidation("Accept", accept);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            await response.ThrowForStatusAsync(Service, cancellationToken).ConfigureAwait(false);
        }

        // HttpResponseMessage.Content is never null from a real HttpClient, but a custom handler on .NET Framework may leave it so.
        var body = response.Content is null ? [] : await response.Content.ReadBytesAsync(maxBytes, cancellationToken).ConfigureAwait(false);
        if (body.Length > maxBytes)
        {
            throw HttpHelpers.UnreadableResponse(Service + " returned more than " + Number(maxBytes) + " bytes; refusing to read it.", response, body);
        }

        if (body.Length == 0)
        {
            return null;
        }

        try
        {
            return handle(body);
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            throw HttpHelpers.UnreadableResponse(Service + " returned a response this library cannot read: " + ex.Message, response, body, ex);
        }
    }

    /// <summary>Builds a query string with escaped values, in the order given.</summary>
    private sealed class Query
    {
        private readonly StringBuilder _text = new();

        public Query Add(string name, string value)
        {
            _ = _text.Append(_text.Length == 0 ? '?' : '&').Append(name).Append('=').Append(Uri.EscapeDataString(value));
            return this;
        }

        public override string ToString() => _text.ToString();
    }
}
