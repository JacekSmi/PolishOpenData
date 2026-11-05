namespace PolishOpenData.Demo.Http;

/// <summary>
/// Removes the <c>User-Agent</c> header that the libraries add to every request. Chromium ignores it, but Firefox may send it,
/// which turns a simple GET into a preflighted one, and GUGiK's preflight answers carry no <c>Access-Control-Allow-Headers</c>.
/// </summary>
internal sealed class StripUserAgentHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Remove("User-Agent");
        return base.SendAsync(request, cancellationToken);
    }
}
