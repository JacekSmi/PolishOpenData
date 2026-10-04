using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using PolishOpenData.Internal;

namespace PolishOpenData.Bir.Wire;

/// <summary>An in-band error row of a BIR answer (HTTP 200 with an <c>ErrorCode</c> element).</summary>
internal sealed class BirError
{
    public BirError(int code, string? messagePl, string? messageEn)
    {
        Code = code;
        MessagePl = messagePl;
        MessageEn = messageEn;
    }

    public int Code { get; }

    public string? MessagePl { get; }

    public string? MessageEn { get; }
}

/// <summary>Maps BIR error rows, SOAP faults and failed HTTP responses to the exceptions of the library (api-facts.md sections 8.1 and 8.3).</summary>
internal static class BirErrors
{
    public const int NotFound = 4;
    public const int InvalidReportName = 5;
    public const int NoPkdForRemovedEntity = 11;
    public const int NotCivilPartnership = 21;
    public const int NoPartners = 22;
    public const int Throttled = 101;

    /// <summary>
    /// The first row that has an <c>ErrorCode</c>. The messages are <c>ErrorMessagePl</c>/<c>ErrorMessageEn</c>; the civil-partnership
    /// answer (code 21) carries its text in <c>DatabaseSearchResultPl</c>/<c>DatabaseSearchResultEn</c> instead. A code that is not a number is reported as 0.
    /// </summary>
    public static BirError? Find(IReadOnlyList<BirRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        foreach (var row in rows)
        {
            var text = row["ErrorCode"];
            if (text is null)
            {
                continue;
            }

            var code = int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
            return new BirError(code, row["ErrorMessagePl"] ?? row["DatabaseSearchResultPl"], row["ErrorMessageEn"] ?? row["DatabaseSearchResultEn"]);
        }

        return null;
    }

    /// <summary>An error row other than "not found": the code, the English message (else the Polish one) and, for code 101, <c>IsTransient</c>.</summary>
    public static PolishOpenDataApiException ToException(BirError error, BirOperation operation, string? snippet)
    {
        ArgumentNullException.ThrowIfNull(error);
        var code = error.Code.ToString(CultureInfo.InvariantCulture);
        var text = error.MessageEn ?? error.MessagePl;
        return new PolishOpenDataApiException(
            "GUS REGON reported error " + code + (text is null ? string.Empty : ": " + text) + " (" + BirOperations.Name(operation) + ").",
            null,
            code,
            snippet,
            isTransient: error.Code == Throttled);
    }

    /// <summary>
    /// A SOAP fault. A sender fault is deterministic (the request or the configured address is wrong), so it is not transient and not an
    /// <see cref="HttpRequestException"/>; a receiver fault is the server's problem and is.
    /// </summary>
    public static Exception ForFault(SoapFaultException fault, HttpStatusCode status, string snippet)
    {
        ArgumentNullException.ThrowIfNull(fault);
        if (!fault.IsSender)
        {
            // the code and reason are GUS's own text about its failure; a request's key or sid is never part of them
            var code = fault.Code + (fault.Subcode is null ? string.Empty : "/" + fault.Subcode);
            return ServerFailure(status, " with a SOAP receiver fault (" + code + ")" + (fault.Reason.Length == 0 ? string.Empty : ": " + fault.Reason), fault);
        }

        var subcode = fault.Subcode is null ? string.Empty : " (" + fault.Subcode + ")";
        return new PolishOpenDataApiException(
            "GUS REGON rejected the request with a SOAP sender fault" + subcode + ": " + fault.Reason + " The request is malformed or BaseAddress is wrong.",
            status,
            null,
            snippet,
            fault);
    }

    /// <summary>
    /// A failed HTTP response: 429 is a quota error, other 5xx a server failure, everything else a rejected request.
    /// <paramref name="attachSnippet"/> is false when the body parsed as a BIR answer: such a body can be a login answer that
    /// holds the session id, so only a body that did not parse (an HTML error page and the like) is quoted.
    /// </summary>
    public static Exception ForHttpFailure(HttpStatusCode status, string? contentType, byte[] body, TimeSpan? retryAfter, bool attachSnippet = true)
    {
        ArgumentNullException.ThrowIfNull(body);
        var number = (int)status;
        if (number == 429)
        {
            return new QuotaExceededException("GUS REGON answered HTTP 429: the request limit was reached.", status, null, null, retryAfter);
        }

        if (number >= 500)
        {
            return ServerFailure(status, string.Empty, null);
        }

        var message = "GUS REGON rejected the request (HTTP " + number.ToString(CultureInfo.InvariantCulture)
            + (contentType is null ? string.Empty : ", " + contentType) + ").";
        if (number == 400 || number == 415)
        {
            message += " The request content type may be wrong; the service expects application/soap+xml with an action parameter.";
        }

        return new PolishOpenDataApiException(message, status, null, attachSnippet ? HttpHelpers.Snippet(body) : null);
    }

    // An HttpRequestException, so that resilience handlers and callers treat it like any 5xx; on .NET it carries the status
    // code, as the KRS and Biała Lista clients do. A receiver fault is the inner exception and its code and reason are in the message.
    private static HttpRequestException ServerFailure(HttpStatusCode status, string detail, SoapFaultException? fault)
    {
        var message = "GUS REGON answered HTTP " + ((int)status).ToString(CultureInfo.InvariantCulture) + detail;
        message += message[message.Length - 1] == '.' ? string.Empty : ".";
#if NET
        return new HttpRequestException(message, fault, status);
#else
        return new HttpRequestException(message, fault);
#endif
    }
}
