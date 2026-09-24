# Test fixtures

Recorded upstream responses that the unit tests replay offline. Each file holds a single response.

- **Sources:** `krs/` comes from the KRS Open API (`api-krs.ms.gov.pl`) of the Ministry of Justice, `bialalista/` from the VAT whitelist API (`wl-api.mf.gov.pl`) of the Ministry of Finance. Retrieved on 2026-09-24; `full-P-0000106150-removed-trimmed.json` is shortened.
- **Basis:** the KRS Open API is listed on dane.gov.pl (dataset 27606, "API Krajowego Rejestru Sądowego (API KRS)") under CC0 1.0. The Biała Lista responses are public register data published by the Ministry of Finance; no licence is claimed for them.
- **Masking:** natural persons in KRS appear only as masked by the Ministry of Justice (for example `F*****`). Free text that can hold names or PESEL numbers is replaced with `[REDACTED]`: `rodzajProkury`, the `umowaStatut` entries and the notary names inside dzial6 merger/split descriptions. Biała Lista representatives are replaced with `JAN KOWALSKI`.
- **Synthetic files:** `synthetic-*` files are hand-made, not recorded, and cover cases that cannot be triggered safely or on demand against the live API (daily-limit and database-update errors, null arrays with an unknown field).

New fixtures must follow the redaction rules in [CONTRIBUTING.md](../../CONTRIBUTING.md).
