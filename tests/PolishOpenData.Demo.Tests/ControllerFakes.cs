using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PolishOpenData.Demo.Search;
using PolishOpenData.Geo;
using PolishOpenData.Rcn;
using PolishOpenData.Uldk;
using PolishOpenData.Uug;

namespace PolishOpenData.Demo.Tests;

// Fakes for the controller tests. PlaceSearch and AddressSearch are sealed, so the controller is built over these three client fakes, which count what they receive.
// Each one answers from a delegate, so a test can hold a call until it releases a Gate, fail it, or let it ignore its token. The older private fakes in
// PlaceSearchTests and AddressSearchTests only return completed tasks and are left as they are.

/// <summary>One call a test holds open: it is entered when the fake is called and ends when the test releases or fails it, or (by default) when the token is cancelled.</summary>
internal sealed class Gate<T>
{
    private readonly TaskCompletionSource<T> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when the fake received the call.</summary>
    public Task Entered => _entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); // a call that never comes fails the test instead of hanging it

    /// <summary>The token the call was made with.</summary>
    public CancellationToken Token { get; private set; }

    /// <summary>The call's task. With <paramref name="ignoreCancellation"/> a cancelled token changes nothing: the call still ends only when the test says so.</summary>
    public Task<T> Wait(CancellationToken token, bool ignoreCancellation = false)
    {
        Token = token;
        if (!ignoreCancellation)
        {
            token.Register(() => _result.TrySetCanceled(token));
        }

        _entered.TrySetResult();
        return _result.Task;
    }

    public void Release(T value) => _result.TrySetResult(value);

    public void Fail(Exception exception) => _result.TrySetException(exception);
}

internal sealed class ScriptedUldk : IUldkClient
{
    public List<Wgs84Point> Calls { get; } = [];

    /// <summary>The answer; by default <see cref="SomeParcel"/>.</summary>
    public Func<Wgs84Point, CancellationToken, Task<UldkParcel?>> OnCall { get; set; } = SomeParcel;

    /// <summary>A parcel without geometry.</summary>
    public static Task<UldkParcel?> SomeParcel(Wgs84Point point, CancellationToken cancellationToken) =>
        Task.FromResult<UldkParcel?>(new UldkParcel { Number = "12/3" });

    public Task<UldkParcel?> GetParcelAtAsync(Wgs84Point point, ParcelGeometryKind geometry = ParcelGeometryKind.None, CancellationToken cancellationToken = default)
    {
        Calls.Add(point);
        return OnCall(point, cancellationToken);
    }

    public Task<UldkParcel?> GetParcelByIdAsync(ParcelId id, ParcelGeometryKind geometry = ParcelGeometryKind.None, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<UldkParcel?> GetParcelAtAsync(Pl1992Point point, ParcelGeometryKind geometry = ParcelGeometryKind.None, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<UldkParcel>> FindParcelsAsync(string regionAndNumber, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

internal sealed class ScriptedRcn : IRcnClient
{
    public List<RcnQuery> Queries { get; } = [];

    /// <summary>The answer; by default <see cref="OneFlat"/>.</summary>
    public Func<RcnQuery, CancellationToken, Task<RcnTransactions>> OnCall { get; set; } = OneFlat;

    /// <summary>One market-price flat in the middle of the asked square, so a place always has one sale.</summary>
    public static Task<RcnTransactions> OneFlat(RcnQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(new RcnTransactions([FlatAtCentreOf(query)], false));

    public Task<RcnTransactions> GetTransactionsAsync(RcnQuery query, CancellationToken cancellationToken = default)
    {
        Queries.Add(query);
        return OnCall(query, cancellationToken);
    }

    public static RcnTransactions Truncated() => new([], true);

    private static RcnTransaction FlatAtCentreOf(RcnQuery query)
    {
        var area = query.Area!.Value;
        return new RcnTransaction
        {
            Layer = RcnLayer.Premises,
            Id = "row",
            Date = new DateOnly(2026, 9, 1),
            Location = new Pl1992Point((area.Min.Easting + area.Max.Easting) / 2, (area.Min.Northing + area.Max.Northing) / 2),
            Market = RcnMarket.Secondary,
            Kind = "wolnyRynek",
            Share = "1/1",
            Right = "wlasnoscLokaluWrazZPrawemZwiazanym",
            PremisesPrice = 600_000m,
            UsableArea = 50m,
            TotalPrice = 600_000m,
            Fields = new Dictionary<string, string>(),
        };
    }
}

internal sealed class ScriptedUug : IUugClient
{
    public List<string> Texts { get; } = [];

    /// <summary>The answer; by default no match.</summary>
    public Func<string, CancellationToken, Task<IReadOnlyList<UugMatch>>> OnCall { get; set; } = (_, _) => Task.FromResult<IReadOnlyList<UugMatch>>([]);

    public Task<IReadOnlyList<UugMatch>> GeocodeAsync(string address, CancellationToken cancellationToken = default)
    {
        Texts.Add(address);
        return OnCall(address, cancellationToken);
    }

    public Task<UugMatch?> ReverseGeocodeAsync(Wgs84Point point, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
