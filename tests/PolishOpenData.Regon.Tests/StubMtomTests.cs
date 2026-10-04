using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using PolishOpenData.Internal;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Bir.Tests;

public class StubMtomTests
{
    private static async Task<HttpResponseMessage> GetAsync(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        using var client = new HttpClient(new StubHttpMessageHandler(responder));
        return await client.GetAsync(new Uri("https://example.test/svc"), HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
    }

    // The raw Content-Type header, read the way the library reads it.
    private static string RawContentType(HttpResponseMessage response) =>
        response.Content.Headers.TryGetValues("Content-Type", out var values) ? string.Join(", ", values) : string.Empty;

    private static string HeadersFileContentType(string name) =>
        Fixture.Read("regon/" + name + ".headers").TrimEnd('\n').Split('\n')[1].Substring("Content-Type: ".Length);

    [Fact]
    public async Task Mtom_replays_status_content_type_and_bytes_through_httpclient()
    {
        using var ok = await GetAsync(static _ => StubHttpMessageHandler.Mtom("search-regon-gus"));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(Fixture.ReadBytes("regon/search-regon-gus.mtom"), await ok.Content.ReadBytesAsync(TestContext.Current.CancellationToken));
        Assert.Equal("multipart/related", ok.Content.Headers.ContentType!.MediaType);

        // the status comes from the .headers file (the recorded fault is HTTP 500) unless it is given explicitly
        using var fault = await GetAsync(static _ => StubHttpMessageHandler.Mtom("fault-sender-500"));
        Assert.Equal(HttpStatusCode.InternalServerError, fault.StatusCode);
        Assert.Equal(Fixture.ReadBytes("regon/fault-sender-500.mtom"), await fault.Content.ReadBytesAsync(TestContext.Current.CancellationToken));

        using var overridden = await GetAsync(static _ => StubHttpMessageHandler.Mtom(HttpStatusCode.ServiceUnavailable, "search-regon-gus"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, overridden.StatusCode);
        Assert.Equal(Fixture.ReadBytes("regon/search-regon-gus.mtom"), await overridden.Content.ReadBytesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Content_type_with_boundary_is_readable_as_a_raw_header()
    {
        // HttpClient may re-serialise the header with a space after each ';', so compare without those spaces.
        using var spaced = await GetAsync(static _ => StubHttpMessageHandler.Mtom("zaloguj-ok"));
        var raw = RawContentType(spaced);
        Assert.Contains("boundary=\"uuid:fe842bcf-8f38-44fb-9430-4dda5ed38c80+id=188161\"", raw, StringComparison.Ordinal);
        Assert.Contains("start=\"<http://tempuri.org/0>\"", raw, StringComparison.Ordinal);
        Assert.Equal(HeadersFileContentType("zaloguj-ok").Replace("; ", ";"), raw.Replace("; ", ";"));

        // the curl-style header of the earlier capture has no spaces after ';'
        using var tight = await GetAsync(static _ => StubHttpMessageHandler.Mtom("fault-sender-500"));
        Assert.Equal(HeadersFileContentType("fault-sender-500").Replace("; ", ";"), RawContentType(tight).Replace("; ", ";"));
    }

    [Fact]
    public async Task MtomBytes_and_Html_build_arbitrary_responses()
    {
        var bytes = new byte[] { 1, 2, 3 };
        using var mtom = await GetAsync(_ => StubHttpMessageHandler.MtomBytes(HttpStatusCode.Created, bytes, "multipart/related; boundary=\"b:+=\"; start=\"<x>\""));
        Assert.Equal(HttpStatusCode.Created, mtom.StatusCode);
        Assert.Equal(bytes, await mtom.Content.ReadBytesAsync(TestContext.Current.CancellationToken));
        Assert.Contains("boundary=\"b:+=\"", RawContentType(mtom), StringComparison.Ordinal);

        using var html = await GetAsync(static _ => StubHttpMessageHandler.Html(HttpStatusCode.UnsupportedMediaType, "<html><body>415</body></html>"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, html.StatusCode);
        Assert.Equal("text/html", html.Content.Headers.ContentType!.MediaType);
        Assert.Equal("utf-8", html.Content.Headers.ContentType.CharSet);
        Assert.Equal("<html><body>415</body></html>", await html.Content.ReadStringAsync(TestContext.Current.CancellationToken));
    }
}
