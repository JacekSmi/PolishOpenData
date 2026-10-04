using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using PolishOpenData.Internal;

namespace PolishOpenData.Bir;

/// <summary>
/// Counts every request sent to GUS and keeps the process under the published limits. The per-second window counts
/// HTTP calls (a 20-id batch is one call); the per-minute and per-hour windows count the weight of the calls (the
/// number of identifiers). One instance must be shared by every client of the process, because the GUS limits apply
/// to the application instance, not to a client object. Per-second pressure waits (up to a maximum); per-minute and
/// per-hour exhaustion throws at once.
/// </summary>
internal sealed class RegonRateLimiter
{
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    private readonly TimeProvider _timeProvider;
    private readonly RegonRateLimit _mode;
    private readonly TimeSpan _maxWait;
    private readonly object _gate = new();
    private readonly List<Entry> _entries = new();
    private int _hourWeight;

    public RegonRateLimiter(TimeProvider timeProvider, RegonRateLimit mode, TimeSpan maxWait)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
        _mode = mode;
        _maxWait = maxWait;
    }

    /// <summary>What the limiter has recorded in the last second, pruned at the current clock time.</summary>
    internal int CallsInLastSecond
    {
        get
        {
            lock (_gate)
            {
                var now = Prune();
                return CallsSince(now - Second);
            }
        }
    }

    /// <summary>The weight recorded in the last minute, pruned at the current clock time.</summary>
    internal int WeightInLastMinute
    {
        get
        {
            lock (_gate)
            {
                var now = Prune();
                return WeightSince(now - Minute);
            }
        }
    }

    /// <summary>The weight recorded in the last hour, pruned at the current clock time.</summary>
    internal int WeightInLastHour
    {
        get
        {
            lock (_gate)
            {
                Prune();
                return _hourWeight;
            }
        }
    }

    /// <summary>The three limits of a window. Published by GUS: research-gus-bir/api-facts.md section 9.</summary>
    internal readonly struct Limits
    {
        public Limits(int perSecond, int perMinute, int perHour)
        {
            PerSecond = perSecond;
            PerMinute = perMinute;
            PerHour = perHour;
        }

        public int PerSecond { get; }

        public int PerMinute { get; }

        public int PerHour { get; }
    }

    public static Limits LimitsAt(RegonRateLimit mode, DateTimeOffset utcNow)
    {
        switch (mode)
        {
            case RegonRateLimit.Off:
                return new Limits(int.MaxValue, int.MaxValue, int.MaxValue);
            case RegonRateLimit.TimeOfDay:
                var hour = TimeZoneInfo.ConvertTime(utcNow, WarsawTime.Zone).Hour;
                if (hour >= 8 && hour < 17)
                {
                    return new Limits(3, 120, 6000);
                }

                if (hour >= 22 || hour < 6)
                {
                    return new Limits(4, 200, 10000);
                }

                return new Limits(3, 150, 8000);
            default:
                return new Limits(3, 120, 6000);
        }
    }

    /// <summary>Waits until a call of this weight fits into every window, then records it.</summary>
    /// <exception cref="QuotaExceededException">The minute or hour window is full, or the total per-second wait would exceed the maximum.</exception>
    public async Task AcquireAsync(int weight, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(weight, 1);
        if (_mode == RegonRateLimit.Off)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

        // MaxRateLimitWait bounds the whole wait of this call, not each single delay: waiters compete again after every delay.
        var deadline = _timeProvider.GetUtcNow() + _maxWait;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TimeSpan wait;
            lock (_gate)
            {
                var now = Prune();
                var limits = LimitsAt(_mode, now);
                if (weight > limits.PerMinute || weight > limits.PerHour)
                {
                    throw new QuotaExceededException(
                        "A single GUS REGON call of weight " + weight.ToString(CultureInfo.InvariantCulture) + " can never fit into the request limits.");
                }

                var minuteWeight = WeightSince(now - Minute);
                if (minuteWeight + weight > limits.PerMinute)
                {
                    var resetsAt = WindowResetsAt(now, Minute, minuteWeight + weight - limits.PerMinute);
                    throw Exceeded("per-minute", limits.PerMinute, now, resetsAt);
                }

                if (_hourWeight + weight > limits.PerHour)
                {
                    var resetsAt = WindowResetsAt(now, Hour, _hourWeight + weight - limits.PerHour);
                    throw Exceeded("per-hour", limits.PerHour, now, resetsAt);
                }

                var inSecond = CallsSince(now - Second);
                if (inSecond < limits.PerSecond)
                {
                    _entries.Add(new Entry(now, weight));
                    _hourWeight += weight;
                    return;
                }

                // The calls inside the window are the last inSecond entries and this call fits once only perSecond - 1 of
                // them remain, so the entry that must leave the window last is the perSecond-th from the end.
                var mustLeave = _entries[_entries.Count - limits.PerSecond];
                wait = mustLeave.At + Second - now;
                if (now + wait > deadline)
                {
                    throw new QuotaExceededException(
                        "GUS REGON per-second request limit (" + limits.PerSecond.ToString(CultureInfo.InvariantCulture) +
                        "): the call would have to wait longer than RegonClientOptions.MaxRateLimitWait.",
                        null,
                        null,
                        now + wait,
                        wait);
                }
            }

            await DelayAsync(wait, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Checks, without recording anything, that a batch of <paramref name="identifiers"/> (plus <paramref name="loginWeight"/>
    /// for a login it will need) fits into what is left of the per-minute and per-hour windows now, so that a batch that would
    /// be refused part way is refused before its first request. Concurrent callers are not reserved against.
    /// </summary>
    /// <exception cref="QuotaExceededException">The batch does not fit into the minute or the hour window.</exception>
    public void EnsureCapacity(int identifiers, int loginWeight)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(identifiers, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(loginWeight, 0);
        if (_mode == RegonRateLimit.Off)
        {
            return;
        }

        lock (_gate)
        {
            var now = Prune();
            var limits = LimitsAt(_mode, now);
            var weight = identifiers + loginWeight;
            var ceiling = Math.Min(limits.PerMinute, limits.PerHour) - loginWeight;
            var ceilingText = "one call may carry at most " + ceiling.ToString(CultureInfo.InvariantCulture) + " identifiers under RegonRateLimit."
                + (_mode == RegonRateLimit.TimeOfDay ? "TimeOfDay at this hour" : "Strictest") + (loginWeight > 0 ? " when it has to log in first" : string.Empty) + ".";
            if (weight > limits.PerMinute || weight > limits.PerHour)
            {
                throw new QuotaExceededException(
                    "A batch of " + identifiers.ToString(CultureInfo.InvariantCulture) + " identifiers can never fit into the GUS REGON request limits ("
                    + limits.PerMinute.ToString(CultureInfo.InvariantCulture) + " per minute): " + ceilingText + " Nothing was sent; split the batch.");
            }

            var minuteWeight = WeightSince(now - Minute);
            if (minuteWeight + weight > limits.PerMinute)
            {
                var resetsAt = WindowResetsAt(now, Minute, minuteWeight + weight - limits.PerMinute);
                throw BatchExceeded("per-minute", limits.PerMinute, identifiers, ceilingText, now, resetsAt);
            }

            if (_hourWeight + weight > limits.PerHour)
            {
                var resetsAt = WindowResetsAt(now, Hour, _hourWeight + weight - limits.PerHour);
                throw BatchExceeded("per-hour", limits.PerHour, identifiers, ceilingText, now, resetsAt);
            }
        }
    }

    private static QuotaExceededException BatchExceeded(string window, int limit, int identifiers, string ceilingText, DateTimeOffset now, DateTimeOffset resetsAt) =>
        new(
            "GUS REGON " + window + " request limit (" + limit.ToString(CultureInfo.InvariantCulture) + "): a batch of "
            + identifiers.ToString(CultureInfo.InvariantCulture) + " identifiers does not fit into what is left of it, so it is refused before its first request; "
            + ceilingText,
            null,
            null,
            resetsAt,
            resetsAt - now);

    private static QuotaExceededException Exceeded(string window, int limit, DateTimeOffset now, DateTimeOffset resetsAt) =>
        new(
            "GUS REGON " + window + " request limit (" + limit.ToString(CultureInfo.InvariantCulture) +
            ") reached: this call is refused locally so that GUS is not overloaded.",
            null,
            null,
            resetsAt,
            resetsAt - now);

    private Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
#if NET
        Task.Delay(delay, _timeProvider, cancellationToken);
#else
        _timeProvider.Delay(delay, cancellationToken);
#endif

    // Drops what is older than an hour and returns the current time. A call counts for a window only while
    // at > now - window, so one that is exactly one window old is already outside. Callers hold _gate.
    private DateTimeOffset Prune()
    {
        var now = _timeProvider.GetUtcNow();
        var cutoff = now - Hour;
        var remove = 0;
        while (remove < _entries.Count && _entries[remove].At <= cutoff)
        {
            _hourWeight -= _entries[remove].Weight;
            remove++;
        }

        if (remove > 0)
        {
            _entries.RemoveRange(0, remove);
        }

        return now;
    }

    private int CallsSince(DateTimeOffset cutoff)
    {
        var count = 0;
        for (var i = _entries.Count - 1; i >= 0 && _entries[i].At > cutoff; i--)
        {
            count++;
        }

        return count;
    }

    private int WeightSince(DateTimeOffset cutoff)
    {
        var sum = 0;
        for (var i = _entries.Count - 1; i >= 0 && _entries[i].At > cutoff; i--)
        {
            sum += _entries[i].Weight;
        }

        return sum;
    }

    // When enough of the oldest entries have left the window for `excess` weight to be freed.
    private DateTimeOffset WindowResetsAt(DateTimeOffset now, TimeSpan window, int excess)
    {
        var cutoff = now - window;
        var freed = 0;
        foreach (var entry in _entries)
        {
            if (entry.At <= cutoff)
            {
                continue;
            }

            freed += entry.Weight;
            if (freed >= excess)
            {
                return entry.At + window;
            }
        }

        return now + window;
    }

    private readonly struct Entry
    {
        public Entry(DateTimeOffset at, int weight)
        {
            At = at;
            Weight = weight;
        }

        public DateTimeOffset At { get; }

        public int Weight { get; }
    }
}
