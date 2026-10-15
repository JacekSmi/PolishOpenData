using System;
using System.IO;
using System.Net;
using System.Net.Http;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;
using PolishOpenData.BialaLista.Offline;
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
    public void Daily_limit_error_points_to_offline_tool()
    {
        var quota = new QuotaExceededException("Biała Lista daily request limit for this IP address is exhausted (WL-191).", (HttpStatusCode)429, "WL-191", null, null);
        foreach (var tool in new[] { "check_vat_bank_account", "lookup_company" })
        {
            Assert.EndsWith("Use check_vat_bank_account_offline, which has no request limit.", ToolErrors.Describe(quota, tool), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("get_krs_extract")]
    public void Krs_rate_limit_does_not_point_to_the_vat_tool(string? tool)
    {
        // KrsFailure (lookup_company) passes no tool name.
        var text = ToolErrors.Describe(new QuotaExceededException("KRS rate limit reached (HTTP 429).", HttpStatusCode.TooManyRequests, null, null, null), tool);

        Assert.Equal("Request limit reached: KRS rate limit reached (HTTP 429).", text);
    }

    [Fact]
    public void Not_published_yet_says_when_the_file_usually_appears()
    {
        var text = ToolErrors.Describe(new VatWhitelistDataUnavailableException("x", new DateOnly(2026, 10, 14), VatWhitelistDataUnavailableReason.NotPublishedYet));
        Assert.Equal("MF has not published the file for 2026-10-14 yet; it usually appears on the evening before.", text);
    }

    [Theory]
    [InlineData(VatWhitelistDataUnavailableReason.OutsideWindow)]
    [InlineData(VatWhitelistDataUnavailableReason.DownloadFailed)]
    [InlineData(VatWhitelistDataUnavailableReason.ChecksumMismatch)]
    [InlineData(VatWhitelistDataUnavailableReason.UnexpectedFormat)]
    public void Other_unavailable_reasons_carry_the_library_message(VatWhitelistDataUnavailableReason reason)
    {
        var text = ToolErrors.Describe(new VatWhitelistDataUnavailableException("library message", new DateOnly(2026, 10, 14), reason));
        Assert.Contains("library message", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Lock_timeout_says_another_process_is_preparing_the_date()
    {
        var text = ToolErrors.Describe(new TimeoutException("Another process held the lock file 'x' for longer than 00:15:00."));
        Assert.Equal("Another process is preparing this date; try again later.", text);
    }

    [Fact]
    public void Offline_io_error_names_the_folder_and_the_environment_variable()
    {
        var text = ToolErrors.Describe(new IOException("The data directory 'C:\\data' cannot be used: access denied"), "check_vat_bank_account_offline");
        Assert.NotNull(text);
        Assert.Contains("C:\\data", text, StringComparison.Ordinal);
        Assert.Contains("POLISHOPENDATA_OFFLINE_DIR", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Io_error_of_another_tool_does_not_mention_the_offline_folder()
    {
        var text = ToolErrors.Describe(new IOException("connection reset"), "lookup_company");
        Assert.Equal("Unexpected error: connection reset", text);
    }

    [Fact]
    public void Transient_and_other_api_errors()
    {
        Assert.Contains("Try again in a minute", ToolErrors.Describe(new PolishOpenDataApiException("x", HttpStatusCode.BadRequest, "WL-195", null, isTransient: true)), StringComparison.Ordinal);
        Assert.Contains("rejected the request", ToolErrors.Describe(new PolishOpenDataApiException("bad date", HttpStatusCode.BadRequest, "WL-103", null)), StringComparison.Ordinal);
    }

    [Fact]
    public void Block_page_says_the_service_blocked_the_request()
    {
        var text = ToolErrors.Describe(new PolishOpenDataApiException("blocked", HttpStatusCode.OK, "blocked", "<html>", isTransient: true));

        Assert.Contains("blocked the request", text, StringComparison.Ordinal);
        Assert.DoesNotContain("updating its data", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("find_parcel")]
    [InlineData("geocode_address")]
    public void Gugik_rate_limit_says_to_wait_and_does_not_point_to_the_vat_tools(string tool)
    {
        var text = ToolErrors.Describe(new QuotaExceededException("ULDK rate limit reached (HTTP 429).", HttpStatusCode.TooManyRequests, null, null, TimeSpan.FromSeconds(30)), tool);

        Assert.NotNull(text);
        Assert.DoesNotContain("check_vat_bank_account", text, StringComparison.Ordinal);
        Assert.Contains("Wait", text, StringComparison.Ordinal);
        Assert.Contains("retry after 30 s", text, StringComparison.Ordinal);
        Assert.DoesNotContain("..", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("KRS returned a response that could not be read (HTTP 200): 'x' is invalid. Path: $.odpis.")]
    [InlineData("KRS returned a response without an extract.")]
    public void An_unreadable_success_response_is_not_called_a_rejection(string message)
    {
        // Built by the libraries for a 200 whose body is malformed or has no usable content.
        var text = ToolErrors.Describe(new PolishOpenDataApiException(message, HttpStatusCode.OK, null, "<html>"));
        Assert.NotNull(text);
        Assert.StartsWith("The registry answered, but its response could not be used: ", text, StringComparison.Ordinal);
        Assert.EndsWith(message, text, StringComparison.Ordinal);
        Assert.DoesNotContain("rejected", text, StringComparison.Ordinal);
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
