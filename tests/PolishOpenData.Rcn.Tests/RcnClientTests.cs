using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using PolishOpenData.Geo;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Rcn.Tests;

public class RcnClientTests
{
    private static readonly RcnQuery Powiat = new() { PowiatCode = "1465" };

    private static RcnQuery Warsaw(int maxRecords = 1000) => new() { PowiatCode = "1465", MaxRecords = maxRecords };

    private static HttpResponseMessage Gml(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/gml+xml") };

    // A minimal valid page of premises rows with ids first, first + 1, ...; the ids are what the tests compare.
    private static string PageXml(int rows, int first = 0, string? next = null)
    {
        var xml = new StringBuilder("<wfs:FeatureCollection xmlns:ms=\"http://mapserver.gis.umn.edu/mapserver\" xmlns:gml=\"http://www.opengis.net/gml/3.2\" xmlns:wfs=\"http://www.opengis.net/wfs/2.0\""
            + (next is null ? string.Empty : " next=\"" + next + "\"") + " numberMatched=\"unknown\" numberReturned=\"" + rows + "\">");
        for (var i = first; i < first + rows; i++)
        {
            xml.Append("<wfs:member><ms:lokale gml:id=\"lokale.").Append(i).Append("\"><gml:boundedBy><gml:Envelope srsName=\"urn:ogc:def:crs:EPSG::2180\">")
                .Append("<gml:lowerCorner>486000.5 638000.25</gml:lowerCorner><gml:upperCorner>486000.5 638000.25</gml:upperCorner></gml:Envelope></gml:boundedBy>")
                .Append("<ms:TRAN_LOKALNY_ID_IIP>id-").Append(i).Append("</ms:TRAN_LOKALNY_ID_IIP><ms:DOK_DATA><gml:timePosition>2025-12-19</gml:timePosition></ms:DOK_DATA>")
                .Append("<ms:TRAN_CENA_BRUTTO>900000</ms:TRAN_CENA_BRUTTO></ms:lokale></wfs:member>");
        }

        return xml.Append("</wfs:FeatureCollection>").ToString();
    }

    private static HttpResponseMessage Page(int rows, int first = 0, string? next = null) => Gml(HttpStatusCode.OK, PageXml(rows, first, next));

    private static Dictionary<string, string> Query(Uri uri) =>
        uri.Query.TrimStart('?').Split('&').Select(p => p.Split(new[] { '=' }, 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]), StringComparer.Ordinal);

    private static (RcnClient Client, StubHttpMessageHandler Stub) Create(Func<HttpRequestMessage, HttpResponseMessage> responder, RcnClientOptions? options = null)
    {
        var stub = new StubHttpMessageHandler(responder);
        return (new RcnClient(new HttpClient(stub), options), stub);
    }

    // Answers each request with the next response in the list.
    private static Func<HttpRequestMessage, HttpResponseMessage> Sequence(params Func<HttpResponseMessage>[] pages)
    {
        var index = 0;
        return _ => pages[index++]();
    }

