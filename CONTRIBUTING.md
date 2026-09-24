# Contributing

## Build and test

```bash
dotnet build -c Release
dotnet test -c Release
```

Requirements: .NET 10 SDK (see `global.json`). On Windows the Core, Shared, Krs and BialaLista tests also run on .NET Framework 4.7.2. Tests use xUnit v3 on Microsoft Testing Platform (`dotnet test --project tests/<Project>` runs one project).

## Rules

- Unit tests never call the network. They replay recorded responses from `tests/Fixtures/`.
- **Never run the smoke tests with `POLISHOPENDATA_SMOKE=1` on your machine.** Biała Lista blocks your IP until midnight after 100 searches a day, including your own searches on podatki.gov.pl. The weekly `nightly-smoke` workflow runs them and opens an issue labelled `smoke-failure` when a registry changes. GitHub disables scheduled workflows in a public repository after 60 days without repository activity; if that happens, re-enable it with `gh workflow enable nightly-smoke`.
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
