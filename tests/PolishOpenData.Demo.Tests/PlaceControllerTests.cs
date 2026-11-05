using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using PolishOpenData.Demo.Search;
using PolishOpenData.Demo.State;
using PolishOpenData.Geo;
using PolishOpenData.Rcn;
using PolishOpenData.Uldk;
using PolishOpenData.Uug;

namespace PolishOpenData.Demo.Tests;

public class PlaceControllerTests
{
    private const string OutsidePoland = "Only places in Poland are supported.";
    private const string Detail = "SECRET-DETAIL-4711";
    private const string KruczaFragment = "#lat=52.22850&lon=21.01720&r=500&m=24";

    private static readonly DateOnly Today = new(2026, 10, 23);
    private static readonly Wgs84Point Krucza = new(52.2285, 21.0172);
    private static readonly Wgs84Point Gdansk = new(54.3520, 18.6466);

    // The controller over the three scripted clients; Views holds every view the controller showed, in order.
    private sealed class Rig
    {
        public Rig()
        {
            Controller = new PlaceController(new PlaceSearch(Uldk, Rcn, () => Today), new AddressSearch(Uug));
            Controller.Changed += () => Views.Add(Controller.View);
        }

        public ScriptedUldk Uldk { get; } = new();

        public ScriptedRcn Rcn { get; } = new();

        public ScriptedUug Uug { get; } = new();

        public PlaceController Controller { get; }

        public List<DemoView> Views { get; } = [];

        public DemoView View => Controller.View;

        public List<DemoPhase> Phases => [.. Views.Select(v => v.Phase)];

        public void ExpectNoRequests()
        {
            Assert.Empty(Uug.Texts);
            Assert.Empty(Uldk.Calls);
            Assert.Empty(Rcn.Queries);
        }
    }

    private static UugMatch Address(string number = "50", string street = "ulica Krucza") =>
        new()
        {
            Kind = UugMatchKind.Address,
            City = "Warszawa",
            Street = street,
            Number = number,
            PostalCode = "00-025",
            Teryt = "146501",
            Simc = "0918123",
            Ulic = "10013",
            Location = Krucza,
            Accuracy = 0.76,
        };

    private static UugMatch Street() => new() { Kind = UugMatchKind.Street, City = "Warszawa", Street = "ulica Krucza", Teryt = "146501", Simc = "0918123", Ulic = "10013", Location = Krucza };

    private static Task<IReadOnlyList<UugMatch>> Matches(params UugMatch[] matches) => Task.FromResult<IReadOnlyList<UugMatch>>(matches);

    private static UldkParcel Parcel(string number) => new() { Number = number };

    // A search the test holds open must end once released; if it does not (a cancellation that never came), fail instead of hanging.
    private static Task Ends(Task search) => search.WaitAsync(TimeSpan.FromSeconds(10));

    private static double SquareWidth(RcnQuery query) => query.Area!.Value.Max.Easting - query.Area.Value.Min.Easting;

    // ---- the only way to a request ----

