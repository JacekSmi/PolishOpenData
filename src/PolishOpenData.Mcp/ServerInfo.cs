using System.Reflection;

namespace PolishOpenData.Mcp;

internal static class ServerInfo
{
    public const string Instructions =
        "Tools for Polish public registers. lookup_company: a company's VAT status, seat address, bank accounts and KRS data by NIP, REGON or KRS number. " +
        "check_vat_bank_account: whether a bank account is on the VAT whitelist for a taxpayer (use before paying a Polish invoice; keep the request ID). " +
        "get_krs_extract: board, supervisory bodies, capital and PKD activities from the court register. " +
        "validate_identifier: offline check of NIP, REGON, KRS, NRB bank account or land-registry (KW) numbers. " +
        "Data comes from official registers (KRS: Ministry of Justice; Biała Lista: Ministry of Finance); cite the source and retrieval time. " +
        "Biała Lista allows about 100 searches per day per IP address, so avoid repeating lookups. This server never queries the eKW land-registry portal.";

    public static string Version { get; } =
        (typeof(ServerInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];
}
