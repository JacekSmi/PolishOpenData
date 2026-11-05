using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using PolishOpenData.Demo.Map;
using PolishOpenData.Demo.Search;
using PolishOpenData.Demo.State;
using PolishOpenData.Geo;

namespace PolishOpenData.Demo.Tests;

public class MapInteropTests
{
    private static readonly Wgs84Point Krucza = new(52.2285, 21.0172);

    // The module the script runs in: it records every call and ends each with the default result (a void call).
    private sealed class FakeModule : IJSObjectReference
    {
        public List<(string Identifier, object?[] Args)> Calls { get; } = [];

        public bool Disposed { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls.Add((identifier, args ?? []));
            return default;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return default;
        }
    }

    private sealed class FakeRuntime(FakeModule module) : IJSRuntime
    {
        public List<(string Identifier, object?[] Args)> Calls { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls.Add((identifier, args ?? []));
            return ValueTask.FromResult((TValue)(object)module);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }

    private sealed class Rig
    {
        public Rig()
        {
            Runtime = new FakeRuntime(Module);
            Interop = new MapInterop(Runtime);
            Controller = new PlaceController(new PlaceSearch(Uldk, Rcn, () => new DateOnly(2026, 10, 23)), new AddressSearch(Uug));
        }

        public FakeModule Module { get; } = new();

        public FakeRuntime Runtime { get; }

        public MapInterop Interop { get; }

        public ScriptedUldk Uldk { get; } = new();

        public ScriptedRcn Rcn { get; } = new();

        public ScriptedUug Uug { get; } = new();

        public PlaceController Controller { get; }

        public DotNetObjectReference<MapInterop> Reference => (DotNetObjectReference<MapInterop>)Assert.Single(Module.Calls, c => c.Identifier == "attach").Args[0]!;
    }

    [Fact]
    public async Task Attach_imports_the_module_and_hands_over_a_callback_reference()
    {
        var rig = new Rig();

        await rig.Interop.AttachAsync(rig.Controller);

        var import = Assert.Single(rig.Runtime.Calls);
        Assert.Equal("import", import.Identifier);
        Assert.Equal(["./js/map.js"], import.Args);
        Assert.Same(rig.Interop, rig.Reference.Value);
    }

    [Fact]
    public async Task Attach_twice_attaches_the_script_once()
    {
        var rig = new Rig();

        await rig.Interop.AttachAsync(rig.Controller);
        await rig.Interop.AttachAsync(rig.Controller);

        Assert.Single(rig.Runtime.Calls);
        Assert.Single(rig.Module.Calls, c => c.Identifier == "attach");
    }

    [Fact]
    public async Task ShowPlace_sends_centre_radius_outline_and_all_dots_in_one_call()
    {
        var rig = new Rig();
        await rig.Interop.AttachAsync(rig.Controller);
        MapDot[] dots = [new(52.2, 21.0, "#440154", true, 1, 0), new(52.3, 21.1, "#6f7a89", false, 2, 1)];

        await rig.Interop.ShowPlaceAsync(Krucza, 500, """{"type":"Polygon","coordinates":[]}""", dots);

        var call = Assert.Single(rig.Module.Calls, c => c.Identifier == "showPlace");
        Assert.Equal(4, call.Args.Length);
        Assert.Equal(new[] { 52.2285, 21.0172 }, call.Args[0]);
        Assert.Equal(500, call.Args[1]);
        Assert.Equal("""{"type":"Polygon","coordinates":[]}""", call.Args[2]);
        Assert.Same(dots, call.Args[3]);
    }

    [Fact]
    public async Task ShowPlace_without_an_outline_sends_null_and_Clear_asks_the_script_to_clear()
    {
        var rig = new Rig();
        await rig.Interop.AttachAsync(rig.Controller);

        await rig.Interop.ShowPlaceAsync(Krucza, 250, null, []);
        await rig.Interop.ClearAsync();

        Assert.Null(Assert.Single(rig.Module.Calls, c => c.Identifier == "showPlace").Args[2]);
        Assert.Equal("clear", rig.Module.Calls[^1].Identifier);
    }

    [Fact]
    public async Task Drawing_before_Attach_is_a_caller_bug()
    {
        var interop = new MapInterop(new FakeRuntime(new FakeModule()));

        await Assert.ThrowsAsync<InvalidOperationException>(() => interop.ShowPlaceAsync(Krucza, 500, null, []));
        await Assert.ThrowsAsync<InvalidOperationException>(() => interop.ClearAsync());
    }

    [Fact]
    public async Task A_map_click_picks_the_point_through_the_controller()
    {
        var rig = new Rig();
        await rig.Interop.AttachAsync(rig.Controller);

        await rig.Interop.OnMapClick(Krucza.Latitude, Krucza.Longitude);

        Assert.Equal([Krucza], rig.Uldk.Calls);
        Assert.Single(rig.Rcn.Queries);
        Assert.Equal(DemoPhase.Result, rig.Controller.View.Phase);
    }

    [Fact]
    public async Task A_map_click_outside_Poland_asks_nothing()
    {
        var rig = new Rig();
        await rig.Interop.AttachAsync(rig.Controller);

        await rig.Interop.OnMapClick(0, 0);

        Assert.Empty(rig.Uldk.Calls);
        Assert.Empty(rig.Rcn.Queries);
        Assert.Equal(DemoPhase.Message, rig.Controller.View.Phase);
    }

    [Fact]
    public void A_dot_click_raises_DotSelected_with_the_group_index()
    {
        var interop = new MapInterop(new FakeRuntime(new FakeModule()));
        var selected = new List<int>();
        interop.DotSelected += selected.Add;

        interop.OnDotClick(7);
        interop.OnDotClick(0);

        Assert.Equal([7, 0], selected);
    }

    [Fact]
    public async Task Dispose_detaches_and_releases_the_module_and_the_reference()
    {
        var rig = new Rig();
        await rig.Interop.AttachAsync(rig.Controller);
        var reference = rig.Reference;

        await rig.Interop.DisposeAsync();

        Assert.Equal("detach", rig.Module.Calls[^1].Identifier);
        Assert.True(rig.Module.Disposed);
        Assert.Throws<ObjectDisposedException>(() => reference.Value);

        // Safe twice, and a late click from the script reaches nobody.
        await rig.Interop.DisposeAsync();
        await rig.Interop.OnMapClick(Krucza.Latitude, Krucza.Longitude);
        Assert.Empty(rig.Uldk.Calls);
    }

    [Fact]
    public async Task Dispose_without_Attach_does_nothing()
    {
        var module = new FakeModule();

        await new MapInterop(new FakeRuntime(module)).DisposeAsync();

        Assert.Empty(module.Calls);
    }
}
