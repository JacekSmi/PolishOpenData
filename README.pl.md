# PolishOpenData

**Biblioteki .NET i serwer MCP dla polskich rejestrów publicznych** — dane firm z KRS, Biała Lista podatników VAT ze sprawdzaniem rachunków (online albo offline na podstawie pliku płaskiego MF), klient rejestru GUS REGON (tylko biblioteka) oraz walidacja offline NIP, REGON, numeru KRS, NRB i numeru księgi wieczystej (KW).

🇬🇧 [English version](https://github.com/JacekSmi/PolishOpenData/blob/main/README.md)

> Projekt nieoficjalny. Niezwiązany z Ministerstwem Sprawiedliwości, Ministerstwem Finansów ani innym organem publicznym. Wyniki mają charakter informacyjny i nie stanowią porady prawnej ani podatkowej; miarodajnym źródłem są same rejestry.

## Pakiety

| Pakiet | Zawartość |
|---|---|
| `PolishOpenData.Core` | Typy wartości offline: `Nip`, `Regon`, `KrsNumber`, `Nrb`, `KwNumber` (+ oficjalna tabela kodów sądów) |
| `PolishOpenData.Krs` | API KRS: odpis aktualny i pełny, podmioty wykreślone, dzienny biuletyn zmian, podsumowanie |
| `PolishOpenData.BialaLista` | Wykaz podatników VAT: status, sprawdzenie rachunku z identyfikatorem zapytania, zapytania zbiorcze, ochrona limitu dziennego |
| `PolishOpenData.BialaLista.Offline` | Sprawdzanie rachunków offline na podstawie dziennego pliku płaskiego Ministerstwa Finansów: bez limitu zapytań, wiele par naraz, bez identyfikatora zapytania |
| `PolishOpenData.Regon` | GUS REGON (BIR1.2): wyszukiwanie po NIP/REGON/KRS, zapytania zbiorcze po 20, pełne raporty, sesja i ogranicznik zapytań; wymaga własnego, bezpłatnego klucza GUS |
| `PolishOpenData.Mcp` | Serwer MCP dla asystentów AI: dane firmy, Biała Lista i sprawdzanie rachunku (online i offline), odpisy KRS, walidacja numerów |

Biblioteki działają na .NET 10 oraz .NET Framework / starszych .NET (netstandard2.0) — także w dodatkach do systemów ERP.

**JSON z generatorem źródeł.** Modele KRS korzystają z publicznych konwerterów z `PolishOpenData.Krs.Serialization` (`KrsDateJsonConverter`, `KrsTimestampJsonConverter`), więc na .NET 8 lub nowszym można dodać `KrsCurrentExtract` albo `KrsFullExtract` do własnego `JsonSerializerContext` (np. do przechowywania odpisów) bez rejestrowania konwerterów. Jeśli parsujesz surowe odpowiedzi API KRS samodzielnie, zamiast wywoływać `IKrsClient`, pamiętaj o dwóch rzeczach: odpis jest opakowany w obiekt `odpis`, więc deserializuj ten wewnętrzny obiekt; a nazwy pól rejestru (np. `dataRejestracjiWKRS`) pasują do właściwości tylko bez rozróżniania wielkości liter, więc użyj `JsonSerializerDefaults.Web` — atrybutu `[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]` na kontekście albo `new JsonSerializerOptions(JsonSerializerDefaults.Web)` z serializacją opartą na refleksji na .NET Framework. W przeciwnym razie właściwości pozostaną `null` bez żadnego błędu.

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

## Szybki start: GUS REGON

`PolishOpenData.Regon` to klient usługi BIR1.2 w GUS: wyszukiwanie po NIP, REGON lub numerze KRS, zapytania zbiorcze po 20 identyfikatorów oraz pełne raporty osób prawnych, przedsiębiorców jednoosobowych i jednostek lokalnych. Wymaga własnego klucza (bezpłatnego: napisz na `regon_bir@stat.gov.pl`); publiczne środowisko testowe GUS działa bez klucza, ale udostępnia starą, zanonimizowaną kopię rejestru.

```bash
dotnet add package PolishOpenData.Regon
```

```csharp
using Microsoft.Extensions.DependencyInjection;
using PolishOpenData;
using PolishOpenData.Bir;
using System.Linq;

var services = new ServiceCollection();
services.AddRegonClient(o => o.UserKey = Environment.GetEnvironmentVariable("MOJA_APLIKACJA_KLUCZ_REGON")); // klucz z własnej konfiguracji
// Publiczne środowisko testowe GUS nie wymaga klucza: services.AddRegonClient(o => o.Environment = RegonEnvironment.Test);
using var provider = services.BuildServiceProvider();
var regon = provider.GetRequiredService<IRegonClient>();

var found = await regon.FindByNipAsync(Nip.Parse("526-104-08-28"));
var entity = found.Value[0];
Console.WriteLine($"{entity.Name}, {entity.Address.City} (REGON {entity.Regon})");

var details = (await regon.GetDetailsAsync(entity, RegonInclude.Pkd)).Value[0];
Console.WriteLine($"{details.LegalFormName}; główne PKD {details.Pkd!.First(p => p.IsPrimary).Code}");
Console.WriteLine($"Źródło: GUS REGON ({found.Environment}), pobrano {found.RetrievedAt:u}");

await regon.LogoutAsync();
```

Domyślnie klient ogranicza się do najostrzejszego okna GUS (3 wywołania na sekundę, 120 na minutę, 6000 na godzinę; zapytanie zbiorcze do 20 identyfikatorów to jedno wywołanie dla limitu na sekundę, a limity na minutę i na godzinę liczą zapytania i identyfikatory: każdy identyfikator wyszukiwania liczy się jako jeden, tak samo jak każde inne zapytanie — logowanie, raport, `GetDataDateAsync`, wylogowanie) i utrzymuje jedną sesję logowania. Metoda zbiorcza sprawdza całą partię przed pierwszym zapytaniem i rzuca `QuotaExceededException` bez wysyłania czegokolwiek, gdy partia nie mieści się w tym, co zostało z okna minutowego lub godzinowego: przy domyślnym oknie jedno wywołanie obejmuje najwyżej 119 różnych identyfikatorów (120, gdy sesja jest już zalogowana, mniej, gdy inne wywołania wykorzystały ostatnią minutę). Nie jest potwierdzone, czy GUS wlicza każdy identyfikator zapytania zbiorczego do swojego limitu na sekundę; jeśli wysyłasz wiele zapytań zbiorczych, używaj mniejszych albo rozłóż je w czasie. Zarejestruj `AddRegonClient` raz na proces i współdziel klienta: GUS liczy zapytania na instancję aplikacji, a sesja działa tylko z adresu IP, który ją utworzył. Wywołaj `LogoutAsync`, gdy proces się kończy; klient nigdy nie robi tego sam. Klient utworzony bez DI dostaje własną, prywatną sesję i własny ogranicznik.

Ponawianie zapytań (retry) jest wyłączone także dla REGON, tak jak dla Białej Listy: ponowienie wykonane przez handler odporności to kolejne zapytanie do GUS, którego ogranicznik nie policzy, a błąd SOAP (Fault), który oznacza, że zapytanie było błędne, przychodzi jako HTTP 500 i też zostałby ponowiony. Jeśli dodajesz `AddStandardResilienceHandler`, wyłącz w nim ponowienia, jak w przykładzie z Białą Listą.

**Klucze i środowisko testowe.** Biblioteka działa w modelu „przynieś własny klucz”: nie zawiera produkcyjnego klucza, nigdy nie czyta zmiennych środowiskowych i nigdy nie zapisuje ani nie wypisuje Twojego klucza. Instrukcja techniczna GUS ogranicza sposób obsługi kluczy w oprogramowaniu rozpowszechnianym wśród innych użytkowników (rozdział 3.6.3), więc zanim zaczniesz rozpowszechniać aplikację korzystającą z tej biblioteki, sprawdź zasady GUS. `RegonEnvironment.Test` wybiera publiczną usługę testową GUS i jej opublikowany klucz testowy (skonfigurowany `UserKey` jest tam pomijany i nigdy nie trafia do usługi testowej, więc możesz zostawić własny klucz w konfiguracji i tylko przełączyć środowisko); każdy wynik podaje środowisko, z którego pochodzi (`RegonResult.Environment`), więc zanonimizowane dane testowe (głównie z 2014 r.) nie wyglądają na aktualne.

**Koszt.** Szczegóły kosztują zapytania: osoba prawna to logowanie, wyszukanie i jeden raport (3 zapytania), każda opcjonalna lista (`RegonInclude.Pkd`, `LocalUnits`, `Partners`) to kolejne zapytanie, a przedsiębiorca jednoosobowy potrzebuje raportu ogólnego oraz jednego raportu na rodzaj działalności. `RegonResult.DataAsOf` jest wypełnione dopiero po wywołaniu `GetDataDateAsync` (jedno zapytanie).

## Sprawdzanie Białej Listy offline

Ministerstwo Finansów publikuje Białą Listę codziennie jako plik płaski (skróty par NIP i rachunek). `PolishOpenData.BialaLista.Offline` pobiera plik na wybrany dzień, raz buduje lokalny indeks, a potem odpowiada na dowolną liczbę zapytań z dysku: bez limitu zapytań, a sprawdzane pary NIP/rachunek nie opuszczają Twojego komputera. Używaj go do dużych partii (przelewy zbiorcze, import do ERP); gdy potrzebujesz identyfikatora zapytania jako dowodu, użyj `PolishOpenData.BialaLista`.

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

await offline.PrepareAsync(); // opcjonalnie: pobiera i indeksuje dzisiejszy plik teraz, a nie przy pierwszym sprawdzeniu
var checks = await offline.CheckManyAsync(new[]
{
    (Nip.Parse("774-000-14-54"), Nrb.Parse("06 1600 1127 1843 9838 2000 0034")),
});
foreach (var c in checks)
    Console.WriteLine($"{c.Nip}: {c.Status} (plik z {c.FileDate:yyyy-MM-dd}, wygenerowany {c.GeneratedOn:yyyy-MM-dd})");
```

- **Trzy odpowiedzi.** `Active`, `Exempt` albo `NotFound`. `NotFound` oznacza „nie znaleziono w pliku” (sprawdzono zwykły skrót i każdą maskę rachunku wirtualnego banku danego rachunku), a nie dowód, że rachunku nie ma na Białej Liście; `MatchedVirtualAccountMask` mówi, że parę znaleziono przez maskę.
- **Pierwsze użycie daty jest wolne.** Pobiera się ok. 250 MB i buduje indeks. Trwa to kilka minut, a gdy serwer MF jest wolny, co zdarza się w godzinach pracy, znacznie dłużej. Pobrany plik jest usuwany po zbudowaniu indeksu. Wywołaj `PrepareAsync` z wyprzedzeniem, aby uniknąć czekania: MF publikuje plik na dzień D około 23:00 w dniu D−1, więc dobry moment to wieczór poprzedniego dnia. `PrepareAsync` przyjmuje `IProgress<VatWhitelistPreparationProgress>` i `CancellationToken`. Klient HTTP zarejestrowany przez `AddVatWhitelistOffline` nie ma limitu czasu; przerywaj tokenem.
- **Katalog danych i zajętość dysku.** Gotowe indeksy trafiają do `PolishOpenData/BialaListaOffline` w lokalnym folderze danych aplikacji użytkownika; inny folder ustawisz w `VatWhitelistOfflineOptions.DataDirectory`. Indeks zajmuje ok. 16 bajtów na wpis plus niewielkie tablice. `DatesToKeep` (domyślnie 5) to liczba ostatnio używanych dat zachowywanych na dysku.
- **Tylko jeden komputer.** Użyj folderu na lokalnym dysku; udział sieciowy nie jest obsługiwany. Procesy na tym samym komputerze, które współdzielą folder, współdzielą gotowe indeksy; gdy jeden proces przygotowuje datę, drugi, który potrzebuje tej samej daty, czeka do `LockTimeout` (domyślnie 15 minut), a potem rzuca `TimeoutException`.
- **Daty.** Od 30 dni wstecz do jutra (czas warszawski), domyślnie dziś. Dla daty zawsze zwracana jest odpowiedź z pliku z tej daty. Poza oknem, gdy plik nie został jeszcze opublikowany, nie da się go pobrać albo nie przejdzie weryfikacji, wywołanie rzuca `VatWhitelistDataUnavailableException` (zob. `Reason`), zamiast odpowiadać danymi z innego dnia.
- **Bez identyfikatora zapytania, bez dowodu.** Plik płaski nie daje identyfikatora zapytania. Gdy potrzebujesz dowodu dla urzędu skarbowego, sprawdź przez klienta REST (`PolishOpenData.BialaLista`) i zachowaj `RequestId`.
- **Czas sprawdzenia i czas danych.** `FileDate` i `GeneratedOn` opisują dane; czas samego sprawdzenia zapisz we własnym zakresie.

## Serwer MCP

Wymaga [.NET 10 SDK](https://dotnet.microsoft.com/download). Bez kluczy API.

```bash
claude mcp add --transport stdio --scope user polish-open-data -- dotnet dnx PolishOpenData.Mcp --yes
```

Konfiguracje dla VS Code, Claude Desktop i Cursor: zob. [README.md](https://github.com/JacekSmi/PolishOpenData/blob/main/README.md#mcp-server-for-ai-assistants).

Narzędzia: `lookup_company` (dane firmy po NIP/REGON/KRS), `check_vat_bank_account` (Biała Lista przed zapłatą faktury), `check_vat_bank_account_offline` (to samo z pliku płaskiego MF: bez limitu zapytań, do 50 par naraz, bez identyfikatora zapytania, a sprawdzane pary nie opuszczają komputera; pierwsze wywołanie dla danej daty pobiera ok. 250 MB, co trwa kilka minut, a przy wolnym serwerze MF znacznie dłużej; `not_found` oznacza „nie znaleziono w pliku”; zmienna `POLISHOPENDATA_OFFLINE_DIR` wskazuje lokalny folder danych, nie udział sieciowy), `get_krs_extract` (zarząd, kapitał, PKD), `validate_identifier` (walidacja numerów offline).

## Księgi wieczyste

`KwNumber` sprawdza offline numery w formacie `WA1M/00012345/1` na podstawie oficjalnej tabeli kodów (Dz.U. 2026 poz. 740) i podpowiada poprawki typowych literówek. To port logiki walidacji z [pyekw](https://github.com/mhajder/pyekw) autorstwa mhajder (MIT). Biblioteka **nie** łączy się z portalem EKW i celowo nie generuje numerów ksiąg — numer KW jest daną osobową (NSA, III OSK 6508/21).

## Odpowiedzialne korzystanie

- Podawaj źródło danych i czas pobrania. Zawiera je każdy wynik narzędzi MCP korzystających z rejestrów (`lookup_company`, `check_vat_bank_account`, `check_vat_bank_account_offline`, `get_krs_extract`) oraz `BialaListaResult` (w narzędziu offline `Source.RetrievedAt` to czas sprawdzenia, a `fileDate` i `generatedOn` to czas danych); `KrsResult` nie dodaje własnego czasu pobrania — użyj `ToSummary().ExtractedAt`, jeśli jest dostępny.
- Biała Lista pozwala na ok. 100 wyszukiwań i 5000 sprawdzeń dziennie z jednego adresu IP; przekroczenie blokuje IP do północy, także w wyszukiwarce ministerstwa.
- KRS maskuje osoby fizyczne w polach strukturalnych. Serwer MCP nigdy nie przekazuje asystentom AI wolnotekstowego pola prokury (`rodzajProkury`) ani surowych sekcji KRS, a z pól wolnotekstowych, które zwraca (sposób reprezentacji, udziały wspólników), usuwa numery przypominające PESEL (11 cyfr albo 6 + 5 cyfr rozdzielonych jedną spacją lub łącznikiem, o ile nie są częścią dłuższego ciągu cyfr). Biblioteki zwracają dane w postaci opublikowanej przez rejestr: `KrsCurrentExtract` zawiera `rodzajProkury`, a `ToSummary()` przekazuje sposób reprezentacji i udziały bez zmian — przed wyświetleniem lub zapisaniem oczyść je we własnym zakresie.
- Biała Lista z mocy ustawy publikuje imiona i nazwiska przedsiębiorców jednoosobowych, reprezentantów, prokurentów i wspólników, a dla części z nich numer PESEL. Biblioteka zwraca je tak, jak zostały opublikowane; serwer MCP zwraca tylko nazwę podatnika i jeden adres (dla jednoosobowej działalności — imię i nazwisko właściciela oraz, gdy nie podano adresu działalności, adres zamieszkania).
- GUS zapisuje adres IP, czas i treść każdego zapytania do REGON. Nie wyliczaj kolejnych identyfikatorów (GUS tego zabrania i odcina dostęp), nie miej kilku kluczy dla jednego podmiotu i mieść się w limitach; ogranicznik klienta domyślnie stosuje najostrzejsze okno (limit na sekundę liczy wywołania, więc zapytanie zbiorcze do 20 identyfikatorów to jedno wywołanie; limity na minutę i na godzinę liczą zapytania i identyfikatory). Nie jest potwierdzone, czy GUS wlicza każdy identyfikator zapytania zbiorczego do limitu na sekundę: przy wielu zapytaniach zbiorczych używaj mniejszych albo rozłóż je w czasie.
- Biblioteka REGON zwraca dane tak, jak zostały opublikowane. Firma przedsiębiorcy jednoosobowego jest z mocy prawa jego imieniem i nazwiskiem, a adres może być adresem zamieszkania; GUS nie publikuje numerów PESEL. Instrukcja mówi, że numery telefonu i faksu przedsiębiorców jednoosobowych nie są przekazywane osobom trzecim; czy tak jest dziś dla każdego pola, nie sprawdzono, a w grudniu 2026 r. zmienia się prawo, więc sprawdź, co zapisujesz (`RegonDetails.Fields` zawiera każde pole, które przysłał GUS). Nazwy mogą zawierać HTML lub fragmenty podobne do SQL (GUS przed tym ostrzega): traktuj je jako dane niezaufane.
- Podawaj źródło i czas danych z REGON: `RegonResult` zawiera środowisko (`Production` albo `Test`), czas pobrania oraz, po wywołaniu `GetDataDateAsync`, datę danych. GUS prosi tych, którzy wykorzystują jego dane ponownie, o wskazanie źródła i czasu oraz o informację, że dane zostały przetworzone.
- Bez scrapingu, bez masowego pobierania, bez łączenia działek z numerami ksiąg wieczystych.

## Możliwe kolejne kroki

Pomysły, nie zobowiązania: dane nieruchomości (działki ULDK, geokoder GUGiK, ceny transakcyjne). Rozważamy narzędzie REGON w serwerze MCP, ale tylko jeśli GUS potwierdzi zasady dotyczące kluczy użytkowników. Jeśli któryś z nich by Ci się przydał, [otwórz issue](https://github.com/JacekSmi/PolishOpenData/issues), aby dać znać.

## Współpraca i licencja

Licencja MIT. Komponenty zewnętrzne i ich licencje: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Zasady współpracy: [CONTRIBUTING.md](CONTRIBUTING.md).
