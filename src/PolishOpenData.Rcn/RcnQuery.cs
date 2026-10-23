using System;
using PolishOpenData.Geo;

namespace PolishOpenData.Rcn;

/// <summary>
/// What to ask the RCN service for. Every query is bounded: it needs an <see cref="Area"/> or a <see cref="PowiatCode"/>.
/// </summary>
public sealed class RcnQuery
{
    /// <summary>The largest <see cref="Area"/>, in square kilometres (with a 1 m² tolerance for floating-point error).</summary>
    public const int MaxAreaSquareKilometres = 25;

    /// <summary>The largest <see cref="MaxRecords"/>.</summary>
    public const int MaxRecordsLimit = 5000;

    /// <summary>Only transactions whose geometry intersects this rectangle: about 25 km² at most (with a 1 m² tolerance for floating-point error).</summary>
    public Pl1992Bbox? Area { get; set; }

    /// <summary>Only transactions in this powiat: its four-digit TERYT code, for example <c>1465</c> for Warsaw.</summary>
    public string? PowiatCode { get; set; }

    /// <summary>Only transactions dated on or after this day (inclusive).</summary>
    public DateOnly? From { get; set; }

    /// <summary>Only transactions dated on or before this day (inclusive).</summary>
    public DateOnly? To { get; set; }

    /// <summary>The layer to read. Default <see cref="RcnLayer.Premises"/>.</summary>
    public RcnLayer Layer { get; set; } = RcnLayer.Premises;

    /// <summary>Only this market; <see langword="null"/> for both.</summary>
    public RcnMarket? Market { get; set; }

    /// <summary>Only this premises use; <see langword="null"/> for all. Valid for <see cref="RcnLayer.Premises"/> only.</summary>
    public RcnPremisesFunction? PremisesFunction { get; set; }

    /// <summary>The most rows to return, 1 to 5,000. Default 1,000.</summary>
    public int MaxRecords { get; set; } = 1000;

    /// <exception cref="ArgumentException">No area and no powiat, a malformed powiat code, <see cref="From"/> after <see cref="To"/>, or a premises function on another layer.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The area is above about 25 km² (1 m² tolerance) or <see cref="MaxRecords"/> is outside 1 to 5,000.</exception>
    internal void Validate()
    {
        if (Area is null && PowiatCode is null)
        {
            throw new ArgumentException("Give an Area or a PowiatCode: an RCN query must be bounded.", nameof(Area));
        }

        if (PowiatCode is not null && !IsFourDigits(PowiatCode))
        {
            throw new ArgumentException("The powiat code must be exactly four digits.", nameof(PowiatCode));
        }

        if (From > To)
        {
            throw new ArgumentException("From is after To.", nameof(From));
        }

        if (PremisesFunction is not null && Layer != RcnLayer.Premises)
        {
            throw new ArgumentException("A premises function applies to the premises layer only.", nameof(PremisesFunction));
        }

        // 1 m² of slack: a 5,000 m box around a centre near easting 262,144 or 524,288 has corners in different
        // floating-point binades, so its width comes out as 5000.00000000006 m and the area as just over 25 km².
        if (Area is { } area
            && (area.Max.Easting - area.Min.Easting) * (area.Max.Northing - area.Min.Northing) > (MaxAreaSquareKilometres * 1_000_000d) + 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Area), $"The area is above {MaxAreaSquareKilometres} km².");
        }

        if (MaxRecords is < 1 or > MaxRecordsLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxRecords), MaxRecords, $"MaxRecords must be 1 to {MaxRecordsLimit}.");
        }
    }

    // Not char.IsDigit: that accepts non-ASCII digits.
    private static bool IsFourDigits(string value)
    {
        if (value.Length != 4)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
