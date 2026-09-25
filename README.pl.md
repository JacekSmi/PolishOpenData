# PolishOpenData

**Biblioteki .NET i serwer MCP dla polskich rejestrów publicznych** — dane firm z KRS, Biała Lista podatników VAT ze sprawdzaniem rachunków oraz walidacja offline NIP, REGON, numeru KRS, NRB i numeru księgi wieczystej (KW).

🇬🇧 [English version](https://github.com/JacekSmi/PolishOpenData/blob/main/README.md)

> Projekt nieoficjalny. Niezwiązany z Ministerstwem Sprawiedliwości, Ministerstwem Finansów ani innym organem publicznym. Wyniki mają charakter informacyjny i nie stanowią porady prawnej ani podatkowej; miarodajnym źródłem są same rejestry.

## Pakiety

| Pakiet | Zawartość |
|---|---|
| `PolishOpenData.Core` | Typy wartości offline: `Nip`, `Regon`, `KrsNumber`, `Nrb`, `KwNumber` (+ oficjalna tabela kodów sądów) |
| `PolishOpenData.Krs` | API KRS: odpis aktualny i pełny, podmioty wykreślone, dzienny biuletyn zmian, podsumowanie |
| `PolishOpenData.BialaLista` | Wykaz podatników VAT: status, sprawdzenie rachunku z identyfikatorem zapytania, zapytania zbiorcze, ochrona limitu dziennego |
| `PolishOpenData.Mcp` | Serwer MCP udostępniający powyższe asystentom AI |

Biblioteki działają na .NET 10 oraz .NET Framework / starszych .NET (netstandard2.0) — także w dodatkach do systemów ERP.

## Walidacja offline

Bez sieci i bez DI:

```bash
dotnet add package PolishOpenData.Core
```

```csharp
using PolishOpenData;

Console.WriteLine(Nip.TryParse("774-000-14-54", out var nip) ? $"Poprawny NIP {nip}" : "Niepoprawny NIP"); // Poprawny NIP 7740001454
Console.WriteLine(KwNumber.TryParse("WA1M/00012345/1", out var kw) ? kw.Court?.Name : "Niepoprawny numer KW"); // Sąd Rejonowy dla Warszawy-Mokotowa …
Console.WriteLine(string.Join(", ", KwNumber.SuggestCorrections("WAIM/00012345/1"))); // WA1M/00012345/1
```

## Szybki start

```bash
dotnet add package PolishOpenData.BialaLista
dotnet add package Microsoft.Extensions.Http.Resilience
```

```csharp
using Microsoft.Extensions.DependencyInjection;
using PolishOpenData;
using PolishOpenData.BialaLista;
using System.Threading.Tasks;

var services = new ServiceCollection();
services.AddBialaListaClient(o => o.TrackQuota = true)
    .AddStandardResilienceHandler(o => o.Retry.ShouldHandle = _ => new ValueTask<bool>(false));
using var provider = services.BuildServiceProvider();
var vat = provider.GetRequiredService<IBialaListaClient>();

var check = await vat.CheckBankAccountAsync(Nip.Parse("774-000-14-54"), Nrb.Parse("06 1600 1127 1843 9838 2000 0034"));
Console.WriteLine(check.Value ? "Rachunek jest na Białej liście" : "Rachunku NIE ma na Białej liście");
Console.WriteLine("Identyfikator zapytania: " + check.RequestId);
```

**Zachowaj `RequestId`.** Każdy `BialaListaResult` zawiera identyfikator i czas zapytania zwrócone przez Białą Listę (`RequestId`, `RequestDateTime`); przechowuj je razem z dokumentacją płatności, aby móc wykazać, kiedy sprawdzono wykaz i jaka była odpowiedź.

Ponawianie zapytań (retry) jest wyłączone dla Białej Listy: każda próba ponowienia to kolejne zapytanie, którego nie liczy powyższy strażnik limitu, a po wyczerpaniu dziennego limitu Biała Lista blokuje cały adres IP (nie tylko ten proces) do północy. Zarejestruj `AddBialaListaClient` raz na proces i współdziel powstałego klienta (albo jeden wspólny `BialaListaQuotaTracker`) — klient utworzony bez DI i bez współdzielonego licznika dostaje własny, prywatny licznik, który nie widzi zapytań innych instancji.

## Serwer MCP

Wymaga [.NET 10 SDK](https://dotnet.microsoft.com/download). Bez kluczy API.

```bash
claude mcp add --transport stdio --scope user polish-open-data -- dotnet dnx PolishOpenData.Mcp --yes
```

Konfiguracje dla VS Code, Claude Desktop i Cursor: zob. [README.md](https://github.com/JacekSmi/PolishOpenData/blob/main/README.md#mcp-server-for-ai-assistants).

Narzędzia: `lookup_company` (dane firmy po NIP/REGON/KRS), `check_vat_bank_account` (Biała Lista przed zapłatą faktury), `get_krs_extract` (zarząd, kapitał, PKD), `validate_identifier` (walidacja numerów offline).

## Księgi wieczyste

`KwNumber` sprawdza offline numery w formacie `WA1M/00012345/1` na podstawie oficjalnej tabeli kodów (Dz.U. 2026 poz. 740) i podpowiada poprawki typowych literówek. To port logiki walidacji z [pyekw](https://github.com/mhajder/pyekw) autorstwa mhajder (MIT). Biblioteka **nie** łączy się z portalem EKW i celowo nie generuje numerów ksiąg — numer KW jest daną osobową (NSA, III OSK 6508/21).

## Odpowiedzialne korzystanie

- Podawaj źródło danych i czas pobrania. Zawiera je każdy wynik narzędzi MCP korzystających z rejestrów (`lookup_company`, `check_vat_bank_account`, `get_krs_extract`) oraz `BialaListaResult`; `KrsResult` nie dodaje własnego czasu pobrania — użyj `ToSummary().ExtractedAt`, jeśli jest dostępny.
- Biała Lista pozwala na ok. 100 wyszukiwań i 5000 sprawdzeń dziennie z jednego adresu IP; przekroczenie blokuje IP do północy, także w wyszukiwarce ministerstwa.
- KRS maskuje osoby fizyczne w polach strukturalnych. Serwer MCP nigdy nie przekazuje asystentom AI wolnotekstowego pola prokury (`rodzajProkury`) ani surowych sekcji KRS, a z pól wolnotekstowych, które zwraca (sposób reprezentacji, udziały wspólników), usuwa numery przypominające PESEL (11 cyfr albo 6 + 5 cyfr rozdzielonych jedną spacją lub łącznikiem, o ile nie są częścią dłuższego ciągu cyfr). Biblioteki zwracają dane w postaci opublikowanej przez rejestr: `KrsCurrentExtract` zawiera `rodzajProkury`, a `ToSummary()` przekazuje sposób reprezentacji i udziały bez zmian — przed wyświetleniem lub zapisaniem oczyść je we własnym zakresie.
- Biała Lista z mocy ustawy publikuje imiona i nazwiska przedsiębiorców jednoosobowych, reprezentantów, prokurentów i wspólników, a dla części z nich numer PESEL. Biblioteka zwraca je tak, jak zostały opublikowane; serwer MCP zwraca tylko nazwę podatnika i jeden adres (dla jednoosobowej działalności — imię i nazwisko właściciela oraz, gdy nie podano adresu działalności, adres zamieszkania).
- Bez scrapingu, bez masowego pobierania, bez łączenia działek z numerami ksiąg wieczystych.

## Możliwe kolejne kroki

Pomysły, nie zobowiązania: wyszukiwanie w GUS REGON (BIR), sprawdzanie offline na podstawie pliku płaskiego Białej Listy oraz dane nieruchomości (działki ULDK, geokoder GUGiK, ceny transakcyjne). Jeśli któryś z nich by Ci się przydał, [otwórz issue](https://github.com/JacekSmi/PolishOpenData/issues), aby dać znać.

## Współpraca i licencja

Licencja MIT. Komponenty zewnętrzne i ich licencje: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Zasady współpracy: [CONTRIBUTING.md](CONTRIBUTING.md).
