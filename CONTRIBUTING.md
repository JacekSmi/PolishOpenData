# Contributing

## Build and test

```bash
dotnet build -c Release
dotnet test -c Release
```

Requirements: .NET 10 SDK (see `global.json`). On Windows the Core, Shared, Krs, BialaLista, BialaLista.Offline, Regon, Geo.Core, Uldk, Uug and Rcn tests also run on .NET Framework 4.7.2. Tests use xUnit v3 on Microsoft Testing Platform (`dotnet test --project tests/<Project>` runs one project).

## Live tests

`tests/PolishOpenData.SmokeTests` checks the libraries, the MCP server (in process, over the MCP protocol) and the published MCP package (`dotnet dnx PolishOpenData.Mcp --yes`, over stdio) against the real KRS, Biała Lista, GUS BIR (REGON), GUGiK ULDK, GUGiK UUG and GUGiK RCN APIs, with ORLEN S.A. as the known subject for KRS and Biała Lista, GUS itself for REGON, a parcel and an address in Warsaw (Krucza 50) for ULDK and UUG, and an area in central Warsaw and powiat 1465 for RCN. Without `POLISHOPENDATA_SMOKE=1` every test is skipped.

```bash
POLISHOPENDATA_SMOKE=1 dotnet test --project tests/PolishOpenData.SmokeTests -c Release
```

```powershell
$env:POLISHOPENDATA_SMOKE='1'; try { dotnet test --project tests/PolishOpenData.SmokeTests -c Release } finally { Remove-Item Env:POLISHOPENDATA_SMOKE }
```

The package test uses the latest version on nuget.org; set `POLISHOPENDATA_SMOKE_MCP_VERSION` to test another one. An exact version (for example `1.0.0`) is also compared with the version the server reports; a floating version or range (for example `1.*`) is only passed to dnx. Its first run downloads the package.

One run costs, per IP address:

| | Libraries | MCP in process | Published package | Total |
|---|---|---|---|---|
| Biała Lista searches (each NIP or REGON counts, also inside a batch) | 4 | 1 | 1 | **6** (5 requests) |
| Biała Lista checks | 1 | 1 | 0 | **2** |
| KRS requests | 5 (up to 9) | 1 | 1 | **7** (up to 11) |
| GUS BIR test environment requests (published test key, no secret) | 5 | 0 | 0 | **5** |
| GUS BIR production requests (needs `POLISHOPENDATA_REGON_KEY`, owner-run, skipped otherwise) | 4 | 4 | 0 | **8** |
| GUGiK ULDK requests (no key) | 5 | 0 | 0 | **5** |
| GUGiK UUG requests (no key) | 3 | 1 | 0 | **4** (the MCP one geocodes the address of the `property_prices` test) |
| GUGiK RCN requests (no key) | 4 (1+1+2) | 1 | 0 | **5** (library: premises in a bbox, residential premises for the median, parcels, buildings; MCP: `property_prices` for "Warszawa, Krucza 50"; each a single bounded query of at most 1,000 rows) |

These are the counts when every call succeeds. The change feed test asks for an earlier weekday, up to 5 in all, only while the feeds it got were empty (a public holiday, or a feed not yet published). Failed calls are not cached, so a cancelled or failed search is sent again by the next test that needs it: a run with failures spends at most 9 searches. The MCP server never retries Biała Lista; its KRS client retries transient failures (5xx, 408, 429, connection errors and 10-second attempt timeouts), which can add KRS requests.

The offline flat-file test (`BialaListaOfflineLiveTests`) downloads one file of about 250 MB from the Ministry of Finance, which has no request quota, and uses 14 Biała Lista checks as its oracle (counted in the checks row). It skips itself after 15 minutes of downloading, because MF's server can be slow during the working day.

The GUS test-environment test needs no secret. GUS publishes limits for the service in general (3 requests per second, 120 per minute); it does not state whether the test service applies them, and 5 requests are far inside them either way. The production test runs only when `POLISHOPENDATA_REGON_KEY` holds your own key and is never run by CI: the production key does not go into GitHub secrets, and GUS forbids several accounts for one entity. GUS logs the IP address, time and content of every request and forbids enumerating identifiers, so run the production test sparingly, never in a loop.

