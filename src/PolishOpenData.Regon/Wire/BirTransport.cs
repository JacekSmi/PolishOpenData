using System;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using PolishOpenData.Internal;

namespace PolishOpenData.Bir.Wire;

/// <summary>One HTTP POST per BIR call: framing of the request, size cap, parsing of the MTOM answer and the mapping of failures.</summary>
internal sealed class BirTransport
{
    public const int MaxResponseBytes = 64 * 1024 * 1024;

    private readonly HttpClient _httpClient;

    public BirTransport(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
    }

    /// <summary>A sid is 1 to 64 ASCII letters, digits, '_' or '-': nothing that could break out of a header value.</summary>
    public static bool IsValidSid(string? sid)
    {
        if (sid is null || sid.Length == 0 || sid.Length > 64)
        {
            return false;
        }

        foreach (var c in sid)
        {
            var valid = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
            if (!valid)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Sends the request and returns the text of the result element of the answer: "" for an empty element (a dead session or nothing found),
    /// never null (a missing result element is an error). The key and the sid are never part of an exception message.
    /// </summary>
    public async Task<string?> SendAsync(BirRequest request, string? sid, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (sid is not null && !IsValidSid(sid))
        {
            throw new ArgumentException("The session id is not a valid sid header value.", nameof(sid));
        }

        // HttpClient does not look at the token before it sends, and the stub handlers of the tests ignore it: check here.
        cancellationToken.ThrowIfCancellationRequested();
        using var message = CreateMessage(request, sid);
        using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        var length = response.Content.Headers.ContentLength;
        if (length > MaxResponseBytes)
        {
            throw new PolishOpenDataApiException(
                "GUS REGON announced a response of " + length.Value.ToString(CultureInfo.InvariantCulture) + " bytes, more than the "
                + MaxResponseBytes.ToString(CultureInfo.InvariantCulture) + " bytes the client accepts.",
                response.StatusCode,
                null,
                null);
        }

        var body = await response.Content.ReadBytesAsync(cancellationToken).ConfigureAwait(false);
        if (body.Length > MaxResponseBytes)
        {
            throw new PolishOpenDataApiException(
                "GUS REGON sent a response of more than " + MaxResponseBytes.ToString(CultureInfo.InvariantCulture) + " bytes, which the client does not accept.",
                response.StatusCode,
                null,
                HttpHelpers.Snippet(body));
        }

        var contentType = response.Content.Headers.TryGetValues("Content-Type", out var values)
            ? string.Join(", ", values)
            : response.Content.Headers.ContentType?.ToString();
        string? result;
        try
        {
            var package = MimeResponse.Parse(body, contentType);
            result = BirReader.ReadResult(package, BirOperations.ResultElement(request.Operation), BirOperations.ResultNamespace(request.Operation));
        }
        catch (SoapFaultException fault)
        {
            throw BirErrors.ForFault(fault, response.StatusCode, HttpHelpers.Snippet(body));
        }
        catch (Exception exception) when (exception is FormatException or XmlException or NotSupportedException or ArgumentException)
        {
            // not a SOAP answer: an IIS error page, a broken multipart body, an xop:Include, a charset nobody knows
            if (!response.IsSuccessStatusCode)
            {
                throw BirErrors.ForHttpFailure(response.StatusCode, contentType, body, HttpHelpers.GetRetryAfter(response));
            }

            throw HttpHelpers.UnreadableResponse(
                "GUS REGON returned a response that could not be read (HTTP " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + "): " + exception.Message,
                response,
                body,
                exception);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw BirErrors.ForHttpFailure(response.StatusCode, contentType, body, HttpHelpers.GetRetryAfter(response));
        }

        return result ?? throw HttpHelpers.UnreadableResponse("GUS REGON returned a response with no result element.", response, body);
    }

    // Content-Type is built like the prototype that talked to the live service (dotnet-approach.md section 3.1):
    // application/soap+xml; charset=utf-8; action="<wsa:Action>".
    // No Accept header (it has no effect), no Expect: 100-continue. The sid goes in the "sid" header only; the key only in the Zaloguj body.
    private static HttpRequestMessage CreateMessage(BirRequest request, string? sid)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, request.Endpoint);
        message.Headers.TryAddWithoutValidation("User-Agent", UserAgent.Value);
        if (sid is not null)
        {
            message.Headers.TryAddWithoutValidation("sid", sid);
        }

        message.Headers.ExpectContinue = false;
        var content = new ByteArrayContent(request.Body);
        var mediaType = new MediaTypeHeaderValue("application/soap+xml") { CharSet = "utf-8" };
        mediaType.Parameters.Add(new NameValueHeaderValue("action", "\"" + request.Action + "\""));
        content.Headers.ContentType = mediaType;
        message.Content = content;
        return message;
    }
}
