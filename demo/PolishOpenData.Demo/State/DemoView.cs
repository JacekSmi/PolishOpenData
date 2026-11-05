using PolishOpenData.Demo.Search;
using PolishOpenData.Uug;

namespace PolishOpenData.Demo.State;

/// <summary>Where the page is in a search.</summary>
internal enum DemoPhase
{
    /// <summary>Nothing asked yet.</summary>
    Idle,

    /// <summary>A request is in flight; <see cref="DemoView.Place"/> is set once the point is known.</summary>
    Searching,

    /// <summary>The geocoder found several matches: <see cref="DemoView.Candidates"/>, to be picked from.</summary>
    Candidates,

    /// <summary>The place was searched: <see cref="DemoView.Parcel"/> and <see cref="DemoView.Sales"/> are set.</summary>
    Result,

    /// <summary>Something to tell the user instead of a result: <see cref="DemoView.Message"/>.</summary>
    Message,
}

/// <summary>
/// What the page shows. Immutable: the controller replaces it as a whole and raises <c>Changed</c>.
/// </summary>
/// <param name="Phase">Where the search is.</param>
/// <param name="Place">The place (point, radius, period, label) once its point is known, also while searching and when the search of that place failed; <c>null</c> otherwise.</param>
/// <param name="Parcel">The parcel at the place; set for <see cref="DemoPhase.Result"/>, and while searching when it is already known (a radius or period change keeps it). Its <c>Parcel</c> is <c>null</c> when ULDK knows none there.</param>
/// <param name="Sales">The sales around the place, for <see cref="DemoPhase.Result"/>. Check <see cref="SalesResult.Outcome"/>: a <see cref="SalesOutcome.TooManySales"/> answer has no sales and no count, and is not "no sales".</param>
/// <param name="Candidates">The matches to pick from, for <see cref="DemoPhase.Candidates"/>; empty otherwise.</param>
/// <param name="Message">What to tell the user, for <see cref="DemoPhase.Message"/> (and a hint for <see cref="DemoPhase.Candidates"/>); never an exception message.</param>
/// <param name="Label">The text that names the place, equal to <c>Place.Label</c>; plain text from a link or the geocoder, never to be interpreted.</param>
internal sealed record DemoView(
    DemoPhase Phase,
    PlaceUrlState? Place,
    ParcelResult? Parcel,
    SalesResult? Sales,
    IReadOnlyList<UugMatch> Candidates,
    string? Message,
    string? Label)
{
    /// <summary>The view before anything was asked.</summary>
    public static DemoView Idle { get; } = new(DemoPhase.Idle, null, null, null, [], null, null);
}
