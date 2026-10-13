using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PolishOpenData.BialaLista.Offline;
using PolishOpenData.Internal;

namespace PolishOpenData.Mcp;

[McpServerToolType]
internal sealed class OfflineVatTools(IVatWhitelistOffline offline, TimeProvider timeProvider, IHostApplicationLifetime lifetime)
{
    private const int MaxPairs = 50;
    private const string Source = "Plik płaski wykazu podatników VAT / Biała Lista (Ministerstwo Finansów)";
    private const string Endpoint = "https://plikplaski.mf.gov.pl/pliki/";

    [McpServerTool(Name = "check_vat_bank_account_offline", Title = "Check bank accounts against the VAT whitelist flat file", ReadOnly = true, OpenWorld = true, Idempotent = true, Destructive = false, UseStructuredContent = true, OutputSchemaType = typeof(VatAccountOfflineCheck))]
    [Description("Checks pairs of NIP and bank account against the Ministry of Finance daily flat file of the VAT whitelist (plik płaski): offline, no request limit, many pairs at once (up to 50), and nothing about the pairs leaves this machine. Answers active, exempt or not_found; not_found means not found in the file, not proof that the account is absent from the whitelist. The first call for a date downloads about 250 MB and takes several minutes, much longer when the Ministry's server is slow. It does not return a request ID; for a check with a request ID use check_vat_bank_account.")]
    public async Task<CallToolResult> CheckVatBankAccountOffline(
        [Description("1 to 50 pairs, each { \"nip\": NIP of the taxpayer (dashes and a PL prefix allowed), \"bankAccount\": 26-digit NRB or PL IBAN, spaces allowed }.")] OfflineVatPair[] pairs,
        [Description("Date to check in YYYY-MM-DD (default today in Poland). Only the last 30 days and tomorrow are available.")] string? date = null,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (pairs is not { Length: >= 1 and <= MaxPairs })
        {
            return ToolResults.Error("Provide 1 to 50 pairs in 'pairs'.");
        }

        var parsed = new List<(Nip Nip, Nrb Account)>(pairs.Length);
        for (var i = 0; i < pairs.Length; i++)
        {
            var label = "pairs[" + i.ToString(CultureInfo.InvariantCulture) + "]";
            if (!Nip.TryParse(pairs[i]?.Nip, out var nip))
            {
                return ToolResults.Error(label + ": '" + pairs[i]?.Nip + "' is not a valid NIP (10 digits with a correct check digit).");
            }

            if (!Nrb.TryParse(pairs[i]?.BankAccount, out var account))
            {
                return ToolResults.Error(label + ": '" + pairs[i]?.BankAccount + "' is not a valid Polish bank account number (NRB: 26 digits with a correct IBAN checksum).");
            }

            parsed.Add((nip, account));
        }

        if (!ToolInput.TryParseDate(date, out var day, out var dateError))
        {
            return ToolResults.Error(dateError!);
        }

        day ??= WarsawTime.Today(timeProvider);   // one date for both calls, even across midnight

        // PrepareAsync checks the date window before any I/O and reports progress only to the call that starts the download.
        // It runs under the server's lifetime, not this call's token: a client that gives up (or times out) leaves the
        // download running, so asking again joins it instead of starting from zero.
        var relay = progress is null ? null : new Relay(progress);
        try
        {
            await offline.PrepareAsync(day, relay, lifetime.ApplicationStopping).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            relay?.Stop();
        }
        var checks = await offline.CheckManyAsync(parsed, day, cancellationToken).ConfigureAwait(false);

        var results = new List<VatAccountOfflineResult>(checks.Count);
        foreach (var check in checks)
        {
            results.Add(ToResult(check));
        }

        var first = checks[0];
        return ToolResults.Ok(
            new VatAccountOfflineCheck
            {
                FileDate = first.FileDate,
                GeneratedOn = first.GeneratedOn,
                Results = results,
                Source = new SourceInfo(Source, Endpoint, timeProvider.GetUtcNow(), null),
            },
            McpJson.Relaxed.VatAccountOfflineCheck);
    }

    private static VatAccountOfflineResult ToResult(VatWhitelistOfflineCheck check)
    {
        var when = check.FileDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var pair = "NIP " + check.Nip + " with account " + check.Account;
        var mask = check.MatchedVirtualAccountMask ? " The account was matched through a virtual-account mask of the file." : string.Empty;
        return new VatAccountOfflineResult
        {
            Nip = check.Nip.ToString(),
            BankAccount = check.Account.ToString(),
            Status = check.Status switch
            {
                VatWhitelistOfflineStatus.Active => "active",
                VatWhitelistOfflineStatus.Exempt => "exempt",
                _ => "not_found",
            },
            ViaVirtualAccountMask = check.MatchedVirtualAccountMask,
            Meaning = check.Status switch
            {
                VatWhitelistOfflineStatus.Active => pair + " is in the Ministry of Finance flat file for " + when + " among active VAT payers." + mask,
                VatWhitelistOfflineStatus.Exempt => pair + " is in the Ministry of Finance flat file for " + when + " among taxpayers exempt from VAT." + mask,
                _ => pair + " was not found in the Ministry of Finance flat file for " + when + "; this is not proof that the account is absent from the whitelist.",
            },
        };
    }

    // Synchronous on purpose: Progress<T> would post to a thread-pool and could deliver reports after the call has returned.
    // Stopped when the call returns: the preparation may outlive it.
    private sealed class Relay(IProgress<ProgressNotificationValue> target) : IProgress<VatWhitelistPreparationProgress>
    {
        private bool _stopped;

        public void Stop()
        {
            lock (this)
            {
                _stopped = true;
            }
        }

        public void Report(VatWhitelistPreparationProgress value)
        {
            lock (this)
            {
                if (!_stopped)
                {
                    target.Report(new ProgressNotificationValue
                    {
                        Progress = value.BytesReceived,
                        Total = value.TotalBytes,
                        Message = value.Stage == VatWhitelistPreparationStage.Downloading ? "Downloading the MF flat file" : "Building the index",
                    });
                }
            }
        }
    }
}
