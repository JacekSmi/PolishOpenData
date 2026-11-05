using Microsoft.JSInterop;
using PolishOpenData.Demo.State;
using PolishOpenData.Geo;

namespace PolishOpenData.Demo.Map;

/// <summary>
/// The .NET end of <c>wwwroot/js/map.js</c>: draws the place on the one map the static shell made, and receives its clicks.
/// The script only draws shapes and forwards events; what a dot means is shown by the page's card, never by the map.
/// A click on bare map goes to <see cref="PlaceController.PickPointAsync(double, double)"/>, which checks that it is in Poland;
/// a click on a dot is announced by <see cref="DotSelected"/> with the index of its group.
/// </summary>
internal sealed class MapInterop(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? _module;
    private DotNetObjectReference<MapInterop>? _self;
    private PlaceController? _controller;

    /// <summary>Raised when a dot is clicked, with <see cref="MapDot.Index"/> of the dot (the position of its group in the result).</summary>
    public event Action<int>? DotSelected;

    /// <summary>
    /// Imports the module (the same instance index.html loaded, so the same map) and starts forwarding clicks: map clicks to
    /// <paramref name="controller"/>, dot clicks to <see cref="DotSelected"/>. Calling it again only replaces the controller.
    /// </summary>
    public async Task AttachAsync(PlaceController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        _controller = controller;
        if (_module is null)
        {
            _module = await js.InvokeAsync<IJSObjectReference>("import", "./js/map.js");
            _self = DotNetObjectReference.Create(this);
            await _module.InvokeVoidAsync("attach", _self);
        }
    }

    /// <summary>
    /// Replaces what the map shows with a place: the dashed circle of <paramref name="radiusMeters"/> around <paramref name="center"/>, the parcel
    /// outline (GeoJSON from <c>ParcelOutline.ToGeoJson</c>, or <c>null</c> for none), and the dots, all in one call. The view fits the circle.
    /// </summary>
    public Task ShowPlaceAsync(Wgs84Point center, int radiusMeters, string? parcelGeoJson, IReadOnlyList<MapDot> dots)
    {
        ArgumentNullException.ThrowIfNull(dots);

        // The centre goes as [lat, lon], the array Leaflet reads as a point.
        return Module.InvokeVoidAsync("showPlace", new[] { center.Latitude, center.Longitude }, radiusMeters, parcelGeoJson, dots).AsTask();
    }

    /// <summary>Removes the circle, the outline and the dots; the view stays where it is.</summary>
    public Task ClearAsync() => Module.InvokeVoidAsync("clear").AsTask();

    /// <summary>Called by the script when the map (not a dot) is clicked.</summary>
    [JSInvokable]
    public Task OnMapClick(double lat, double lng) => _controller?.PickPointAsync(lat, lng) ?? Task.CompletedTask;

    /// <summary>Called by the script when a dot is clicked; <paramref name="groupIndex"/> is <see cref="MapDot.Index"/>.</summary>
    [JSInvokable]
    public void OnDotClick(int groupIndex) => DotSelected?.Invoke(groupIndex);

    /// <summary>Stops the script forwarding clicks, clears the map's place, and releases the module and the callback reference. Safe to call twice.</summary>
    public async ValueTask DisposeAsync()
    {
        var module = _module;
        var self = _self;
        _module = null;
        _self = null;
        _controller = null;
        DotSelected = null;
        if (module is null)
        {
            return;
        }

        try
        {
            await module.InvokeVoidAsync("detach");
        }
        finally
        {
            self?.Dispose();
            await module.DisposeAsync();
        }
    }

    private IJSObjectReference Module => _module ?? throw new InvalidOperationException("Call AttachAsync first.");
}
