using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PolishOpenData.Tests.Shared;

/// <summary>One request as a <see cref="StubHttpMessageHandler"/> saw it, copied before the responder ran.</summary>
internal sealed class RecordedRequest
{
    public RecordedRequest(HttpRequestMessage request, byte[] body)
    {
        Uri = request.RequestUri!;
        Method = request.Method.Method;
        Sid = HeaderText(request.Headers, "sid");
        ContentType = request.Content?.Headers.ContentType?.ToString();
        UserAgent = HeaderText(request.Headers, "User-Agent");
        ExpectContinue = request.Headers.ExpectContinue == true;
        Body = body;
    }

    public Uri Uri { get; }

    public string Method { get; }

    /// <summary>The value of the <c>sid</c> request header, or null.</summary>
    public string? Sid { get; }

    /// <summary>For example <c>application/soap+xml; charset=utf-8; action="..."</c>; null when there was no content.</summary>
    public string? ContentType { get; }

    public string? UserAgent { get; }

    public bool ExpectContinue { get; }

    /// <summary>The request body; empty for a request without content.</summary>
    public byte[] Body { get; }

    public string BodyText => Encoding.UTF8.GetString(Body);

    private static string? HeaderText(HttpRequestHeaders headers, string name) =>
        headers.TryGetValues(name, out var values) ? string.Join(" ", values) : null;
}

/// <summary>Answers HTTP requests from a function; records what was asked. No network.</summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    public List<Uri> RequestUris { get; } = new();

    public List<string> UserAgents { get; } = new();

    /// <summary>Every request, in arrival order. A request is added before the responder runs; guard reads with <c>lock (Requests)</c> when calls are concurrent.</summary>
    public List<RecordedRequest> Requests { get; } = new();

    public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage FromFixture(HttpStatusCode status, string relativePath) =>
        Json(status, Fixture.Read(relativePath));

    public static HttpResponseMessage Empty(HttpStatusCode status) => new(status);

    /// <summary>
    /// Replays a recorded BIR answer: the status and the raw Content-Type come from <c>regon/&lt;name&gt;.headers</c>,
    /// the body from <c>regon/&lt;name&gt;.mtom</c>. Shared only because the Regon tests need it; the folder is fixed on purpose.
    /// </summary>
    public static HttpResponseMessage Mtom(string name)
    {
        var (status, contentType) = ReadHeaders(name);
        return MtomBytes(status, Fixture.ReadBytes("regon/" + name + ".mtom"), contentType);
    }

    /// <summary>Like <see cref="Mtom(string)"/> with an explicit status instead of the recorded one.</summary>
    public static HttpResponseMessage Mtom(HttpStatusCode status, string name) =>
        MtomBytes(status, Fixture.ReadBytes("regon/" + name + ".mtom"), ReadHeaders(name).ContentType);

    /// <summary>A response with the given bytes whose Content-Type is stored verbatim, so the code under test reads exactly the header string that was recorded.</summary>
    public static HttpResponseMessage MtomBytes(HttpStatusCode status, byte[] body, string contentType)
    {
        var content = new ByteArrayContent(body);
        content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        return new HttpResponseMessage(status) { Content = content };
    }

    /// <summary>An HTML error page such as the one IIS sends for an unsupported media type.</summary>
    public static HttpResponseMessage Html(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "text/html") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? Array.Empty<byte>() : await ReadBodyAsync(request.Content, cancellationToken);
        lock (RequestUris)
        {
            RequestUris.Add(request.RequestUri!);
            UserAgents.Add(request.Headers.TryGetValues("User-Agent", out var values) ? string.Join(" ", values) : string.Empty);
        }

        lock (Requests)
        {
            Requests.Add(new RecordedRequest(request, body));
        }

        return _responder(request);
    }

    // The two-line format is pinned by FixtureFramingTests: "HTTP <status>" and "Content-Type: <raw value>".
    private static (HttpStatusCode Status, string ContentType) ReadHeaders(string name)
    {
        var lines = Fixture.Read("regon/" + name + ".headers").TrimEnd('\n').Split('\n');
        var status = (HttpStatusCode)int.Parse(lines[0].Split(' ')[1], NumberStyles.None, CultureInfo.InvariantCulture);
        return (status, lines[1].Substring("Content-Type: ".Length));
    }

    private static Task<byte[]> ReadBodyAsync(HttpContent content, CancellationToken cancellationToken) =>
#if NET
        content.ReadAsByteArrayAsync(cancellationToken);
#else
        content.ReadAsByteArrayAsync();
#endif
}
