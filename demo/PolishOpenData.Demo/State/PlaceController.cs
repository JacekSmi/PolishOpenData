using PolishOpenData.Demo.Search;
using PolishOpenData.Geo;
using PolishOpenData.Uug;

namespace PolishOpenData.Demo.State;

/// <summary>
/// The state machine behind the page: it turns what the user does (Enter on an address, a picked candidate, a map click, a radius or period change, an opened link)
/// into at most one GUGiK request per question, and shows the outcome as an immutable <see cref="DemoView"/>.
/// <list type="bullet">
/// <item>Every action that can change the view starts a new search: it cancels the previous one, and only the latest search may write <see cref="View"/>
/// (a generation counter; a cancelled search that still ends, with an answer or an error, is dropped). Cancellation is no error and shows no message.</item>
/// <item>Once a point is known, the parcel (ULDK) and the sales (RCN) are asked concurrently, the cache first, so a radius or period change asks RCN only.
/// A typed address asks UUG first, once. The same (point rounded to 5 decimals, radius, period) and the same point's parcel are answered from the session cache.</item>
/// <item>A request that is still running is shared: a search for the same key (a radius change, a double click, a link of the same point) joins it instead of sending
/// another, so a place costs one ULDK and one RCN request however often its search is superseded. A request has its own cancellation, not the search's:
/// it is cancelled when the view moves to a place it does not belong to (another point; for the sales also another radius or period) or to no place.</item>
/// <item>Any failure of either request is a message for the whole place (<see cref="FriendlyMessage"/>); the search waits for the other request and keeps what succeeded
/// in the cache, so a retry of the same place repeats only the failed request. When both fail the parcel's error is the one told.</item>
/// <item>Nothing but the methods below leads to a request: there is no text-changed entry point.</item>
/// </list>
/// Blazor WebAssembly runs on one thread but these methods interleave at every await, hence the generation counter instead of locks.
/// </summary>
internal sealed class PlaceController(PlaceSearch places, AddressSearch addresses) : IDisposable
{
    private const string OutsidePoland = "Only places in Poland are supported.";

    // A superseded search that still gets an answer caches it (the key is complete, so it is right for any later search) but never touches View.
    // The caches grow with the places looked at in one session; a page reload empties them.
    private readonly SharedRequests<Wgs84Point, ParcelResult> _parcels = new();
    private readonly SharedRequests<(Wgs84Point Point, int RadiusMeters, int PeriodMonths), SalesResult> _sales = new();
    private CancellationTokenSource? _current;
    private int _generation;

    /// <summary>What the page shows now. Replaced as a whole; <see cref="Changed"/> follows every replacement.</summary>
    public DemoView View { get; private set; } = DemoView.Idle;

    /// <summary>The radius (metres) the next place is searched with: the last choice, from the page or from a link.</summary>
    public int RadiusMeters { get; private set; } = PlaceUrlState.DefaultRadius;

    /// <summary>The period (months) the next place is searched with.</summary>
    public int PeriodMonths { get; private set; } = PlaceUrlState.DefaultMonths;

    /// <summary>Raised after each replacement of <see cref="View"/>, the <see cref="DemoPhase.Searching"/> ones too.</summary>
    public event Action? Changed;

    /// <summary>The link fragment (with <c>#</c>) of the current place, <see cref="string.Empty"/> when the view has none. Compare fragments, not <see cref="PlaceUrlState"/> records: a parsed link equals the place only up to the rounding.</summary>
    public string CurrentFragment() => View.Place?.ToFragment() ?? string.Empty;

