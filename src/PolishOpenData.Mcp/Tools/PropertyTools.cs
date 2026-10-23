using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PolishOpenData.Geo;
using PolishOpenData.Internal;
using PolishOpenData.Rcn;
using PolishOpenData.Uug;

namespace PolishOpenData.Mcp;

[McpServerToolType]
internal sealed class PropertyTools(IRcnClient rcn, IUugClient uug, TimeProvider timeProvider)
{
    private const string RcnName = "Rejestr Cen Nieruchomości (RCN), GUGiK";
    private const string RcnEndpoint = "https://mapy.geoportal.gov.pl/wss/service/rcn";

    private const int DefaultRadiusMeters = 500;
    private const int MinRadiusMeters = 50;
    private const int MaxRadiusMeters = 2500;
    private const int MaxListed = 50;

    private const string LagNote = "The RCN register is published with a delay of several months, so recent sales are missing; the newest transaction found is dated ";
    private const string NoTransactionsNote = "There are no transactions in this period; the RCN register is published with a delay of several months, so recent sales may not be in it yet.";
    private const string SampleNote = "Price per m² is the premises price divided by its usable area. Statistics use market sales of whole premises only: shares, non-market sales (such as auctions), other rights and rows without a price or area are excluded and counted.";
    private const string SmallSampleNote = "Fewer than 5 transactions are in the sample, so the figures are weak evidence.";
    private const string RepeatedPremisesTotalNote = "When several premises were sold in one deed, totalPrice is the price of the whole deed, repeated on each row; compare premises by price or pricePerSquareMetre instead.";
    private const string RepeatedTotalNote = "A transaction that covers several parcels or buildings repeats its total price on each row; do not add the rows up. No statistics are given for this kind.";

