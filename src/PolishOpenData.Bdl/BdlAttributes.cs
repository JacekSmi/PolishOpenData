namespace PolishOpenData.Bdl;

/// <summary>What the data attribute of a BDL cell says about its value.</summary>
public enum BdlValueKind
{
    /// <summary>A real number (possibly flagged, for example as an estimate).</summary>
    Value,

    /// <summary>No value: confidential, unavailable, not applicable or not yet published. The number on the wire is a placeholder.</summary>
    NoValue,

    /// <summary>An attribute id this library does not know; the number is kept as given.</summary>
    Unknown,
}

/// <summary>The attribute attached to a BDL value (symbol and meaning).</summary>
/// <param name="Id">BDL attribute id.</param>
/// <param name="Symbol">One-letter flag shown by GUS (for example <c>x</c>, <c>s</c>), or null.</param>
/// <param name="Kind">Whether the cell holds a value.</param>
public sealed record BdlValueFlag(int Id, string? Symbol, BdlValueKind Kind);

internal static class BdlAttributes
{
    // From GET /attributes (2026-11-13). Ids whose number is a placeholder are NoValue.
    private static readonly BdlValueFlag[] Known =
    [
        new(0, null, BdlValueKind.NoValue),
        new(1, null, BdlValueKind.Value),
        new(3, "k", BdlValueKind.Value),
        new(4, "x", BdlValueKind.NoValue),
        new(7, "a", BdlValueKind.Value),
        new(9, "s", BdlValueKind.Value),
        new(11, "M", BdlValueKind.Value),
        new(13, "K", BdlValueKind.Value),
        new(14, "X", BdlValueKind.NoValue),
        new(15, "-", BdlValueKind.NoValue),
        new(17, "A", BdlValueKind.Value),
        new(20, "v", BdlValueKind.Value),
        new(21, "v", BdlValueKind.Value),
        new(50, "n", BdlValueKind.NoValue),
        new(91, "x", BdlValueKind.NoValue),
        new(94, "z", BdlValueKind.Value),
        new(97, "p", BdlValueKind.Value),
        new(98, "Z", BdlValueKind.Value),
    ];

    internal static BdlValueFlag Get(int id)
    {
        foreach (var a in Known)
        {
            if (a.Id == id)
            {
                return a;
            }
        }

        return new BdlValueFlag(id, null, BdlValueKind.Unknown);
    }

    internal static double? ValueFor(int attributeId, double? raw) =>
        Get(attributeId).Kind == BdlValueKind.NoValue ? null : raw;
}
