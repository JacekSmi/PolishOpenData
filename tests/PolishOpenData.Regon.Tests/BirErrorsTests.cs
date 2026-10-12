using System;
using System.Net;
using System.Net.Http;
using System.Text;
using PolishOpenData.Bir.Wire;

namespace PolishOpenData.Bir.Tests;

public class BirErrorsTests
{
    private static BirError? FindIn(string fixture) => BirErrors.Find(BirReader.ReadRows(RegonTestResponses.Inner(fixture)));

    [Fact]
    public void Find_returns_the_first_error_row()
    {
        var rows = BirReader.ReadRows(
            "<root><dane><Regon>000331501</Regon></dane>"
            + "<dane><ErrorCode>4</ErrorCode><ErrorMessagePl>Brak.</ErrorMessagePl><ErrorMessageEn>None.</ErrorMessageEn></dane>"
            + "<dane><ErrorCode>5</ErrorCode></dane></root>");

        var error = BirErrors.Find(rows);

        Assert.NotNull(error);
        Assert.Equal(BirErrors.NotFound, error!.Code);
        Assert.Equal("Brak.", error.MessagePl);
        Assert.Equal("None.", error.MessageEn);

        var notFound = FindIn("synthetic-search-notfound");
        Assert.Equal(4, notFound!.Code);
        Assert.Equal("Nie znaleziono podmiotu dla podanych kryteriów wyszukiwania.", notFound.MessagePl);
        Assert.Equal("No data found for the specified search criteria.", notFound.MessageEn);
    }

    [Fact]
    public void Find_returns_null_without_an_error_row()
    {
        Assert.Null(FindIn("synthetic-search-batch-nips"));
        Assert.Null(BirErrors.Find(Array.Empty<BirRow>()));
        Assert.Null(BirErrors.Find(BirReader.ReadRows("<root><dane/></root>")));
    }

    [Fact]
    public void Code_21_message_comes_from_DatabaseSearchResult()
    {
        var error = FindIn("synthetic-report-error-21");

        Assert.Equal(BirErrors.NotCivilPartnership, error!.Code);
        Assert.Equal("Podmiot nie jest spółką cywilną", error.MessagePl);
        Assert.Equal("The entity is not a civil partnership", error.MessageEn);

        Assert.Equal(BirErrors.NoPartners, FindIn("synthetic-report-error-22")!.Code);
        Assert.Equal(BirErrors.NoPkdForRemovedEntity, FindIn("synthetic-report-error-11")!.Code);
        Assert.Equal(BirErrors.InvalidReportName, FindIn("synthetic-report-error-5")!.Code);
    }

    [Fact]
    public void Unparseable_error_code_is_reported_as_code_zero()
    {
        var error = BirErrors.Find(BirReader.ReadRows("<root><dane><ErrorCode>abc</ErrorCode></dane></root>"));

        Assert.Equal(0, error!.Code);
    }

    [Fact]
    public void Code_101_is_transient()
    {
        var transient = BirErrors.ToException(new BirError(BirErrors.Throttled, "Poczekaj.", "Try again in a few minutes."), BirOperation.DaneSzukajPodmioty, "snippet");
        Assert.True(transient.IsTransient);
        Assert.Equal("101", transient.ErrorCode);
        Assert.Null(transient.StatusCode);
        Assert.Equal("snippet", transient.ResponseSnippet);

        var permanent = BirErrors.ToException(new BirError(BirErrors.InvalidReportName, null, "Invalid report name."), BirOperation.DanePobierzPelnyRaport, null);
        Assert.False(permanent.IsTransient);
        Assert.Equal("5", permanent.ErrorCode);
        Assert.Null(permanent.ResponseSnippet);
    }

