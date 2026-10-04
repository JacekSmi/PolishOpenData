using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PolishOpenData.Bir.Wire;
using PolishOpenData.Internal;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Bir.Tests;

public class BirTransportTests
{
    private const string DummySid = "0123456789abcdefghij";
    private const string DummyKey = "dummy-key-not-a-secret";

    private static readonly Uri Endpoint = new("https://wyszukiwarkaregontest.stat.gov.pl/wsBIR/UslugaBIRzewnPubl.svc");

    private static (BirTransport Transport, StubHttpMessageHandler Stub) Create(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var stub = new StubHttpMessageHandler(responder);
        return (new BirTransport(new HttpClient(stub)), stub);
    }

    private static BirRequest SearchRequest() => BirRequest.Search(Endpoint, BirSearchField.Regon, "000331501");

    [Fact]
    public async Task Posts_soap_with_sid_action_and_user_agent()
    {
        var acceptHeaders = -1;
        var (transport, stub) = Create(request =>
        {
            acceptHeaders = request.Headers.Accept.Count;
            return StubHttpMessageHandler.Mtom("search-regon-gus");
        });
        var birRequest = SearchRequest();

        var result = await transport.SendAsync(birRequest, DummySid, TestContext.Current.CancellationToken);

        Assert.StartsWith("<root>", result, StringComparison.Ordinal);
        var recorded = Assert.Single(stub.Requests);
        Assert.Equal("POST", recorded.Method);
        Assert.Equal(Endpoint, recorded.Uri);
        Assert.Equal(DummySid, recorded.Sid);
        Assert.Equal("application/soap+xml; charset=utf-8; action=\"" + birRequest.Action + "\"", recorded.ContentType);
        Assert.Equal("http://CIS/BIR/PUBL/2014/07/IUslugaBIRzewnPubl/DaneSzukajPodmioty", birRequest.Action);
        Assert.StartsWith("PolishOpenData/", recorded.UserAgent, StringComparison.Ordinal);
        Assert.Equal(UserAgent.Value, recorded.UserAgent);
        Assert.False(recorded.ExpectContinue);
        Assert.Equal(0, acceptHeaders);   // no Accept header: it has no effect on this service
        Assert.Equal(birRequest.Body, recorded.Body);
        Assert.DoesNotContain(DummySid, recorded.BodyText, StringComparison.Ordinal);   // the sid travels in the header only
    }

