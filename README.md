# PolishOpenData

**.NET libraries and an MCP server for Polish public registers** — company lookup in KRS, the VAT whitelist (Biała Lista) with bank-account checks (online, or offline against the Ministry of Finance flat file), a client for the GUS REGON register (library only), cadastral parcel lookup (ULDK), address geocoding (UUG) and real estate transaction prices (RCN) from GUGiK, and offline validation of NIP, REGON, KRS, NRB and land-registry (KW) numbers.

[![CI](https://github.com/JacekSmi/PolishOpenData/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/JacekSmi/PolishOpenData/actions/workflows/ci.yml?query=branch%3Amain)
[![NuGet](https://img.shields.io/nuget/v/PolishOpenData.Mcp?label=PolishOpenData.Mcp)](https://www.nuget.org/packages/PolishOpenData.Mcp)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/JacekSmi/PolishOpenData/blob/main/LICENSE)

🇵🇱 [Polska wersja](https://github.com/JacekSmi/PolishOpenData/blob/main/README.pl.md)

> Unofficial project. Not affiliated with the Ministry of Justice, the Ministry of Finance or any other public body. Results are informational, not legal or tax advice; the registers are the authoritative source.

## Packages

| Package | What it does | Targets |
|---|---|---|
| [`PolishOpenData.Core`](https://www.nuget.org/packages/PolishOpenData.Core) | Offline value types: `Nip`, `Regon`, `KrsNumber`, `Nrb`, `KwNumber` (+ official court-code table) | net10.0, netstandard2.0 |
| [`PolishOpenData.Krs`](https://www.nuget.org/packages/PolishOpenData.Krs) | KRS Open API: current/full extracts, removed entities, daily change feed, English summary | net10.0, netstandard2.0 |
| [`PolishOpenData.BialaLista`](https://www.nuget.org/packages/PolishOpenData.BialaLista) | VAT whitelist: status, bank-account check with request ID, batches, daily-limit guard | net10.0, netstandard2.0 |
| [`PolishOpenData.BialaLista.Offline`](https://www.nuget.org/packages/PolishOpenData.BialaLista.Offline) | VAT whitelist checked offline against the Ministry of Finance daily flat file: no request limit, many pairs at once, no request ID | net10.0, netstandard2.0 |
| [`PolishOpenData.Regon`](https://www.nuget.org/packages/PolishOpenData.Regon) | GUS REGON (BIR1.2): search by NIP/REGON/KRS, batches of 20, full reports, session and request limiter; needs your own free GUS key | net10.0, netstandard2.0 |
| [`PolishOpenData.Geo.Core`](https://www.nuget.org/packages/PolishOpenData.Geo.Core) | Typed Polish coordinates: `Pl1992Point` (EPSG:2180) and `Wgs84Point` with a built-in conversion, `Pl1992Bbox`, `ParcelId` (TERYT parcel identifier) | net10.0, netstandard2.0 |
| [`PolishOpenData.Uldk`](https://www.nuget.org/packages/PolishOpenData.Uldk) | GUGiK ULDK: cadastral parcel by ID, by point or by region name and number, with its boundary as WKT | net10.0, netstandard2.0 |
| [`PolishOpenData.Uug`](https://www.nuget.org/packages/PolishOpenData.Uug) | GUGiK UUG: geocode a Polish address to a WGS-84 point with a match accuracy, or find the nearest address to a point | net10.0, netstandard2.0 |
| [`PolishOpenData.Rcn`](https://www.nuget.org/packages/PolishOpenData.Rcn) | GUGiK RCN (Rejestr Cen Nieruchomości): real estate transactions (premises, parcels, buildings) in an area and period, with price per m² statistics | net10.0, netstandard2.0 |
| [`PolishOpenData.Mcp`](https://www.nuget.org/packages/PolishOpenData.Mcp) | MCP server for AI assistants: company lookup, VAT whitelist and bank-account check (online and offline), KRS extracts, parcels, address geocoding and property prices, identifier validation | .NET 10 tool |

## Offline validation

No network, no DI:

```bash
dotnet add package PolishOpenData.Core
```

```csharp
using PolishOpenData;

Console.WriteLine(Nip.TryParse("774-000-14-54", out var nip) ? $"Valid NIP {nip}" : "Invalid NIP"); // Valid NIP 7740001454
Console.WriteLine(KwNumber.TryParse("WA1M/00012345/1", out var kw) ? kw.Court?.Name : "Invalid KW number"); // Sąd Rejonowy dla Warszawy-Mokotowa …
Console.WriteLine(string.Join(", ", KwNumber.SuggestCorrections("WAIM/00012345/1"))); // WA1M/00012345/1
```

## Quick start: check a counterparty

```bash
dotnet add package PolishOpenData.BialaLista
dotnet add package PolishOpenData.Krs
dotnet add package Microsoft.Extensions.Http.Resilience
```

```csharp
using Microsoft.Extensions.DependencyInjection;
using PolishOpenData;
using PolishOpenData.BialaLista;
using PolishOpenData.Krs;
using System.Threading.Tasks;

var services = new ServiceCollection();
services.AddBialaListaClient(o => o.TrackQuota = true)
    .AddStandardResilienceHandler(o => o.Retry.ShouldHandle = _ => new ValueTask<bool>(false));
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

A runnable version is in [`samples/CheckCounterparty`](https://github.com/JacekSmi/PolishOpenData/blob/main/samples/CheckCounterparty).

**Keep `RequestId`.** Every `BialaListaResult` carries the request identifier and time returned by Biała Lista (`RequestId`, `RequestDateTime`); keep them with your payment records to document when you checked the whitelist and what it answered.

**Source-generated JSON.** The KRS models use public converters from `PolishOpenData.Krs.Serialization` (`KrsDateJsonConverter`, `KrsTimestampJsonConverter`), so on .NET 8 or later you can add `KrsCurrentExtract` or `KrsFullExtract` to your own `JsonSerializerContext` (for example to cache extracts) without registering any converter. If you parse raw KRS API responses yourself instead of calling `IKrsClient`, note two things: the extract is wrapped in an `odpis` object, so deserialize that inner object; and the registry's field names (such as `dataRejestracjiWKRS`) match the properties only case-insensitively, so use `JsonSerializerDefaults.Web` — `[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]` on your context, or `new JsonSerializerOptions(JsonSerializerDefaults.Web)` with the reflection-based serializer on .NET Framework. Otherwise the properties stay `null` without an error.

## Quick start: look up an entity in the GUS REGON register

`PolishOpenData.Regon` is a client for GUS's BIR1.2 web service: search by NIP, REGON or KRS number, batches of up to 20 identifiers, and the full reports of legal persons, sole traders and local units. It needs a key of your own (free: write to `regon_bir@stat.gov.pl`); GUS's public test environment works without a key but serves an old, anonymised copy of the register.

```bash
dotnet add package PolishOpenData.Regon
```

```csharp
using Microsoft.Extensions.DependencyInjection;
using PolishOpenData;
using PolishOpenData.Bir;
using System.Linq;

var services = new ServiceCollection();
services.AddRegonClient(o => o.UserKey = Environment.GetEnvironmentVariable("MY_APP_REGON_KEY")); // your key, from your own configuration
// GUS's public test environment needs no key: services.AddRegonClient(o => o.Environment = RegonEnvironment.Test);
using var provider = services.BuildServiceProvider();
var regon = provider.GetRequiredService<IRegonClient>();

var found = await regon.FindByNipAsync(Nip.Parse("526-104-08-28"));
var entity = found.Value[0];
Console.WriteLine($"{entity.Name}, {entity.Address.City} (REGON {entity.Regon})");

var details = (await regon.GetDetailsAsync(entity, RegonInclude.Pkd)).Value[0];
Console.WriteLine($"{details.LegalFormName}; main PKD {details.Pkd!.First(p => p.IsPrimary).Code}");
Console.WriteLine($"Source: GUS REGON ({found.Environment}), retrieved {found.RetrievedAt:u}");

await regon.LogoutAsync();
```

The client limits itself to GUS's strictest window by default (3 calls per second, 120 per minute, 6,000 per hour; a batch of up to 20 identifiers is one call for the per-second limit, while the per-minute and per-hour limits count requests and identifiers: each identifier of a search counts one, and so does every other request — login, report, `GetDataDateAsync`, logout) and keeps one login session. A batch method checks its whole batch before the first request and throws `QuotaExceededException` without sending anything when it does not fit into what is left of the minute or hour window: under the default window one call carries at most 119 distinct identifiers (120 when the session is already logged in, fewer when other calls used the last minute). Whether GUS also counts every identifier of a batch toward its own per-second limit is not confirmed; if you send many batches, use smaller ones or space them out. Register `AddRegonClient` once per process and reuse the client: GUS counts requests per application instance, and a session works only from the IP address that created it. Call `LogoutAsync` when your process stops; the client never does it by itself. A client created without DI gets a private session and limiter.

Retries stay off for REGON, as for Biała Lista: a retry made by a resilience handler is another GUS request that the request limiter cannot count, and a SOAP fault, which means your request was wrong, arrives as HTTP 500 and would be retried. If you add `AddStandardResilienceHandler`, switch its retries off as in the Biała Lista example above.

**Keys and the test environment.** The library is bring-your-own-key: it ships no production key, never reads environment variables and never logs or prints your key. GUS's technical manual (section 3.6.3) calls it unacceptable for software to make every end user apply for their own key, but GUS told the maintainer on 2026-10-15 that for this project each end user may apply for and use their own key; that answer covers this project, so check GUS's rules before you distribute your own application built on the library. `RegonEnvironment.Test` selects GUS's public test service and its published test key (a `UserKey` that you configure is ignored there and never sent to the test service, so you can keep your own key configured and switch the environment); every result says which environment it came from (`RegonResult.Environment`), so anonymised test data (mostly from 2014) cannot pass for current data.

**Cost.** Details cost requests: a legal person takes a login, a search and one report (3 requests), each optional list (`RegonInclude.Pkd`, `LocalUnits`, `Partners`) one more, and a sole trader the general report plus one report per activity type. `RegonResult.DataAsOf` is filled once `GetDataDateAsync` has been called (one request).

## Offline VAT whitelist check

The Ministry of Finance publishes the VAT whitelist every day as a flat file (plik płaski) of hashed NIP and account pairs. `PolishOpenData.BialaLista.Offline` downloads the file for a date, builds a local index once, and then answers any number of checks from disk: no request limit, and nothing about the checked NIP/account pairs leaves your machine. Use it for large batches (a payment run, an ERP import); use `PolishOpenData.BialaLista` when you need a request ID as proof.

```bash
dotnet add package PolishOpenData.BialaLista.Offline
```

```csharp
using Microsoft.Extensions.DependencyInjection;
using PolishOpenData;
using PolishOpenData.BialaLista.Offline;

var services = new ServiceCollection();
services.AddVatWhitelistOffline(o => o.DatesToKeep = 3);
using var provider = services.BuildServiceProvider();
var offline = provider.GetRequiredService<IVatWhitelistOffline>();

await offline.PrepareAsync(); // optional: downloads and indexes today's file now instead of in the first check
var checks = await offline.CheckManyAsync(new[]
{
    (Nip.Parse("774-000-14-54"), Nrb.Parse("06 1600 1127 1843 9838 2000 0034")),
});
foreach (var c in checks)
    Console.WriteLine($"{c.Nip}: {c.Status} (file of {c.FileDate:yyyy-MM-dd}, generated {c.GeneratedOn:yyyy-MM-dd})");
```

- **Three answers.** `Active`, `Exempt` or `NotFound`. `NotFound` means not found in the file (the plain hash and every virtual-account mask of the account's bank were tried), not proof that the account is absent from the whitelist; `MatchedVirtualAccountMask` tells you when a pair was found through a mask.
- **First use of a date is slow.** It downloads about 250 MB and builds an index. That took about 4 minutes on a fast connection (October 2026), but much longer when the Ministry's server is slow, which happens during the working day. The download is deleted once the index is built. Call `PrepareAsync` ahead of time to avoid the wait: the Ministry publishes the file for day D at about 23:00 on D−1, so the evening before is a good moment. `PrepareAsync` takes an `IProgress<VatWhitelistPreparationProgress>` and a `CancellationToken`. The HTTP client registered by `AddVatWhitelistOffline` has no timeout; cancel through the token.
- **Data directory and disk use.** Prepared indexes live in `PolishOpenData/BialaListaOffline` under the user's local application data folder; set `VatWhitelistOfflineOptions.DataDirectory` to use another folder. An index takes about 60 MB per date (3.7 million entries in October 2026). `DatesToKeep` (default 5) is the number of most recently used dates kept on disk.
- **One machine only.** Use a folder on a local disk; a network share is not supported. Processes on the same machine that share the folder share the prepared indexes; while one process prepares a date, another that needs the same date waits up to `LockTimeout` (15 minutes by default) and then throws `TimeoutException`.
- **Dates.** From 30 days back to tomorrow (Warsaw time), default today. A date is only ever answered with the file of that date. Outside the window, or when the file is not published yet, cannot be downloaded or fails verification, the call throws `VatWhitelistDataUnavailableException` (see `Reason`) instead of answering with another day's data.
- **No request ID, no evidence.** The flat file gives no request identifier. When you need proof for the tax office, check with the REST client (`PolishOpenData.BialaLista`) and keep its `RequestId`.
- **Time of the check and time of the data.** `FileDate` and `GeneratedOn` describe the data; record when you ran the check yourself.

## Parcels and geocoding

`PolishOpenData.Uldk` finds cadastral parcels (działki ewidencyjne) with GUGiK's ULDK service and `PolishOpenData.Uug` turns an address into coordinates with GUGiK's geocoder; the clients share the coordinate types of `PolishOpenData.Geo.Core`. Both services are public and need no key.

```bash
dotnet add package PolishOpenData.Uldk
dotnet add package PolishOpenData.Uug
```

```csharp
using Microsoft.Extensions.DependencyInjection;
using PolishOpenData.Geo;
using PolishOpenData.Uldk;
using PolishOpenData.Uug;

var services = new ServiceCollection();
services.AddUldkClient();
services.AddUugClient();
using var provider = services.BuildServiceProvider();
var uug = provider.GetRequiredService<IUugClient>();
var uldk = provider.GetRequiredService<IUldkClient>();

// Address -> coordinates -> parcel. Only an Address match is a point; a partial address gives a Street or City match.
var matches = await uug.GeocodeAsync("Warszawa, Krucza 50");
if (matches is [{ Kind: UugMatchKind.Address } match])
{
    var parcel = await uldk.GetParcelAtAsync(match.Location, ParcelGeometryKind.Wgs84);
    Console.WriteLine($"{parcel?.Id}, {parcel?.CommuneName}");   // null: no parcel at that point
    Console.WriteLine(parcel?.Geometry?.Wkt);                    // POLYGON((longitude latitude, ...)), EPSG:4326
}

// By identifier or by region name and number ("Krzewina 134" can match several parcels, so there is no geometry).
var byId = await uldk.GetParcelByIdAsync(ParcelId.Parse("146510_8.0507.41/1"));
var byName = await uldk.FindParcelsAsync("Krzewina 134");
```

- **Axis order.** No position is a bare pair of doubles: `Wgs84Point(Latitude, Longitude)` and `Pl1992Point(Easting, Northing)` name their axes, and there are no `X`/`Y` members. Latitude comes first in `Wgs84Point` (Warsaw is about 52.2, 21.0), whereas WKT, including the geometry the library returns, writes longitude first. A swapped WGS-84 pair is rejected with `ArgumentOutOfRangeException` before any request, because the ranges for Poland (latitude 48.9 to 55.0, longitude 14.0 to 24.2) do not overlap.
- **Not found is not an error.** A parcel that does not exist is `null`, a search without a match an empty list. An answer that is an HTML page (the GUGiK firewall) throws `PolishOpenDataApiException` with `ErrorCode = "blocked"`.
- **What a parcel has.** ID, number, region, commune, county, voivodeship, the bounding box in PL-1992 (`Extent`) and, on request, the boundary as WKT in PL-1992 or in WGS-84 (`ParcelGeometryKind`; WGS-84 costs one extra request). ULDK reports no area.
- **An address point is not a property.** It marks a building entrance, so the parcel found at it is the one under that entrance, not necessarily the whole property.
- **Retries and parallelism.** The libraries never retry. Registered with `AddUldkClient` or `AddUugClient`, a client opens at most 4 connections per service. If you want retries, add `.AddStandardResilienceHandler()` (from the `Microsoft.Extensions.Http.Resilience` package) to the returned builder, and keep them few.

## Property prices

`PolishOpenData.Rcn` reads real estate transactions from GUGiK's RCN register (Rejestr Cen Nieruchomości) through its public WFS service: premises (flats, garages, shops), parcels and buildings, with price, date and place. It uses the coordinate types of `PolishOpenData.Geo.Core` and needs no key.

```bash
dotnet add package PolishOpenData.Rcn
```

```csharp
using Microsoft.Extensions.DependencyInjection;
using PolishOpenData.Geo;
using PolishOpenData.Rcn;

var services = new ServiceCollection();
services.AddRcnClient();
using var provider = services.BuildServiceProvider();
var rcn = provider.GetRequiredService<IRcnClient>();

// Flats sold in 2025 in a 300 m square in central Warsaw (PL-1992: easting, northing).
var transactions = await rcn.GetTransactionsAsync(new RcnQuery
{
    Area = new Pl1992Bbox(new Pl1992Point(637800, 486300), new Pl1992Point(638100, 486600)),
    From = new DateOnly(2025, 1, 1),
    To = new DateOnly(2025, 12, 31),
    PremisesFunction = RcnPremisesFunction.Residential,
});
var summary = transactions.Summarize();   // throws InvalidOperationException when transactions.Truncated
Console.WriteLine($"{summary.Count} sales, median {summary.Median:N0} PLN/m², newest {summary.Newest}");
```

- **Bounded queries only.** An `RcnQuery` needs an area (`Area`, at most 25 km²) or a four-digit powiat code (`PowiatCode`; `1465` is Warsaw). The service has no gmina filter: use an area for anything smaller than a powiat. `MaxRecords` defaults to 1,000 (1 to 5,000). When more rows exist, `Truncated` is `true`, nothing is dropped silently and `Summarize()` throws: narrow the area, the period or the filters. The client sends one `count`-bounded request per page of 500 rows.
- **Price per m² comes from the premises row.** `RcnTransaction.PricePerSquareMetre` is the price of the premises divided by its usable area. It is never derived from `TotalPrice`, the price of the whole deed, which repeats on every row of a deed that covers several premises, parcels or buildings. It is `null` when the price or the area is missing or not positive. Garages and shops share the premises layer with flats, so filter with `PremisesFunction` (the MCP tool defaults to residential).
- **`Summarize()` takes a market-price sample.** It uses premises sales that have a price per m², no partial share (`1/1` or none), a free-market or unstated kind of sale and ownership of the premises with the associated right (or no right stated). It reports the median, the quartiles (linear interpolation) and how many rows each exclusion removed; fewer than 5 sales are flagged `SmallSample`.
- **The data lag by months.** On 15 October 2026 the newest transaction in powiat 1465 was dated 28 May 2026. Check `Newest` before you quote a figure as current.
- **Position and fields.** `Location` is a `Pl1992Point`, the centre of the feature's envelope (the library does not read polygon geometry). Typed properties cover what has a meaning for a layer (date, market, rooms, floor, usable area, parcel and building identifiers, address); `Fields` holds every published field as text, including the notary's name and the deed number. The client never retries and, registered with `AddRcnClient`, opens at most 4 connections. The service reports errors as HTTP 400 with an OGC exception report, which the client throws as `PolishOpenDataApiException`.

## MCP server for AI assistants

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download). No API keys needed; the optional REGON tool needs your own free GUS key (see below). The parcel, geocoding and property price tools send every request to GUGiK, and retry a transient failure once.

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

**Optional: the REGON tool.** `lookup_regon` appears only when the server finds your own free GUS key in the environment variable `POLISHOPENDATA_REGON_KEY`; without it the server has no REGON tool and sends nothing to GUS. Apply for the key at GUS (`regon_bir@stat.gov.pl`); it stays on your machine and is sent only to GUS. Add it to the server's `env` block, for example in `claude_desktop_config.json` or `.cursor/mcp.json`:

```json
{
  "mcpServers": {
    "polish-open-data": {
      "command": "dotnet",
      "args": ["dnx", "PolishOpenData.Mcp", "--yes"],
      "env": { "POLISHOPENDATA_REGON_KEY": "your-own-gus-key" }
    }
  }
}
```

Claude Code (`claude mcp add --env`) and VS Code (`env` in `.vscode/mcp.json`) take the same variable.

| Tool | What it answers |
|---|---|
| `lookup_company` | "Who is NIP 774-000-14-54?" — name, VAT status, seat, bank accounts, KRS legal form, capital, removal |
| `check_vat_bank_account` | "Can I pay this invoice to this account?" — whitelist check with request ID |
| `check_vat_bank_account_offline` | Same question against the Ministry of Finance daily flat file: no request limit, up to 50 pairs at once, nothing about the pairs leaves your machine, no request ID (use `check_vat_bank_account` when you need one). The first call for a date downloads about 250 MB (the file is deleted once the index is built) and takes several minutes, or much longer when the Ministry's server is slow; the tool reports progress. `not_found` means not found in the file, not proof of absence. Prepared indexes are kept in the user's local application data folder; set `POLISHOPENDATA_OFFLINE_DIR` to use another local folder (not a network share) |
| `get_krs_extract` | Board and supervisory members (masked by the ministry), capital, shareholders, PKD activities |
| `find_parcel` | "Which parcel is this?" — by parcel ID, WGS-84 coordinates (latitude first), address or region name and number ("Krzewina 134", all matches). Returns the parcel ID, number, region, commune, county, voivodeship and the center of its bounding box; `includeGeometry` adds the full boundary as WKT in EPSG:4326 (not for region searches). An address gives a parcel only when it resolves to exactly one address point (a building entrance, not necessarily the whole property); otherwise the geocoder's candidates. No parcel area (ULDK has none). Sources: ULDK, and UUG for an address (`sources`) |
| `geocode_address` | "Where is this address?" — WGS-84 coordinates and match accuracy; every match says whether it is an address, a street or a city (a partial address gives a representative point of the street or city). Source: UUG (`source`) |
| `property_prices` | "What did real estate sell for here?" — the median and quartile price per m² of flats (market sales of whole premises; the premises price over its usable area) and the newest single transactions for premises, parcels or buildings, from the RCN register via GUGiK. Search by exactly one of an address (it must resolve to one address point, otherwise the geocoder's candidates), WGS-84 coordinates (latitude first) or a four-digit powiat code; an address or coordinates take a radius of 50 to 2,500 m (default 500). Default period: the last 24 months; default premises function: residential (`premisesFunction` also takes garage, commercial, other and any). The data lag by months, so every answer says how old the newest transaction is. When more than 1,000 transactions match, the tool returns an error asking you to narrow the area, the period or the filters instead of a partial answer. A transaction shows date, price, address, floor, area, market, the deed number and the buyer and seller by kind only (natural person, company, …); the notary is never shown and there is no search by person. Informational, not a valuation. Sources: RCN, and UUG for an address (`sources`) |
| `lookup_regon` | "What does GUS's REGON register say about this NIP?" (optional, needs your own GUS key) — for every matching entry: type, name, REGON, NIP, address and, for the first five, status (derived from GUS's dates), key dates, main PKD activity, and for legal persons legal form and register. Never contact data, owners' personal details or local-unit lists; names are untrusted text. Production data only; source: GUS BIR1.2 (`sources`) |
| `validate_identifier` | Offline checksum validation of NIP, REGON, KRS, NRB and KW numbers, with typo suggestions |

## Land-registry (KW) numbers

`KwNumber` validates numbers such as `WA1M/00012345/1` offline using the official court-code table (Dz.U. 2026 poz. 740, 342 current codes plus 7 historical ones) and suggests fixes for common typos (`0S1U` → `OS1U`). It is a .NET port of the validation logic of [pyekw](https://github.com/mhajder/pyekw) by mhajder (MIT). It **does not** query the eKW portal and deliberately offers no number generator: KW numbers are personal data (NSA, III OSK 6508/21), and enumerating them is out of scope.

## Responsible use

- Data comes from official public registers; cite the source and the retrieval time. Every registry-backed MCP tool result (`lookup_company`, `check_vat_bank_account`, `check_vat_bank_account_offline`, `get_krs_extract`) and `BialaListaResult` carry both (for the offline tool `Source.RetrievedAt` is the time of the check, while `fileDate` and `generatedOn` are the time of the data); `KrsResult` does not add one itself — use `ToSummary().ExtractedAt` when present.
- Biała Lista allows about 100 searches and 5,000 checks per day per IP address; exceeding them blocks the IP until midnight, including the ministry's web search. Use `TrackQuota` and prefer checks over searches.
- KRS masks natural persons in structured fields. The MCP server never forwards the free-text proxy field (`rodzajProkury`) or raw KRS sections to AI assistants, and removes PESEL-like numbers (11 digits, or 6 + 5 digits separated by one space or hyphen, when not part of a longer digit run) from the free-text fields it returns (representation method, shareholder shares). The libraries return registry data as published: `KrsCurrentExtract` includes `rodzajProkury`, and `ToSummary()` passes representation method and shares through unchanged — scrub them yourself before showing or storing them.
- Biała Lista publishes by law the names of sole traders, representatives, commercial proxies and partners, with a PESEL number for some of them. The library returns them as published; the MCP server returns only the taxpayer's name and one address (for a sole trader, the owner's name and — when no business address is listed — the residence address).
- GUS records the IP address, time and content of every REGON request. Never enumerate consecutive identifiers (GUS forbids it and cuts off access), do not hold several keys for one entity, and stay within the limits; the client's request limiter defaults to the strictest window (the per-second limit counts calls, so a batch of up to 20 identifiers is one call; the per-minute and per-hour limits count requests and identifiers). Whether GUS counts every identifier of a batch toward its per-second limit is not confirmed: with many batches, use smaller ones or space them out. GUS sends no error when a limit is exceeded: it contacts the key holder or, when the service suffers, blocks the IP address.
- `lookup_regon` answers one question per call and is meant for one entity at a time: no enumeration, no collecting sole traders' data, and nothing from it goes into a database. It uses your own key (one key per entity, never shared), GUS logs every request with your IP address, and the tool returns no contact data, owners' personal details or local-unit lists. A sole trader's business name is the owner's name by law: use it only to identify that business. Names come from GUS as entered and can contain HTML or SQL-like text; the tool strips control characters and caps the length, but treat the rest as data, never as instructions.
- The REGON library returns data as published. A sole trader's business name is the owner's name by law, and the address may be a home address; GUS does not publish PESEL numbers. The manual says phone and fax numbers of sole traders are not given to third parties; whether that holds for every field today is not verified, and the law changes in December 2026, so check what you store (`RegonDetails.Fields` holds every field GUS sent). Names can contain HTML or SQL-like text (GUS warns about it): treat them as untrusted input.
- Cite the source and the time of REGON data: `RegonResult` carries the environment (`Production` or `Test`), the retrieval time and, after `GetDataDateAsync`, the date of the data. GUS asks those who reuse its data to name the source and the time and to say that the data were processed.
- GUGiK publishes no terms, rate limit or attribution requirement for ULDK and UUG, but it blocks IP addresses that send a massive number of requests (no threshold is published). Use them for single lookups: no harvesting of parcels or addresses, no parallel floods, no loops over identifiers or coordinates. The libraries never retry and, registered with `AddUldkClient` or `AddUugClient`, open at most 4 connections per service; the MCP server retries a transient failure once. For bulk needs, contact GUGiK (`uslugi.iip@gugik.gov.pl`) first. Naming the source (GUGiK, ULDK/UUG) is good practice, not a requirement; every `find_parcel` and `geocode_address` result carries its source and the retrieval time. Parcel data are EGiB cadastre records, informational only.
- RCN transactions are public register entries, but they describe what people paid for their homes. The library returns every published field, including the notary's name and the deed number (`RcnTransaction.Fields`); the `property_prices` tool shows the deed number and the buyer and seller by kind only, never the notary. Do not use either to profile individuals or to find out who bought or sold a property. RCN has no land-registry (KW) number, and nothing here links a transaction or a parcel to one. GUGiK publishes no limit for RCN: send single, bounded queries (the client sends one `count`-bounded request per page of 500 rows), with no harvesting of the register and no loops over areas or periods. The data lag by months, and the price per m² is derived from the premises row: informational, not a valuation. Every `property_prices` result carries its sources and the retrieval time.
- No scraping, no bulk harvesting, and never land-registry (KW) numbers: the parcel and property tools neither accept nor return them or owner data, and nothing in this project links a parcel to a land-registry number. Do not try to build such a link from parcel data.

## Possible next steps

Ideas, not commitments: more property data beyond transaction prices, which are done. If one of them would help you, [open an issue](https://github.com/JacekSmi/PolishOpenData/issues) to show interest.

## Contributing and license

See [CONTRIBUTING.md](https://github.com/JacekSmi/PolishOpenData/blob/main/CONTRIBUTING.md). MIT licensed. Third-party components and their licences are listed in [THIRD-PARTY-NOTICES.md](https://github.com/JacekSmi/PolishOpenData/blob/main/THIRD-PARTY-NOTICES.md).

<!-- mcp-name: io.github.JacekSmi/PolishOpenData -->
