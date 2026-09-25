using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using PolishOpenData;
using PolishOpenData.BialaLista;
using PolishOpenData.Krs;

// Usage: dotnet run --project samples/CheckCounterparty -- <NIP> [bank account]
// Makes live calls to the Ministry of Finance and Ministry of Justice APIs.
if (args.Length == 0 || !Nip.TryParse(args[0], out var nip))
{
    Console.Error.WriteLine("Usage: CheckCounterparty <NIP> [bank account number]");
    return 1;
}

// Validate the account before any request, so a mistyped account stops here and costs no Biała Lista request.
Nrb? account = null;
if (args.Length > 1)
{
    if (!Nrb.TryParse(args[1], out var parsedAccount))
    {
        Console.Error.WriteLine("'" + args[1] + "' is not a valid Polish bank account number (NRB: 26 digits with a correct IBAN checksum; spaces and a PL prefix are allowed).");
        Console.Error.WriteLine("Usage: CheckCounterparty <NIP> [bank account number]");
        return 1;
    }

    account = parsedAccount;
}

var services = new ServiceCollection();
services.AddBialaListaClient(o => o.TrackQuota = true).AddStandardResilienceHandler(o =>
{
    // A retry is another upstream request the quota guard below does not count, and Biała Lista blocks the whole
    // IP address (not just this process) until midnight once the daily limit is reached: never retry it.
    o.Retry.ShouldHandle = static _ => ValueTask.FromResult(false);
});
services.AddKrsClient().AddStandardResilienceHandler();
await using var provider = services.BuildServiceProvider();
var vat = provider.GetRequiredService<IBialaListaClient>();
var krs = provider.GetRequiredService<IKrsClient>();

var found = await vat.FindByNipAsync(nip);
if (found.Value is not { } subject)
{
    Console.WriteLine(nip.ToString() + ": not on the VAT whitelist (request " + found.RequestId + ").");
    return 0;
}

Console.WriteLine(subject.Name + " | VAT: " + subject.VatStatus.ToString() + " | accounts on the whitelist: " +
    subject.AccountNumbers.Count.ToString(CultureInfo.InvariantCulture) + " | request " + found.RequestId);

if (account is { } nrb)
{
    var check = await vat.CheckBankAccountAsync(nip, nrb);
    Console.WriteLine(check.Value
        ? "Account " + nrb.ToString() + " is on the whitelist (request " + check.RequestId + ")."
        : "Account " + nrb.ToString() + " is NOT on the whitelist for this taxpayer (request " + check.RequestId + ").");
}

if (subject.Krs is { } krsNumber && (await krs.GetCurrentExtractAsync(krsNumber)).Extract is { } extract)
{
    var summary = extract.ToSummary();
    Console.WriteLine("KRS " + summary.Krs.ToString() + ": " + summary.LegalForm + ", share capital " +
        summary.ShareCapital?.ToString("N2", CultureInfo.InvariantCulture) + " " + summary.ShareCapitalCurrency +
        ", registered " + summary.RegisteredOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
}

return 0;
