using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using PolishOpenData.Krs;
using PolishOpenData.Mcp;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Mcp.Tests;

/// <summary>
/// In-flight de-duplication in <see cref="CachedRegistries"/>. Each test resolves several <see cref="CachedRegistries"/>
/// instances from one provider, as the tools do (the class is transient), so sharing must work across instances.
/// Failures used here (KRS 400, a thrown cancellation) are ones the resilience handler does not retry.
/// </summary>
public sealed class CachedRegistriesTests
{
    private static readonly KrsNumber Orlen = KrsNumber.Parse("28860");
    private static readonly Nip OrlenNip = Nip.Parse("7740001454");

    private static HttpResponseMessage OrlenExtract() =>
        StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "krs/current-P-0000028860-orlen.json");

    private static ServiceProvider Build(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 9, 24, 8, 0, 0, TimeSpan.Zero)));
        services.AddPolishOpenDataServices(http => http.ConfigurePrimaryHttpMessageHandler(() => handler));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Concurrent_identical_calls_share_one_upstream_request()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new GatedHttpMessageHandler(_ => OrlenExtract());
        using var provider = Build(handler);
        var first = provider.GetRequiredService<CachedRegistries>();
        var second = provider.GetRequiredService<CachedRegistries>();
        Assert.NotSame(first, second);

        var a = first.GetCurrentExtractAsync(Orlen, ct);
        await handler.Entered.WaitAsync(ct);
        var b = second.GetCurrentExtractAsync(Orlen, ct);
        handler.Release();

        var resultA = await a;
        var resultB = await b;
        Assert.Equal(KrsLookupStatus.Found, resultA.Status);
        Assert.Same(resultA, resultB);
        Assert.Equal(1, handler.Requests);

        // Afterwards the value is cached as before: no further request.
        Assert.Same(resultA, await provider.GetRequiredService<CachedRegistries>().GetCurrentExtractAsync(Orlen, ct));
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task Concurrent_whitelist_searches_share_a_request_only_for_the_same_date()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new GatedHttpMessageHandler(_ => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "bialalista/search-nip-orlen.json"));
        using var provider = Build(handler);

        var a = provider.GetRequiredService<CachedRegistries>().FindByNipAsync(OrlenNip, new DateOnly(2026, 9, 24), ct);
        await handler.Entered.WaitAsync(ct);
        var b = provider.GetRequiredService<CachedRegistries>().FindByNipAsync(OrlenNip, new DateOnly(2026, 9, 24), ct);
        var otherDay = provider.GetRequiredService<CachedRegistries>().FindByNipAsync(OrlenNip, new DateOnly(2026, 9, 23), ct);
        handler.Release();

        Assert.Same(await a, await b);
        Assert.NotSame(await a, await otherDay);
        Assert.Equal(2, handler.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_failed_or_cancelled_shared_call_is_not_reused(bool upstreamCancels)
    {
        var ct = TestContext.Current.CancellationToken;
        var responses = 0;
        var handler = new GatedHttpMessageHandler(_ =>
        {
            if (Interlocked.Increment(ref responses) > 1)
            {
                return OrlenExtract();
            }

            // A cancellation that is not the caller's, such as HttpClient's own timeout.
            return upstreamCancels
                ? throw new TaskCanceledException("Simulated upstream timeout.")
                : StubHttpMessageHandler.Json(HttpStatusCode.BadRequest, "{}");
        });
        using var provider = Build(handler);

        var a = provider.GetRequiredService<CachedRegistries>().GetCurrentExtractAsync(Orlen, ct);
        await handler.Entered.WaitAsync(ct);
        var b = provider.GetRequiredService<CachedRegistries>().GetCurrentExtractAsync(Orlen, ct);
        handler.Release();

        if (upstreamCancels)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => a);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => b);
        }
        else
        {
            var errorA = await Assert.ThrowsAnyAsync<PolishOpenDataApiException>(() => a);
            var errorB = await Assert.ThrowsAnyAsync<PolishOpenDataApiException>(() => b);
            Assert.Same(errorA, errorB);   // the waiters shared the one failed call
        }

        Assert.Equal(1, handler.Requests);

        // The next request does not get the failure again: it asks the registry and succeeds.
        var retry = await provider.GetRequiredService<CachedRegistries>().GetCurrentExtractAsync(Orlen, ct);
        Assert.Equal(KrsLookupStatus.Found, retry.Status);
        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task One_caller_cancelling_does_not_cancel_the_shared_call()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new GatedHttpMessageHandler(_ => OrlenExtract());
        using var provider = Build(handler);
        using var cancelA = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var a = provider.GetRequiredService<CachedRegistries>().GetCurrentExtractAsync(Orlen, cancelA.Token);
        await handler.Entered.WaitAsync(ct);
        var b = provider.GetRequiredService<CachedRegistries>().GetCurrentExtractAsync(Orlen, ct);

        await cancelA.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => a);
        Assert.False(b.IsCompleted);

        handler.Release();
        Assert.Equal(KrsLookupStatus.Found, (await b).Status);
        Assert.Equal(1, handler.Requests);
        Assert.False(handler.UpstreamCancelled);   // the request that reached the registry was never cancelled
    }

    [Fact]
    public async Task A_result_is_cached_even_when_its_only_caller_cancelled()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new GatedHttpMessageHandler(_ => OrlenExtract());
        using var provider = Build(handler);
        using var cancelA = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var a = provider.GetRequiredService<CachedRegistries>().GetCurrentExtractAsync(Orlen, cancelA.Token);
        await handler.Entered.WaitAsync(ct);
        await cancelA.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => a);
        handler.Release();

        // Joins the call that is still running, or finds its cached result: either way, no second request.
        Assert.Equal(KrsLookupStatus.Found, (await provider.GetRequiredService<CachedRegistries>().GetCurrentExtractAsync(Orlen, ct)).Status);
        Assert.Equal(1, handler.Requests);
        Assert.False(handler.UpstreamCancelled);
    }

    [Fact]
    public async Task A_call_that_completes_or_throws_synchronously_does_not_stay_in_flight()
    {
        var calls = new InFlightCalls();
        var starts = 0;

        Task<string> Succeed() => Task.FromResult("value " + (++starts).ToString(CultureInfo.InvariantCulture));
        Assert.Equal("value 1", await calls.GetOrStart("key", Succeed));
        Assert.Equal("value 2", await calls.GetOrStart("key", Succeed));

        Task<string> Throw() => throw new InvalidOperationException("thrown before any await");
        await Assert.ThrowsAsync<InvalidOperationException>(() => calls.GetOrStart("other", Throw));
        Assert.Equal("value 3", await calls.GetOrStart("other", Succeed));
    }
}
