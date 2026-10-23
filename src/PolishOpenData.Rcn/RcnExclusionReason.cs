namespace PolishOpenData.Rcn;

/// <summary>Why <see cref="RcnPriceStatistics.Classify"/> leaves a premises row out of the market-price sample; the first reason that applies wins.</summary>
public enum RcnExclusionReason
{
    /// <summary>The row counts in the statistics.</summary>
    None,

    /// <summary>No price per square metre: the price or the usable area is missing or not above zero.</summary>
    MissingPriceOrArea,

    /// <summary>Only a share of the premises was sold (anything but <c>1/1</c>).</summary>
    Share,

    /// <summary>A transaction kind other than <c>wolnyRynek</c>, e.g. an auction sale.</summary>
    NonMarketKind,

    /// <summary>A right other than ownership of the premises with the attached right.</summary>
    OtherRight,
}
