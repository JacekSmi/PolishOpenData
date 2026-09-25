using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using PolishOpenData.Internal;
using PolishOpenData.Krs;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.SmokeTests;

/// <summary>The KRS client against the live KRS Open API (gated and budgeted by <see cref="Live"/>).</summary>
public sealed class KrsLiveTests
{
    // Weekdays tried by the change-feed test: the longest run of weekday public holidays (24-26 December), a feed not
    // yet published for yesterday, and one more.
    private const int MaxFeedDays = 5;

    private static readonly KrsNumber Orlen = KrsNumber.Parse(Live.OrlenKrs);

    [Fact(Timeout = Live.TestTimeout)]
    public async Task Krs_current_extract_of_orlen_matches_the_model()
    {
        Live.SkipUnlessEnabled();
        using var http = Live.CreateHttpClient();
        var client = new KrsClient(http);

        KrsResult<KrsCurrentExtract> result;
        try
        {
            result = await client.GetCurrentExtractAsync(Orlen, KrsRegister.Entrepreneurs, TestContext.Current.CancellationToken);
        }
        catch (QuotaExceededException ex)
        {
            Live.SkipOnQuota(ex);
            return;
        }

        Assert.Equal(KrsLookupStatus.Found, result.Status);
        Assert.Empty(UnknownFields.Find(result.Extract));   // a new upstream field fails here and opens an issue
        var summary = result.Extract!.ToSummary();
        Assert.Contains("ORLEN", summary.Name, StringComparison.Ordinal);
        Assert.Equal(Nip.Parse(Live.OrlenNip), summary.Nip);
        Assert.NotNull(summary.Representation);
    }

    [Fact(Timeout = Live.TestTimeout)]
    public async Task Krs_full_extract_of_orlen_has_a_typed_header()
    {
        Live.SkipUnlessEnabled();
        using var http = Live.CreateHttpClient();
        var client = new KrsClient(http);

        KrsResult<KrsFullExtract> result;
        try
        {
            result = await client.GetFullExtractAsync(Orlen, cancellationToken: TestContext.Current.CancellationToken);
        }
        catch (QuotaExceededException ex)
        {
            Live.SkipOnQuota(ex);
            return;
        }

        Assert.Equal(KrsLookupStatus.Found, result.Status);
        Assert.Equal(KrsRegister.Entrepreneurs, result.Register);
        var extract = result.Extract!;
        Assert.Empty(UnknownFields.Find(extract));   // the header (entry history) is typed; Dane stays raw JSON
        var header = extract.NaglowekP!;
        Assert.Equal(Live.OrlenKrs, header.NumerKrs);
        Assert.NotNull(header.DataCzasOdpisu);
        Assert.NotEmpty(header.Wpis!);
        Assert.InRange(extract.RegisteredOn!.Value, new DateOnly(2001, 1, 1), WarsawTime.Today(TimeProvider.System));
        Assert.False(extract.IsRemoved);
        Assert.Equal(JsonValueKind.Object, extract.Dane!.Value.ValueKind);
    }

    [Fact(Timeout = Live.TestTimeout)]
    public async Task Krs_number_above_the_assigned_range_is_not_found_in_either_register()
    {
        // KRS numbers are assigned in sequence and were near 1.27 million in 2026 (tests/Fixtures/krs has 0001268296),
        // so 0009999999 is unassigned for decades. It still fits in 32 bits, in case the API parses it as an int.
        // Expected: 404 from register P, then 404 from register S (two requests).
        Live.SkipUnlessEnabled();
        using var http = Live.CreateHttpClient();
        var client = new KrsClient(http);

        KrsResult<KrsCurrentExtract> result;
        try
        {
            result = await client.GetCurrentExtractAsync(KrsNumber.Parse("0009999999"), cancellationToken: TestContext.Current.CancellationToken);
        }
        catch (QuotaExceededException ex)
        {
            Live.SkipOnQuota(ex);
            return;
        }

        Assert.Equal(KrsLookupStatus.NotFound, result.Status);
        Assert.Null(result.Register);
        Assert.Null(result.Extract);
    }

    [Fact(Timeout = Live.TestTimeout)]
    public async Task Krs_change_feed_of_a_recent_weekday_is_not_empty()
    {
        // A weekday feed lists thousands of entities (tests/Fixtures/krs/biuletyn-2026-09-22.json has 3,338). The client
        // yields nothing for 404 and 204 and skips entries it cannot parse, so a moved route or a new entry format shows
        // up only as an empty feed. Stepping back over empty days covers public holidays and a feed not yet published.
        Live.SkipUnlessEnabled();
        using var http = Live.CreateHttpClient();
        var client = new KrsClient(http);

        var tried = new List<string>();
        var changed = new List<KrsNumber>();
        var day = WarsawTime.Today(TimeProvider.System);
        try
        {
            while (changed.Count == 0 && tried.Count < MaxFeedDays)
            {
                day = LastWeekdayBefore(day);
                tried.Add(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                await foreach (var krs in client.GetChangedAsync(day, TestContext.Current.CancellationToken))
                {
                    changed.Add(krs);
                    if (changed.Count == 50)
                    {
                        break;
                    }
                }
            }
        }
        catch (QuotaExceededException ex)
        {
            Live.SkipOnQuota(ex);
            return;
        }

        Assert.True(changed.Count > 0, "The KRS change feed was empty for " + string.Join(", ", tried) + "; the Biuletyn route or its entry format may have changed.");
    }

    private static DateOnly LastWeekdayBefore(DateOnly today)
    {
        var day = today.AddDays(-1);
        while (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            day = day.AddDays(-1);
        }

        return day;
    }
}
