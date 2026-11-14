using System;
using System.Reflection;

namespace PolishOpenData.Mcp;

internal static class ServerInfo
{
    public static string InstructionsFor(bool regonEnabled, bool ceidgEnabled = false) =>
        (regonEnabled ? Base.Replace("legal acts: Sejm)", "legal acts: Sejm; REGON: GUS)", StringComparison.Ordinal) + RegonSentence : Base) +
        (ceidgEnabled ? CeidgSentence : string.Empty);

    private const string CeidgSentence =
        " lookup_ceidg: a sole trader's business in the CEIDG register by NIP (status, start/suspension/end dates, address, PKD codes; the owner's name is personal data); needs the user's own CEIDG token and allows 1,000 requests per hour, so call it only when the other tools do not answer the question.";

    private const string RegonSentence =
        " lookup_regon: the GUS REGON register by NIP, REGON or KRS number (name, address, entity type, and for the first matches status and dates, main PKD activity, legal form); needs the user's own GUS key, so call it only when the other tools do not answer the question.";

    private const string Base =
        "Tools for Polish public registers. lookup_company: a company's VAT status, seat address, bank accounts and KRS data by NIP, REGON or KRS number. " +
        "check_vat_bank_account: whether a bank account is on the VAT whitelist for a taxpayer (use before paying a Polish invoice; keep the request ID). " +
        "check_vat_bank_account_offline: the same whitelist check from the Ministry of Finance daily flat file: no request limit, many pairs at once, but no request ID; the first call for a date downloads about 250 MB and takes several minutes, much longer when the Ministry's server is slow, and not_found means not found in the file, not proof of absence. " +
        "get_krs_extract: board, supervisory bodies, capital and PKD activities from the court register. " +
        "validate_identifier: offline check of NIP, REGON, KRS, NRB bank account or land-registry (KW) numbers. " +
        "geocode_address: coordinates (WGS-84) of a Polish address, saying whether the match is an address, a street or a city. " +
        "find_parcel: a cadastral parcel (działka) by parcel ID, coordinates, address or region name and number, optionally with its boundary; data from EGiB via GUGiK, never land-registry (KW) numbers or owners. " +
        "property_prices: what real estate sold for near an address or coordinates, or in a powiat: median and quartile price per m² of flats and the latest single transactions (premises, parcels or buildings); data from the RCN register via GUGiK, published with a delay, informational and not a valuation; never land-registry (KW) numbers. " +
        "find_statistical_unit, search_statistics, get_statistics: GUS local statistics (population, unemployment, housing...) for voivodeships, powiats and gminas by year: find the unit, search the indicator, then read its values; a null value means no data, never zero; anonymous use allows 100 requests per 15 minutes and search_statistics costs up to 6, so do not repeat searches; cite \"Główny Urząd Statystyczny (GUS), Bank Danych Lokalnych, CC BY 4.0\". " +
        "search_legal_acts, get_legal_act, get_legal_article: Polish legal acts (Dziennik Ustaw, Monitor Polski) from the Sejm ELI API: search, then read an act's status and consolidated texts, or one article as plain text. The API serves text as HTML only up to 2024, so a quoted article is often older than the law in force: always read isCurrent and warning and tell the user; pass the BASE act's ELI id (for example DU/1964/93, the civil code), not an amending act; cite \"Sejm RP, ELI API\". " +
        "Data comes from official registers (KRS: Ministry of Justice; Biała Lista: Ministry of Finance; RCN: GUGiK; statistics: GUS; legal acts: Sejm); cite the source and retrieval time. " +
        "Biała Lista allows about 100 searches per day per IP address, so avoid repeating lookups or use check_vat_bank_account_offline. This server never queries the eKW land-registry portal.";

    public static string Version { get; } =
        (typeof(ServerInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];
}
