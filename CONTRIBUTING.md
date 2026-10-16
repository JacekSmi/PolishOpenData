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

## Releasing

Push a tag `vX.Y.Z` on a commit that is on `main`; `.github/workflows/release.yml` checks that and does the rest (NuGet via Trusted Publishing, GitHub release, MCP Registry). The publish job runs in the `release` environment.

One-time setup by the repository owner. Do steps 2, 3 and 5 only after the repository is public: on GitHub Free, environments can only be configured on public repositories; a nuget.org policy created for a private repository is only temporarily active for 7 days and goes inactive if nothing is published in that window (the window can be restarted); and private vulnerability reporting exists only for public repositories.
1. A nuget.org account.
2. GitHub → Settings → Environments → **New environment** `release` → Deployment branches and tags: **Selected branches and tags**, add the tag rule `v*`. No required reviewers are needed. (The first release run would create the environment by itself, but without the tag rule.)
3. nuget.org → username menu → **Trusted Publishing** → add a policy: Repository Owner `JacekSmi`, Repository `PolishOpenData`, Workflow File `release.yml`, Environment `release`. Scope: allow pushing new packages and new versions, package glob `PolishOpenData.*` (the first release creates four new package IDs). Create it for the final, public repository. Optionally reserve the `PolishOpenData.*` ID prefix on nuget.org.
4. GitHub repository secret `NUGET_USER` = the nuget.org profile name (not the e-mail address).
5. GitHub → Settings → Advanced Security → enable **Private vulnerability reporting** (`SECURITY.md` relies on it).

Package validation compares each library package with the release named by `PackageValidationBaselineVersion` in `src/Directory.Build.props` (currently 1.5.0), so an accidental breaking change fails `dotnet pack`. After a release is on nuget.org, raise it to that version. The MCP tool package is not compared: the .NET SDK turns package validation off for tool packages. `PolishOpenData.Rcn` overrides `PackageValidationBaselineVersion` with an empty value in its project file, because no release of it exists yet to compare with: after 1.6.0 is on nuget.org, raise the baseline in `src/Directory.Build.props` to 1.6.0 and delete that override.
