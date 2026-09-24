using System;

namespace PolishOpenData;

/// <summary>
/// A land-registry department of a district court (<i>wydział ksiąg wieczystych sądu rejonowego</i>) identified by the
/// 4-character code at the start of every KW number.
/// </summary>
public sealed class KwCourt
{
    internal KwCourt(string code, string name, DateOnly? lastValidOn)
    {
        Code = code;
        Name = name;
        LastValidOn = lastValidOn;
    }

    /// <summary>The 4-character code, e.g. <c>WA1M</c>.</summary>
    public string Code { get; }

    /// <summary>The court's name as worded in the regulation's annex.</summary>
    public string Name { get; }

    /// <summary>The last day the code was listed, or <c>null</c> when it is in the current annex.</summary>
    public DateOnly? LastValidOn { get; }

    /// <summary>True when the code is listed in the current annex (Dz.U. 2026 poz. 740).</summary>
    public bool IsCurrent => LastValidOn is null;

    /// <inheritdoc/>
    public override string ToString() => Code + " – " + Name;
}
