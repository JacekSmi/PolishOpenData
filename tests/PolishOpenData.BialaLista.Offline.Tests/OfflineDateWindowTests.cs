using System;
using Microsoft.Extensions.Time.Testing;
using PolishOpenData.Internal;

namespace PolishOpenData.BialaLista.Offline.Tests;

public sealed class OfflineDateWindowTests
{
    private static readonly DateOnly Today = new(2026, 10, 13);

    [Fact]
    public void Tomorrow_allowed() => OfflineDateWindow.Check(Today.AddDays(1), Today);

    [Fact]
    public void Day_after_tomorrow_outside() => AssertOutside(Today.AddDays(2), Today);

    [Fact]
    public void Thirty_days_back_allowed() => OfflineDateWindow.Check(Today.AddDays(-30), Today);

    [Fact]
    public void Thirty_one_days_back_outside() => AssertOutside(Today.AddDays(-31), Today);

    [Fact]
    public void Window_uses_Warsaw_date_not_UTC()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 13, 23, 30, 0, TimeSpan.Zero));
        var today = WarsawTime.Today(clock);
        Assert.Equal(new DateOnly(2026, 10, 14), today);

        OfflineDateWindow.Check(new DateOnly(2026, 10, 15), today);
        AssertOutside(new DateOnly(2026, 10, 16), today);
    }

    private static void AssertOutside(DateOnly date, DateOnly today)
    {
        var exception = Assert.Throws<VatWhitelistDataUnavailableException>(() => OfflineDateWindow.Check(date, today));
        Assert.Equal(VatWhitelistDataUnavailableReason.OutsideWindow, exception.Reason);
        Assert.Equal(date, exception.Date);
    }
}