    [Fact]
    public void Typing_makes_no_request()
    {
        var rig = new Rig();

        // A text reaches a request only through SubmitAddressAsync (Enter) and, as a link, LoadFromFragmentAsync (which never asks the geocoder).
        // There is no text-changed entry point: no other method takes a string and no property of the controller accepts one.
        var type = typeof(PlaceController);
        var withText = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetParameters().Any(p => p.ParameterType == typeof(string)))
            .Select(m => m.Name)
            .Order();
        Assert.Equal(new[] { "LoadFromFragmentAsync", "SubmitAddressAsync" }, withText);
        Assert.DoesNotContain(type.GetProperties(), p => p.PropertyType == typeof(string) && p.SetMethod is { IsPublic: true });
        rig.ExpectNoRequests();
        Assert.Equal(DemoPhase.Idle, rig.View.Phase);
        Assert.Empty(rig.Views);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" \t\r\n ")]
    public async Task Empty_text_changes_nothing(string text)
    {
        var rig = new Rig();
        await rig.Controller.PickPointAsync(Krucza);
        var before = rig.View;
        var changes = rig.Views.Count;

        await rig.Controller.SubmitAddressAsync(text);

        Assert.Same(before, rig.View);
        Assert.Equal(changes, rig.Views.Count);
        Assert.Empty(rig.Uug.Texts);
    }

    [Fact]
    public async Task Empty_text_does_not_cancel_the_search_in_flight()
    {
        var rig = new Rig();
        var gate = new Gate<UldkParcel?>();
        rig.Uldk.OnCall = (_, token) => gate.Wait(token);
        var search = rig.Controller.PickPointAsync(Krucza);
        await gate.Entered;

        await rig.Controller.SubmitAddressAsync("  ");

        Assert.False(gate.Token.IsCancellationRequested);
        gate.Release(Parcel("1"));
        await Ends(search);
        Assert.Equal(DemoPhase.Result, rig.View.Phase);
    }

    // ---- cancellation ----

    [Fact]
    public async Task A_new_pick_cancels_the_previous_search()
    {
        var rig = new Rig();
        var uldk = new Gate<UldkParcel?>();
        var rcn = new Gate<RcnTransactions>();
        rig.Uldk.OnCall = (point, token) => point == Krucza ? uldk.Wait(token) : Task.FromResult<UldkParcel?>(Parcel("second"));
        rig.Rcn.OnCall = (query, token) => rig.Rcn.Queries.Count == 1 ? rcn.Wait(token) : ScriptedRcn.OneFlat(query, token);
        var first = rig.Controller.PickPointAsync(Krucza);
        await uldk.Entered;
        await rcn.Entered;

        await rig.Controller.PickPointAsync(Gdansk);
        await Ends(first);

        Assert.True(uldk.Token.IsCancellationRequested);
        Assert.True(rcn.Token.IsCancellationRequested);
        Assert.Equal(DemoPhase.Result, rig.View.Phase);
        Assert.Equal("#lat=54.35200&lon=18.64660&r=500&m=24", rig.Controller.CurrentFragment());
        Assert.Equal("second", rig.View.Parcel!.Parcel!.Number);
        Assert.Null(rig.View.Message);
        // Cancellation is no error and says nothing: the first search showed Searching and nothing more.
        Assert.Equal(new[] { DemoPhase.Searching, DemoPhase.Searching, DemoPhase.Result }, rig.Phases);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_cancelled_search_never_overwrites_the_view(bool staleSearchFails)
    {
        // The stale fakes ignore their token and answer (or fail) after the new search has finished: nothing of it may reach the view.
        var rig = new Rig();
        var uldk = new Gate<UldkParcel?>();
        var rcn = new Gate<RcnTransactions>();
        rig.Uldk.OnCall = (point, token) => point == Krucza ? uldk.Wait(token, ignoreCancellation: true) : Task.FromResult<UldkParcel?>(Parcel("second"));
        rig.Rcn.OnCall = (query, token) => rig.Rcn.Queries.Count == 1 ? rcn.Wait(token, ignoreCancellation: true) : ScriptedRcn.OneFlat(query, token);
        var first = rig.Controller.PickPointAsync(Krucza);
        await uldk.Entered;
        await rcn.Entered;
        await rig.Controller.PickPointAsync(Gdansk);
        var shown = rig.View;

        if (staleSearchFails)
        {
            uldk.Fail(new HttpRequestException(Detail));
            rcn.Fail(new QuotaExceededException(Detail));
        }
        else
        {
            uldk.Release(Parcel("stale"));
            rcn.Release(new RcnTransactions([], false));
        }

        await Ends(first);

        Assert.Same(shown, rig.View);
        Assert.DoesNotContain(rig.Views, v => v.Message is not null || v.Parcel?.Parcel?.Number == "stale" || v.Sales?.Outcome == SalesOutcome.NoSales);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_cancelled_address_search_never_overwrites_the_view(bool geocodingFails)
    {
        // The typed search is superseded by a click while the geocoder is still working; the geocoder ignores its token and answers late.
        var rig = new Rig();
        var uug = new Gate<IReadOnlyList<UugMatch>>();
        rig.Uug.OnCall = (_, token) => uug.Wait(token, ignoreCancellation: true);
        var typed = rig.Controller.SubmitAddressAsync("Warszawa, Krucza 50");
        await uug.Entered;
        await rig.Controller.PickPointAsync(Gdansk);
        var shown = rig.View;

        if (geocodingFails)
        {
            uug.Fail(new HttpRequestException(Detail));
        }
        else
        {
            uug.Release([Address()]);
        }

        await Ends(typed);

        Assert.True(uug.Token.IsCancellationRequested);
        Assert.Same(shown, rig.View);
        Assert.Equal(Gdansk, Assert.Single(rig.Uldk.Calls)); // the stale address was not followed up
        Assert.Single(rig.Rcn.Queries);
        Assert.DoesNotContain(rig.Views, v => v.Message is not null);
    }

    [Fact]
    public async Task A_typed_search_that_finishes_while_it_is_being_cancelled_does_not_write_the_view()
    {
        // The geocoder fake answers from inside the cancellation callback, so the superseded typed search resumes while the new search is still starting.
        var rig = new Rig();
        rig.Uug.OnCall = (_, token) =>
        {
            var answer = new TaskCompletionSource<IReadOnlyList<UugMatch>>();
            token.Register(() => answer.TrySetResult([Address()]));
            return answer.Task;
        };
        var typed = rig.Controller.SubmitAddressAsync("Warszawa, Krucza 50");

        await rig.Controller.PickPointAsync(Gdansk);
        await Ends(typed);

        Assert.Equal(Gdansk, Assert.Single(rig.Uldk.Calls)); // the stale address was not followed up
        Assert.DoesNotContain(rig.Views, v => v.Place?.Point == Krucza);
        Assert.Equal("#lat=54.35200&lon=18.64660&r=500&m=24", rig.Controller.CurrentFragment());
    }

    [Fact]
    public async Task A_search_that_finishes_while_it_is_being_cancelled_does_not_write_the_view()
    {
        // The fake answers from inside the cancellation callback, so the superseded search resumes while the new one is still starting.
        var rig = new Rig();
        rig.Uldk.OnCall = (point, token) =>
        {
            if (point != Krucza)
            {
                return Task.FromResult<UldkParcel?>(Parcel("second"));
            }

            var answer = new TaskCompletionSource<UldkParcel?>();
            token.Register(() => answer.TrySetResult(Parcel("stale")));
            return answer.Task;
        };
        var first = rig.Controller.PickPointAsync(Krucza);

        await rig.Controller.PickPointAsync(Gdansk);
        await Ends(first);

        Assert.DoesNotContain(rig.Views, v => v.Parcel?.Parcel?.Number == "stale");
        Assert.Equal("second", rig.View.Parcel!.Parcel!.Number);
    }

    [Fact]
    public async Task A_cache_hit_still_supersedes_a_search_in_flight()
    {
        var rig = new Rig();
        await rig.Controller.PickPointAsync(Krucza);
        var gate = new Gate<RcnTransactions>();
        rig.Rcn.OnCall = (_, token) => gate.Wait(token, ignoreCancellation: true);
        var slow = rig.Controller.ChangeRadiusAsync(1000);
        await gate.Entered;

        await Ends(rig.Controller.ChangeRadiusAsync(500)); // answered from the cache: no request
        gate.Release(new RcnTransactions([], false)); // the 1000 m answer arrives late
        await Ends(slow);

        Assert.True(gate.Token.IsCancellationRequested);
        Assert.Equal(2, rig.Rcn.Queries.Count);
        Assert.Equal(500, rig.View.Place!.RadiusMeters);
        Assert.Equal(SalesOutcome.Ok, rig.View.Sales!.Outcome);
    }

    [Fact]
    public async Task Disposing_the_controller_cancels_the_search_in_flight()
    {
        var rig = new Rig();
        var gate = new Gate<UldkParcel?>();
        rig.Uldk.OnCall = (_, token) => gate.Wait(token, ignoreCancellation: true);
        var search = rig.Controller.PickPointAsync(Krucza);
        await gate.Entered;
        var shown = rig.View;

        rig.Controller.Dispose();
        gate.Release(Parcel("late"));
        await Ends(search);

        Assert.True(gate.Token.IsCancellationRequested);
        Assert.Same(shown, rig.View);
    }

    [Fact]
    public async Task A_click_outside_Poland_supersedes_the_search_in_flight()
    {
        var rig = new Rig();
        var gate = new Gate<UldkParcel?>();
        rig.Uldk.OnCall = (_, token) => gate.Wait(token, ignoreCancellation: true);
        var search = rig.Controller.PickPointAsync(Krucza);
        await gate.Entered;

        await rig.Controller.PickPointAsync(default);
        gate.Release(Parcel("stale"));
        await Ends(search);

        Assert.True(gate.Token.IsCancellationRequested);
        Assert.Equal(DemoPhase.Message, rig.View.Phase);
        Assert.Equal(OutsidePoland, rig.View.Message);
    }

    // ---- one request per distinct question ----

    [Fact]
    public async Task The_parcel_and_sales_requests_run_concurrently()
    {
        var rig = new Rig();
        var uldk = new Gate<UldkParcel?>();
        var rcn = new Gate<RcnTransactions>();
        rig.Uldk.OnCall = (_, token) => uldk.Wait(token);
        rig.Rcn.OnCall = (_, token) => rcn.Wait(token);

        var search = rig.Controller.PickPointAsync(Krucza);
        await uldk.Entered;

        // The sales request went out while the parcel request is still open.
        Assert.Single(rig.Rcn.Queries);
        Assert.Equal(DemoPhase.Searching, rig.View.Phase);

        uldk.Release(Parcel("1"));
        rcn.Release(await ScriptedRcn.OneFlat(rig.Rcn.Queries[0], CancellationToken.None));
        await Ends(search);

        Assert.Equal(DemoPhase.Result, rig.View.Phase);
        Assert.Single(rig.Uldk.Calls);
        Assert.Single(rig.Rcn.Queries);
    }

    [Fact]
    public async Task Changing_the_radius_queries_only_RCN()
    {
        var rig = new Rig();
        await rig.Controller.PickPointAsync(Krucza);
        var parcel = rig.View.Parcel;

        await rig.Controller.ChangeRadiusAsync(1000);

        Assert.Single(rig.Uldk.Calls);
        Assert.Equal(2, rig.Rcn.Queries.Count);
        Assert.Equal(2000, SquareWidth(rig.Rcn.Queries[1]), 3);
        Assert.Same(parcel, rig.View.Parcel);
        Assert.Equal(1000, rig.View.Place!.RadiusMeters);
        Assert.Equal(1000, rig.Controller.RadiusMeters);
        Assert.Equal(DemoPhase.Result, rig.View.Phase);
        // The parcel stays on screen while the new sales are fetched.
        Assert.Same(parcel, rig.Views[^2].Parcel);
        Assert.Equal(DemoPhase.Searching, rig.Views[^2].Phase);
        Assert.Null(rig.Views[^2].Sales);
    }

    [Fact]
    public async Task Changing_the_period_queries_only_RCN()
    {
        var rig = new Rig();
        await rig.Controller.PickPointAsync(Krucza);
        var parcel = rig.View.Parcel;

        await rig.Controller.ChangePeriodAsync(12);

        Assert.Single(rig.Uldk.Calls);
        Assert.Equal(2, rig.Rcn.Queries.Count);
        Assert.Equal(Today.AddMonths(-12), rig.Rcn.Queries[1].From);
        Assert.Same(parcel, rig.View.Parcel);
        Assert.Equal(12, rig.View.Place!.PeriodMonths);
        Assert.Equal(12, rig.Controller.PeriodMonths);
    }

    [Fact]
    public async Task The_same_place_is_not_fetched_twice()
    {
        var rig = new Rig();
        await rig.Controller.PickPointAsync(Krucza);
        var sales500 = rig.View.Sales;

        await rig.Controller.ChangeRadiusAsync(1000);
        Assert.Equal(2, rig.Rcn.Queries.Count);
        await rig.Controller.ChangeRadiusAsync(500);

        // One RCN request per distinct radius, one ULDK request in all, and the cached answer is the very same object.
        Assert.Equal(2, rig.Rcn.Queries.Count);
        Assert.Single(rig.Uldk.Calls);
        Assert.Same(sales500, rig.View.Sales);

        await rig.Controller.ChangePeriodAsync(12);
        await rig.Controller.ChangePeriodAsync(24);
        await rig.Controller.PickPointAsync(Krucza);

        Assert.Equal(3, rig.Rcn.Queries.Count);
        Assert.Single(rig.Uldk.Calls);
        Assert.Same(sales500, rig.View.Sales);
    }

    [Fact]
    public async Task A_point_in_the_same_five_decimal_cell_is_the_same_place()
    {
        var rig = new Rig();
        await rig.Controller.PickPointAsync(new Wgs84Point(52.22850004, 21.01720003));
        await rig.Controller.PickPointAsync(new Wgs84Point(52.22849996, 21.01719998));

        Assert.Single(rig.Uldk.Calls);
        Assert.Single(rig.Rcn.Queries);
        Assert.Equal(KruczaFragment, rig.Controller.CurrentFragment());
    }

    [Fact]
    public async Task A_point_that_differs_in_the_fifth_decimal_is_another_place()
    {
        var rig = new Rig();
        await rig.Controller.PickPointAsync(Krucza);
        await rig.Controller.PickPointAsync(new Wgs84Point(52.22851, 21.0172));

        Assert.Equal(2, rig.Uldk.Calls.Count);
        Assert.Equal(2, rig.Rcn.Queries.Count);
    }

    // ---- a place that is still loading is not asked for twice ----

    [Fact]
    public async Task Changing_the_radius_while_the_parcel_is_loading_does_not_ask_ULDK_again()
    {
        var rig = new Rig();
        var uldk = new Gate<UldkParcel?>();
        rig.Uldk.OnCall = (_, token) => uldk.Wait(token);
        var pick = rig.Controller.PickPointAsync(Krucza);
        await uldk.Entered;

        var change = rig.Controller.ChangeRadiusAsync(1000);
        uldk.Release(Parcel("1"));
        await Ends(pick);
        await Ends(change);

        Assert.Single(rig.Uldk.Calls);
        Assert.False(uldk.Token.IsCancellationRequested);
        Assert.Equal(2, rig.Rcn.Queries.Count);
        Assert.Equal(2000, SquareWidth(rig.Rcn.Queries[1]), 3);
        Assert.Equal(DemoPhase.Result, rig.View.Phase);
        Assert.Equal(1000, rig.View.Place!.RadiusMeters);
        Assert.Equal("1", rig.View.Parcel!.Parcel!.Number);
        Assert.Equal(SalesOutcome.Ok, rig.View.Sales!.Outcome);
    }

    [Fact]
    public async Task Changing_the_period_while_the_parcel_is_loading_does_not_ask_ULDK_again()
    {
        var rig = new Rig();
        var uldk = new Gate<UldkParcel?>();
        rig.Uldk.OnCall = (_, token) => uldk.Wait(token);
        var pick = rig.Controller.PickPointAsync(Krucza);
        await uldk.Entered;

        var change = rig.Controller.ChangePeriodAsync(12);
        uldk.Release(Parcel("1"));
        await Ends(pick);
        await Ends(change);

        Assert.Single(rig.Uldk.Calls);
        Assert.False(uldk.Token.IsCancellationRequested);
        Assert.Equal(2, rig.Rcn.Queries.Count);
        Assert.Equal(Today.AddMonths(-12), rig.Rcn.Queries[1].From);
        Assert.Equal(DemoPhase.Result, rig.View.Phase);
        Assert.Equal(12, rig.View.Place!.PeriodMonths);
        Assert.Equal("1", rig.View.Parcel!.Parcel!.Number);
    }

    [Fact]
    public async Task Opening_a_link_of_the_same_point_with_another_radius_while_loading_does_not_ask_ULDK_again()
    {
        var rig = new Rig();
        var uldk = new Gate<UldkParcel?>();
        rig.Uldk.OnCall = (_, token) => uldk.Wait(token);
        var pick = rig.Controller.PickPointAsync(Krucza);
        await uldk.Entered;

        var load = rig.Controller.LoadFromFragmentAsync("#lat=52.22850&lon=21.01720&r=1000&m=12");
        uldk.Release(Parcel("1"));
        await Ends(pick);
        await Ends(load);

        Assert.Single(rig.Uldk.Calls);
        Assert.False(uldk.Token.IsCancellationRequested);
        Assert.Equal(2, rig.Rcn.Queries.Count);
        Assert.Equal(DemoPhase.Result, rig.View.Phase);
        Assert.Equal("#lat=52.22850&lon=21.01720&r=1000&m=12", rig.Controller.CurrentFragment());
    }

    [Fact]
    public async Task Picking_the_same_place_twice_while_loading_asks_each_service_once()
    {
        // A double click: the second search joins the requests of the first, which are still running, instead of cancelling and repeating them.
        var rig = new Rig();
        var uldk = new Gate<UldkParcel?>();
        var rcn = new Gate<RcnTransactions>();
        rig.Uldk.OnCall = (_, token) => uldk.Wait(token);
        rig.Rcn.OnCall = (_, token) => rcn.Wait(token);
        var first = rig.Controller.PickPointAsync(Krucza);
        await uldk.Entered;
        await rcn.Entered;

        var second = rig.Controller.PickPointAsync(new Wgs84Point(52.22850004, 21.01720003)); // the same 5-decimal cell
        uldk.Release(Parcel("1"));
        rcn.Release(await ScriptedRcn.OneFlat(rig.Rcn.Queries[0], CancellationToken.None));
        await Ends(first);
        await Ends(second);

        Assert.Single(rig.Uldk.Calls);
        Assert.Single(rig.Rcn.Queries);
        Assert.False(uldk.Token.IsCancellationRequested);
        Assert.False(rcn.Token.IsCancellationRequested);
        Assert.Equal(DemoPhase.Result, rig.View.Phase);
        Assert.Equal(KruczaFragment, rig.Controller.CurrentFragment());
    }

    [Fact]
    public async Task Picking_another_point_while_the_parcel_is_loading_cancels_that_request_and_starts_a_new_one()
    {
        var rig = new Rig();
        var uldk = new Gate<UldkParcel?>();
        rig.Uldk.OnCall = (point, token) => point == Krucza ? uldk.Wait(token) : Task.FromResult<UldkParcel?>(Parcel("gdansk"));
        var first = rig.Controller.PickPointAsync(Krucza);
        await uldk.Entered;

        await rig.Controller.PickPointAsync(Gdansk);
        await Ends(first);

        Assert.True(uldk.Token.IsCancellationRequested);
        Assert.Equal(new[] { Krucza, Gdansk }, rig.Uldk.Calls);
        Assert.Equal("gdansk", rig.View.Parcel!.Parcel!.Number);
        Assert.Equal(DemoPhase.Result, rig.View.Phase);
    }

    [Fact]
    public async Task A_cancelled_parcel_request_is_not_remembered()
    {
        var rig = new Rig();
        var uldk = new Gate<UldkParcel?>();
        rig.Uldk.OnCall = (point, token) => rig.Uldk.Calls.Count == 1 ? uldk.Wait(token) : ScriptedUldk.SomeParcel(point, token);
        var first = rig.Controller.PickPointAsync(Krucza);
        await uldk.Entered;
        await rig.Controller.PickPointAsync(Gdansk); // cancels the first request
        await Ends(first);

        await rig.Controller.PickPointAsync(Krucza);

        Assert.Equal(new[] { Krucza, Gdansk, Krucza }, rig.Uldk.Calls);
        Assert.Equal(DemoPhase.Result, rig.View.Phase);
        Assert.Equal(KruczaFragment, rig.Controller.CurrentFragment());
    }

    [Fact]
    public async Task A_failed_parcel_request_is_not_remembered_by_the_next_search_of_the_place()
    {
        var rig = new Rig();
        var uldk = new Gate<UldkParcel?>();
        rig.Uldk.OnCall = (point, token) => rig.Uldk.Calls.Count == 1 ? uldk.Wait(token) : ScriptedUldk.SomeParcel(point, token);
        var first = rig.Controller.PickPointAsync(Krucza);
        await uldk.Entered;
        uldk.Fail(new HttpRequestException(Detail));
        await Ends(first);
        Assert.Equal(DemoPhase.Message, rig.View.Phase);

        await rig.Controller.PickPointAsync(Krucza);

        Assert.Equal(2, rig.Uldk.Calls.Count);
        Assert.Equal(DemoPhase.Result, rig.View.Phase);
    }

    [Fact]
    public async Task The_point_is_rounded_to_five_decimals_once_and_used_everywhere()
    {
        var rig = new Rig();

        await rig.Controller.PickPointAsync(new Wgs84Point(52.2285012, 21.0172098));

        // The request, the view and the link all carry the same rounded point, so the link reproduces exactly this search.
        Assert.Equal(new Wgs84Point(52.22850, 21.01721), rig.Uldk.Calls[0]);
        Assert.Equal(new Wgs84Point(52.22850, 21.01721), rig.View.Place!.Point);
        var fragment = rig.Controller.CurrentFragment();
        Assert.Equal("#lat=52.22850&lon=21.01721&r=500&m=24", fragment);
        var other = new Rig();
        await other.Controller.LoadFromFragmentAsync(fragment);
        Assert.Equal(fragment, other.Controller.CurrentFragment());
        Assert.Equal(rig.Uldk.Calls[0], other.Uldk.Calls[0]);
    }

    [Fact]
    public async Task A_pick_keeps_the_chosen_radius_and_period()
    {
        var rig = new Rig();

        await rig.Controller.ChangeRadiusAsync(1000); // no place yet: remembered, nothing asked
        await rig.Controller.ChangePeriodAsync(60);
        rig.ExpectNoRequests();
        Assert.Equal(DemoPhase.Idle, rig.View.Phase);

        await rig.Controller.PickPointAsync(Krucza);
        await rig.Controller.PickPointAsync(Gdansk);

        Assert.Equal(1000, rig.View.Place!.RadiusMeters);
        Assert.Equal(60, rig.View.Place.PeriodMonths);
        Assert.All(rig.Rcn.Queries, q => Assert.Equal(2000, SquareWidth(q), 3));
        Assert.All(rig.Rcn.Queries, q => Assert.Equal(Today.AddMonths(-60), q.From));
    }

    [Fact]
    public async Task A_radius_or_period_the_page_does_not_offer_is_a_caller_bug()
    {
        var rig = new Rig();
        await rig.Controller.PickPointAsync(Krucza);
        var before = rig.View;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => rig.Controller.ChangeRadiusAsync(123));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => rig.Controller.ChangePeriodAsync(7));

        Assert.Same(before, rig.View);
        Assert.Equal(500, rig.Controller.RadiusMeters);
        Assert.Equal(24, rig.Controller.PeriodMonths);
        Assert.Single(rig.Rcn.Queries);
    }

    // ---- links ----

    [Fact]
    public async Task A_shared_link_makes_no_geocoder_request()
    {
        var rig = new Rig();

        await rig.Controller.LoadFromFragmentAsync("#lat=52.22850&lon=21.01720&r=1000&m=12&q=Warszawa%2C%20Krucza%2050");

        Assert.Empty(rig.Uug.Texts);
        Assert.Single(rig.Uldk.Calls);
        Assert.Single(rig.Rcn.Queries);
        Assert.Equal(DemoPhase.Result, rig.View.Phase);
        Assert.Equal("Warszawa, Krucza 50", rig.View.Label);
        Assert.Equal("Warszawa, Krucza 50", rig.View.Place!.Label);
        Assert.Equal(1000, rig.View.Place.RadiusMeters);
        Assert.Equal(12, rig.View.Place.PeriodMonths);
        Assert.Equal(Today.AddMonths(-12), rig.Rcn.Queries[0].From);
        Assert.Equal("#lat=52.22850&lon=21.01720&r=1000&m=12&q=Warszawa%2C%20Krucza%2050", rig.Controller.CurrentFragment());

        // The label stays with the place when the radius changes.
        await rig.Controller.ChangeRadiusAsync(250);
        Assert.Equal("#lat=52.22850&lon=21.01720&r=250&m=12&q=Warszawa%2C%20Krucza%2050", rig.Controller.CurrentFragment());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#")]
    [InlineData("garbage")]
    [InlineData("#lat=0&lon=0")]
    [InlineData("#lat=52.2285")]
    [InlineData("#lat=52.2285&lon=21.0172&r=777")]
    [InlineData("#lat=52.2285&lat=52.2286&lon=21.0172")]
    public async Task An_invalid_link_leaves_the_controller_idle_and_asks_nothing(string? fragment)
    {
        var rig = new Rig();

        await rig.Controller.LoadFromFragmentAsync(fragment);

        Assert.Same(DemoView.Idle, rig.View);
        Assert.Empty(rig.Views);
        rig.ExpectNoRequests();
        Assert.Equal(string.Empty, rig.Controller.CurrentFragment());
    }

    [Fact]
    public async Task An_invalid_link_changes_nothing_once_there_is_a_result()
    {
        var rig = new Rig();
        await rig.Controller.PickPointAsync(Krucza);
        var before = rig.View;

        await rig.Controller.LoadFromFragmentAsync("#nonsense");

        Assert.Same(before, rig.View);
        Assert.Single(rig.Uldk.Calls);
    }

    [Fact]
    public async Task Opening_the_link_of_the_place_already_shown_does_nothing()
    {
        // Compared as fragments: the point of the parsed link equals the controller's only up to the rounding.
        var rig = new Rig();
        var gate = new Gate<UldkParcel?>();
        rig.Uldk.OnCall = (_, token) => gate.Wait(token);
        var search = rig.Controller.PickPointAsync(new Wgs84Point(52.2285004, 21.0172002));
        await gate.Entered;
        var changes = rig.Views.Count;

        await rig.Controller.LoadFromFragmentAsync(rig.Controller.CurrentFragment()); // while searching: the search goes on, uncancelled

        Assert.False(gate.Token.IsCancellationRequested);
        Assert.Equal(changes, rig.Views.Count);
        gate.Release(Parcel("1"));
        await Ends(search);
        changes = rig.Views.Count;

        await rig.Controller.LoadFromFragmentAsync(KruczaFragment); // once shown: nothing
        await rig.Controller.LoadFromFragmentAsync("#lat=52.2285003&lon=21.0172004&r=500&m=24"); // the same link, written with more decimals

        Assert.Equal(changes, rig.Views.Count);
        Assert.Single(rig.Uldk.Calls);
        Assert.Single(rig.Rcn.Queries);
    }

    [Fact]
    public async Task Opening_a_link_with_another_radius_or_label_shows_that_place()
    {
        var rig = new Rig();
        await rig.Controller.PickPointAsync(Krucza);

        await rig.Controller.LoadFromFragmentAsync("#lat=52.22850&lon=21.01720&r=1000&m=24");
        Assert.Equal(1000, rig.View.Place!.RadiusMeters);
        Assert.Equal(2, rig.Rcn.Queries.Count);

        await rig.Controller.LoadFromFragmentAsync("#lat=52.22850&lon=21.01720&r=1000&m=24&q=Home");
        Assert.Equal("Home", rig.View.Label);
        Assert.Equal(2, rig.Rcn.Queries.Count); // the label is no new question
        Assert.Single(rig.Uldk.Calls);
    }

    [Fact]
    public async Task The_link_of_a_failed_place_is_not_retried_by_itself_but_picking_the_place_again_retries()
    {
        // A page that rewrites its URL and reacts to the change calls LoadFromFragmentAsync with the very fragment that just failed (a 429, say):
        // that must not send a request nobody asked for, or a quota error would loop.
        var rig = new Rig();
        rig.Rcn.OnCall = (_, _) => Task.FromException<RcnTransactions>(Make("quota"));
        await rig.Controller.LoadFromFragmentAsync(KruczaFragment + "&q=Home");
        Assert.Equal(DemoPhase.Message, rig.View.Phase);
        Assert.Equal("GUGiK is limiting requests right now. Wait a minute and try again.", rig.View.Message);
        var failed = rig.View;
        var changes = rig.Views.Count;

        await rig.Controller.LoadFromFragmentAsync(rig.Controller.CurrentFragment());
        await rig.Controller.LoadFromFragmentAsync(KruczaFragment + "&q=Home");

        Assert.Same(failed, rig.View);
        Assert.Equal(changes, rig.Views.Count);
        Assert.Single(rig.Uldk.Calls);
        Assert.Single(rig.Rcn.Queries);

        // The user asks again: that is a retry, and only the request that failed is repeated.
        rig.Rcn.OnCall = ScriptedRcn.OneFlat;
        await rig.Controller.PickPointAsync(rig.View.Place!.Point);

        Assert.Equal(DemoPhase.Result, rig.View.Phase);
        Assert.Single(rig.Uldk.Calls);
        Assert.Equal(2, rig.Rcn.Queries.Count);
    }

    // ---- typed addresses ----

    [Fact]
    public async Task A_typed_address_makes_one_geocoder_request_then_the_place_requests()
    {
        var rig = new Rig();
        var gate = new Gate<IReadOnlyList<UugMatch>>();
        rig.Uug.OnCall = (_, token) => gate.Wait(token);

        var submit = rig.Controller.SubmitAddressAsync("Warszawa, Krucza 50");
        await gate.Entered;

        // First the geocoder alone.
        Assert.Equal(DemoPhase.Searching, rig.View.Phase);
        Assert.Null(rig.View.Place);
        Assert.Null(rig.View.Label); // the typed text is never echoed
        Assert.Empty(rig.Uldk.Calls);
        Assert.Empty(rig.Rcn.Queries);

        gate.Release([Address()]);
        await Ends(submit);

        Assert.Equal("Warszawa, Krucza 50", Assert.Single(rig.Uug.Texts));
        Assert.Equal(Krucza, Assert.Single(rig.Uldk.Calls));
        Assert.Single(rig.Rcn.Queries);
        Assert.Equal(DemoPhase.Result, rig.View.Phase);
        Assert.Equal("ulica Krucza 50, Warszawa", rig.View.Label);
        Assert.Equal(KruczaFragment + "&q=ulica%20Krucza%2050%2C%20Warszawa", rig.Controller.CurrentFragment());
        Assert.Equal(new[] { DemoPhase.Searching, DemoPhase.Searching, DemoPhase.Result }, rig.Phases);
        Assert.Null(rig.Views[0].Place);
        Assert.NotNull(rig.Views[1].Place);
    }

    [Fact]
    public async Task Candidates_wait_for_a_pick()
    {
        var rig = new Rig();
        var matches = new[] { Address("50"), Address("52"), Street() };
        rig.Uug.OnCall = (_, _) => Matches(matches);

        await rig.Controller.SubmitAddressAsync("Warszawa, Krucza");

        Assert.Equal(DemoPhase.Candidates, rig.View.Phase);
        Assert.Equal(matches, rig.View.Candidates);
        Assert.False(string.IsNullOrWhiteSpace(rig.View.Message));
        Assert.Null(rig.View.Place);
        Assert.Empty(rig.Uldk.Calls);
        Assert.Empty(rig.Rcn.Queries);
        Assert.Equal(string.Empty, rig.Controller.CurrentFragment());

        await rig.Controller.PickCandidateAsync(matches[1]);

        Assert.Equal(DemoPhase.Result, rig.View.Phase);
        Assert.Single(rig.Uug.Texts);
        Assert.Single(rig.Uldk.Calls);
        Assert.Single(rig.Rcn.Queries);
        Assert.Equal("ulica Krucza 52, Warszawa", rig.View.Label);
        Assert.Empty(rig.View.Candidates);
        Assert.Null(rig.View.Message);
    }

    [Fact]
    public async Task A_street_candidate_is_labelled_without_a_number()
    {
        var rig = new Rig();

        await rig.Controller.PickCandidateAsync(Street());

        Assert.Equal("ulica Krucza, Warszawa", rig.View.Label);
    }

    [Fact]
    public async Task A_KW_number_is_refused_with_no_request_at_all()
    {
        var rig = new Rig();

        await rig.Controller.SubmitAddressAsync("WL1A/00272852/9");

        Assert.Equal(DemoPhase.Message, rig.View.Phase);
        Assert.Equal("This demo never accepts land-registry (KW) numbers.", rig.View.Message);
        Assert.DoesNotContain(rig.Views, v => v.Label is not null || v.Place is not null);
        rig.ExpectNoRequests();
    }

    [Fact]
    public async Task No_match_gives_a_message_and_no_further_request()
    {
        var rig = new Rig();

        await rig.Controller.SubmitAddressAsync("Nowhere 1");

        Assert.Equal(DemoPhase.Message, rig.View.Phase);
        Assert.Equal("No address matched; check the spelling or add the town.", rig.View.Message);
        Assert.Single(rig.Uug.Texts);
        Assert.Empty(rig.Uldk.Calls);
        Assert.Empty(rig.Rcn.Queries);
    }

    // ---- results ----

    [Fact]
    public async Task No_parcel_still_shows_the_sales()
    {
        var rig = new Rig();
        rig.Uldk.OnCall = (_, _) => Task.FromResult<UldkParcel?>(null);

        await rig.Controller.PickPointAsync(Krucza);

        Assert.Equal(DemoPhase.Result, rig.View.Phase);
        Assert.Null(rig.View.Parcel!.Parcel);
        Assert.Null(rig.View.Parcel.OutlineGeoJson);
        Assert.Equal(SalesOutcome.Ok, rig.View.Sales!.Outcome);
        Assert.Equal(1, rig.View.Sales.TotalCount);
        Assert.Null(rig.View.Message);
    }

    [Fact]
    public async Task Too_many_sales_shows_the_narrow_message()
    {
        var rig = new Rig();
        rig.Rcn.OnCall = (_, _) => Task.FromResult(ScriptedRcn.Truncated());

        await rig.Controller.PickPointAsync(Krucza);

        // A result, not "no sales": the page tells the user to narrow the search. The count is 0 there and must not be read as an answer.
        Assert.Equal(DemoPhase.Result, rig.View.Phase);
        Assert.Equal(SalesOutcome.TooManySales, rig.View.Sales!.Outcome);
        Assert.Equal(0, rig.View.Sales.TotalCount);
        Assert.NotNull(rig.View.Parcel!.Parcel);
        Assert.Null(rig.View.Message);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(21.0172, 52.2285)] // swapped axes
    [InlineData(double.NaN, 21.0172)]
    [InlineData(52.2285, double.PositiveInfinity)]
    [InlineData(91.0, 200.0)]
    [InlineData(48.8, 21.0)]
    public async Task A_click_outside_Poland_gives_a_message(double latitude, double longitude)
    {
        var rig = new Rig();

        await rig.Controller.PickPointAsync(latitude, longitude);

        Assert.Equal(DemoPhase.Message, rig.View.Phase);
        Assert.Equal(OutsidePoland, rig.View.Message);
        Assert.Null(rig.View.Place);
        Assert.Equal(string.Empty, rig.Controller.CurrentFragment());
        rig.ExpectNoRequests();
    }

    [Fact]
    public async Task A_default_point_is_outside_Poland()
    {
        var rig = new Rig();

        // default(Wgs84Point) skips the validating constructor: 0, 0.
        await rig.Controller.PickPointAsync(default);

        Assert.Equal(OutsidePoland, rig.View.Message);
        rig.ExpectNoRequests();
    }

    [Fact]
    public async Task A_click_by_coordinates_in_Poland_searches()
    {
        var rig = new Rig();

        await rig.Controller.PickPointAsync(52.2285, 21.0172);

        Assert.Equal(DemoPhase.Result, rig.View.Phase);
        Assert.Equal(KruczaFragment, rig.Controller.CurrentFragment());
        Assert.Equal(Krucza, Assert.Single(rig.Uldk.Calls));
    }

    // ---- failures ----

    private static readonly (string Kind, string Message)[] FailureKinds =
    [
        ("quota", "GUGiK is limiting requests right now. Wait a minute and try again."),
        ("blocked", "GUGiK's service refused the request. Try again in a few minutes."),
        ("api", "A GUGiK service answered with an error. Try again later."),
        ("http", "A GUGiK service is not reachable right now. Check your connection and try again."),
        ("timeout", "A GUGiK service took too long to answer. Try again."),
        ("other", "Something went wrong. Try again."),
    ];

    public static TheoryData<string, string, string> Failures()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var stage in new[] { "uldk", "rcn", "uug" })
        {
            foreach (var (kind, message) in FailureKinds)
            {
                data.Add(stage, kind, message);
            }
        }

        return data;
    }

    private static Exception Make(string kind) => kind switch
    {
        "quota" => new QuotaExceededException(Detail, HttpStatusCode.TooManyRequests, null, null, TimeSpan.FromSeconds(30)),
        "blocked" => new PolishOpenDataApiException(Detail, HttpStatusCode.OK, "blocked", Detail, isTransient: true),
        "api" => new PolishOpenDataApiException(Detail, HttpStatusCode.BadRequest, "uldk_error", Detail),
        "http" => new HttpRequestException(Detail),
        // What HttpClient throws when its timeout ends: the caller's token is not cancelled.
        "timeout" => new TaskCanceledException(Detail, new TimeoutException(Detail)),
        _ => new InvalidOperationException(Detail),
    };

    private static void Break(Rig rig, string stage, Exception exception)
    {
        switch (stage)
        {
            case "uldk":
                rig.Uldk.OnCall = (_, _) => Task.FromException<UldkParcel?>(exception);
                break;
            case "rcn":
                rig.Rcn.OnCall = (_, _) => Task.FromException<RcnTransactions>(exception);
                break;
            default:
                rig.Uug.OnCall = (_, _) => Task.FromException<IReadOnlyList<UugMatch>>(exception);
                break;
        }
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task Service_errors_become_messages(string stage, string kind, string expected)
    {
        var rig = new Rig();
        Break(rig, stage, Make(kind));

        // Nothing propagates: the awaited call completes and the view says what happened.
        if (stage == "uug")
        {
            await rig.Controller.SubmitAddressAsync("Warszawa, Krucza 50");
        }
        else
        {
            await rig.Controller.PickPointAsync(Krucza);
        }

        Assert.Equal(DemoPhase.Message, rig.View.Phase);
        Assert.Equal(expected, rig.View.Message);
        Assert.DoesNotContain(Detail, rig.View.Message, StringComparison.Ordinal);
        Assert.Null(rig.View.Parcel);
        Assert.Null(rig.View.Sales);
        Assert.Equal(DemoPhase.Searching, rig.Views[0].Phase);
        Assert.Equal(DemoPhase.Message, rig.Views[^1].Phase);
        // A failed place keeps its point, so the link and the radius and period choices still work; a failed geocoding has none.
        Assert.Equal(stage != "uug", rig.View.Place is not null);
    }

    [Fact]
    public async Task A_synchronous_throw_from_a_client_is_a_message_too()
    {
        var rig = new Rig();
        rig.Uldk.OnCall = (_, _) => throw new HttpRequestException(Detail);

        await rig.Controller.PickPointAsync(Krucza);

        Assert.Equal(DemoPhase.Message, rig.View.Phase);
        Assert.Equal("A GUGiK service is not reachable right now. Check your connection and try again.", rig.View.Message);
    }

    [Theory]
    [InlineData("uldk")]
    [InlineData("rcn")]
    public async Task One_failed_request_gives_a_message_and_a_retry_repeats_only_that_one(string failing)
    {
        // The rule: any failure of either request is a message for the whole place, and only what succeeded is cached.
        var rig = new Rig();
        Break(rig, failing, new HttpRequestException(Detail));

        await rig.Controller.PickPointAsync(Krucza);

        Assert.Equal(DemoPhase.Message, rig.View.Phase);
        Assert.Null(rig.View.Parcel);
        Assert.Null(rig.View.Sales);
        Assert.Single(rig.Uldk.Calls);
        Assert.Single(rig.Rcn.Queries);

        rig.Uldk.OnCall = ScriptedUldk.SomeParcel;
        rig.Rcn.OnCall = ScriptedRcn.OneFlat;
        await rig.Controller.PickPointAsync(rig.View.Place!.Point);

        Assert.Equal(DemoPhase.Result, rig.View.Phase);
        Assert.NotNull(rig.View.Parcel);
        Assert.NotNull(rig.View.Sales);
        Assert.Equal(failing == "uldk" ? 2 : 1, rig.Uldk.Calls.Count);
        Assert.Equal(failing == "rcn" ? 2 : 1, rig.Rcn.Queries.Count);
    }

    [Fact]
    public async Task A_failure_waits_for_the_other_request_so_that_its_answer_is_kept()
    {
        var rig = new Rig();
        var rcn = new Gate<RcnTransactions>();
        rig.Uldk.OnCall = (_, _) => Task.FromException<UldkParcel?>(new HttpRequestException(Detail));
        rig.Rcn.OnCall = (_, token) => rcn.Wait(token);
        var search = rig.Controller.PickPointAsync(Krucza);
        await rcn.Entered;

        Assert.Equal(DemoPhase.Searching, rig.View.Phase); // the failed half does not end the search before the other half has answered
        rcn.Release(await ScriptedRcn.OneFlat(rig.Rcn.Queries[0], CancellationToken.None));
        await Ends(search);

        Assert.Equal(DemoPhase.Message, rig.View.Phase);
        rig.Uldk.OnCall = ScriptedUldk.SomeParcel;
        await rig.Controller.PickPointAsync(Krucza);
        Assert.Single(rig.Rcn.Queries);
        Assert.Equal(DemoPhase.Result, rig.View.Phase);
    }

    [Fact]
    public async Task When_both_requests_fail_the_parcel_error_is_the_one_told()
    {
        var rig = new Rig();
        Break(rig, "uldk", Make("quota"));
        Break(rig, "rcn", Make("http"));

        await rig.Controller.PickPointAsync(Krucza);

        Assert.Equal("GUGiK is limiting requests right now. Wait a minute and try again.", rig.View.Message);
    }

    [Fact]
    public async Task A_failed_place_can_be_retried_with_another_radius()
    {
        var rig = new Rig();
        Break(rig, "rcn", Make("http"));
        await rig.Controller.PickPointAsync(Krucza);
        rig.Rcn.OnCall = ScriptedRcn.OneFlat;

        await rig.Controller.ChangeRadiusAsync(1000);

        Assert.Equal(DemoPhase.Result, rig.View.Phase);
        Assert.Equal(1000, rig.View.Place!.RadiusMeters);
    }

    // ---- the link and the events ----

    [Fact]
    public async Task Fragment_reflects_the_current_place()
    {
        var rig = new Rig();
        Assert.Equal(string.Empty, rig.Controller.CurrentFragment());

        await rig.Controller.PickPointAsync(Krucza);
        Assert.Equal(KruczaFragment, rig.Controller.CurrentFragment());

        await rig.Controller.ChangeRadiusAsync(1000);
        Assert.Equal("#lat=52.22850&lon=21.01720&r=1000&m=24", rig.Controller.CurrentFragment());

        await rig.Controller.ChangePeriodAsync(60);
        Assert.Equal("#lat=52.22850&lon=21.01720&r=1000&m=60", rig.Controller.CurrentFragment());

        await rig.Controller.PickPointAsync(Gdansk);
        Assert.Equal("#lat=54.35200&lon=18.64660&r=1000&m=60", rig.Controller.CurrentFragment());

        await rig.Controller.PickPointAsync(0, 0);
        Assert.Equal(string.Empty, rig.Controller.CurrentFragment());
    }

    [Fact]
    public async Task Changed_is_raised_for_every_replacement_of_the_view()
    {
        var rig = new Rig();

        await rig.Controller.PickPointAsync(Krucza);
        Assert.Equal(new[] { DemoPhase.Searching, DemoPhase.Result }, rig.Phases);

        await rig.Controller.PickPointAsync(0, 0);
        Assert.Equal(new[] { DemoPhase.Searching, DemoPhase.Result, DemoPhase.Message }, rig.Phases);

        // A view is a snapshot: the ones already shown are not touched by later searches.
        Assert.Equal(DemoPhase.Searching, rig.Views[0].Phase);
        Assert.Equal(DemoPhase.Result, rig.Views[1].Phase);
        Assert.NotNull(rig.Views[1].Sales);
    }
}
