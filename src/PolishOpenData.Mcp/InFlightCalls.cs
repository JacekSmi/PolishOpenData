using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PolishOpenData.Mcp;

/// <summary>
/// Registry calls in progress, one per cache key, shared by every caller that asks for the same key meanwhile.
/// Registered as a singleton: <see cref="CachedRegistries"/> is transient (it holds typed HttpClients), so the map
/// has to outlive it.
/// </summary>
internal sealed class InFlightCalls : IDisposable
{
    private readonly ConcurrentDictionary<string, Task> _calls = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _stopping = new();
    private readonly CancellationToken _stoppingToken;

    public InFlightCalls() => _stoppingToken = _stopping.Token;

    /// <summary>
    /// Returns the call in progress for <paramref name="key"/>, or starts <paramref name="call"/> and shares it until
    /// it completes. The entry is removed as soon as the call completes, whatever the outcome, so a failure reaches
    /// only the callers that were already waiting for it and the next caller starts a new call.
    /// </summary>
    /// <param name="key">The cache key. A key must always be used with the same <typeparamref name="T"/>; the cache keys are prefixed by kind.</param>
    /// <param name="call">Starts the upstream call. Its token is not any caller's: it is cancelled only when the server shuts down.</param>
    public Task<T> GetOrStart<T>(string key, Func<CancellationToken, Task<T>> call)
    {
        var owner = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var shared = _calls.GetOrAdd(key, owner.Task);
        if (!ReferenceEquals(shared, owner.Task))
        {
            return (Task<T>)shared;
        }

        // The entry is in the map before the call starts, so even a call that completes synchronously is removed.
        _ = RunAsync(key, owner, call);
        return owner.Task;
    }

    /// <summary>
    /// Cancels the calls still in progress. The container disposes this singleton when the server shuts down, before
    /// the HTTP clients it created earlier, whose resilience pipelines would otherwise wait for those calls to end.
    /// </summary>
    public void Dispose()
    {
        _stopping.Cancel();
        _stopping.Dispose();
    }

    private async Task RunAsync<T>(string key, TaskCompletionSource<T> owner, Func<CancellationToken, Task<T>> call)
    {
        var entry = new KeyValuePair<string, Task>(key, owner.Task);
        try
        {
            var value = await call(_stoppingToken).ConfigureAwait(false);
            _calls.TryRemove(entry);
            owner.TrySetResult(value);
        }
        catch (Exception ex)
        {
            _calls.TryRemove(entry);
            owner.TrySetException(ex);

            // Every waiter may have stopped waiting (each waits with its own token); reading Exception marks the
            // failure observed, so it is not reported again as an unobserved task exception.
            _ = owner.Task.Exception;
        }
    }
}
