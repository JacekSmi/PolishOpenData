using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;

namespace PolishOpenData.Bir.Tests;

public class BirSessionTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 3, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    // A login that hands out sid-1, sid-2, ... and counts how often it was called.
    private sealed class CountingLogin
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public Task<string> LoginAsync(CancellationToken cancellationToken)
        {
            var number = Interlocked.Increment(ref _count);
            return Task.FromResult("sid-" + number.ToString(CultureInfo.InvariantCulture));
        }
    }

    [Fact]
    public async Task First_call_logs_in_once()
    {
        using var session = new BirSession(new FakeTimeProvider(Start), Lifetime);
        var login = new CountingLogin();
        var ct = TestContext.Current.CancellationToken;
        Assert.Null(session.CurrentSid);

        var first = await session.GetSidAsync(login.LoginAsync, ct);
        var second = await session.GetSidAsync(login.LoginAsync, ct);

        Assert.Equal("sid-1", first);
        Assert.Equal("sid-1", second);
        Assert.Equal("sid-1", session.CurrentSid);
        Assert.Equal(1, login.Count);
    }

    [Fact]
    public async Task Concurrent_callers_share_one_login()
    {
        using var session = new BirSession(new FakeTimeProvider(Start), Lifetime);
        var ct = TestContext.Current.CancellationToken;
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var logins = 0;

        async Task<string> SlowLoginAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref logins);
            started.TrySetResult(true);
            return await release.Task;
        }

        var callers = Enumerable.Range(0, 8).Select(_ => Task.Run(() => session.GetSidAsync(SlowLoginAsync, ct), ct)).ToArray();
        await started.Task;
        await Task.Delay(TimeSpan.FromMilliseconds(100), ct);   // let the other callers reach the gate
        release.SetResult("sid-shared");

        var sids = await Task.WhenAll(callers);

        Assert.All(sids, sid => Assert.Equal("sid-shared", sid));
        Assert.Equal(1, Volatile.Read(ref logins));
    }

    [Fact]
    public async Task A_session_older_than_the_lifetime_is_refreshed()
    {
        var clock = new FakeTimeProvider(Start);
        using var session = new BirSession(clock, Lifetime);
        var login = new CountingLogin();
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal("sid-1", await session.GetSidAsync(login.LoginAsync, ct));

        clock.Advance(TimeSpan.FromMinutes(29) + TimeSpan.FromSeconds(59));
        Assert.Equal("sid-1", await session.GetSidAsync(login.LoginAsync, ct));
        Assert.Equal(1, login.Count);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("sid-2", await session.GetSidAsync(login.LoginAsync, ct));
        Assert.Equal(2, login.Count);
    }

    [Fact]
    public async Task Renew_with_a_stale_sid_returns_the_newer_one_without_a_login()
    {
        using var session = new BirSession(new FakeTimeProvider(Start), Lifetime);
        var login = new CountingLogin();
        var ct = TestContext.Current.CancellationToken;
        var first = await session.GetSidAsync(login.LoginAsync, ct);
        var renewed = await session.RenewAsync(first, login.LoginAsync, ct);

        var again = await session.RenewAsync(first, login.LoginAsync, ct);

        Assert.Equal("sid-1", first);
        Assert.Equal("sid-2", renewed);
        Assert.Equal("sid-2", again);
        Assert.Equal(2, login.Count);
    }

    [Fact]
    public async Task Renew_with_the_current_sid_logs_in_again()
    {
        using var session = new BirSession(new FakeTimeProvider(Start), Lifetime);
        var login = new CountingLogin();
        var ct = TestContext.Current.CancellationToken;
        var first = await session.GetSidAsync(login.LoginAsync, ct);

        var renewed = await session.RenewAsync(first, login.LoginAsync, ct);

        Assert.Equal("sid-2", renewed);
        Assert.Equal("sid-2", session.CurrentSid);
        Assert.Equal(2, login.Count);
    }

    [Fact]
    public async Task A_failed_login_is_not_cached()
    {
        using var session = new BirSession(new FakeTimeProvider(Start), Lifetime);
        var login = new CountingLogin();
        var ct = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<RegonAuthenticationException>(() =>
            session.GetSidAsync(_ => throw new RegonAuthenticationException("rejected"), ct));

        Assert.Null(session.CurrentSid);
        Assert.Equal("sid-1", await session.GetSidAsync(login.LoginAsync, ct));
        Assert.Equal(1, login.Count);
    }

    [Fact]
    public async Task Detach_returns_and_clears_the_sid()
    {
        using var session = new BirSession(new FakeTimeProvider(Start), Lifetime);
        var login = new CountingLogin();
        var ct = TestContext.Current.CancellationToken;
        Assert.Null(session.Detach());
        await session.GetSidAsync(login.LoginAsync, ct);

        var detached = session.Detach();

        Assert.Equal("sid-1", detached);
        Assert.Null(session.CurrentSid);
        Assert.Null(session.Detach());
        Assert.Equal("sid-2", await session.GetSidAsync(login.LoginAsync, ct));
    }

    [Fact]
    public void Data_date_and_bir121_flag_round_trip()
    {
        using var session = new BirSession(new FakeTimeProvider(Start), Lifetime);
        Assert.Null(session.DataAsOf);
        Assert.Null(session.Bir121Supported);

        session.DataAsOf = new DateOnly(2018, 10, 19);
        session.Bir121Supported = false;

        Assert.Equal(new DateOnly(2018, 10, 19), session.DataAsOf);
        Assert.False(session.Bir121Supported);

        session.Bir121Supported = true;
        session.DataAsOf = null;

        Assert.True(session.Bir121Supported);
        Assert.Null(session.DataAsOf);
    }

    [Fact]
    public async Task Data_date_is_never_read_torn_while_another_thread_writes_it()
    {
        // the session is shared by every client of the process; a DateOnly? read while it is written must be one of the written values
        using var session = new BirSession(new FakeTimeProvider(Start), Lifetime);
        var first = new DateOnly(2018, 10, 19);
        var second = new DateOnly(2026, 12, 31);
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var writer = Task.Run(
            () =>
            {
                for (var i = 0; !stop.IsCancellationRequested; i++)
                {
                    session.DataAsOf = (i % 3) switch { 0 => first, 1 => second, _ => null };
                    session.Bir121Supported = (i % 3) switch { 0 => true, 1 => false, _ => null };
                }
            },
            TestContext.Current.CancellationToken);

        var reads = 0;
        while (!stop.IsCancellationRequested)
        {
            var date = session.DataAsOf;
            Assert.True(date is null || date == first || date == second, "torn read: " + date);
            _ = session.Bir121Supported;
            reads++;
        }

        await writer;
        Assert.True(reads > 0);
    }

    [Fact]
    public async Task A_cancelled_wait_for_the_login_gate_throws()
    {
        using var session = new BirSession(new FakeTimeProvider(Start), Lifetime);
        var ct = TestContext.Current.CancellationToken;
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var holder = session.GetSidAsync(_ => release.Task, ct);   // takes the gate and keeps it
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var waiting = session.GetSidAsync(new CountingLogin().LoginAsync, cts.Token);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        release.SetResult("sid-held");
        Assert.Equal("sid-held", await holder);
    }
}