    [Fact]
    public async Task Request_shape()
    {
        var (client, stub) = Create(_ => Page(1));
        var query = new RcnQuery { PowiatCode = "1465", From = new DateOnly(2026, 1, 1) };

        _ = await client.GetTransactionsAsync(query, TestContext.Current.CancellationToken);

        var request = Assert.Single(stub.Requests);
        Assert.Equal("mapy.geoportal.gov.pl", request.Uri.Host);
        Assert.Equal("/wss/service/rcn", request.Uri.AbsolutePath);
        var parameters = Query(request.Uri);
        Assert.Equal("WFS", parameters["service"]);
        Assert.Equal("2.0.0", parameters["version"]);
        Assert.Equal("GetFeature", parameters["request"]);
        Assert.Equal("ms:lokale", parameters["typeNames"]);
        Assert.Equal("urn:ogc:def:crs:EPSG::2180", parameters["srsName"]);
        Assert.Equal("500", parameters["count"]);
        Assert.Equal("0", parameters["startIndex"]);
        Assert.Equal("DOK_DATA DESC,gid", parameters["sortBy"]);
        Assert.Equal(RcnFilter.Build(query), parameters["filter"]);
        Assert.StartsWith("PolishOpenData/", request.UserAgent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pages_are_fetched_until_a_short_page()
    {
        var (client, stub) = Create(Sequence(() => Page(500), () => Page(200, 500)));

        var result = await client.GetTransactionsAsync(Warsaw(), TestContext.Current.CancellationToken);

        Assert.Equal(700, result.Items.Count);
        Assert.False(result.Truncated);
        Assert.Equal(2, stub.Requests.Count);
        Assert.Equal("500", Query(stub.Requests[1].Uri)["startIndex"]);
        Assert.Equal("500", Query(stub.Requests[1].Uri)["count"]);
    }

    [Fact]
    public async Task Truncated_when_more_rows_than_MaxRecords()
    {
        var (client, stub) = Create(Sequence(() => Page(2), () => Page(2, 2)), new RcnClientOptions { PageSize = 2 });

        var result = await client.GetTransactionsAsync(Warsaw(3), TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "id-0", "id-1", "id-2" }, result.Items.Select(t => t.Id));
        Assert.True(result.Truncated);
        Assert.Equal(new[] { "2", "2" }, stub.Requests.Select(r => Query(r.Uri)["count"]));
        Assert.Equal(new[] { "0", "2" }, stub.Requests.Select(r => Query(r.Uri)["startIndex"]));
    }

    [Fact]
    public async Task Exactly_MaxRecords_rows_is_not_truncated()
    {
        var (client, stub) = Create(Sequence(() => Page(2), () => Page(0, 2)), new RcnClientOptions { PageSize = 2 });

        var result = await client.GetTransactionsAsync(Warsaw(2), TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Items.Count);
        Assert.False(result.Truncated);
        Assert.Equal("1", Query(stub.Requests[1].Uri)["count"]);
        Assert.Equal("2", Query(stub.Requests[1].Uri)["startIndex"]);
    }

    [Fact]
    public async Task Every_request_has_count_and_startIndex()
    {
        var area = new Pl1992Bbox(new Pl1992Point(637800, 486300), new Pl1992Point(638100, 486600));
        foreach (var query in new[] { Powiat, new RcnQuery { Area = area } })
        {
            var (client, stub) = Create(_ => Page(1));

            _ = await client.GetTransactionsAsync(query, TestContext.Current.CancellationToken);

            var parameters = Query(Assert.Single(stub.Requests).Uri);
            Assert.True(parameters.ContainsKey("count"));
            Assert.True(parameters.ContainsKey("startIndex"));
        }
    }

    [Fact]
    public async Task A_next_link_is_never_requested()
    {
        var (client, stub) = Create(Sequence(() => Page(2, 0, "https://evil.example/x"), () => Page(1, 2, "https://evil.example/x")), new RcnClientOptions { PageSize = 2 });

        var result = await client.GetTransactionsAsync(Warsaw(), TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Items.Count);
        Assert.Equal(2, stub.RequestUris.Count);
        Assert.All(stub.RequestUris, uri => Assert.Equal("mapy.geoportal.gov.pl", uri.Host));
    }

    [Fact]
    public async Task Invalid_query_throws_before_any_request()
    {
        var (client, stub) = Create(_ => Page(1));

        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.GetTransactionsAsync(new RcnQuery(), TestContext.Current.CancellationToken));

        Assert.Empty(stub.Requests);
    }

    [Theory]
    [InlineData(RcnLayer.Premises, "ms:lokale")]
    [InlineData(RcnLayer.Parcels, "ms:dzialki")]
    [InlineData(RcnLayer.Buildings, "ms:budynki")]
    public async Task Layers_use_their_type_names(RcnLayer layer, string typeName)
    {
        var (client, stub) = Create(_ => Page(1));

        _ = await client.GetTransactionsAsync(new RcnQuery { PowiatCode = "1465", Layer = layer }, TestContext.Current.CancellationToken);

        Assert.Equal(typeName, Query(Assert.Single(stub.Requests).Uri)["typeNames"]);
    }

    [Fact]
    public async Task Http_400_exception_report_is_an_api_exception()
    {
        var (client, _) = Create(_ => Gml(HttpStatusCode.BadRequest, Fixture.Read("rcn/exception-unknown-property.xml")));

        var exception = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => client.GetTransactionsAsync(Powiat, TestContext.Current.CancellationToken));

        Assert.Equal("InvalidParameterValue", exception.ErrorCode);
        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Contains("Property 'NOPE' is unknown", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Http_200_exception_report_throws()
    {
        var (client, _) = Create(_ => Gml(HttpStatusCode.OK, Fixture.Read("rcn/exception-unknown-property.xml")));

        var exception = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => client.GetTransactionsAsync(Powiat, TestContext.Current.CancellationToken));

        Assert.Equal("InvalidParameterValue", exception.ErrorCode);
    }

