using System;

namespace PolishOpenData.Internal;

/// <summary>Europe/Warsaw time helpers that work on .NET Framework, under InvariantGlobalization and on Linux.</summary>
internal static class WarsawTime
{
    private static readonly Lazy<TimeZoneInfo> LazyZone = new(Resolve);

    /// <summary>The Warsaw time zone.</summary>
    public static TimeZoneInfo Zone => LazyZone.Value;

    /// <summary>Today's calendar date in Warsaw.</summary>
    public static DateOnly Today(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        var local = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), Zone);
        return DateOnly.FromDateTime(local.DateTime);
    }

    /// <summary>The next midnight in Warsaw, as a UTC instant.</summary>
    public static DateTimeOffset NextMidnightUtc(TimeProvider timeProvider)
    {
        var tomorrow = Today(timeProvider).AddDays(1).ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(tomorrow, Zone.GetUtcOffset(tomorrow)).ToUniversalTime();
    }

    /// <summary>Interprets a wall-clock time (Kind ignored) as Warsaw local time.</summary>
    public static DateTimeOffset FromLocal(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, Zone.GetUtcOffset(unspecified));
    }

    /// <summary>Tries a system time-zone id; <c>null</c> when the OS does not know it.</summary>
    public static TimeZoneInfo? TryFind(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }
    }

    /// <summary>Zero-dependency fallback: CET/CEST with the EU daylight-saving rule in force since 1996.</summary>
    public static TimeZoneInfo CreateCustom()
    {
        var start = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday);
        var end = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(1996, 1, 1), DateTime.MaxValue.Date, TimeSpan.FromHours(1), start, end);
        return TimeZoneInfo.CreateCustomTimeZone("Europe/Warsaw", TimeSpan.FromHours(1), "(UTC+01:00) Warsaw", "CET", "CEST", [rule]);
    }

    private static TimeZoneInfo Resolve() =>
        TryFind("Europe/Warsaw") ?? TryFind("Central European Standard Time") ?? CreateCustom();
}