    [Fact]
    public void Error_message_prefers_english_then_polish()
    {
        var both = BirErrors.ToException(new BirError(5, "Polski tekst.", "English text."), BirOperation.DanePobierzPelnyRaport, null);
        Assert.StartsWith("GUS REGON reported error 5: English text.", both.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Polski", both.Message, StringComparison.Ordinal);
        Assert.Contains("DanePobierzPelnyRaport", both.Message, StringComparison.Ordinal);

        var polishOnly = BirErrors.ToException(new BirError(5, "Polski tekst.", null), BirOperation.DanePobierzPelnyRaport, null);
        Assert.StartsWith("GUS REGON reported error 5: Polski tekst.", polishOnly.Message, StringComparison.Ordinal);

        var neither = BirErrors.ToException(new BirError(7, null, null), BirOperation.GetValue, null);
        Assert.StartsWith("GUS REGON reported error 7", neither.Message, StringComparison.Ordinal);
        Assert.IsType<PolishOpenDataApiException>(neither);
    }

    [Fact]
    public void Sender_fault_is_a_non_transient_api_exception()
    {
        var fault = new SoapFaultException("s:Sender", "a:DestinationUnreachable", "The message with To 'urn:wrong-address' cannot be processed.");

        var exception = BirErrors.ForFault(fault, HttpStatusCode.InternalServerError, "snippet");

        var api = Assert.IsType<PolishOpenDataApiException>(exception);
        Assert.False(api.IsTransient);
        Assert.Equal(HttpStatusCode.InternalServerError, api.StatusCode);
        Assert.Equal("snippet", api.ResponseSnippet);
        Assert.Contains("a:DestinationUnreachable", api.Message, StringComparison.Ordinal);
        Assert.Contains("urn:wrong-address", api.Message, StringComparison.Ordinal);
        Assert.Same(fault, api.InnerException);
    }

    [Fact]
    public void Receiver_fault_is_an_http_request_exception()
    {
        var fault = new SoapFaultException("s:Receiver", null, "Server too busy.");

        var exception = BirErrors.ForFault(fault, HttpStatusCode.InternalServerError, "snippet");

        var http = Assert.IsType<HttpRequestException>(exception);
        Assert.Equal("GUS REGON answered HTTP 500 with a SOAP receiver fault (s:Receiver): Server too busy.", http.Message);
        Assert.Same(fault, http.InnerException);
#if NET
        Assert.Equal(HttpStatusCode.InternalServerError, http.StatusCode);
#endif
    }

    [Fact]
    public void Receiver_fault_with_a_success_status_names_the_fault()
    {
        var fault = new SoapFaultException("s:Receiver", "a:InternalServiceFault", "Internal error.");

        var exception = BirErrors.ForFault(fault, HttpStatusCode.OK, "snippet");

        var http = Assert.IsType<HttpRequestException>(exception);
        Assert.Contains("SOAP receiver fault (s:Receiver/a:InternalServiceFault): Internal error.", http.Message, StringComparison.Ordinal);
#if NET
        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
#endif
    }

    [Fact]
    public void Html_415_is_an_api_exception_with_a_snippet()
    {
        var body = Encoding.UTF8.GetBytes("<html><head><title>415 Cannot process the message</title></head><body>wrong content type</body></html>");

        var exception = BirErrors.ForHttpFailure((HttpStatusCode)415, "text/html; charset=utf-8", body, null);

        var api = Assert.IsType<PolishOpenDataApiException>(exception);
        Assert.Equal((HttpStatusCode)415, api.StatusCode);
        Assert.False(api.IsTransient);
        Assert.StartsWith("<html>", api.ResponseSnippet, StringComparison.Ordinal);
        Assert.Contains("415", api.Message, StringComparison.Ordinal);
        Assert.Contains("content type", api.Message, StringComparison.Ordinal);
        Assert.Contains("text/html", api.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Html_400_is_an_api_exception()
    {
        var exception = BirErrors.ForHttpFailure(HttpStatusCode.BadRequest, "text/html", Encoding.UTF8.GetBytes("<h1>Bad Request</h1>"), null);

        var api = Assert.IsType<PolishOpenDataApiException>(exception);
        Assert.Equal(HttpStatusCode.BadRequest, api.StatusCode);
        Assert.Contains("400", api.Message, StringComparison.Ordinal);
        Assert.Contains("content type", api.Message, StringComparison.Ordinal);
        Assert.Equal("<h1>Bad Request</h1>", api.ResponseSnippet);

        // other 4xx are rejections too, without the content-type hint
        var notFound = Assert.IsType<PolishOpenDataApiException>(BirErrors.ForHttpFailure(HttpStatusCode.NotFound, null, Array.Empty<byte>(), null));
        Assert.DoesNotContain("content type", notFound.Message, StringComparison.Ordinal);
        Assert.Equal(string.Empty, notFound.ResponseSnippet);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public void Server_errors_are_http_request_exceptions(int status)
    {
        var exception = BirErrors.ForHttpFailure((HttpStatusCode)status, "text/html", Encoding.UTF8.GetBytes("<html>down</html>"), null);

        var http = Assert.IsType<HttpRequestException>(exception);
        Assert.Equal("GUS REGON answered HTTP " + status + ".", http.Message);
        Assert.Null(http.InnerException);
#if NET
        Assert.Equal((HttpStatusCode)status, http.StatusCode);
#endif
    }

    [Fact]
    public void Status_429_is_quota_exceeded_with_retry_after()
    {
        var exception = BirErrors.ForHttpFailure((HttpStatusCode)429, null, Array.Empty<byte>(), TimeSpan.FromSeconds(30));

        var quota = Assert.IsType<QuotaExceededException>(exception);
        Assert.Equal((HttpStatusCode)429, quota.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(30), quota.RetryAfter);
        Assert.Null(quota.ResetsAt);
        Assert.Contains("429", quota.Message, StringComparison.Ordinal);
    }
}
