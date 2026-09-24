# Contributing

## Build and test

```bash
dotnet build -c Release
dotnet test -c Release
```

Requirements: .NET 10 SDK (see `global.json`). On Windows the Core, Shared, Krs and BialaLista tests also run on .NET Framework 4.7.2. Tests use xUnit v3 on Microsoft Testing Platform (`dotnet test --project tests/<Project>` runs one project).

## Rules

- Unit tests never call the network. They replay recorded responses from `tests/Fixtures/`.
- **Never run the smoke tests with `POLISHOPENDATA_SMOKE=1` on your machine.** Biała Lista blocks your IP until midnight after 100 searches a day, including your own searches on podatki.gov.pl. The nightly workflow runs them and opens an issue labelled `smoke-failure` when a registry changes.
- New fixtures must be redacted before committing: replace people's names with `JAN`/`KOWALSKI`, and free-text fields that can contain names or PESEL numbers (KRS `rodzajProkury`, `umowaStatut` text) with `[REDACTED]`.
- Warnings are errors; code uses invariant culture and ordinal comparisons; every async test passes `TestContext.Current.CancellationToken`.

## Updating the KW court table

When a new regulation replaces annex 1 to Dz.U. 2016 poz. 312, update `src/PolishOpenData.Core/Resources/kw-courts.tsv` (`code<TAB>last_valid_on<TAB>name`): add new codes with an empty `last_valid_on`, and for removed codes set `last_valid_on` to the day before the new act takes effect (never delete a code — old books keep it). Update the counts in `KwCourtsTests` and cite the act in the file header.

## Releasing

Push a tag `vX.Y.Z` on `main`; `.github/workflows/release.yml` does the rest (NuGet via Trusted Publishing, GitHub release, MCP Registry).

One-time setup by the repository owner:
1. A nuget.org account.
2. nuget.org → username menu → **Trusted Publishing** → add a policy: Repository Owner `JacekSmi`, Repository `PolishOpenData`, Workflow File `release.yml`, no environment. Scope: allow pushing new packages and new versions, package glob `PolishOpenData.*` (the first release creates four new package IDs). Optionally reserve the `PolishOpenData.*` ID prefix on nuget.org.
3. GitHub repository secret `NUGET_USER` = the nuget.org profile name (not the e-mail address).
4. GitHub → Settings → Advanced Security → enable **Private vulnerability reporting** (`SECURITY.md` relies on it).
5. After 1.0.0 ships, set `<PackageValidationBaselineVersion>1.0.0</PackageValidationBaselineVersion>` in `src/Directory.Build.props` so accidental breaking changes fail the build.
