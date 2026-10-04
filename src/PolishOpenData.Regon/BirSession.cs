using System;
using System.Threading;
using System.Threading.Tasks;

namespace PolishOpenData.Bir;

/// <summary>
/// The BIR session state of the process: one session id (<c>sid</c>), logged in lazily and renewed after the
/// configured lifetime, plus two small caches. It owns no <c>HttpClient</c>; the caller passes the login. Register
/// one instance per process, so that the transient typed client does not log in again on every resolve.
/// </summary>
internal sealed class BirSession : IDisposable
{
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _lifetime;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _lock = new();
    private string? _sid;
    private DateTimeOffset _loggedInAt;

    public BirSession(TimeProvider timeProvider, TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
        _lifetime = lifetime;
    }

    /// <summary>The current session id, or <c>null</c> when there is none.</summary>
    public string? CurrentSid
    {
        get
        {
            lock (_lock)
            {
                return _sid;
            }
        }
    }

    /// <summary>The date of the GUS data (<c>StanDanych</c>) from the last <c>GetValue</c> that parsed; a best-effort cache.</summary>
    public DateOnly? DataAsOf { get; set; }

    /// <summary><c>null</c> = unknown; <c>false</c> after GUS answered ErrorCode 5 for <c>BIR121JednLokalnaOsPrawnej</c>; a best-effort cache.</summary>
    public bool? Bir121Supported { get; set; }

    /// <summary>The current session id, logging in first when there is none or it is older than the lifetime. Concurrent callers share one login.</summary>
    public async Task<string> GetSidAsync(Func<CancellationToken, Task<string>> login, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(login);
        if (TryGetFresh(out var fresh))
        {
            return fresh;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryGetFresh(out fresh))
            {
                return fresh;
            }

            return await LoginAndStoreAsync(login, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Replaces a session id that GUS did not recognise. When another caller has already renewed it (the current id
    /// differs from the rejected one), that newer id is returned without a login.
    /// </summary>
    public async Task<string> RenewAsync(string rejectedSid, Func<CancellationToken, Task<string>> login, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rejectedSid);
        ArgumentNullException.ThrowIfNull(login);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = CurrentSid;
            if (current is not null && !string.Equals(current, rejectedSid, StringComparison.Ordinal))
            {
                return current;
            }

            return await LoginAndStoreAsync(login, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Returns the current session id and forgets it, so that the next call logs in again.</summary>
    public string? Detach()
    {
        lock (_lock)
        {
            var sid = _sid;
            _sid = null;
            _loggedInAt = default;
            return sid;
        }
    }

    /// <summary>Releases the login gate. The dependency-injection container calls this for the singleton at shutdown.</summary>
    public void Dispose() => _gate.Dispose();

    private async Task<string> LoginAndStoreAsync(Func<CancellationToken, Task<string>> login, CancellationToken cancellationToken)
    {
        var created = await login(cancellationToken).ConfigureAwait(false);
        lock (_lock)
        {
            _sid = created;
            _loggedInAt = _timeProvider.GetUtcNow();
        }

        return created;
    }

    private bool TryGetFresh(out string sid)
    {
        lock (_lock)
        {
            if (_sid is not null && _timeProvider.GetUtcNow() - _loggedInAt < _lifetime)
            {
                sid = _sid;
                return true;
            }
        }

        sid = string.Empty;
        return false;
    }
}
