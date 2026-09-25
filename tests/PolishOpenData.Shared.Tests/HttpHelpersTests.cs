using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using PolishOpenData.Internal;

namespace PolishOpenData.Shared.Tests;

public class HttpHelpersTests
{
    [Fact]
    public void User_agent_names_the_project()
    {
        Assert.StartsWith("PolishOpenData/", UserAgent.Value, StringComparison.Ordinal);
        Assert.EndsWith("(+https://github.com/JacekSmi/PolishOpenData)", UserAgent.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("+", UserAgent.Value.Split(' ')[0], StringComparison.Ordinal);   // no "+commit" metadata
    }

    [Fact]
    public void Get_request_carries_user_agent_and_accept()
    {
        using var request = HttpHelpers.CreateGet(new Uri("https://example.test/x"));
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(UserAgent.Value, string.Join(" ", request.Headers.GetValues("User-Agent")));
        Assert.Equal("application/json", request.Headers.GetValues("Accept").Single());
    }

    [Fact]
    public void Truncates_long_text()
    {
        Assert.Null(HttpHelpers.Truncate(null));
        Assert.Equal("abc", HttpHelpers.Truncate("abc"));
        Assert.Equal(512, HttpHelpers.Truncate(new string('x', 2000))!.Length);
    }

    [Fact]
    public void Snippet_is_the_start_of_the_utf8_body_without_a_byte_order_mark()
    {
        Assert.Equal(string.Empty, HttpHelpers.Snippet([]));
        Assert.Equal("{\"a\":1}", HttpHelpers.Snippet(Encoding.UTF8.GetBytes("\uFEFF{\"a\":1}")));
        Assert.Equal(new string('\u017C', 512), HttpHelpers.Snippet(Encoding.UTF8.GetBytes(new string('\u017C', 2000))));   // 2 bytes each
        Assert.Equal(new string('\u4E2D', 512), HttpHelpers.Snippet(Encoding.UTF8.GetBytes(new string('\u4E2D', 600))));    // 3 bytes each
        Assert.Equal("abc", HttpHelpers.Snippet(Encoding.UTF8.GetBytes("abcdef"), 3));
    }

    [Fact]
    public void Reads_retry_after_delta()
    {
        using var response = new HttpResponseMessage((HttpStatusCode)429);
        Assert.Null(HttpHelpers.GetRetryAfter(response));
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
        Assert.Equal(TimeSpan.FromSeconds(120), HttpHelpers.GetRetryAfter(response));
    }
}
