using System;
using System.Net;
using System.Net.Http;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;
using PolishOpenData.Mcp;

namespace PolishOpenData.Mcp.Tests;

public class ToolErrorsTests
{
    [Fact]
    public void Quota_message_includes_reset_time()
    {
        var text = ToolErrors.Describe(new QuotaExceededException(
            "Biała Lista daily request limit for this IP address is exhausted (WL-191).",
            (HttpStatusCode)429,
            "WL-191",
            new DateTimeOffset(2026, 9, 24, 22, 0, 0, TimeSpan.Zero),
            null));
        Assert.NotNull(text);
        Assert.Contains("WL-191", text, StringComparison.Ordinal);
        Assert.Contains("2026-09-24 22:00 UTC", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Transient_and_other_api_errors()
    {
        Assert.Contains("Try again in a minute", ToolErrors.Describe(new PolishOpenDataApiException("x", HttpStatusCode.BadRequest, "WL-195", null, isTransient: true)), StringComparison.Ordinal);
        Assert.Contains("rejected the request", ToolErrors.Describe(new PolishOpenDataApiException("bad date", HttpStatusCode.BadRequest, "WL-103", null)), StringComparison.Ordinal);
    }

    [Fact]
    public void Transport_errors_and_cancellation()
    {
        Assert.Contains("unavailable", ToolErrors.Describe(new HttpRequestException("503")), StringComparison.Ordinal);
        Assert.Null(ToolErrors.Describe(new OperationCanceledException()));
    }

    [Theory]
    [MemberData(nameof(ResilienceRejections))]
    public void Resilience_rejections_are_reported_like_transport_errors(ExecutionRejectedException rejection)
    {
        Assert.Contains("unavailable", ToolErrors.Describe(rejection), StringComparison.Ordinal);
    }

    public static TheoryData<ExecutionRejectedException> ResilienceRejections() => new()
    {
        new TimeoutRejectedException("The operation didn't complete within the allowed timeout."),
        new BrokenCircuitException("The circuit is now open and is not allowing calls."),
    };
}
