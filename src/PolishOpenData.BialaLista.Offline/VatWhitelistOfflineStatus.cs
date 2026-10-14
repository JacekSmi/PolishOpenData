namespace PolishOpenData.BialaLista.Offline;

/// <summary>What the flat file says about a taxpayer's bank account.</summary>
public enum VatWhitelistOfflineStatus
{
    /// <summary>The pair of NIP and bank account was not found in the file (the plain hash and every mask of the account's bank were tried). This is not a statement about the taxpayer's registration.</summary>
    NotFound,

    /// <summary>The pair was found among the active VAT taxpayers.</summary>
    Active,

    /// <summary>The pair was found among the taxpayers exempt from VAT.</summary>
    Exempt,
}
