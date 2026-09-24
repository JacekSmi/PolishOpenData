using System;
using System.Globalization;
using PolishOpenData.Internal;

namespace PolishOpenData.BialaLista;

/// <summary>Kind of whitelist request, counted separately by the upstream.</summary>
public enum BialaListaRequestKind
{
    /// <summary>A <c>search</c> request (single or batch).</summary>
    Search,

    /// <summary>A <c>check</c> request (NIP/REGON + account).</summary>
    Check,
}

/// <summary>
/// Thread-safe per-day request counter (days roll over at midnight Europe/Warsaw). Register one instance per process —
/// the upstream limit applies to the IP address, not to a client instance.
/// </summary>
public sealed class BialaListaQuotaTracker
{
    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();
    private DateOnly _day;
    private int _searches;
    private int _checks;
    private bool _exhausted;

    /// <summary>Creates a tracker.</summary>
    public BialaListaQuotaTracker(TimeProvider? timeProvider = null) => _timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>Search requests reserved today.</summary>
    public int SearchesUsedToday
    {
        get
        {
            lock (_gate)
            {
                Roll();
                return _searches;
            }
        }
    }

    /// <summary>Check requests reserved today.</summary>
    public int ChecksUsedToday
    {
        get
        {
            lock (_gate)
            {
                Roll();
                return _checks;
            }
        }
    }

    /// <summary>Reserves one request, or throws <see cref="QuotaExceededException"/> when the daily limit is reached.</summary>
    public void Reserve(BialaListaRequestKind kind, int dailyLimit)
    {
        lock (_gate)
        {
            Roll();
            if (_exhausted)
            {
                throw Exceeded("Biała Lista reported that today's request limit for this IP address is exhausted.");
            }

            var used = kind == BialaListaRequestKind.Search ? _searches : _checks;
            if (used >= dailyLimit)
            {
                throw Exceeded(
                    "Local guard: " + dailyLimit.ToString(CultureInfo.InvariantCulture) + " " +
                    (kind == BialaListaRequestKind.Search ? "search" : "check") +
                    " requests were already sent today; one more could make Biała Lista block this IP address.");
            }

            if (kind == BialaListaRequestKind.Search)
            {
                _searches++;
            }
            else
            {
                _checks++;
            }
        }
    }

    /// <summary>Records that the upstream reported the limit; every request is refused until midnight.</summary>
    public void MarkExhausted()
    {
        lock (_gate)
        {
            Roll();
            _exhausted = true;
        }
    }

    private void Roll()
    {
        var today = WarsawTime.Today(_timeProvider);
        if (today != _day)
        {
            _day = today;
            _searches = 0;
            _checks = 0;
            _exhausted = false;
        }
    }

    private QuotaExceededException Exceeded(string message) =>
        new(message + " The limit resets at midnight Europe/Warsaw.", null, null, WarsawTime.NextMidnightUtc(_timeProvider), null);
}