    /// <summary>
    /// Enter on the search box: one geocoder request, then the place requests when the text names one address point.
    /// Empty or white-space text does nothing at all: no request, no change of the view, no cancellation of a search in flight.
    /// A KW-shaped text and an unmatched address give a message, several matches give <see cref="DemoPhase.Candidates"/>.
    /// </summary>
    public Task SubmitAddressAsync(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? Task.CompletedTask
            : StartAsync(async (generation, token) =>
            {
                Show(DemoView.Idle with { Phase = DemoPhase.Searching });
                AddressOutcome outcome;
                try
                {
                    outcome = await addresses.ResolveAsync(text, token);
                }
                catch (Exception exception)
                {
                    ShowFailure(generation, exception, place: null);
                    return;
                }

                if (generation != _generation)
                {
                    return;
                }

                if (outcome is { Kind: AddressOutcomeKind.Resolved, Match: { } match })
                {
                    await StartPlaceAsync(match.Location, Describe(match), generation);
                }
                else if (outcome.Kind == AddressOutcomeKind.Candidates)
                {
                    Show(DemoView.Idle with { Phase = DemoPhase.Candidates, Candidates = outcome.Candidates, Message = outcome.Message });
                }
                else
                {
                    Show(DemoView.Idle with { Phase = DemoPhase.Message, Message = outcome.Message ?? FriendlyMessage.Generic });
                }
            });

    /// <summary>A candidate the user picked: the place requests, no geocoder request. The label is the address as the geocoder writes it.</summary>
    public Task PickCandidateAsync(UugMatch match) =>
        StartAsync((generation, _) => StartPlaceAsync(match.Location, Describe(match), generation));

    /// <summary>A point on the map. Outside Poland (also <c>default(Wgs84Point)</c>, which skips the constructor's check) it is a message and no request.</summary>
    public Task PickPointAsync(Wgs84Point point) =>
        StartAsync((generation, _) => StartPlaceAsync(point, null, generation));

    /// <summary>
    /// A point as the raw numbers of a map click, which may be anything: outside Poland, swapped, not a number. Checked here, so that no invalid
    /// <see cref="Wgs84Point"/> is ever built; same outcome as <see cref="PickPointAsync(Wgs84Point)"/>.
    /// </summary>
    public Task PickPointAsync(double latitude, double longitude) =>
        Wgs84Point.IsInPoland(latitude, longitude)
            ? PickPointAsync(new Wgs84Point(latitude, longitude))
            : StartAsync((_, _) =>
            {
                Show(DemoView.Idle with { Phase = DemoPhase.Message, Message = OutsidePoland });
                return Task.CompletedTask;
            });

    /// <summary>
    /// Sets the radius for this and the next places; with a place shown, asks RCN again (the parcel stays, and an answer already known is not asked for twice).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The radius is not one of <see cref="PlaceUrlState.Radii"/> (a caller bug, not user input); nothing changes.</exception>
    public Task ChangeRadiusAsync(int meters)
    {
        if (!PlaceUrlState.Radii.Contains(meters))
        {
            throw new ArgumentOutOfRangeException(nameof(meters), meters, "The radius is not one the page offers.");
        }

        RadiusMeters = meters;
        return View.Place is { } place ? StartAsync((generation, _) => FetchPlaceAsync(place with { RadiusMeters = meters }, generation)) : Task.CompletedTask;
    }

    /// <summary>The same for the period.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The period is not one of <see cref="PlaceUrlState.Months"/> (a caller bug, not user input); nothing changes.</exception>
    public Task ChangePeriodAsync(int months)
    {
        if (!PlaceUrlState.Months.Contains(months))
        {
            throw new ArgumentOutOfRangeException(nameof(months), months, "The period is not one the page offers.");
        }

        PeriodMonths = months;
        return View.Place is { } place ? StartAsync((generation, _) => FetchPlaceAsync(place with { PeriodMonths = months }, generation)) : Task.CompletedTask;
    }

    /// <summary>
    /// Opens a shared link: the place requests (1 ULDK and 1 RCN, none to the geocoder), and the link's radius, period and label are taken as they are.
    /// An empty or invalid fragment changes nothing and asks nothing (the controller starts <see cref="DemoPhase.Idle"/>, so at start-up this leaves it idle;
    /// a hand-edited address later does not wipe what is shown). The link of the place already searching, shown or failed is a no-op, judged by comparing fragments:
    /// a page that reacts to its own link updates neither restarts nor cancels a search, and a failed place (a 429, say) is not retried without a user action.
    /// Picking the place again, or changing the radius or period, is the retry.
    /// </summary>
    public Task LoadFromFragmentAsync(string? fragment)
    {
        if (!PlaceUrlState.TryParse(fragment, out var place))
        {
            return Task.CompletedTask;
        }

        // CurrentFragment() is empty when the view has no place and a fragment never is, so equal fragments mean the view has this very place (Searching, Result or Message).
        if (place.ToFragment() == CurrentFragment())
        {
            return Task.CompletedTask;
        }

        RadiusMeters = place.RadiusMeters;
        PeriodMonths = place.PeriodMonths;
        return StartAsync((generation, _) => FetchPlaceAsync(place, generation));
    }

    /// <summary>Cancels the search in flight and its requests, which then show nothing more (the page is going away).</summary>
    public void Dispose()
    {
        _generation++;
        _current?.Cancel();
        _parcels.CancelExcept(_ => false);
        _sales.CancelExcept(_ => false);
    }

    // One search per user action: it cancels the previous one. The generation goes up before the cancellation, because a cancellation callback may resume the
    // superseded search at once, inside Cancel(), and that search must already find itself stale. The token of a search is the geocoder's: the place requests
    // are shared between searches and cancelled by Show.
    private async Task StartAsync(Func<int, CancellationToken, Task> search)
    {
        var previous = _current;
        var current = _current = new CancellationTokenSource();
        var generation = ++_generation;
        previous?.Cancel();
        try
        {
            await search(generation, current.Token);
        }
        finally
        {
            if (ReferenceEquals(_current, current))
            {
                _current = null;
                current.Dispose();
            }
        }
    }

    // The common way in once a point is known, whatever the source of the point.
    private Task StartPlaceAsync(Wgs84Point point, string? label, int generation)
    {
        if (!Wgs84Point.IsInPoland(point.Latitude, point.Longitude))
        {
            Show(DemoView.Idle with { Phase = DemoPhase.Message, Message = OutsidePoland });
            return Task.CompletedTask;
        }

        return FetchPlaceAsync(new PlaceUrlState(point, RadiusMeters, PeriodMonths, label), generation);
    }

    // Parcel and sales, concurrently, the cache first. Both are always awaited, so what succeeded is cached even when the other one failed.
    private async Task FetchPlaceAsync(PlaceUrlState requested, int generation)
    {
        // Rounded once here (the box of Poland has 5-decimal edges, so a point inside stays inside): the cache key, the requests, the view and the link agree.
        var place = requested with { Point = new Wgs84Point(Math.Round(requested.Point.Latitude, 5), Math.Round(requested.Point.Longitude, 5)) };
        Show(new DemoView(DemoPhase.Searching, place, _parcels.TryGetAnswer(place.Point, out var known) ? known : null, null, [], null, place.Label));

        var parcelTask = Attempt(() => _parcels.GetAsync(place.Point, token => places.FindParcelAsync(place.Point, token)));
        var salesTask = Attempt(() => _sales.GetAsync(SalesKey(place), token => places.FindSalesAsync(place.Point, place.RadiusMeters, place.PeriodMonths, token)));
        var (parcel, parcelError) = await parcelTask;
        var (sales, salesError) = await salesTask;
        if (generation != _generation)
        {
            return;
        }

        var error = parcelError ?? salesError;
        if (error is null)
        {
            Show(new DemoView(DemoPhase.Result, place, parcel, sales, [], null, place.Label));
        }
        else
        {
            ShowFailure(generation, error, place);
        }
    }

    // The boundary: nothing a request throws gets past here.
    private static async Task<(T? Value, Exception? Error)> Attempt<T>(Func<Task<T>> request)
    {
        try
        {
            return (await request(), null);
        }
        catch (Exception exception)
        {
            return (default, exception);
        }
    }

    // A failure of a superseded search (a cancellation above all) is dropped; only the latest search tells the user.
    private void ShowFailure(int generation, Exception exception, PlaceUrlState? place)
    {
        if (generation == _generation)
        {
            Show(new DemoView(DemoPhase.Message, place, null, null, [], FriendlyMessage.For(exception), place?.Label));
        }
    }

    // Every view is shown by the latest search, and the requests the shown place does not need are cancelled first (all of them when the view has no place).
    // The sweep runs before the view is replaced: it may resume a superseded search at once, and that search finds itself stale.
    private void Show(DemoView view)
    {
        var place = view.Place;
        _parcels.CancelExcept(point => place is not null && point == place.Point);
        _sales.CancelExcept(key => place is not null && key == SalesKey(place));
        View = view;
        Changed?.Invoke();
    }

    private static (Wgs84Point Point, int RadiusMeters, int PeriodMonths) SalesKey(PlaceUrlState place) => (place.Point, place.RadiusMeters, place.PeriodMonths);

    // "ulica Krucza 50, Warszawa": the address as the geocoder writes it, with the parts it has.
    private static string? Describe(UugMatch match)
    {
        var street = $"{match.Street} {match.Number}".Trim();
        var label = street.Length == 0 ? match.City : match.City is null ? street : $"{street}, {match.City}";
        return string.IsNullOrWhiteSpace(label) ? null : label;
    }

    /// <summary>
    /// The answers known for keys, and the requests still running for them: a request for a key that is running joins that request instead of sending another.
    /// A request has its own <see cref="CancellationTokenSource"/>, cancelled by <see cref="CancelExcept"/>. Only a successful answer is kept: a failed or cancelled
    /// request is forgotten, so asking again sends it again.
    /// </summary>
    private sealed class SharedRequests<TKey, TValue>
        where TKey : notnull
    {
        private readonly Dictionary<TKey, TValue> _answers = [];
        private readonly Dictionary<TKey, (CancellationTokenSource Source, Task<TValue> Task)> _running = [];

        public bool TryGetAnswer(TKey key, out TValue answer) => _answers.TryGetValue(key, out answer!);

        public Task<TValue> GetAsync(TKey key, Func<CancellationToken, Task<TValue>> request)
        {
            if (_answers.TryGetValue(key, out var answer))
            {
                return Task.FromResult(answer);
            }

            // A request that has ended without an answer (failed, cancelled) stays in the map until it is swept or replaced here; it is never joined.
            if (_running.TryGetValue(key, out var running) && !running.Task.IsCompleted)
            {
                return running.Task;
            }

            var source = new CancellationTokenSource();
            var task = RunAsync(key, request, source);
            _running[key] = (source, task);
            return task;
        }

        /// <summary>Cancels, and forgets, every running request whose key <paramref name="keep"/> does not accept.</summary>
        public void CancelExcept(Func<TKey, bool> keep)
        {
            // A snapshot: cancelling may resume a request at once, inside Cancel().
            foreach (var key in _running.Keys.Where(k => !keep(k)).ToList())
            {
                var (source, task) = _running[key];
                _running.Remove(key);
                if (!task.IsCompleted)
                {
                    source.Cancel();
                }
            }
        }

        private async Task<TValue> RunAsync(TKey key, Func<CancellationToken, Task<TValue>> request, CancellationTokenSource source)
        {
            using (source)
            {
                var answer = await request(source.Token);
                _answers[key] = answer;
                return answer;
            }
        }
    }
}
