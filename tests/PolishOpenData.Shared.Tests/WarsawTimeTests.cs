using System;
using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using PolishOpenData.Internal;

namespace PolishOpenData.Shared.Tests;

public class WarsawTimeTests
{
    [Theory]
    [InlineData("2026-09-23T22:30:00Z", "2026-09-24")]   // CEST (UTC+2): 00:30 next day in Warsaw
    [InlineData("2026-09-23T21:59:59Z", "2026-09-23")]
    [InlineData("2026-12-31T23:00:00Z", "2027-01-01")]   // CET (UTC+1)
    [InlineData("2026-12-31T22:59:59Z", "2026-12-31")]
    public void Today_is_the_warsaw_calendar_date(string utc, string expected)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse(utc, CultureInfo.InvariantCulture));
        Assert.Equal(expected, WarsawTime.Today(clock).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("2026-09-23T10:00:00Z", "2026-09-23T22:00:00Z")]   // summer: midnight = 22:00 UTC
    [InlineData("2026-12-01T10:00:00Z", "2026-12-01T23:00:00Z")]   // winter: midnight = 23:00 UTC
    public void Next_midnight_is_returned_in_utc(string now, string expected)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse(now, CultureInfo.InvariantCulture));
        Assert.Equal(DateTimeOffset.Parse(expected, CultureInfo.InvariantCulture), WarsawTime.NextMidnightUtc(clock));
    }

    [Fact]
    public void From_local_applies_the_seasonal_offset()
    {
        Assert.Equal(TimeSpan.FromHours(2), WarsawTime.FromLocal(new DateTime(2026, 9, 24, 2, 34, 18)).Offset);
        Assert.Equal(TimeSpan.FromHours(1), WarsawTime.FromLocal(new DateTime(2026, 12, 1, 10, 0, 0)).Offset);
        Assert.Equal(
            new DateTimeOffset(2026, 9, 24, 0, 34, 18, TimeSpan.Zero),
            WarsawTime.FromLocal(new DateTime(2026, 9, 24, 2, 34, 18)).ToUniversalTime());
    }

    [Fact]
    public void Try_from_local_rejects_a_moment_before_the_first_utc_instant()
    {
        // Warsaw is always ahead of UTC, so its first wall-clock instant maps to a UTC time before year 1
        Assert.False(WarsawTime.TryFromLocal(DateTime.MinValue, out _));
        Assert.True(WarsawTime.TryFromLocal(new DateTime(2026, 9, 24, 2, 34, 18), out var value));
        Assert.Equal(WarsawTime.FromLocal(new DateTime(2026, 9, 24, 2, 34, 18)), value);
        Assert.True(WarsawTime.TryFromLocal(DateTime.MaxValue, out var last));
        Assert.Equal(DateTime.MaxValue, last.DateTime);
    }

    [Fact]
    public void Custom_zone_matches_the_system_zone_2000_to_2040()
    {
        var system = WarsawTime.TryFind("Europe/Warsaw") ?? WarsawTime.TryFind("Central European Standard Time");
        Assert.NotNull(system);
        var custom = WarsawTime.CreateCustom();
        for (var t = new DateTimeOffset(2000, 1, 1, 0, 30, 0, TimeSpan.Zero); t.Year < 2040; t = t.AddMinutes(30 * 97))
        {
            Assert.Equal(system!.GetUtcOffset(t), custom.GetUtcOffset(t));
        }
    }
}
