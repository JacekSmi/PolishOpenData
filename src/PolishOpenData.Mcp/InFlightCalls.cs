using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PolishOpenData.Mcp;

/// <summary>
/// Registry calls in progress, one per cache key, shared by every caller that asks for the same key meanwhile.
/// Registered as a singleton: <see cref="CachedRegistries"/> is transient (it holds typed HttpClients), so the map
/// has to outlive it.
/// </summary>
internal sealed class InFlightCalls
{
    private readonly ConcurrentDictionary<string, Task> _calls = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns the call in progress for <paramref name="key"/>, or starts <paramref name="call"/> and shares it until
    /// it completes. The entry is removed as soon as the call completes, whatever the outcome, so a failure reaches
    /// only the callers that were already waiting for it and the next caller starts a new call.
    /// </summary>
    /// <remarks>A key must always be used with the same <typeparamref name="T"/>; the cache keys are prefixed by kind.</remarks>
    public Task<T> GetOrStart<T>(string key, Func<Task<T>> call)
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

    private async Task RunAsync<T>(string key, TaskCompletionSource<T> owner, Func<Task<T>> call)
    {
        var entry = new KeyValuePair<string, Task>(key, owner.Task);
        try
        {
            var value = await call().ConfigureAwait(false);
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
