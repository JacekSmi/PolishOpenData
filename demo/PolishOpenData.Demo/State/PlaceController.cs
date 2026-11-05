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
    private readonly Dictionary<Wgs84Point, ParcelResult> _parcels = [];
    private readonly Dictionary<(Wgs84Point Point, int RadiusMeters, int PeriodMonths), SalesResult> _sales = [];
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
                    await StartPlaceAsync(match.Location, Describe(match), generation, token);
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
        StartAsync((generation, token) => StartPlaceAsync(match.Location, Describe(match), generation, token));

    /// <summary>A point on the map. Outside Poland (also <c>default(Wgs84Point)</c>, which skips the constructor's check) it is a message and no request.</summary>
    public Task PickPointAsync(Wgs84Point point) =>
        StartAsync((generation, token) => StartPlaceAsync(point, null, generation, token));

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
        return View.Place is { } place ? StartAsync((generation, token) => FetchPlaceAsync(place with { RadiusMeters = meters }, generation, token)) : Task.CompletedTask;
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
        return View.Place is { } place ? StartAsync((generation, token) => FetchPlaceAsync(place with { PeriodMonths = months }, generation, token)) : Task.CompletedTask;
    }

    /// <summary>
    /// Opens a shared link: the place requests (1 ULDK and 1 RCN, none to the geocoder), and the link's radius, period and label are taken as they are.
    /// An empty or invalid fragment changes nothing and asks nothing (the controller starts <see cref="DemoPhase.Idle"/>, so at start-up this leaves it idle;
    /// a hand-edited address later does not wipe what is shown). The link of the place already searching or shown is a no-op, judged by comparing fragments,
    /// so a page that reacts to its own link updates neither restarts nor cancels a search.
    /// </summary>
    public Task LoadFromFragmentAsync(string? fragment)
    {
        if (!PlaceUrlState.TryParse(fragment, out var place))
        {
            return Task.CompletedTask;
        }

        if (View.Phase is DemoPhase.Searching or DemoPhase.Result && place.ToFragment() == CurrentFragment())
        {
            return Task.CompletedTask;
        }

        RadiusMeters = place.RadiusMeters;
        PeriodMonths = place.PeriodMonths;
        return StartAsync((generation, token) => FetchPlaceAsync(place, generation, token));
    }

    /// <summary>Cancels the search in flight, which then shows nothing more (the page is going away).</summary>
    public void Dispose()
    {
        _generation++;
        _current?.Cancel();
    }

    // One search per user action: it cancels the previous one. The generation goes up before the cancellation, because a cancellation callback may resume the
    // superseded search at once, inside Cancel(), and that search must already find itself stale.
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
    private Task StartPlaceAsync(Wgs84Point point, string? label, int generation, CancellationToken token)
    {
        if (!Wgs84Point.IsInPoland(point.Latitude, point.Longitude))
        {
            Show(DemoView.Idle with { Phase = DemoPhase.Message, Message = OutsidePoland });
            return Task.CompletedTask;
        }

        return FetchPlaceAsync(new PlaceUrlState(point, RadiusMeters, PeriodMonths, label), generation, token);
    }

    // Parcel and sales, concurrently, the cache first. Both are always awaited, so what succeeded is cached even when the other one failed.
    private async Task FetchPlaceAsync(PlaceUrlState requested, int generation, CancellationToken token)
    {
        // Rounded once here (the box of Poland has 5-decimal edges, so a point inside stays inside): the cache key, the requests, the view and the link agree.
        var place = requested with { Point = new Wgs84Point(Math.Round(requested.Point.Latitude, 5), Math.Round(requested.Point.Longitude, 5)) };
        Show(new DemoView(DemoPhase.Searching, place, _parcels.GetValueOrDefault(place.Point), null, [], null, place.Label));

        var parcelTask = Attempt(() => ParcelAsync(place.Point, token));
        var salesTask = Attempt(() => SalesAsync(place, token));
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

    private async Task<ParcelResult> ParcelAsync(Wgs84Point point, CancellationToken token)
    {
        if (_parcels.TryGetValue(point, out var cached))
        {
            return cached;
        }

        var found = await places.FindParcelAsync(point, token);
        _parcels[point] = found;
        return found;
    }

    private async Task<SalesResult> SalesAsync(PlaceUrlState place, CancellationToken token)
    {
        var key = (place.Point, place.RadiusMeters, place.PeriodMonths);
        if (_sales.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var found = await places.FindSalesAsync(place.Point, place.RadiusMeters, place.PeriodMonths, token);
        _sales[key] = found;
        return found;
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

    private void Show(DemoView view)
    {
        View = view;
        Changed?.Invoke();
    }

    // "ulica Krucza 50, Warszawa": the address as the geocoder writes it, with the parts it has.
    private static string? Describe(UugMatch match)
    {
        var street = $"{match.Street} {match.Number}".Trim();
        var label = street.Length == 0 ? match.City : match.City is null ? street : $"{street}, {match.City}";
        return string.IsNullOrWhiteSpace(label) ? null : label;
    }
}
