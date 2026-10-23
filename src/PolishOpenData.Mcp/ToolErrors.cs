using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using Polly;
using PolishOpenData.BialaLista.Offline;
using PolishOpenData.Bir;

namespace PolishOpenData.Mcp;

/// <summary>Turns exceptions into short, actionable text for the model. Returns null for cancellation (let it propagate).</summary>
internal static class ToolErrors
{
    private const string OfflineTool = "check_vat_bank_account_offline";
    private const string VatTool = "check_vat_bank_account";
    private const string CompanyTool = "lookup_company";
    private const string GeocodeTool = "geocode_address";
    private const string ParcelTool = "find_parcel";
    private const string PropertyTool = "property_prices";

    /// <param name="exception">The failure.</param>
    /// <param name="toolName">The tool that failed, when known: some errors only have a specific meaning for one tool.</param>
    public static string? Describe(Exception exception, string? toolName = null) => exception switch
    {
        OperationCanceledException => null,
        // GUS answers an unknown or revoked key with an empty login result. Never echo the key.
        RegonAuthenticationException => "GUS did not accept the key in " + McpServerSetup.RegonKeyVariable + ". Check it, or remove the variable to hide lookup_regon.",
        // Only the offline tool waits for a lock file held by another process (the library's lock timeout).
        TimeoutException => "Another process is preparing this date; try again later.",
        // The offline library reports an unusable data directory as an IOException whose message names the folder.
        IOException io when toolName == OfflineTool =>
            io.Message + " Set " + McpServerSetup.OfflineDirectoryVariable + " to a folder this server can write to.",
        // GUGiK (ULDK, UUG, RCN) rate limit: nothing to do with Biała Lista, so no pointer to the VAT tools; just slow down.
        QuotaExceededException quota when toolName is GeocodeTool or ParcelTool or PropertyTool =>
            "GUGiK is rate-limiting requests: " + quota.Message.TrimEnd('.') +
            (quota.RetryAfter is { } wait ? " (retry after " + ((int)Math.Ceiling(wait.TotalSeconds)).ToString(CultureInfo.InvariantCulture) + " s)" : string.Empty) +
            ". Wait a minute before trying again and avoid repeated or parallel lookups.",
        // The pointer to the offline VAT tool only helps for Biała Lista; a KRS 429 (no tool name) gets none.
        QuotaExceededException quota => "Request limit reached: " + quota.Message +
            (quota.ResetsAt is { } at ? " (resets " + at.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC)." : string.Empty) +
            (toolName is VatTool or CompanyTool ? " Use check_vat_bank_account_offline, which has no request limit." : string.Empty),
        VatWhitelistDataUnavailableException { Reason: VatWhitelistDataUnavailableReason.NotPublishedYet } unavailable =>
            "MF has not published the file for " + unavailable.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " yet; it usually appears on the evening before.",
        VatWhitelistDataUnavailableException unavailable => unavailable.Message + (unavailable.Reason == VatWhitelistDataUnavailableReason.DownloadFailed ? " Try again later." : string.Empty),
        // The F5 firewall page of the GUGiK services (ULDK, UUG): the request never reached the service.
        PolishOpenDataApiException { ErrorCode: "blocked" } => "The registry service blocked the request (it answered with a firewall page instead of data). Try again in a few minutes.",
        PolishOpenDataApiException { IsTransient: true } transient => "The registry is updating its data (" + transient.ErrorCode + "). Try again in a minute.",
        // A 2xx whose body the libraries could not read (malformed JSON, a value in the wrong format, no extract or
        // result): the registry accepted the request, so "rejected" would mislead.
        PolishOpenDataApiException { StatusCode: >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices } unreadable =>
            "The registry answered, but its response could not be used: " + unreadable.Message,
        PolishOpenDataApiException api => "The registry rejected the request: " + api.Message,
        PolishOpenDataException other => other.Message,
        HttpRequestException http => "The registry service is unavailable right now (" + http.Message + "). Try again later.",
        // Thrown by the resilience handlers this server enables (Microsoft.Extensions.Http.Resilience): a timeout
        // (TimeoutRejectedException) or an open circuit breaker (BrokenCircuitException) both mean the registry
        // could not be reached in time, same as a transport failure.
        ExecutionRejectedException rejected => "The registry service is unavailable right now (" + rejected.Message + "). Try again later.",
        _ => "Unexpected error: " + exception.Message,
    };
}
