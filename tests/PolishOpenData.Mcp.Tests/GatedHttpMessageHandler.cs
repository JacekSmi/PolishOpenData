using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PolishOpenData.Mcp.Tests;

/// <summary>
/// Answers requests from a function like <c>StubHttpMessageHandler</c>, but holds the requests selected by
/// <c>hold</c> (all of them by default) until <see cref="Release"/>, asynchronously, so a test can act while a call is
/// in progress. Counts the held requests that reached it. No network.
/// </summary>
internal sealed class GatedHttpMessageHandler(
    Func<HttpRequestMessage, HttpResponseMessage> respond,
    Func<HttpRequestMessage, bool>? hold = null) : HttpMessageHandler
{
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _requests;
    private int _cancelled;

    /// <summary>Completes when the first held request arrives.</summary>
    public Task Entered => _entered.Task;

    /// <summary>The number of held requests so far.</summary>
    public int Requests => Volatile.Read(ref _requests);

    /// <summary>True when a held request was cancelled while it waited.</summary>
    public bool UpstreamCancelled => Volatile.Read(ref _cancelled) != 0;

    /// <summary>Lets every held request, and every later one, through.</summary>
    public void Release() => _release.TrySetResult();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (hold is null || hold(request))
        {
            Interlocked.Increment(ref _requests);
            _entered.TrySetResult();
            try
            {
                await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Exchange(ref _cancelled, 1);
                throw;
            }
        }

        return respond(request);
    }
}
