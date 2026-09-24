using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace PolishOpenData.Mcp;

[McpServerToolType]
internal sealed partial class IdentifierTools
{
    private static readonly string[] KnownKinds = ["nip", "regon", "krs", "nrb", "kw"];

    [McpServerTool(Name = "validate_identifier", Title = "Validate a Polish identifier", ReadOnly = true, OpenWorld = false, Idempotent = true, Destructive = false, UseStructuredContent = true, OutputSchemaType = typeof(IdentifierValidationReport))]
    [Description("Validates Polish identifiers offline (no network): NIP tax number, REGON, KRS number, NRB bank account, or land-registry (księga wieczysta, KW) number such as WA1M/00012345/1. Explains checksum failures and suggests likely typo fixes for KW numbers.")]
    public static CallToolResult ValidateIdentifier(
        [Description("The identifier to check, e.g. 774-000-14-54, 610188201, 0000028860, PL06 1600 1127 1843 9838 2000 0034 or WL1A/00272852/9.")] string value,
        [Description("Optional kind: nip, regon, krs, nrb or kw. Leave empty to detect it from the format.")] string? kind = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        string[] kinds;
        if (string.IsNullOrWhiteSpace(kind))
        {
            kinds = Detect(value);
        }
        else
        {
            var known = KnownKinds.FirstOrDefault(k => string.Equals(k, kind.Trim(), StringComparison.OrdinalIgnoreCase));
            if (known is null)
            {
                return ToolResults.Error("Unknown kind '" + kind + "'. Use one of: nip, regon, krs, nrb, kw.");
            }

            kinds = [known];
        }

        var results = kinds.Select(k => Validate(k, value)).ToList();
        return ToolResults.Ok(new IdentifierValidationReport(value, results), McpJson.Relaxed.IdentifierValidationReport);
    }

    private static string[] Detect(string value)
    {
        var trimmed = value.Trim();
        var body = trimmed.StartsWith("PL", StringComparison.OrdinalIgnoreCase) ? trimmed.Substring(2) : trimmed;
        if (trimmed.Contains('/', StringComparison.Ordinal) || body.Any(char.IsLetter))
        {
            return ["kw"];
        }

        return body.Count(char.IsDigit) switch
        {
            26 => ["nrb"],
            9 or 14 => ["regon"],
            10 => ["nip", "krs"],
            >= 1 and <= 8 => ["krs"],
            _ => KnownKinds,
        };
    }

    private static IdentifierValidation Validate(string kind, string value) => kind switch
    {
        "nip" => Nip.TryParse(value, out var nip)
            ? Valid("nip", nip.ToString(), "Valid NIP (tax identification number).")
            : Invalid("nip", "Not a valid NIP: expected 10 digits with a correct check digit (dashes, spaces and a PL prefix are allowed)."),
        "regon" => ValidateRegon(value),
        "krs" => KrsNumber.TryParse(value, out var krs)
            ? Valid("krs", krs.ToString(), "Well-formed KRS number. KRS numbers have no check digit, so this does not prove the entity exists; use get_krs_extract.")
            : Invalid("krs", "Not a KRS number: expected 1 to 10 digits, not all zeros."),
        "nrb" => Nrb.TryParse(value, out var nrb)
            ? Valid("nrb", nrb.ToString(), "Valid Polish bank account number (IBAN " + nrb.ToIban() + "). Use check_vat_bank_account to see whether it is on the VAT whitelist.")
            : Invalid("nrb", "Not a valid NRB: expected 26 digits passing the IBAN checksum (a PL prefix and spaces are allowed)."),
        _ => ValidateKw(value),
    };

    private static IdentifierValidation ValidateRegon(string value)
    {
        if (Regon.TryParse(value, out var regon))
        {
            var detail = regon.IsLocalUnit
                ? "Valid 14-digit REGON of a local unit of entity " + regon.BaseRegon.ToString() + "."
                : "Valid 9-digit REGON.";
            var valid = Valid("regon", regon.ToString(), detail);
            return regon.IsLocalUnit && regon.ToString().EndsWith("00000", StringComparison.Ordinal)
                ? valid with { Warnings = ["If this number comes from KRS, it is the 9-digit REGON " + regon.BaseRegon.ToString() + " padded with 00000."] }
                : valid;
        }

        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Length == 14 && digits.EndsWith("00000", StringComparison.Ordinal) && Regon.TryParse(digits.AsSpan(0, 9), out var baseRegon))
        {
            return new IdentifierValidation
            {
                Kind = "regon",
                IsValid = false,
                Normalized = baseRegon.ToString(),
                Details = ["The 14-digit form fails its check digit."],
                Warnings = ["This looks like the padded REGON used by KRS (9 digits + 00000). The 9-digit REGON " + baseRegon.ToString() + " is valid."],
            };
        }

        return Invalid("regon", "Not a valid REGON: expected 9 or 14 digits with correct check digits.");
    }

    private static IdentifierValidation ValidateKw(string value)
    {
        if (KwNumber.TryParse(value, out var kw))
        {
            var details = new List<string>();
            var warnings = new List<string>();
            if (kw.Court is { } court)
            {
                details.Add(court.IsCurrent
                    ? "Court: " + court.Name + "."
                    : "Historical court code (last listed " + court.LastValidOn!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "): " + court.Name + ". Books keep the code of the court where they were founded.");
            }
            else
            {
                warnings.Add("Court code " + kw.CourtCode + " is not in the current or historical code table (it may pre-date 2012 or be a typo).");
            }

            details.Add("Offline check only: it does not prove that the land register exists. This server never queries the eKW portal.");
            return new IdentifierValidation { Kind = "kw", IsValid = true, Normalized = kw.ToString(), Details = details, Warnings = warnings };
        }

        var suggestions = KwNumber.SuggestCorrections(value).Select(s => s.ToString()).ToList();
        var reasons = new List<string> { "Not a valid KW number: expected CCCC/NNNNNNNN/K (court code, 8-digit number, check digit)." };
        var match = KwShape().Match(value.Trim().ToUpperInvariant().Replace(" ", string.Empty, StringComparison.Ordinal));
        if (suggestions.Count == 0 && match.Success &&
            KwNumber.TryComputeCheckDigit(match.Groups[1].Value, match.Groups[2].Value, out var expected) &&
            !string.Equals(expected.ToString(CultureInfo.InvariantCulture), match.Groups[3].Value, StringComparison.Ordinal))
        {
            reasons.Add("The check digit should be " + expected.ToString(CultureInfo.InvariantCulture) + " for " + match.Groups[1].Value + "/" + match.Groups[2].Value + ".");
        }

        return new IdentifierValidation { Kind = "kw", IsValid = false, Details = reasons, Suggestions = suggestions };
    }

    private static IdentifierValidation Valid(string kind, string normalized, string detail) =>
        new() { Kind = kind, IsValid = true, Normalized = normalized, Details = [detail] };

    private static IdentifierValidation Invalid(string kind, string detail) =>
        new() { Kind = kind, IsValid = false, Details = [detail] };

    [GeneratedRegex("^([A-Z0-9]{4})[/-]?([0-9]{8})[/-]?([0-9])$")]
    private static partial Regex KwShape();
}
