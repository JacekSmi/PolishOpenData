using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PolishOpenData.BialaLista;
using PolishOpenData.Internal;
using PolishOpenData.Krs;

namespace PolishOpenData.Mcp;

[McpServerToolType]
internal sealed partial class CompanyTools(CachedRegistries registries, TimeProvider timeProvider)
{
    private const int MaxListedAccounts = 20;
    private const string KrsSource = "KRS Open API (Ministerstwo Sprawiedliwości)";
    private const string KrsEndpoint = "https://api-krs.ms.gov.pl";
    private const string VatSource = "Wykaz podatników VAT / Biała Lista (Ministerstwo Finansów)";
    private const string VatEndpoint = "https://wl-api.mf.gov.pl";

    [McpServerTool(Name = "lookup_company", Title = "Look up a Polish company", ReadOnly = true, OpenWorld = true, Idempotent = true, Destructive = false, UseStructuredContent = true, OutputSchemaType = typeof(CompanyOverview))]
    [Description("Looks up a Polish company or organisation by exactly one of NIP, REGON or KRS number. Combines the VAT whitelist (Biała Lista: VAT status, seat address, bank accounts) with the National Court Register (KRS: legal form, share capital, registration, removal). Public registry data.")]
    public async Task<CallToolResult> LookupCompany(
        [Description("NIP tax number (10 digits; dashes and a PL prefix are allowed).")] string? nip = null,
        [Description("REGON (9 or 14 digits).")] string? regon = null,
        [Description("KRS number (up to 10 digits).")] string? krs = null,
        [Description("Date to check in YYYY-MM-DD (Biała Lista only; default today in Poland).")] string? date = null,
        CancellationToken cancellationToken = default)
    {
        if (new[] { nip, regon, krs }.Count(v => !string.IsNullOrWhiteSpace(v)) != 1)
        {
            return ToolResults.Error("Provide exactly one of nip, regon or krs.");
        }

        if (!ToolInput.TryParseDate(date, out var day, out var dateError))
        {
            return ToolResults.Error(dateError!);
        }

        var today = WarsawTime.Today(timeProvider);
        if (FutureDateError(date, day, today) is { } futureError)
        {
            return ToolResults.Error(futureError);
        }

        day ??= today;   // resolve "today" before caching, so a cached answer never outlives its day

        var warnings = new List<string>();
        var sources = new List<SourceInfo>();
        BialaListaResult<VatSubject?>? vat = null;
        KrsNumber? krsNumber;
        string query;

        if (!string.IsNullOrWhiteSpace(nip))
        {
            if (!Nip.TryParse(nip, out var parsed))
            {
                return ToolResults.Error("'" + nip + "' is not a valid NIP (10 digits with a correct check digit).");
            }

            query = "NIP " + parsed.ToString();
            vat = await registries.FindByNipAsync(parsed, day, cancellationToken).ConfigureAwait(false);
            krsNumber = vat.Value?.Krs;
        }
        else if (!string.IsNullOrWhiteSpace(regon))
        {
            if (!Regon.TryParse(regon, out var parsed))
            {
                return ToolResults.Error("'" + regon + "' is not a valid REGON (9 or 14 digits with correct check digits).");
            }

            query = "REGON " + parsed.ToString();
            vat = await registries.FindByRegonAsync(parsed, day, cancellationToken).ConfigureAwait(false);
            krsNumber = vat.Value?.Krs;
        }
        else
        {
            if (!KrsNumber.TryParse(krs, out var parsed))
            {
                return ToolResults.Error("'" + krs + "' is not a KRS number (1 to 10 digits).");
            }

            query = "KRS " + parsed.ToString();
            krsNumber = parsed;
        }

        KrsResult<KrsCurrentExtract>? krsResult = null;
        KrsCompanySummary? summary = null;
        DateOnly? removedOn = null;
        if (krsNumber is { } number)
        {
            krsResult = await registries.GetCurrentExtractAsync(number, cancellationToken).ConfigureAwait(false);
            summary = krsResult.Extract?.ToSummary();
            if (krsResult.Status == KrsLookupStatus.Removed)
            {
                removedOn = (await registries.GetFullExtractAsync(number, cancellationToken).ConfigureAwait(false)).Extract?.RemovedOn;
                warnings.Add("The entity was removed from KRS" + (removedOn is { } d ? " on " + Format(d) : string.Empty) + ".");
            }
            else if (krsResult.Status == KrsLookupStatus.NotFound)
            {
                warnings.Add("KRS has no entity with number " + number.ToString() + ".");
            }

            sources.Add(new SourceInfo(KrsSource, KrsEndpoint, summary?.ExtractedAt ?? timeProvider.GetUtcNow(), null));
        }
        else if (vat?.Value is null && (!string.IsNullOrWhiteSpace(nip) || !string.IsNullOrWhiteSpace(regon)))
        {
            // Genuinely no taxpayer for this identifier (not just "found, but this taxpayer has no KRS number" —
            // that's a sole trader, not a dead end) and no KRS number came back from it either: KRS itself has no
            // search by NIP or REGON, so this is a dead end unless the caller happens to know the KRS number.
            warnings.Add("Not on the VAT whitelist, and no KRS number is known for it. If this is an organisation " +
                "not registered for VAT (KRS has no search by NIP or REGON), try again with 'krs' if you know its KRS number.");
        }

        // A KRS-first query: take the NIP from the extract and ask the VAT whitelist.
        if (vat is null && summary?.Nip is { } nipFromKrs)
        {
            vat = await registries.FindByNipAsync(nipFromKrs, day, cancellationToken).ConfigureAwait(false);
        }

        VatInfo? vatInfo = null;
        if (vat is not null)
        {
            sources.Add(new SourceInfo(VatSource, VatEndpoint, vat.RequestDateTime, vat.RequestId));
            if (vat.Value is { } subject)
            {
                vatInfo = ToVatInfo(subject);
                if (subject.AccountNumbers.Count > MaxListedAccounts)
                {
                    warnings.Add("Showing " + MaxListedAccounts.ToString(CultureInfo.InvariantCulture) + " of " +
                        subject.AccountNumbers.Count.ToString(CultureInfo.InvariantCulture) +
                        " bank accounts; use check_vat_bank_account to verify a specific account.");
                }
            }
            else
            {
                vatInfo = new VatInfo
                {
                    Status = "not_found",
                    StatusExplanation = "Not on the VAT whitelist for this date: not registered for VAT in Poland, or the identifier is wrong.",
                };
            }
        }

        var overview = new CompanyOverview
        {
            Query = query,
            Found = vat?.Value is not null || krsResult?.Status is KrsLookupStatus.Found or KrsLookupStatus.Removed,
            Name = vat?.Value?.Name ?? summary?.Name,
            Nip = (vat?.Value?.Nip ?? summary?.Nip)?.ToString(),
            Regon = (vat?.Value?.Regon ?? summary?.Regon)?.ToString(),
            Krs = krsNumber?.ToString(),
            Vat = vatInfo,
            KrsRegistry = krsResult is null ? null : ToKrsInfo(krsResult, summary, removedOn),
            Warnings = warnings,
            Sources = sources,
        };
        return ToolResults.Ok(overview, McpJson.Relaxed.CompanyOverview);
    }

