namespace PolishOpenData.Demo.State;

/// <summary>
/// The short sentence the page shows when a GUGiK request fails. Plain English, no technical detail: it never contains the exception's message,
/// and it is not the MCP server's wording (<c>ToolErrors</c> in <c>src/PolishOpenData.Mcp</c> writes for a model and quotes details).
/// </summary>
internal static class FriendlyMessage
{
    /// <summary>The sentence for anything not listed in <see cref="For"/>.</summary>
    public const string Generic = "Something went wrong. Try again.";

    /// <summary>
    /// The message for a failure of a GUGiK request. A cancelled search is no failure and gets no message: the controller drops it before it comes here.
    /// A <see cref="TaskCanceledException"/> that gets here is an HttpClient timeout, since the caller's own cancellation never reaches this method;
    /// any other <see cref="OperationCanceledException"/> is not expected and is <see cref="Generic"/>.
    /// </summary>
    public static string For(Exception exception) => exception switch
    {
        // Order matters: QuotaExceededException derives from PolishOpenDataApiException.
        QuotaExceededException => "GUGiK is limiting requests right now. Wait a minute and try again.",
        PolishOpenDataApiException { ErrorCode: "blocked" } => "GUGiK's service refused the request. Try again in a few minutes.",
        PolishOpenDataApiException => "A GUGiK service answered with an error. Try again later.",
        HttpRequestException => "A GUGiK service is not reachable right now. Check your connection and try again.",
        TaskCanceledException => "A GUGiK service took too long to answer. Try again.",
        _ => Generic,
    };
}
