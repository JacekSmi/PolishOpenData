using PolishOpenData.Uug;

namespace PolishOpenData.Demo.Search;

/// <summary>How an address search ended.</summary>
internal enum AddressOutcomeKind
{
    /// <summary>The text names exactly one address point: <see cref="AddressOutcome.Match"/>.</summary>
    Resolved,

    /// <summary>The geocoder found something that is not one address point (a street, a town, several numbers): <see cref="AddressOutcome.Candidates"/>.</summary>
    Candidates,

    /// <summary>Nothing to look up, or nothing found; see <see cref="AddressOutcome.Message"/>.</summary>
    None,

    /// <summary>The text is shaped like a land-registry (KW) number; nothing was asked.</summary>
    Refused,
}

/// <summary>What typing an address came to.</summary>
/// <param name="Kind">How the search ended.</param>
/// <param name="Match">The one address point, for <see cref="AddressOutcomeKind.Resolved"/>.</param>
/// <param name="Candidates">The matches the geocoder returned, each distinct one once, in its order, for <see cref="AddressOutcomeKind.Candidates"/>; empty otherwise.</param>
/// <param name="Message">What to tell the user for every kind but <see cref="AddressOutcomeKind.Resolved"/>; it never repeats the typed text. <c>null</c> for an empty text.</param>
internal sealed record AddressOutcome(AddressOutcomeKind Kind, UugMatch? Match, IReadOnlyList<UugMatch> Candidates, string? Message);

/// <summary>Turns typed text into one address point with the GUGiK geocoder (UUG), at most one request.</summary>
internal sealed class AddressSearch(IUugClient uug)
{
    private const string KwRefusal = "This demo never accepts land-registry (KW) numbers.";
    private const string NothingFound = "No address matched; check the spelling or add the town.";
    private const string Unresolved = "The address did not resolve to a single address point; pick one of the candidates or give the full address with a house number.";

    /// <summary>
    /// Empty or white-space text makes no request and gives <see cref="AddressOutcomeKind.None"/>; a KW-shaped text makes none and gives <see cref="AddressOutcomeKind.Refused"/>.
    /// Otherwise one geocoder request: exactly one distinct address point is <see cref="AddressOutcomeKind.Resolved"/>, no match at all is <see cref="AddressOutcomeKind.None"/>,
    /// anything else is <see cref="AddressOutcomeKind.Candidates"/> with the distinct matches.
    /// Mirrors <c>property_prices</c> and <c>find_parcel</c>: keep it in step with <c>src/PolishOpenData.Mcp/AddressResolution.cs</c> (the one-distinct-address-point rule)
    /// and <c>src/PolishOpenData.Mcp/Tools/IdentifierTools.cs</c> (<c>LooksLikeKw</c>, reimplemented in <see cref="KwShape"/>); a change in one is checked in the other.
    /// </summary>
    public async Task<AddressOutcome> ResolveAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new AddressOutcome(AddressOutcomeKind.None, null, [], null);
        }

        // Checked before any request and never echoed back. Shape only: a KW with a wrong check digit is refused too.
        if (KwShape.Matches(text))
        {
            return new AddressOutcome(AddressOutcomeKind.Refused, null, [], KwRefusal);
        }

        var matches = await uug.GeocodeAsync(text, cancellationToken);
        if (matches.Count == 0)
        {
            return new AddressOutcome(AddressOutcomeKind.None, null, [], NothingFound);
        }

        // The same address may come back more than once (e.g. one point per entrance record); one distinct address is enough, and the list shows each row once
        // (the first of each, in the geocoder's order). A street or a city is its own row by the same key.
        var distinct = matches.DistinctBy(m => (m.Kind, m.City, m.CityPart, m.Street, m.Number, m.PostalCode, m.Teryt, m.Simc, m.Ulic)).ToList();
        var points = distinct.Where(m => m.Kind == UugMatchKind.Address).ToList();
        return points.Count == 1
            ? new AddressOutcome(AddressOutcomeKind.Resolved, points[0], [], null)
            : new AddressOutcome(AddressOutcomeKind.Candidates, null, distinct, Unresolved);
    }

}