    [Fact]
    public async Task Http_400_without_a_report_falls_back_to_the_status_error()
    {
        var (client, _) = Create(_ => StubHttpMessageHandler.Html(HttpStatusCode.BadRequest, "<html>Bad request</html>"));

        var exception = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => client.GetTransactionsAsync(Powiat, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Null(exception.ErrorCode);
        Assert.Contains("HTTP 400", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Http_400_without_a_report_reads_an_unbuffered_body_once()
    {
        var (client, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StreamContent(new OneShotStream("<html>Bad request</html>")) });

        var exception = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => client.GetTransactionsAsync(Powiat, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal("<html>Bad request</html>", exception.ResponseSnippet);
    }

    // A response body that can be read to the end once; reading it again or after disposal throws, as the network stream does on .NET Framework.
    private sealed class OneShotStream : MemoryStream
    {
        private bool _disposed;
        private bool _ended;

        public OneShotStream(string text)
            : base(Encoding.UTF8.GetBytes(text))
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_disposed || _ended)
            {
                throw new ObjectDisposedException(nameof(OneShotStream), "the body was read twice");
            }

            var read = base.Read(buffer, offset, count);
            _ended = read == 0;
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromResult(Read(buffer, offset, count));

#if NET
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var temp = new byte[buffer.Length];
            var read = Read(temp, 0, temp.Length);
            temp.AsMemory(0, read).CopyTo(buffer);
            return new ValueTask<int>(read);
        }
#endif

        protected override void Dispose(bool disposing)
        {
            _disposed = true;
            base.Dispose(disposing);
        }
    }

    [Fact]
    public async Task Http_429_is_a_quota_exception()
    {
        var (client, _) = Create(_ => StubHttpMessageHandler.Empty((HttpStatusCode)429));

        _ = await Assert.ThrowsAsync<QuotaExceededException>(() => client.GetTransactionsAsync(Powiat, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Http_503_is_an_http_request_exception()
    {
        var (client, _) = Create(_ => StubHttpMessageHandler.Empty(HttpStatusCode.ServiceUnavailable));

        _ = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetTransactionsAsync(Powiat, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Html_block_page_throws_blocked()
    {
        var (client, _) = Create(_ => StubHttpMessageHandler.Html(HttpStatusCode.OK, Fixture.Read("rcn/block-page.html")));

        var exception = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => client.GetTransactionsAsync(Powiat, TestContext.Current.CancellationToken));

        Assert.Equal("blocked", exception.ErrorCode);
        Assert.True(exception.IsTransient);
    }

    // The block page is recognised by its body too, when the content type says GML or says HTML in another case,
    // and when a byte order mark or white space comes first.
    [Theory]
    [InlineData("application/gml+xml", "<html><body>Request Rejected</body></html>", false)]
    [InlineData("application/gml+xml", "<!DOCTYPE HTML PUBLIC \"-//W3C//DTD HTML 4.01//EN\"><html></html>", true)]
    [InlineData("application/gml+xml", "  \r\n<!doctype html><html></html>", false)]
    [InlineData("Text/HTML", "not even markup", false)]
    public async Task Html_block_page_is_recognised_from_the_body_and_a_case_insensitive_content_type(string contentType, string body, bool withByteOrderMark)
    {
        var bytes = (withByteOrderMark ? new byte[] { 0xEF, 0xBB, 0xBF } : Array.Empty<byte>()).Concat(Encoding.UTF8.GetBytes(body)).ToArray();
        var (client, _) = Create(_ =>
        {
            var content = new ByteArrayContent(bytes);
            content.Headers.TryAddWithoutValidation("Content-Type", contentType);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });

        var exception = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => client.GetTransactionsAsync(Powiat, TestContext.Current.CancellationToken));

        Assert.Equal("blocked", exception.ErrorCode);
        Assert.True(exception.IsTransient);
    }

    [Fact]
    public async Task Cancellation_is_honoured()
    {
        var (client, stub) = Create(_ => Page(1));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetTransactionsAsync(Powiat, cts.Token));

        Assert.Empty(stub.Requests);
    }

    [Fact]
    public void AddRcnClient_registers_the_client_with_four_connections()
    {
        using var provider = new ServiceCollection().AddRcnClient().Services.BuildServiceProvider();

        _ = Assert.IsType<RcnClient>(provider.GetRequiredService<IRcnClient>());
        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(nameof(IRcnClient));
        while (handler is DelegatingHandler delegating)
        {
            handler = delegating.InnerHandler!;
        }

        Assert.Equal(4, Assert.IsType<HttpClientHandler>(handler).MaxConnectionsPerServer);
    }
}