    [Fact]
    public async Task Login_request_carries_no_sid_header()
    {
        var (transport, stub) = Create(static _ => StubHttpMessageHandler.Mtom("zaloguj-ok"));

        var result = await transport.SendAsync(BirRequest.Login(Endpoint, DummyKey), null, TestContext.Current.CancellationToken);

        var recorded = Assert.Single(stub.Requests);
        Assert.Null(recorded.Sid);
        // the key is in the envelope body and nowhere else
        Assert.Contains("<ns:pKluczUzytkownika>" + DummyKey + "</ns:pKluczUzytkownika>", recorded.BodyText, StringComparison.Ordinal);
        Assert.DoesNotContain(DummyKey, recorded.Uri.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(DummyKey, recorded.ContentType, StringComparison.Ordinal);
        Assert.DoesNotContain(DummyKey, recorded.UserAgent, StringComparison.Ordinal);
        Assert.Equal(DummySid, result);
    }

    [Fact]
    public async Task Returns_the_result_text_of_the_recorded_login()
    {
        var (transport, _) = Create(static _ => StubHttpMessageHandler.Mtom("zaloguj-ok"));

        Assert.Equal("0123456789abcdefghij", await transport.SendAsync(BirRequest.Login(Endpoint, DummyKey), null, TestContext.Current.CancellationToken));

        var (logout, _) = Create(static _ => StubHttpMessageHandler.Mtom("wyloguj-true"));
        Assert.Equal("true", await logout.SendAsync(BirRequest.Logout(Endpoint, DummySid), DummySid, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Empty_result_returns_an_empty_string()
    {
        var (search, _) = Create(static _ => StubHttpMessageHandler.Mtom("search-empty"));
        Assert.Equal(string.Empty, await search.SendAsync(SearchRequest(), DummySid, TestContext.Current.CancellationToken));

        // GetValue answers in the other namespace; the transport asks for the right one
        var (getValue, _) = Create(static _ => StubHttpMessageHandler.Mtom("getvalue-empty"));
        Assert.Equal(string.Empty, await getValue.SendAsync(BirRequest.GetValue(Endpoint, "StanDanych"), DummySid, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Plain_soap_response_is_accepted()
    {
        var envelope = "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body><DaneSzukajPodmiotyResponse xmlns=\"http://CIS/BIR/PUBL/2014/07\"><DaneSzukajPodmiotyResult>&lt;root /&gt;</DaneSzukajPodmiotyResult></DaneSzukajPodmiotyResponse></s:Body></s:Envelope>";
        var (transport, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(envelope, Encoding.UTF8, "application/soap+xml") });

        var result = await transport.SendAsync(SearchRequest(), DummySid, TestContext.Current.CancellationToken);

        Assert.Equal("<root />", result);
    }

    [Fact]
    public async Task Unreadable_body_is_an_api_exception_with_a_snippet()
    {
        var (transport, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("this is not xml at all", Encoding.UTF8, "text/plain") });

        var exception = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => transport.SendAsync(SearchRequest(), DummySid, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        Assert.Equal("this is not xml at all", exception.ResponseSnippet);
        Assert.Contains("could not be read", exception.Message, StringComparison.Ordinal);
        Assert.NotNull(exception.InnerException);

        // a multipart answer without a boundary parameter is unreadable too
        var (noBoundary, _) = Create(_ => StubHttpMessageHandler.MtomBytes(HttpStatusCode.OK, Encoding.UTF8.GetBytes("--b\r\n\r\n<a/>\r\n--b--"), "multipart/related; type=\"application/xop+xml\""));
        var second = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => noBoundary.SendAsync(SearchRequest(), DummySid, TestContext.Current.CancellationToken));
        Assert.IsType<FormatException>(second.InnerException);
    }

    [Fact]
    public async Task Missing_result_element_is_an_unreadable_response()
    {
        // a well-formed answer for another operation: the Wyloguj answer to a search
        var (transport, _) = Create(static _ => StubHttpMessageHandler.Mtom("wyloguj-true"));

        var exception = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => transport.SendAsync(SearchRequest(), DummySid, TestContext.Current.CancellationToken));

        Assert.Contains("no result element", exception.Message, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        Assert.StartsWith("\r\n--uuid:", exception.ResponseSnippet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Oversized_content_length_is_refused()
    {
        var (transport, _) = Create(_ =>
        {
            var content = new ByteArrayContent(new byte[10]);
            content.Headers.ContentLength = BirTransport.MaxResponseBytes + 1L;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });

        var exception = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => transport.SendAsync(SearchRequest(), DummySid, TestContext.Current.CancellationToken));

        Assert.Contains("67108865", exception.Message, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("tab\there")]
    [InlineData("line\r\nbreak: injected")]
    [InlineData("zażółć")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123456789")]
    public async Task Invalid_sid_is_rejected_before_sending(string sid)
    {
        var (transport, stub) = Create(static _ => StubHttpMessageHandler.Mtom("search-regon-gus"));

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => transport.SendAsync(SearchRequest(), sid, TestContext.Current.CancellationToken));

        Assert.Equal("sid", exception.ParamName);
        Assert.Empty(stub.Requests);
        if (sid.Length > 0)
        {
            Assert.DoesNotContain(sid, exception.Message, StringComparison.Ordinal);   // a sid is a credential: never echoed
        }
    }

    [Fact]
    public async Task Recorded_sender_fault_maps_to_an_api_exception()
    {
        var (transport, _) = Create(static _ => StubHttpMessageHandler.Mtom("fault-sender-500"));

        var exception = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => transport.SendAsync(SearchRequest(), DummySid, TestContext.Current.CancellationToken));

        Assert.False(exception.IsTransient);
        Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
        Assert.Contains("a:DestinationUnreachable", exception.Message, StringComparison.Ordinal);
        Assert.Contains("urn:wrong-address", exception.Message, StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(exception.ResponseSnippet));
        Assert.IsType<SoapFaultException>(exception.InnerException);
    }

    [Fact]
    public async Task Receiver_fault_and_gateway_errors_map_to_http_request_exceptions()
    {
        var receiver = "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body><s:Fault><s:Code><s:Value>s:Receiver</s:Value></s:Code><s:Reason><s:Text xml:lang=\"en\">busy</s:Text></s:Reason></s:Fault></s:Body></s:Envelope>";
        var (faulted, _) = Create(_ => RegonTestResponses.Response(HttpStatusCode.InternalServerError, RegonTestResponses.MtomEnvelope(receiver)));
        var first = await Assert.ThrowsAsync<HttpRequestException>(() => faulted.SendAsync(SearchRequest(), DummySid, TestContext.Current.CancellationToken));
        Assert.Equal("GUS REGON answered HTTP 500.", first.Message);

        var (unavailable, _) = Create(_ => StubHttpMessageHandler.Html(HttpStatusCode.ServiceUnavailable, "<html>down</html>"));
        var second = await Assert.ThrowsAsync<HttpRequestException>(() => unavailable.SendAsync(SearchRequest(), DummySid, TestContext.Current.CancellationToken));
        Assert.Equal("GUS REGON answered HTTP 503.", second.Message);

        // an HTTP 500 whose body is not SOAP at all is a server failure too
        var (broken, _) = Create(_ => StubHttpMessageHandler.Html(HttpStatusCode.InternalServerError, "<html>oops</html>"));
        var third = await Assert.ThrowsAsync<HttpRequestException>(() => broken.SendAsync(SearchRequest(), DummySid, TestContext.Current.CancellationToken));
        Assert.Equal("GUS REGON answered HTTP 500.", third.Message);
    }

    [Fact]
    public async Task Html_415_page_maps_to_an_api_exception()
    {
        var page = "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Strict//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-strict.dtd\"><html><head><title>415 Cannot process the message because the content type was not the expected type</title></head><body>text/xml</body></html>";
        var (transport, _) = Create(_ => StubHttpMessageHandler.Html((HttpStatusCode)415, page));

        var exception = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => transport.SendAsync(SearchRequest(), DummySid, TestContext.Current.CancellationToken));

        Assert.Equal((HttpStatusCode)415, exception.StatusCode);
        Assert.False(exception.IsTransient);
        Assert.Contains("content type", exception.Message, StringComparison.Ordinal);
        Assert.StartsWith("<!DOCTYPE html", exception.ResponseSnippet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_429_maps_to_quota_exceeded()
    {
        var (transport, _) = Create(_ =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)429) { Content = new StringContent("slow down") };
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            return response;
        });

        var exception = await Assert.ThrowsAsync<QuotaExceededException>(() => transport.SendAsync(SearchRequest(), DummySid, TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromSeconds(7), exception.RetryAfter);
    }

    [Fact]
    public async Task Transport_failure_propagates_as_http_request_exception()
    {
        var failure = new HttpRequestException("connection reset");
        var (transport, _) = Create(_ => throw failure);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => transport.SendAsync(SearchRequest(), DummySid, TestContext.Current.CancellationToken));

        Assert.Same(failure, exception);
    }

    [Fact]
    public async Task Cancellation_is_honoured()
    {
        var (transport, stub) = Create(static _ => StubHttpMessageHandler.Mtom("search-regon-gus"));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transport.SendAsync(SearchRequest(), DummySid, cancelled.Token));

        Assert.Empty(stub.Requests);   // nothing was sent after the token was cancelled
    }

    [Theory]
    [InlineData("0123456789abcdefghij", true)]
    [InlineData("ABCDEFGHIJKLMNOPQRST", true)]
    [InlineData("a", true)]
    [InlineData("under_score-and-hyphen", true)]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123", true)]   // 64 characters
    [InlineData("01234567890123456789012345678901234567890123456789012345678901234", false)]   // 65
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("a b", false)]
    [InlineData("a;b", false)]
    [InlineData("a\nb", false)]
    [InlineData("żółć", false)]
    public void IsValidSid_accepts_and_rejects(string? sid, bool expected)
    {
        Assert.Equal(expected, BirTransport.IsValidSid(sid));
    }

    [Fact]
    public void Response_size_cap_is_64_mebibytes()
    {
        Assert.Equal(64 * 1024 * 1024, BirTransport.MaxResponseBytes);
        Assert.Equal("67108864", BirTransport.MaxResponseBytes.ToString(CultureInfo.InvariantCulture));
    }
}
