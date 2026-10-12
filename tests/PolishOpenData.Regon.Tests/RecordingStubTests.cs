using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Bir.Tests;

public class RecordingStubTests
{
    private static StubHttpMessageHandler Stub() => new(static _ => new HttpResponseMessage(HttpStatusCode.OK));

    [Fact]
    public async Task Stub_records_method_headers_and_body_of_a_post()
    {
        var stub = Stub();
        using var client = new HttpClient(stub);
        var body = Encoding.UTF8.GetBytes("<a>zażółć</a>");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/svc");
        var content = new ByteArrayContent(body);
        var mediaType = new MediaTypeHeaderValue("application/soap+xml") { CharSet = "utf-8" };
        mediaType.Parameters.Add(new NameValueHeaderValue("action", "\"urn:test/Op\""));
        content.Headers.ContentType = mediaType;
        request.Content = content;
        request.Headers.ExpectContinue = false;
        request.Headers.TryAddWithoutValidation("sid", "0123456789abcdefghij");
        request.Headers.TryAddWithoutValidation("User-Agent", "PolishOpenData/9.9 (+https://example.test)");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);

        var recorded = Assert.Single(stub.Requests);
        Assert.Equal("POST", recorded.Method);
        Assert.Equal(new Uri("https://example.test/svc"), recorded.Uri);
        Assert.Equal("0123456789abcdefghij", recorded.Sid);
        Assert.Equal("application/soap+xml; charset=utf-8; action=\"urn:test/Op\"", recorded.ContentType);
        Assert.Equal("PolishOpenData/9.9 (+https://example.test)", recorded.UserAgent);
        Assert.False(recorded.ExpectContinue);
        Assert.Equal(body, recorded.Body);
        Assert.Equal("<a>zażółć</a>", recorded.BodyText);
    }

    [Fact]
    public async Task Stub_records_a_get_without_a_body()
    {
        var stub = Stub();
        using var client = new HttpClient(stub);

        using var response = await client.GetAsync(new Uri("https://example.test/a?x=1"), TestContext.Current.CancellationToken);

        var recorded = Assert.Single(stub.Requests);
        Assert.Equal("GET", recorded.Method);
        Assert.Equal(new Uri("https://example.test/a?x=1"), recorded.Uri);
        Assert.Empty(recorded.Body);
        Assert.Equal(string.Empty, recorded.BodyText);
        Assert.Null(recorded.Sid);
        Assert.Null(recorded.ContentType);
        Assert.Null(recorded.UserAgent);
        Assert.False(recorded.ExpectContinue);
    }

    [Fact]
    public async Task Stub_still_records_uris_and_user_agents()
    {
        var stub = Stub();
        using var client = new HttpClient(stub);
        using var first = new HttpRequestMessage(HttpMethod.Get, "https://example.test/one");
        first.Headers.TryAddWithoutValidation("User-Agent", "Agent/1");
        using var second = new HttpRequestMessage(HttpMethod.Get, "https://example.test/two");

        using var firstResponse = await client.SendAsync(first, TestContext.Current.CancellationToken);
        using var secondResponse = await client.SendAsync(second, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { new Uri("https://example.test/one"), new Uri("https://example.test/two") }, stub.RequestUris);
        Assert.Equal(new[] { "Agent/1", string.Empty }, stub.UserAgents);
    }
}
