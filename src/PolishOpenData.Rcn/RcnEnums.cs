namespace PolishOpenData.Rcn;

/// <summary>A layer of the RCN service.</summary>
public enum RcnLayer
{
    /// <summary>Premises transactions (<c>ms:lokale</c>).</summary>
    Premises,

    /// <summary>Parcel transactions (<c>ms:dzialki</c>).</summary>
    Parcels,

    /// <summary>Building transactions (<c>ms:budynki</c>).</summary>
    Buildings,
}

/// <summary>The market a transaction took place on.</summary>
public enum RcnMarket
{
    /// <summary>Primary market, from a developer (<c>rynekPierwotny</c>).</summary>
    Primary,

    /// <summary>Secondary market, resale (<c>rynekWtorny</c>).</summary>
    Secondary,
}

/// <summary>What a premises is used for.</summary>
public enum RcnPremisesFunction
{
    /// <summary>A flat (<c>mieszkalna</c>).</summary>
    Residential,

    /// <summary>A garage or parking space (<c>garaz</c>).</summary>
    Garage,

    /// <summary>Retail or services (<c>handlowoUslugowa</c>).</summary>
    Commercial,

    /// <summary>Any other use (<c>inna</c>).</summary>
    Other,
}
