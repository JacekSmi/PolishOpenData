using System;
using System.Globalization;
using System.Net.Http;

namespace PolishOpenData.Mcp;

/// <summary>Turns exceptions into short, actionable text for the model. Returns null for cancellation (let it propagate).</summary>
internal static class ToolErrors
{
    public static string? Describe(Exception exception) => exception switch
    {
        OperationCanceledException => null,
        QuotaExceededException quota => "Request limit reached: " + quota.Message +
            (quota.ResetsAt is { } at ? " (resets " + at.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC)" : string.Empty),
        PolishOpenDataApiException { IsTransient: true } transient => "The registry is updating its data (" + transient.ErrorCode + "). Try again in a minute.",
        PolishOpenDataApiException api => "The registry rejected the request: " + api.Message,
        PolishOpenDataException other => other.Message,
        HttpRequestException http => "The registry service is unavailable right now (" + http.Message + "). Try again later.",
        _ => "Unexpected error: " + exception.Message,
    };
}