    [McpServerTool(Name = "check_vat_bank_account", Title = "Check a bank account on the VAT whitelist", ReadOnly = true, OpenWorld = true, Idempotent = true, Destructive = false, UseStructuredContent = true, OutputSchemaType = typeof(VatAccountCheck))]
    [Description("Checks whether a bank account is on the Polish VAT whitelist (Biała Lista) for a taxpayer identified by NIP or REGON. Use before paying an invoice over 15,000 PLN; keep the returned request ID and time to document when the whitelist was checked and what it answered.")]
    public async Task<CallToolResult> CheckVatBankAccount(
        [Description("Bank account number: 26-digit NRB or PL IBAN; spaces allowed.")] string bankAccount,
        [Description("NIP of the taxpayer (give nip or regon).")] string? nip = null,
        [Description("REGON of the taxpayer (give nip or regon).")] string? regon = null,
        [Description("Date to check in YYYY-MM-DD (default today in Poland).")] string? date = null,
        CancellationToken cancellationToken = default)
    {
        if (!Nrb.TryParse(bankAccount, out var account))
        {
            return ToolResults.Error("'" + bankAccount + "' is not a valid Polish bank account number (NRB: 26 digits with a correct IBAN checksum).");
        }

        if (string.IsNullOrWhiteSpace(nip) == string.IsNullOrWhiteSpace(regon))
        {
            return ToolResults.Error("Provide exactly one of nip or regon.");
        }

        if (!ToolInput.TryParseDate(date, out var day, out var dateError))
        {
            return ToolResults.Error(dateError!);
        }

        var today = WarsawTime.Today(timeProvider);
        if (FutureDateError(date, day, today) is { } futureError)
        {
            return ToolResults.Error(futureError);
        }

        day ??= today;   // resolve "today" before caching, so a cached answer never outlives its day

        BialaListaResult<bool> result;
        string? nipText = null;
        string? regonText = null;
        if (!string.IsNullOrWhiteSpace(nip))
        {
            if (!Nip.TryParse(nip, out var parsedNip))
            {
                return ToolResults.Error("'" + nip + "' is not a valid NIP (10 digits with a correct check digit).");
            }

            nipText = parsedNip.ToString();
            result = await registries.CheckAsync(parsedNip, account, day, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            if (!Regon.TryParse(regon, out var parsedRegon))
            {
                return ToolResults.Error("'" + regon + "' is not a valid REGON (9 or 14 digits with correct check digits).");
            }

            regonText = parsedRegon.ToString();
            result = await registries.CheckAsync(parsedRegon, account, day, cancellationToken).ConfigureAwait(false);
        }

        var when = Format(day ?? DateOnly.FromDateTime(result.RequestDateTime.DateTime));
        var meaning = result.Value
            ? "The account " + account.ToString() + " is on the VAT whitelist for this taxpayer on " + when + ": the taxpayer is an active VAT payer and has reported this account."
            : "The account " + account.ToString() + " is NOT on the VAT whitelist for this taxpayer on " + when + ". Either the account is not assigned to this taxpayer, or the taxpayer is not an active VAT payer. This does not tell who owns the account.";
        meaning += " Keep request ID " + result.RequestId + " as evidence of the check.";

        return ToolResults.Ok(
            new VatAccountCheck
            {
                BankAccount = account.ToString(),
                Nip = nipText,
                Regon = regonText,
                AssignedToActiveVatPayer = result.Value,
                Meaning = meaning,
                RequestId = result.RequestId,
                RequestDateTime = result.RequestDateTime,
                Source = new SourceInfo(VatSource, VatEndpoint, result.RequestDateTime, result.RequestId),
            },
            McpJson.Relaxed.VatAccountCheck);
    }

    [McpServerTool(Name = "get_krs_extract", Title = "Get a KRS company summary", ReadOnly = true, OpenWorld = true, Idempotent = true, Destructive = false, UseStructuredContent = true, OutputSchemaType = typeof(KrsExtractView))]
    [Description("Returns a summary of the current National Court Register (KRS) extract: name, legal form, NIP/REGON, address, share capital, management and supervisory boards (names masked by the ministry), proxies, shareholders (companies and natural persons with masked names), and PKD activities. Reports removed entities with their removal date.")]
    public async Task<CallToolResult> GetKrsExtract(
        [Description("KRS number (up to 10 digits), e.g. 28860 or 0000028860.")] string krs,
        CancellationToken cancellationToken = default)
    {
        if (!KrsNumber.TryParse(krs, out var number))
        {
            return ToolResults.Error("'" + krs + "' is not a KRS number (1 to 10 digits).");
        }

        var result = await registries.GetCurrentExtractAsync(number, cancellationToken).ConfigureAwait(false);
        KrsExtractView view;
        switch (result.Status)
        {
            case KrsLookupStatus.Found:
                var summary = ScrubPesel(result.Extract!.ToSummary());
                view = new KrsExtractView
                {
                    Status = "found",
                    Summary = summary,
                    Note = "Names of natural persons are masked by the Ministry of Justice.",
                    Sources = [new SourceInfo(KrsSource, KrsEndpoint, summary.ExtractedAt ?? timeProvider.GetUtcNow(), null)],
                };
                break;
            case KrsLookupStatus.Removed:
                var full = await registries.GetFullExtractAsync(number, cancellationToken).ConfigureAwait(false);
                view = new KrsExtractView
                {
                    Status = "removed",
                    RemovedOn = full.Extract?.RemovedOn,
                    Note = "The entity was removed from KRS; no current extract exists.",
                    Sources = [new SourceInfo(KrsSource, KrsEndpoint, full.Extract?.NaglowekP?.DataCzasOdpisu ?? timeProvider.GetUtcNow(), null)],
                };
                break;
            default:
                view = new KrsExtractView
                {
                    Status = "not_found",
                    Note = "No entity with KRS " + number.ToString() + " in the register of entrepreneurs (P) or of associations and foundations (S).",
                    Sources = [new SourceInfo(KrsSource, KrsEndpoint, timeProvider.GetUtcNow(), null)],
                };
                break;
        }

        return ToolResults.Ok(view, McpJson.Relaxed.KrsExtractView);
    }

    private static VatInfo ToVatInfo(VatSubject subject) => new()
    {
        Status = subject.VatStatus switch
        {
            VatStatus.Active => "active",
            VatStatus.Exempt => "exempt",
            VatStatus.NotRegistered => "not_registered",
            _ => "unknown",
        },
        StatusExplanation = subject.VatStatus switch
        {
            VatStatus.Active => "Active VAT payer (Czynny).",
            VatStatus.Exempt => "Registered but exempt from VAT (Zwolniony).",
            VatStatus.NotRegistered => "Not registered as a VAT payer (Niezarejestrowany)" +
                (subject.RemovalDate is { } removal ? ", removed on " + Format(removal) : string.Empty) + ".",
            _ => "Unrecognised status '" + subject.VatStatusRaw + "'.",
        },
        Address = subject.WorkingAddress ?? subject.ResidenceAddress,
        BankAccountCount = subject.AccountNumbers.Count,
        BankAccounts = subject.AccountNumbers.Take(MaxListedAccounts).Select(a => a.ToString()).ToList(),
        HasVirtualAccounts = subject.HasVirtualAccounts,
        RegisteredForVatOn = subject.RegistrationLegalDate,
        RemovedOn = subject.RemovalDate,
        RemovalBasis = subject.RemovalBasis,
    };

    private static KrsInfo ToKrsInfo(KrsResult<KrsCurrentExtract> result, KrsCompanySummary? summary, DateOnly? removedOn) => result.Status switch
    {
        KrsLookupStatus.Found => new KrsInfo
        {
            Status = "found",
            Register = RegisterName(result.Register),
            LegalForm = summary?.LegalForm,
            Address = summary?.Address,
            ShareCapital = summary?.ShareCapital,
            ShareCapitalCurrency = summary?.ShareCapitalCurrency,
            RegisteredOn = summary?.RegisteredOn,
            MainActivity = summary?.MainActivity is { } activity ? activity.Code + " " + activity.Description : null,
        },
        KrsLookupStatus.Removed => new KrsInfo { Status = "removed", Register = RegisterName(result.Register), RemovedOn = removedOn },
        _ => new KrsInfo { Status = "not_found" },
    };

    private static string? RegisterName(KrsRegister? register) => register switch
    {
        KrsRegister.Entrepreneurs => "entrepreneurs",
        KrsRegister.Associations => "associations",
        _ => null,
    };

    // KRS does not mask these free-text fields: strip PESEL-like numbers before this server returns them. The
    // library itself never does this, so callers who go through PolishOpenData.Krs directly still see the raw text.
    private static KrsCompanySummary ScrubPesel(KrsCompanySummary summary) => summary with
    {
        Representation = summary.Representation is { } representation ? ScrubBody(representation) : null,
        SupervisoryBodies = summary.SupervisoryBodies.Select(ScrubBody).ToList(),
        Shareholders = summary.Shareholders.Select(ScrubShareholder).ToList(),
    };

    private static KrsBody ScrubBody(KrsBody body) => body with { RepresentationMethod = ScrubPeselText(body.RepresentationMethod) };

    private static KrsShareholderSummary ScrubShareholder(KrsShareholderSummary shareholder) => shareholder with { Shares = ScrubPeselText(shareholder.Shares) };

    internal static string? ScrubPeselText(string? text) => text is null ? null : PeselPattern().Replace(text, "[PESEL removed]");

    private static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // Biała Lista answers a future date with WL-103 and still counts the request against the daily limit; reject
    // it locally first. The lower boundary (dates before the register existed) is not verified: Biała Lista
    // reports WL-118 for those, and misjudging "too old" locally is riskier than letting the upstream decide.
    private static string? FutureDateError(string? date, DateOnly? day, DateOnly today) =>
        day > today
            ? "'" + date + "' is in the future; today in Poland is " + Format(today) + ". Biała Lista rejects future dates (WL-103), and the rejected request would still count against the daily limit."
            : null;

    // A PESEL-like number: 11 digits, or 6 + 5 (birth date, serial) separated by one space or hyphen, and not part of
    // a longer digit run, so a 14-digit REGON or a 26-digit NRB in the same text is left intact.
    [GeneratedRegex(@"(?<!\d)(?:\d{11}|\d{6}[ -]\d{5})(?!\d)")]
    private static partial Regex PeselPattern();
}