GUGiK publishes no limit for ULDK, UUG and RCN (the RCN client sends one `count`-bounded request per page), but it blocks IP addresses that send massive numbers of requests: a few runs a day are fine, never run them in a loop. A `blocked` error (the firewall page) fails the test; wait before running again.

Biała Lista allows 100 searches and 5,000 checks per IP address per day, and exceeding either blocks the IP until midnight Warsaw time, including your own searches on podatki.gov.pl. Running the live tests locally a few times a day is fine; don't run them in a loop. A test that reaches a limit is skipped, not failed. The same table is in the `Live` class.

## Rules

- Unit tests never call the network. They replay recorded responses from `tests/Fixtures/`.
- Live tests (`tests/PolishOpenData.SmokeTests`) call the real registries and run only with `POLISHOPENDATA_SMOKE=1`; see [Live tests](#live-tests) for their cost. The weekly `nightly-smoke` workflow runs them (including the GUS test-environment REGON test, but never the production one) and opens an issue labelled `smoke-failure` when a registry changes. GitHub disables scheduled workflows in a public repository after 60 days without repository activity; if that happens, re-enable it with `gh workflow enable nightly-smoke`.
- New fixtures must be redacted before committing: keep the ministry's masks (such as `F*****`) as returned; replace any unmasked name of a natural person with `JAN`/`KOWALSKI`; replace free-text fields that can contain names or PESEL numbers (KRS `rodzajProkury`, `umowaStatut` text) with `[REDACTED]`, and likewise the names of notaries or proxies inside other free text, such as the dzial6 merger/split descriptions (`opisPolaczeniaPodzialuPrzeksztalcenia`). Sources and the redaction already applied are listed in [`tests/Fixtures/README.md`](tests/Fixtures/README.md).
- The `PolishOpenData.Regon` fixtures live in `tests/Fixtures/regon/` and `.gitattributes` marks them `-text`: the recorded `.mtom` files are byte copies of GUS responses whose CRLF framing must survive, so copy them with `cp`, never through an editor, and keep `.gitattributes` committed before you add one. Recorded fixtures hold legal entities only (GUS itself); natural persons appear only as hand-made `synthetic-*` files with `JAN`/`KOWALSKI` and the identifiers of GUS's test database. Sources are in [`tests/Fixtures/README.md`](tests/Fixtures/README.md).
- Warnings are errors; code uses invariant culture and ordinal comparisons; every async test passes `TestContext.Current.CancellationToken`.

## Updating the KW court table

When a new regulation replaces annex 1 to Dz.U. 2016 poz. 312, update `src/PolishOpenData.Core/Resources/kw-courts.tsv` (`code<TAB>last_valid_on<TAB>name`): add new codes with an empty `last_valid_on`, and for removed codes set `last_valid_on` to the day before the new act takes effect (never delete a code — old books keep it). Update the counts in `KwCourtsTests` and cite the act in the file header.

## The demo

`demo/PolishOpenData.Demo` is a Blazor WebAssembly page, a map of parcels and flat sale prices built with the Uldk, Uug and Rcn packages, live at https://jaceksmi.github.io/PolishOpenData/. Its unit tests are in `tests/PolishOpenData.Demo.Tests`: net10.0 only (so not in the .NET Framework list above), no network, no browser. `dotnet test --project tests/PolishOpenData.Demo.Tests` runs them.

Run it locally, then open http://localhost:5190/PolishOpenData/:

```bash
dotnet run --project demo/PolishOpenData.Demo -- --pathbase=/PolishOpenData
```

In Git Bash on Windows, start the command with `MSYS_NO_PATHCONV=1` (`MSYS_NO_PATHCONV=1 dotnet run ...`): without it Git Bash turns `/PolishOpenData` into a Windows path and the server stops at startup. PowerShell, cmd and other systems need nothing.

`index.html` commits `<base href="/PolishOpenData/" />`, the path of the Pages site, so the pathbase argument is needed. Pass it on the command line: with only the copy in `Properties/launchSettings.json`, `dotnet run` serves the files outside `/PolishOpenData/` and the page loads without its styles and scripts. The browser calls the GUGiK services directly (UUG, ULDK, RCN and the orthophoto tiles), so a local run makes real requests: a search costs 1 UUG, 1 ULDK and 1 RCN request, a map click 1 ULDK and 1 RCN, a changed radius or period 1 RCN, plus the map tiles. Be as gentle as with the live tests: nothing that searches in a loop.

`.github/workflows/pages.yml` deploys it to GitHub Pages. A push to `main` that touches `demo/`, `src/`, `Directory.Packages.props`, `Directory.Build.props`, `global.json` or the workflow itself, or "Run workflow" on `main`, publishes the demo and deploys it; a fork does not deploy. One-time setting by the repository owner, before the first run: GitHub → Settings → Pages → Build and deployment → Source: **GitHub Actions** (until then the build job fails in `configure-pages` with "Get Pages site failed"). The workflow needs no secret.

To publish locally the way the workflow does (only the output folder differs: `artifacts/` is ignored by git):

```bash
dotnet publish demo/PolishOpenData.Demo/PolishOpenData.Demo.csproj -c Release -o artifacts/pages -p:CompressionEnabled=false
```

`artifacts/pages/wwwroot` is the site: about 7.5 MB, about 2.9 MB gzipped, which is what Pages sends. `CompressionEnabled=false` leaves out the `.br` and `.gz` copies Blazor writes, because Pages ignores them (it compresses on the fly, never with brotli). To look at it, serve that folder under `/PolishOpenData/` with any static file server. If its `index.html` still contains `#[.{fingerprint}]`, delete `demo/PolishOpenData.Demo/obj` and `bin` and publish again (this happened once, after the fingerprint settings and `index.html` changed between two publishes; CI always starts clean).

Caching: Pages sends `Cache-Control: max-age=600` for every file. At publish time the runtime in `_framework` and every `.js` file get a content hash in their names (`StaticWebAssetFingerprintPattern` in the demo's csproj), so a new `index.html` always names scripts that match it, and Blazor's `import("./js/map.js")` reaches the same hashed file through the import map, so there is one module and one map. Every `.js` in `index.html` must therefore use the `#[.{fingerprint}]` placeholder: such a file is published only under its hashed name, and a plain `src` is a 404. Two gaps remain, because `index.html` and the `.css` files cannot be fingerprinted (the SDK turns a placeholder in a `<link href>` into the plain name). For up to 10 minutes after a deploy a browser can hold the previous `index.html`, and a reload can then pair the new page with the previous `app.css` or `leaflet.css`, which is cosmetic. A browser that holds the previous `index.html` but not every file it names (they are gone from the server after a deploy) can show a page that does not start; reloading after the 10 minutes fixes that. The runtime in `_framework` has always had the same exposure.

## Releasing

Push a tag `vX.Y.Z` on a commit that is on `main`; `.github/workflows/release.yml` checks that and does the rest (NuGet via Trusted Publishing, GitHub release, MCP Registry). The publish job runs in the `release` environment.

One-time setup by the repository owner. Do steps 2, 3 and 5 only after the repository is public: on GitHub Free, environments can only be configured on public repositories; a nuget.org policy created for a private repository is only temporarily active for 7 days and goes inactive if nothing is published in that window (the window can be restarted); and private vulnerability reporting exists only for public repositories.
1. A nuget.org account.
2. GitHub → Settings → Environments → **New environment** `release` → Deployment branches and tags: **Selected branches and tags**, add the tag rule `v*`. No required reviewers are needed. (The first release run would create the environment by itself, but without the tag rule.)
3. nuget.org → username menu → **Trusted Publishing** → add a policy: Repository Owner `JacekSmi`, Repository `PolishOpenData`, Workflow File `release.yml`, Environment `release`. Scope: allow pushing new packages and new versions, package glob `PolishOpenData.*` (the first release creates four new package IDs). Create it for the final, public repository. Optionally reserve the `PolishOpenData.*` ID prefix on nuget.org.
4. GitHub repository secret `NUGET_USER` = the nuget.org profile name (not the e-mail address).
5. GitHub → Settings → Advanced Security → enable **Private vulnerability reporting** (`SECURITY.md` relies on it).

Package validation compares each library package with the release named by `PackageValidationBaselineVersion` in `src/Directory.Build.props` (currently 1.6.0), so an accidental breaking change fails `dotnet pack`. After a release is on nuget.org, raise it to that version. The MCP tool package is not compared: the .NET SDK turns package validation off for tool packages.
