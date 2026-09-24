using System;
using Microsoft.Extensions.Time.Testing;
using PolishOpenData.BialaLista;

namespace PolishOpenData.BialaLista.Tests;

public class QuotaTrackerTests
{
    [Fact]
    public void Counts_per_kind_and_blocks_at_the_limit()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 24, 8, 0, 0, TimeSpan.Zero));
        var tracker = new BialaListaQuotaTracker(clock);

        tracker.Reserve(BialaListaRequestKind.Search, 2);
        tracker.Reserve(BialaListaRequestKind.Search, 2);
        tracker.Reserve(BialaListaRequestKind.Check, 2);

        var ex = Assert.Throws<QuotaExceededException>(() => tracker.Reserve(BialaListaRequestKind.Search, 2));
        Assert.Null(ex.StatusCode);
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 22, 0, 0, TimeSpan.Zero), ex.ResetsAt);
        Assert.Equal(2, tracker.SearchesUsedToday);
        Assert.Equal(1, tracker.ChecksUsedToday);
    }

    [Fact]
    public void Resets_at_warsaw_midnight()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 24, 21, 59, 0, TimeSpan.Zero));
        var tracker = new BialaListaQuotaTracker(clock);
        tracker.Reserve(BialaListaRequestKind.Search, 1);
        Assert.Throws<QuotaExceededException>(() => tracker.Reserve(BialaListaRequestKind.Search, 1));

        clock.Advance(TimeSpan.FromMinutes(2));   // 22:01 UTC = 00:01 in Warsaw
        tracker.Reserve(BialaListaRequestKind.Search, 1);
        Assert.Equal(1, tracker.SearchesUsedToday);
    }

    [Fact]
    public void Exhausted_blocks_everything_until_midnight()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 24, 8, 0, 0, TimeSpan.Zero));
        var tracker = new BialaListaQuotaTracker(clock);
        tracker.MarkExhausted();

        Assert.Throws<QuotaExceededException>(() => tracker.Reserve(BialaListaRequestKind.Check, 5000));
        clock.Advance(TimeSpan.FromDays(1));
        tracker.Reserve(BialaListaRequestKind.Check, 5000);
    }
}
