using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;

namespace PolishOpenData.Bir.Tests;

public class RegonRateLimiterTests
{
    // 08:00 UTC is 10:00 in Warsaw (CEST): the 3 / 120 / 6000 window.
    private static readonly DateTimeOffset Start = new(2026, 10, 3, 8, 0, 0, TimeSpan.Zero);

    private static (RegonRateLimiter Limiter, FakeTimeProvider Clock) Create(RegonRateLimit mode = RegonRateLimit.Strictest, TimeSpan? maxWait = null)
    {
        var clock = new FakeTimeProvider(Start);
        return (new RegonRateLimiter(clock, mode, maxWait ?? TimeSpan.FromSeconds(5)), clock);
    }

    private static async Task AssertCompletesAsync(Task task, CancellationToken cancellationToken)
    {
        var finished = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10), cancellationToken));
        Assert.Same(task, finished);
        await task;
    }

    // Six calls of weight 20 (three per second) make the per-minute maximum of 120: three at start, three at start + 1 s.
    // The clock is left at start + 1 s.
    private static async Task FillTheMinuteAsync(RegonRateLimiter limiter, FakeTimeProvider clock, CancellationToken cancellationToken)
    {
        for (var i = 0; i < 3; i++)
        {
            await limiter.AcquireAsync(20, cancellationToken);
        }

        clock.Advance(TimeSpan.FromSeconds(1));
        for (var i = 0; i < 3; i++)
        {
            await limiter.AcquireAsync(20, cancellationToken);
        }
    }

    [Fact]
    public async Task Usage_probes_report_calls_and_weights()
    {
        var (limiter, clock) = Create();
        var ct = TestContext.Current.CancellationToken;

        await limiter.AcquireAsync(1, ct);
        await limiter.AcquireAsync(4, ct);

        Assert.Equal(2, limiter.CallsInLastSecond);
        Assert.Equal(5, limiter.WeightInLastMinute);
        Assert.Equal(5, limiter.WeightInLastHour);

        clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(0, limiter.CallsInLastSecond);
        Assert.Equal(0, limiter.WeightInLastMinute);
        Assert.Equal(5, limiter.WeightInLastHour);

        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(0, limiter.WeightInLastHour);
    }

    [Fact]
    public void Strictest_limits_are_3_120_and_6000()
    {
        for (var hour = 0; hour < 24; hour++)
        {
            var limits = RegonRateLimiter.LimitsAt(RegonRateLimit.Strictest, new DateTimeOffset(2026, 10, 3, hour, 30, 0, TimeSpan.Zero));

            Assert.Equal(3, limits.PerSecond);
            Assert.Equal(120, limits.PerMinute);
            Assert.Equal(6000, limits.PerHour);
        }
    }

    [Theory]
    [InlineData("2026-10-03T03:59:00Z", 4, 200, 10000)]   // 05:59 in Warsaw (CEST, UTC+2)
    [InlineData("2026-10-03T04:00:00Z", 3, 150, 8000)]    // 06:00
    [InlineData("2026-10-03T05:59:00Z", 3, 150, 8000)]    // 07:59
    [InlineData("2026-10-03T06:00:00Z", 3, 120, 6000)]    // 08:00
    [InlineData("2026-10-03T14:59:00Z", 3, 120, 6000)]    // 16:59
    [InlineData("2026-10-03T15:00:00Z", 3, 150, 8000)]    // 17:00
    [InlineData("2026-10-03T19:59:00Z", 3, 150, 8000)]    // 21:59
    [InlineData("2026-10-03T20:00:00Z", 4, 200, 10000)]   // 22:00
    [InlineData("2026-12-01T06:59:00Z", 3, 150, 8000)]    // 07:59 in winter (CET, UTC+1)
    [InlineData("2026-12-01T07:00:00Z", 3, 120, 6000)]    // 08:00 in winter
    public void TimeOfDay_selects_the_window_by_the_warsaw_hour(string utc, int perSecond, int perMinute, int perHour)
    {
        var now = DateTimeOffset.Parse(utc, CultureInfo.InvariantCulture);

        var limits = RegonRateLimiter.LimitsAt(RegonRateLimit.TimeOfDay, now);

        Assert.Equal(perSecond, limits.PerSecond);
        Assert.Equal(perMinute, limits.PerMinute);
        Assert.Equal(perHour, limits.PerHour);
    }

    [Fact]
    public async Task Off_never_waits_or_throws()
    {
        var (limiter, _) = Create(RegonRateLimit.Off);
        var ct = TestContext.Current.CancellationToken;
        var limits = RegonRateLimiter.LimitsAt(RegonRateLimit.Off, Start);

        for (var i = 0; i < 1000; i++)
        {
            var task = limiter.AcquireAsync(5000, ct);

            Assert.Equal(TaskStatus.RanToCompletion, task.Status);
            await task;
        }

        Assert.Equal(int.MaxValue, limits.PerSecond);
        Assert.Equal(int.MaxValue, limits.PerMinute);
        Assert.Equal(int.MaxValue, limits.PerHour);
        Assert.Equal(0, limiter.CallsInLastSecond);
    }

    [Fact]
    public async Task Fourth_call_in_one_second_waits_until_the_window_clears()
    {
        var (limiter, clock) = Create();
        var ct = TestContext.Current.CancellationToken;
        for (var i = 0; i < 3; i++)
        {
            await limiter.AcquireAsync(1, ct);
        }

        var fourth = limiter.AcquireAsync(1, ct);
        Assert.False(fourth.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(1));
        await AssertCompletesAsync(fourth, ct);

        Assert.Equal(1, limiter.CallsInLastSecond);
        Assert.Equal(4, limiter.WeightInLastMinute);
    }

    [Fact]
    public async Task A_wait_longer_than_the_maximum_throws_quota_exceeded()
    {
        var (limiter, _) = Create(maxWait: TimeSpan.Zero);
        var ct = TestContext.Current.CancellationToken;
        for (var i = 0; i < 3; i++)
        {
            await limiter.AcquireAsync(1, ct);
        }

        var ex = await Assert.ThrowsAsync<QuotaExceededException>(() => limiter.AcquireAsync(1, ct));

        Assert.Equal(TimeSpan.FromSeconds(1), ex.RetryAfter);
        Assert.Equal(Start.AddSeconds(1), ex.ResetsAt);
        Assert.Null(ex.StatusCode);
        Assert.Equal(3, limiter.CallsInLastSecond);
    }

    [Fact]
    public async Task Minute_exhaustion_throws_with_the_reset_time()
    {
        var (limiter, clock) = Create();
        var ct = TestContext.Current.CancellationToken;
        await FillTheMinuteAsync(limiter, clock, ct);

        Assert.Equal(120, limiter.WeightInLastMinute);

        var ex = await Assert.ThrowsAsync<QuotaExceededException>(() => limiter.AcquireAsync(1, ct));

        Assert.Contains("per-minute", ex.Message, StringComparison.Ordinal);
        Assert.Equal(Start.AddMinutes(1), ex.ResetsAt);
        Assert.Equal(TimeSpan.FromSeconds(59), ex.RetryAfter);
        Assert.Equal(120, limiter.WeightInLastMinute);
    }

    [Fact]
    public async Task Hour_exhaustion_throws_with_the_reset_time()
    {
        var (limiter, clock) = Create();
        var ct = TestContext.Current.CancellationToken;

        // 50 minutes at the per-minute maximum of 120 make 6,000 in the hour; the minute window is never the limit.
        for (var minute = 0; minute < 50; minute++)
        {
            await FillTheMinuteAsync(limiter, clock, ct);
            clock.Advance(TimeSpan.FromSeconds(59));
        }

        clock.Advance(TimeSpan.FromSeconds(1));   // the entries of the last minute are now more than a minute old
        Assert.Equal(6000, limiter.WeightInLastHour);
        Assert.Equal(0, limiter.WeightInLastMinute);

        var ex = await Assert.ThrowsAsync<QuotaExceededException>(() => limiter.AcquireAsync(1, ct));

        Assert.Contains("per-hour", ex.Message, StringComparison.Ordinal);
        Assert.Equal(Start.AddHours(1), ex.ResetsAt);
        Assert.Equal(Start.AddHours(1) - clock.GetUtcNow(), ex.RetryAfter);
    }

    [Fact]
    public async Task Batch_of_20_ids_passes_under_strictest()
    {
        var (limiter, _) = Create();
        var ct = TestContext.Current.CancellationToken;

        var task = limiter.AcquireAsync(20, ct);

        Assert.Equal(TaskStatus.RanToCompletion, task.Status);
        await task;
        Assert.Equal(1, limiter.CallsInLastSecond);
        Assert.Equal(20, limiter.WeightInLastMinute);
    }

    [Fact]
    public async Task Batch_weight_counts_toward_the_minute_window()
    {
        var (limiter, clock) = Create();
        var ct = TestContext.Current.CancellationToken;
        await FillTheMinuteAsync(limiter, clock, ct);

        var ex = await Assert.ThrowsAsync<QuotaExceededException>(() => limiter.AcquireAsync(1, ct));

        Assert.Contains("per-minute", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Window_slides_after_a_minute()
    {
        var (limiter, clock) = Create();
        var ct = TestContext.Current.CancellationToken;
        await FillTheMinuteAsync(limiter, clock, ct);

        // now = start + 1 s with 120 recorded. At start + 60 s the entries of start + 0 s are one minute old and out.
        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(60, limiter.WeightInLastMinute);

        await limiter.AcquireAsync(60, ct);
        Assert.Equal(120, limiter.WeightInLastMinute);
        await Assert.ThrowsAsync<QuotaExceededException>(() => limiter.AcquireAsync(1, ct));

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(60, limiter.WeightInLastMinute);
    }

    [Fact]
    public async Task Cancellation_while_waiting_is_honoured()
    {
        var (limiter, _) = Create();
        var ct = TestContext.Current.CancellationToken;
        for (var i = 0; i < 3; i++)
        {
            await limiter.AcquireAsync(1, ct);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var fourth = limiter.AcquireAsync(1, cts.Token);
        Assert.False(fourth.IsCompleted);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fourth);
        Assert.Equal(3, limiter.CallsInLastSecond);
    }

    [Fact]
    public async Task Concurrent_callers_never_exceed_the_per_second_limit()
    {
        var (limiter, clock) = Create();
        var ct = TestContext.Current.CancellationToken;

        // Each call runs synchronously up to its first real wait: three record at once, the other seven wait on the fake clock.
        var calls = Enumerable.Range(0, 10).Select(_ => limiter.AcquireAsync(1, ct)).ToArray();
        Assert.True(limiter.CallsInLastSecond <= 3);

        var all = Task.WhenAll(calls);
        for (var step = 0; step < 100 && !all.IsCompleted; step++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            await Task.WhenAny(all, Task.Delay(TimeSpan.FromMilliseconds(50), ct));
            Assert.True(limiter.CallsInLastSecond <= 3);
        }

        await AssertCompletesAsync(all, ct);
        Assert.Equal(10, limiter.WeightInLastMinute);
    }

    // Ten callers at once: three record at start, three after 1 s, three after 2 s and the last after 3 s.
    private static async Task<(int Passed, int Refused)> RunTenConcurrentCallersAsync(TimeSpan maxWait, CancellationToken cancellationToken)
    {
        var (limiter, clock) = Create(maxWait: maxWait);
        var calls = Enumerable.Range(0, 10).Select(_ => limiter.AcquireAsync(1, cancellationToken)).ToArray();
        var all = Task.WhenAll(calls);
        for (var step = 0; step < 20 && !calls.All(c => c.IsCompleted); step++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            await Task.WhenAny(all, Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken));
        }

        var passed = 0;
        var refused = 0;
        foreach (var call in calls)
        {
            await AssertCompletesAsync(call.ContinueWith(static _ => { }, TaskScheduler.Default), cancellationToken);
            if (call.Status == TaskStatus.RanToCompletion)
            {
                passed++;
            }
            else
            {
                Assert.IsType<QuotaExceededException>(call.Exception!.InnerException);
                refused++;
            }
        }

        Assert.Equal(passed, limiter.WeightInLastMinute);
        return (passed, refused);
    }

    [Fact]
    public async Task MaxRateLimitWait_bounds_the_total_wait_of_a_call()
    {
        // With a maximum of 1 s the callers of the third second would wait 2 s in all: they are refused.
        var (passed, refused) = await RunTenConcurrentCallersAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        Assert.Equal(6, passed);
        Assert.Equal(4, refused);
    }

    [Fact]
    public async Task Callers_within_MaxRateLimitWait_all_pass()
    {
        // With the default maximum of 5 s the callers of the third and fourth second still pass.
        var (passed, refused) = await RunTenConcurrentCallersAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(10, passed);
        Assert.Equal(0, refused);
    }

    [Fact]
    public async Task Zero_weight_is_rejected()
    {
        var (limiter, _) = Create();
        var ct = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => limiter.AcquireAsync(0, ct));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => limiter.AcquireAsync(-3, ct));

        Assert.Equal(0, limiter.CallsInLastSecond);
    }

    [Fact]
    public async Task A_weight_above_a_limit_is_refused_at_once()
    {
        var (limiter, _) = Create();
        var ct = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<QuotaExceededException>(() => limiter.AcquireAsync(121, ct));

        Assert.Equal(0, limiter.WeightInLastMinute);
    }
}
