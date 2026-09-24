# PolishOpenData

**.NET libraries and an MCP server for Polish public registers** — company lookup in KRS, the VAT whitelist (Biała Lista) with bank-account checks, and offline validation of NIP, REGON, KRS, NRB and land-registry (KW) numbers.

[![CI](https://github.com/JacekSmi/PolishOpenData/actions/workflows/ci.yml/badge.svg)](https://github.com/JacekSmi/PolishOpenData/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/PolishOpenData.Mcp?label=PolishOpenData.Mcp)](https://www.nuget.org/packages/PolishOpenData.Mcp)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

🇵🇱 [Polska wersja](README.pl.md)

> Unofficial project. Not affiliated with the Ministry of Justice, the Ministry of Finance or any other public body.

## Packages

| Package | What it does | Targets |
|---|---|---|
| [`PolishOpenData.Core`](https://www.nuget.org/packages/PolishOpenData.Core) | Offline value types: `Nip`, `Regon`, `KrsNumber`, `Nrb`, `KwNumber` (+ official court-code table) | net10.0, netstandard2.0 |
| [`PolishOpenData.Krs`](https://www.nuget.org/packages/PolishOpenData.Krs) | KRS Open API: current/full extracts, removed entities, daily change feed, English summary | net10.0, netstandard2.0 |
| [`PolishOpenData.BialaLista`](https://www.nuget.org/packages/PolishOpenData.BialaLista) | VAT whitelist: status, bank-account check with request ID, batches, daily-limit guard | net10.0, netstandard2.0 |
| [`PolishOpenData.Mcp`](https://www.nuget.org/packages/PolishOpenData.Mcp) | MCP server exposing all of the above to AI assistants | .NET 10 tool |

## Quick start: check a counterparty

```csharp
using Microsoft.Extensions.DependencyInjection;
using PolishOpenData;
using PolishOpenData.BialaLista;
using PolishOpenData.Krs;
using System.Threading.Tasks;

var services = new ServiceCollection();
services.AddBialaListaClient(o => o.TrackQuota = true)
    .AddStandardResilienceHandler(o => o.Retry.ShouldHandle = static _ => ValueTask.FromResult(false));
services.AddKrsClient().AddStandardResilienceHandler();
using var provider = services.BuildServiceProvider();

var vat = provider.GetRequiredService<IBialaListaClient>();
var nip = Nip.Parse("774-000-14-54");

var result = await vat.FindByNipAsync(nip);
Console.WriteLine($"{result.Value?.Name}: {result.Value?.VatStatus} (request {result.RequestId})");

var check = await vat.CheckBankAccountAsync(nip, Nrb.Parse("06 1600 1127 1843 9838 2000 0034"));
Console.WriteLine(check.Value ? "Account is on the whitelist" : "Account is NOT on the whitelist");

var krs = provider.GetRequiredService<IKrsClient>();
var extract = await krs.GetCurrentExtractAsync(result.Value!.Krs!.Value);
Console.WriteLine(extract.Extract?.ToSummary().ShareCapital);
```

Retries are off for Biała Lista: every retry the resilience handler would make is another upstream request that the quota guard above cannot count, and once the daily limit is reached Biała Lista blocks the whole IP address — not just this process — until midnight. Register `AddBialaListaClient` once per process and reuse the resulting client (or pass one shared `BialaListaQuotaTracker` explicitly); a client created without DI and without a shared tracker gets its own private tracker that does not see requests made by other instances.

A runnable version is in [`samples/CheckCounterparty`](samples/CheckCounterparty).

**Keep `RequestId`.** It is the ministry's proof that you checked the whitelist before paying (VAT Act art. 96b).

## MCP server for AI assistants

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download). No API keys.

[![Install in VS Code](https://img.shields.io/badge/VS_Code-Install_MCP_server-0098FF?logo=visualstudiocode)](https://insiders.vscode.dev/redirect?url=vscode%3Amcp%2Finstall%3F%257B%2522name%2522%253A%2522polish-open-data%2522%252C%2522type%2522%253A%2522stdio%2522%252C%2522command%2522%253A%2522dotnet%2522%252C%2522args%2522%253A%255B%2522dnx%2522%252C%2522PolishOpenData.Mcp%2522%252C%2522--yes%2522%255D%257D)
[![Install in VS Code Insiders](https://img.shields.io/badge/VS_Code_Insiders-Install_MCP_server-24bfa5?logo=visualstudiocode)](https://insiders.vscode.dev/redirect?url=vscode-insiders%3Amcp%2Finstall%3F%257B%2522name%2522%253A%2522polish-open-data%2522%252C%2522type%2522%253A%2522stdio%2522%252C%2522command%2522%253A%2522dotnet%2522%252C%2522args%2522%253A%255B%2522dnx%2522%252C%2522PolishOpenData.Mcp%2522%252C%2522--yes%2522%255D%257D)

**Claude Code**

```bash
claude mcp add --transport stdio --scope user polish-open-data -- dotnet dnx PolishOpenData.Mcp --yes
```

**Claude Desktop / Cursor** (`claude_desktop_config.json` or `.cursor/mcp.json`)

```json
{
  "mcpServers": {
    "polish-open-data": { "command": "dotnet", "args": ["dnx", "PolishOpenData.Mcp", "--yes"] }
  }
}
```

**VS Code** (`.vscode/mcp.json`)

```json
{
  "servers": {
    "polish-open-data": { "type": "stdio", "command": "dotnet", "args": ["dnx", "PolishOpenData.Mcp", "--yes"] }
  }
}
```

| Tool | What it answers |
|---|---|
| `lookup_company` | "Who is NIP 774-000-14-54?" — name, VAT status, seat, bank accounts, KRS legal form, capital, removal |
| `check_vat_bank_account` | "Can I pay this invoice to this account?" — whitelist check with request ID |
| `get_krs_extract` | Board and supervisory members (masked by the ministry), capital, shareholders, PKD activities |
| `validate_identifier` | Offline checksum validation of NIP, REGON, KRS, NRB and KW numbers, with typo suggestions |

A hosted endpoint (no .NET needed) is planned for v1.1.

## Land-registry (KW) numbers

`KwNumber` validates numbers such as `WA1M/00012345/1` offline using the official court-code table (Dz.U. 2026 poz. 740, 342 current codes plus 7 historical ones) and suggests fixes for common typos (`0S1U` → `OS1U`). It is a .NET port of the validation logic of [pyekw](https://github.com/mhajder/pyekw) by mhajder (MIT). It **does not** query the eKW portal and deliberately offers no number generator: KW numbers are personal data (NSA, III OSK 6508/21), and enumerating them is out of scope.

## Responsible use

- Data comes from official public registers; cite the source and the retrieval time (every result carries them).
- Biała Lista allows about 100 searches and 5,000 checks per day per IP address; exceeding them blocks the IP until midnight, including the ministry's web search. Use `TrackQuota` and prefer checks over searches.
- KRS masks natural persons in structured fields, but some free-text fields are not masked; this project never forwards them to AI assistants.
- No scraping, no bulk harvesting, no linking of parcels to land-registry numbers.

## Roadmap

v1.1 hosted MCP endpoint · v1.2 GUS REGON (BIR) · v1.3 offline VAT whitelist flat file · then property data (ULDK parcels, GUGiK geocoder, transaction prices). See the [design spec](docs/superpowers/specs/2026-09-23-polishopendata-design.md).

## Contributing and license

See [CONTRIBUTING.md](CONTRIBUTING.md). MIT licensed.

<!-- mcp-name: io.github.JacekSmi/PolishOpenData -->