    [McpServerTool(Name = "property_prices", Title = "Property transaction prices", ReadOnly = true, OpenWorld = true, Idempotent = true, Destructive = false, UseStructuredContent = true, OutputSchemaType = typeof(PropertyPricesResult))]
    [Description("Answers what real estate sold for in a Polish area: the median and quartile price per square metre of flats (premises) and the single transactions, newest first, for premises, parcels or buildings. Data come from the RCN register (Rejestr Cen Nieruchomości) via GUGiK and are published with a delay of several months; the result says how old the newest transaction is. Single transactions are public register entries (address, price, deed number); buyers and sellers are shown only by kind. Informational, not a valuation. Search by exactly one of: an address (geocoded first; it must resolve to a single address point, otherwise the geocoder's candidates are returned), WGS-84 coordinates (latitude + longitude; latitude comes first) with a radius, or a powiat code. Never returns or accepts land-registry (KW) numbers or owner data.")]
    public async Task<CallToolResult> PropertyPrices(
        [Description("Address with a house number, e.g. \"Warszawa, Krucza 50\"; searches within radiusMeters of it. A street or town alone gives candidates, not prices.")] string? address = null,
        [Description("WGS-84 latitude in degrees north, 48.9 to 55.0 (give together with longitude).")] double? latitude = null,
        [Description("WGS-84 longitude in degrees east, 14.0 to 24.2 (give together with latitude).")] double? longitude = null,
        [Description("Four-digit TERYT powiat code, e.g. 1465 for Warsaw; searches the whole powiat (it may match too many transactions: narrow the period).")] string? powiat = null,
        [Description("Search radius in metres around the address or coordinates, 50 to 2500 (default 500). Not for a powiat search.")] int? radiusMeters = null,
        [Description("First day of the period, YYYY-MM-DD. Default: 24 months before dateTo.")] string? dateFrom = null,
        [Description("Last day of the period, YYYY-MM-DD. Default: today (Warsaw time).")] string? dateTo = null,
        [Description("premises (flats, garages, shops: default), parcels or buildings. Statistics are given for premises only."), DefaultValue("premises")] string? kind = null,
        [Description("any (default), primary (from a developer) or secondary (resale)."), DefaultValue("any")] string? market = null,
        [Description("For kind premises: residential, garage, commercial, other or any. Default: residential. Not valid for parcels or buildings.")] string? premisesFunction = null,
        [Description("How many single transactions to list, 0 to 50 (default 20); the statistics always cover all found.")] int maxTransactions = 20,
        CancellationToken cancellationToken = default)
    {
        // Checked first, before any request, and never echoed back. Shape only: a KW with a wrong check digit is refused too.
        if (IdentifierTools.LooksLikeKw(address))
        {
            return ToolResults.Error("property_prices never accepts land-registry (KW) numbers. Give an address, coordinates or a powiat code.");
        }

        if ((latitude is null) != (longitude is null))
        {
            return ToolResults.Error("Give latitude and longitude together.");
        }

        var byAddress = !string.IsNullOrWhiteSpace(address);
        var byPowiat = !string.IsNullOrWhiteSpace(powiat);
        if ((byAddress ? 1 : 0) + (byPowiat ? 1 : 0) + (latitude is null ? 0 : 1) != 1)
        {
            return ToolResults.Error("Give exactly one of: address, latitude+longitude, powiat.");
        }

        if (byPowiat)
        {
            if (radiusMeters is not null)
            {
                return ToolResults.Error("radiusMeters applies to address and coordinate searches, not to a powiat search.");
            }

            if (powiat!.Trim() is not { Length: 4 } code || code.Any(c => c is < '0' or > '9'))
            {
                return ToolResults.Error("powiat must be a four-digit TERYT powiat code, e.g. 1465 for Warsaw.");
            }

            powiat = code;
        }

        var radius = radiusMeters ?? DefaultRadiusMeters;
        if (!byPowiat && radius is < MinRadiusMeters or > MaxRadiusMeters)
        {
            return ToolResults.Error("radiusMeters must be " + MinRadiusMeters.ToString(CultureInfo.InvariantCulture) + " to " + MaxRadiusMeters.ToString(CultureInfo.InvariantCulture) + " metres.");
        }

        if (!ToolInput.TryParseDate(dateFrom, out var from, out var dateError) || !ToolInput.TryParseDate(dateTo, out var to, out dateError))
        {
            return ToolResults.Error(dateError!);
        }

        var end = to ?? WarsawTime.Today(timeProvider);
        var begin = from ?? end.AddMonths(-24);
        if (begin > end)
        {
            return ToolResults.Error("dateFrom is after dateTo.");
        }

        kind = (kind ?? "premises").Trim().ToLowerInvariant();
        market = (market ?? "any").Trim().ToLowerInvariant();
        var layer = kind switch
        {
            "premises" => RcnLayer.Premises,
            "parcels" => RcnLayer.Parcels,
            "buildings" => RcnLayer.Buildings,
            _ => (RcnLayer?)null,
        };
        if (layer is null)
        {
            return ToolResults.Error("kind must be premises, parcels or buildings.");
        }

        RcnMarket? rcnMarket;
        switch (market)
        {
            case "any": rcnMarket = null; break;
            case "primary": rcnMarket = RcnMarket.Primary; break;
            case "secondary": rcnMarket = RcnMarket.Secondary; break;
            default: return ToolResults.Error("market must be any, primary or secondary.");
        }

        RcnPremisesFunction? function = null;
        string? functionName = null;
        if (!string.IsNullOrWhiteSpace(premisesFunction))
        {
            if (layer != RcnLayer.Premises)
            {
                return ToolResults.Error("premisesFunction applies to kind premises only.");
            }

            functionName = premisesFunction.Trim().ToLowerInvariant();
            switch (functionName)
            {
                case "any": break;
                case "residential": function = RcnPremisesFunction.Residential; break;
                case "garage": function = RcnPremisesFunction.Garage; break;
                case "commercial": function = RcnPremisesFunction.Commercial; break;
                case "other": function = RcnPremisesFunction.Other; break;
                default: return ToolResults.Error("premisesFunction must be residential, garage, commercial, other or any.");
            }
        }
        else if (layer == RcnLayer.Premises)
        {
            function = RcnPremisesFunction.Residential;
            functionName = "residential";
        }

        if (maxTransactions is < 0 or > MaxListed)
        {
            return ToolResults.Error("maxTransactions must be 0 to " + MaxListed.ToString(CultureInfo.InvariantCulture) + ".");
        }

        // Where to search: a powiat, or a square around a centre that is trimmed to a circle afterwards.
        Wgs84Point? centre = null;
        GeocodeMatch? matched = null;
        SourceInfo? uugSource = null;
        string areaKind;
        if (byAddress)
        {
            var lookup = await AddressResolution.ResolveAsync(uug, address!, cancellationToken).ConfigureAwait(false);
            uugSource = AddressResolution.UugSource(timeProvider);   // when UUG answered, not when RCN does
            if (lookup.Point is not { } point)
            {
                return Result(
                    new AreaView { Kind = "address", RadiusMeters = radius },
                    begin, end, layer.Value, market, functionName,
                    statistics: null, [], 0, null, 0, null, [.. lookup.Matches.Select(AddressResolution.ToMatch)],
                    [AddressResolution.UnresolvedNote],
                    [uugSource]);
            }

            centre = point.Location;
            matched = AddressResolution.ToMatch(point);
            areaKind = "address";
        }
        else if (latitude is { } lat && longitude is { } lon)
        {
            try
            {
                centre = new Wgs84Point(lat, lon);
            }
            catch (ArgumentOutOfRangeException)
            {
                return ToolResults.Error("Coordinates must be WGS-84 within Poland: latitude 48.9 to 55.0 and longitude 14.0 to 24.2. "
                    + "Latitude comes first (Warsaw is about latitude 52.2, longitude 21.0); a swapped pair is rejected.");
            }

            areaKind = "point";
        }
        else
        {
            areaKind = "powiat";
        }

        var query = new RcnQuery
        {
            PowiatCode = powiat,
            From = begin,
            To = end,
            Layer = layer.Value,
            Market = rcnMarket,
            PremisesFunction = function,
        };
        Pl1992Point centrePl = default;
        if (centre is { } wgs)
        {
            try
            {
                centrePl = wgs.ToPl1992();
                query.Area = new Pl1992Bbox(
                    new Pl1992Point(centrePl.Easting - radius, centrePl.Northing - radius),
                    new Pl1992Point(centrePl.Easting + radius, centrePl.Northing + radius));
            }
            catch (ArgumentOutOfRangeException)
            {
                return ToolResults.Error("The search square around this point leaves the area the grid covers; try a smaller radius.");
            }
        }

        RcnTransactions found;
        try
        {
            found = await rcn.GetTransactionsAsync(query, cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentOutOfRangeException e) when (e.ParamName == nameof(RcnQuery.Area))
        {
            // The library refused the query before any request because of its area limit: a tool-level error, not an unexpected one.
            return ToolResults.Error("The search area is too large for the register; try a smaller radius.");
        }

        var rcnSource = new SourceInfo(RcnName, RcnEndpoint, timeProvider.GetUtcNow(), null);
        if (found.Truncated)
        {
            return ToolResults.Error("More than " + query.MaxRecords.ToString("N0", CultureInfo.InvariantCulture)
                + " transactions match, so the answer would be incomplete; narrow the area (a smaller radius), the period or the filters.");
        }

        // The service answers a square; keep what lies within the radius of the centre (metres in PL-1992).
        var rows = centre is null
            ? found.Items
            : [.. found.Items.Where(t =>
            {
                var dx = t.Location.Easting - centrePl.Easting;
                var dy = t.Location.Northing - centrePl.Northing;
                return (dx * dx) + (dy * dy) <= (double)radius * radius;
            })];

        PriceStatsView? stats = null;
        if (layer == RcnLayer.Premises)
        {
            var summary = new RcnTransactions(rows, false).Summarize();
            stats = new PriceStatsView
            {
                Count = summary.Count,
                MedianPerSquareMetre = Round(summary.Median),
                Q1PerSquareMetre = Round(summary.Q1),
                Q3PerSquareMetre = Round(summary.Q3),
                SmallSample = summary.SmallSample,
                ExcludedMissingPriceOrArea = summary.Excluded.MissingPriceOrArea,
                ExcludedShare = summary.Excluded.Share,
                ExcludedNonMarketKind = summary.Excluded.NonMarketKind,
                ExcludedOtherRight = summary.Excluded.OtherRight,
            };
        }

        var newest = rows.Count == 0 ? (DateOnly?)null : rows.Max(t => t.Date);
        var notes = new List<string>
        {
            newest is { } date ? LagNote + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "." : NoTransactionsNote,
        };
        if (stats is not null)
        {
            notes.Add(SampleNote);
        }

        if (stats is { SmallSample: true })
        {
            notes.Add(SmallSampleNote);
        }

        notes.Add(layer == RcnLayer.Premises ? RepeatedPremisesTotalNote : RepeatedTotalNote);

        var listed = rows.Take(maxTransactions).Select(ToView).ToList();
        return Result(
            new AreaView
            {
                Kind = areaKind,
                Center = centre is { } c ? new GeoPosition(c.Latitude, c.Longitude) : null,
                RadiusMeters = centre is null ? null : radius,
                Powiat = powiat,
            },
            begin, end, layer.Value, market, functionName,
            stats, listed, rows.Count, newest, rows.Count - listed.Count, matched, null,
            notes,
            uugSource is null ? [rcnSource] : [uugSource, rcnSource]);
    }

    private static CallToolResult Result(
        AreaView area, DateOnly from, DateOnly to, RcnLayer layer, string market, string? function,
        PriceStatsView? statistics, IReadOnlyList<TransactionView> transactions, int count, DateOnly? newest, int omitted,
        GeocodeMatch? matched, IReadOnlyList<GeocodeMatch>? candidates, IReadOnlyList<string> notes, IReadOnlyList<SourceInfo> sources) =>
        ToolResults.Ok(
            new PropertyPricesResult
            {
                Area = area,
                Period = new PeriodView(Format(from), Format(to)),
                Kind = layer switch { RcnLayer.Parcels => "parcels", RcnLayer.Buildings => "buildings", _ => "premises" },
                Market = market.Trim().ToLowerInvariant(),
                PremisesFunction = function,
                Statistics = statistics,
                TransactionCount = count,
                NewestTransaction = newest is { } d ? Format(d) : null,
                Transactions = transactions,
                TransactionsOmitted = omitted,
                MatchedAddress = matched,
                Candidates = candidates,
                Notes = notes,
                Sources = sources,
            },
            McpJson.Relaxed.PropertyPricesResult);

    private static TransactionView ToView(RcnTransaction t)
    {
        GeoPosition? position = null;
        try
        {
            var wgs = t.Location.ToWgs84();
            position = new GeoPosition(wgs.Latitude, wgs.Longitude);
        }
        catch (ArgumentOutOfRangeException)
        {
            // a point inside the PL-1992 range but outside Poland's WGS-84 box: no position rather than no answer
        }

        return new TransactionView
        {
            Date = Format(t.Date),
            Market = t.Market switch { RcnMarket.Primary => "primary", RcnMarket.Secondary => "secondary", _ => null },
            Kind = t.Kind,
            SellerType = t.SellerType,
            BuyerType = t.BuyerType,
            TotalPrice = t.TotalPrice,
            DocumentNumber = t.DocumentNumber,
            Address = t.Address?.ToString() is { Length: > 0 } text ? text : null,
            Position = position,
            Function = t.PremisesFunction switch
            {
                RcnPremisesFunction.Residential => "residential",
                RcnPremisesFunction.Garage => "garage",
                RcnPremisesFunction.Commercial => "commercial",
                RcnPremisesFunction.Other => "other",
                _ => null,
            },
            Rooms = t.Rooms,
            Floor = t.Floor,
            UsableAreaSquareMetres = t.Layer == RcnLayer.Buildings ? t.BuildingUsableArea : t.UsableArea,
            Price = t.Layer switch { RcnLayer.Parcels => t.ParcelPrice, RcnLayer.Buildings => t.BuildingPrice, _ => t.PremisesPrice },
            PricePerSquareMetre = Round(t.PricePerSquareMetre),
            ParcelId = t.ParcelId?.ToString(),
            LandAreaSquareMetres = t.LandArea,
            BuildingKind = t.BuildingKind,
        };
    }

    private static decimal? Round(decimal? value) => value is { } v ? decimal.Round(v, 0, MidpointRounding.AwayFromZero) : null;

    private static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
