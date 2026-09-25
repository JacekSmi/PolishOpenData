# Contributing

## Build and test

```bash
dotnet build -c Release
dotnet test -c Release
```

Requirements: .NET 10 SDK (see `global.json`). On Windows the Core, Shared, Krs and BialaLista tests also run on .NET Framework 4.7.2. Tests use xUnit v3 on Microsoft Testing Platform (`dotnet test --project tests/<Project>` runs one project).

## Live tests

`tests/PolishOpenData.SmokeTests` checks the libraries, the MCP server (in process, over the MCP protocol) and the published MCP package (`dotnet dnx PolishOpenData.Mcp --yes`, over stdio) against the real KRS and Biała Lista APIs, with ORLEN S.A. as the known subject. Without `POLISHOPENDATA_SMOKE=1` every test is skipped.

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

These are the counts when every call succeeds. The change feed test asks for an earlier weekday, up to 5 in all, only while the feeds it got were empty (a public holiday, or a feed not yet published). Failed calls are not cached, so a cancelled or failed search is sent again by the next test that needs it: a run with failures spends at most 9 searches. The MCP server never retries Biała Lista; its KRS client retries transient failures (5xx, 408, 429, connection errors and 10-second attempt timeouts), which can add KRS requests.

Biała Lista allows 100 searches and 5,000 checks per IP address per day, and exceeding either blocks the IP until midnight Warsaw time, including your own searches on podatki.gov.pl. Running the live tests locally a few times a day is fine; don't run them in a loop. A test that reaches a limit is skipped, not failed. The same table is in the `Live` class.

## Rules

- Unit tests never call the network. They replay recorded responses from `tests/Fixtures/`.
- Live tests (`tests/PolishOpenData.SmokeTests`) call the real registries and run only with `POLISHOPENDATA_SMOKE=1`; see [Live tests](#live-tests) for their cost. The weekly `nightly-smoke` workflow runs them and opens an issue labelled `smoke-failure` when a registry changes. GitHub disables scheduled workflows in a public repository after 60 days without repository activity; if that happens, re-enable it with `gh workflow enable nightly-smoke`.
- New fixtures must be redacted before committing: keep the ministry's masks (such as `F*****`) as returned; replace any unmasked name of a natural person with `JAN`/`KOWALSKI`; replace free-text fields that can contain names or PESEL numbers (KRS `rodzajProkury`, `umowaStatut` text) with `[REDACTED]`, and likewise the names of notaries or proxies inside other free text, such as the dzial6 merger/split descriptions (`opisPolaczeniaPodzialuPrzeksztalcenia`). Sources and the redaction already applied are listed in [`tests/Fixtures/README.md`](tests/Fixtures/README.md).
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
6. After 1.0.0 ships, set `<PackageValidationBaselineVersion>1.0.0</PackageValidationBaselineVersion>` in `src/Directory.Build.props` so accidental breaking changes fail the build.
