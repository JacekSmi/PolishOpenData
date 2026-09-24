using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PolishOpenData.Tests.Shared;

/// <summary>Answers HTTP requests from a function; records what was asked. No network.</summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    public List<Uri> RequestUris { get; } = new();

    public List<string> UserAgents { get; } = new();

    public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage FromFixture(HttpStatusCode status, string relativePath) =>
        Json(status, Fixture.Read(relativePath));

    public static HttpResponseMessage Empty(HttpStatusCode status) => new(status);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (RequestUris)
        {
            RequestUris.Add(request.RequestUri!);
            UserAgents.Add(request.Headers.TryGetValues("User-Agent", out var values) ? string.Join(" ", values) : string.Empty);
        }

        return Task.FromResult(_responder(request));
    }
}
