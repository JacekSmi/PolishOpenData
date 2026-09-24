# PolishOpenData

**Biblioteki .NET i serwer MCP dla polskich rejestrów publicznych** — dane firm z KRS, Biała Lista podatników VAT ze sprawdzaniem rachunków oraz walidacja offline NIP, REGON, numeru KRS, NRB i numeru księgi wieczystej (KW).

🇬🇧 [English version](README.md)

> Projekt nieoficjalny. Niezwiązany z Ministerstwem Sprawiedliwości, Ministerstwem Finansów ani innym organem publicznym.

## Pakiety

| Pakiet | Zawartość |
|---|---|
| `PolishOpenData.Core` | Typy wartości offline: `Nip`, `Regon`, `KrsNumber`, `Nrb`, `KwNumber` (+ oficjalna tabela kodów sądów) |
| `PolishOpenData.Krs` | API KRS: odpis aktualny i pełny, podmioty wykreślone, dzienny biuletyn zmian, podsumowanie |
| `PolishOpenData.BialaLista` | Wykaz podatników VAT: status, sprawdzenie rachunku z identyfikatorem zapytania, zapytania zbiorcze, ochrona limitu dziennego |
| `PolishOpenData.Mcp` | Serwer MCP udostępniający powyższe asystentom AI |

Biblioteki działają na .NET 10 oraz .NET Framework / starszych .NET (netstandard2.0) — także w dodatkach do systemów ERP.

## Szybki start

```csharp
using Microsoft.Extensions.DependencyInjection;
using PolishOpenData;
using PolishOpenData.BialaLista;

var services = new ServiceCollection();
services.AddBialaListaClient(o => o.TrackQuota = true).AddStandardResilienceHandler();
using var provider = services.BuildServiceProvider();
var vat = provider.GetRequiredService<IBialaListaClient>();

var check = await vat.CheckBankAccountAsync(Nip.Parse("774-000-14-54"), Nrb.Parse("06 1600 1127 1843 9838 2000 0034"));
Console.WriteLine(check.Value ? "Rachunek jest na Białej liście" : "Rachunku NIE ma na Białej liście");
Console.WriteLine("Identyfikator zapytania (zachowaj jako dowód): " + check.RequestId);
```

## Serwer MCP

Wymaga [.NET 10 SDK](https://dotnet.microsoft.com/download). Bez kluczy API.

```bash
claude mcp add --transport stdio --scope user polish-open-data -- dotnet dnx PolishOpenData.Mcp --yes
```

Konfiguracje dla VS Code, Claude Desktop i Cursor: zob. [README.md](README.md#mcp-server-for-ai-assistants).

Narzędzia: `lookup_company` (dane firmy po NIP/REGON/KRS), `check_vat_bank_account` (Biała Lista przed zapłatą faktury), `get_krs_extract` (zarząd, kapitał, PKD), `validate_identifier` (walidacja numerów offline).

## Księgi wieczyste

`KwNumber` sprawdza offline numery w formacie `WA1M/00012345/1` na podstawie oficjalnej tabeli kodów (Dz.U. 2026 poz. 740) i podpowiada poprawki typowych literówek. To port logiki walidacji z [pyekw](https://github.com/mhajder/pyekw) (MIT). Biblioteka **nie** łączy się z portalem EKW i celowo nie generuje numerów ksiąg — numer KW jest daną osobową (NSA, III OSK 6508/21).

## Odpowiedzialne korzystanie

- Podawaj źródło danych i czas pobrania (każdy wynik je zawiera).
- Biała Lista pozwala na ok. 100 wyszukiwań i 5000 sprawdzeń dziennie z jednego adresu IP; przekroczenie blokuje IP do północy, także w wyszukiwarce ministerstwa.
- Bez scrapingu, bez masowego pobierania, bez łączenia działek z numerami ksiąg wieczystych.

Licencja MIT. Zasady współpracy: [CONTRIBUTING.md](CONTRIBUTING.md).
