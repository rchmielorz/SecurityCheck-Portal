# Powód nieprzeskanowania przy skanie Incomplete — Plan Brief

> Full plan: `context/changes/partial-scan-result/plan.md`
> Frame brief: `context/changes/partial-scan-result/frame.md`
> Research: `context/changes/partial-scan-result/research.md`

## What & Why

Skan `Incomplete` wymienia dziś tylko ścieżki brakujących lock-ów. Nie mówi, dlaczego pozycja nie została przeskanowana (restore NuGet nieudany, npm bez locka), a katalog z kilkoma `.csproj` w ogóle nie trafia na listę. Plan zapisuje i pokazuje powód przy każdej takiej pozycji i dopisuje do listy projekty z kilkoma `.csproj`. Rama ustaliła, że problemem nie jest stan wyniku (`Incomplete` jest uczciwy), tylko brak powodu.

## Starting Point

`ScanOutcome.Incomplete` niesie listę ścieżek (`MissingLockFiles`), zapisaną w kolumnie `text[]` skanu i wystawioną w API. `DotnetLockFileGenerator` loguje powód nieudanego restore tylko w workerze. Dla encji potomnych istnieje wzorzec `ScanFinding`, a komunikaty błędów przechodzą przez `Sanitize`.

## Desired End State

Strona skanu `Incomplete` pokazuje każdą nieprzeskanowaną pozycję z powodem po polsku, przy nieudanym restore z oczyszczoną pierwszą linią komunikatu. Katalogi z wieloma `.csproj` bez locka są wymienione jako nieprzeskanowane. Stare skany pokazują swoje ścieżki z powodem „nieznany".

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Stan skanu częściowego | `Incomplete` bez zmian | `Completed` przywróciłby fałszywe „Brak podatności"; użytkownik uznał `Incomplete` za uczciwy | Frame |
| Czego brakowało | Powodu nieprzeskanowania | Wskazane przez użytkownika; lista przeskanowanych nie jest potrzebna | Frame |
| Zakres pozycji | Restore nieudany, npm bez locka, kilka `.csproj` w katalogu | Trzy sytuacje wybrane przez użytkownika | Frame |
| Kształt danych | Tabela potomna `ScanUnscannedItem` | Ten sam wzorzec co `ScanFinding`, bez mapowania JSON | Plan |
| Poziom szczegółu | Kod powodu + oczyszczona pierwsza linia | Odpowiada na „dlaczego" (np. 401 dla feedu) bez zaglądania do logów | Plan |
| Kilka `.csproj` w katalogu | Nie generować, pokazać jako nieprzeskanowane | Usuwa fałszywe „Completed" z F2, zgodnie z „nigdy czysto bez dowodu" | Plan |
| Stare skany | Migracja kopiuje ścieżki z powodem `Unknown`, usuwa kolumnę | Brak utraty danych, jedno źródło prawdy | Plan |
| Kolejność faz | Od detektora do UI, z tymczasowym mapowaniem w fazie 2 | Każda faza zostawia build i testy zielone | Plan |

## Scope

**In scope:** typ `UnscannedItem` i kody powodów, detektor i generator zwracające powody, `ScanOutcome`/`TrivyScanner`, tabela, migracja, zapis z oczyszczaniem, kontrakt API, UI, testy, `AGENTS.md`.

**Out of scope:** stan `Completed` dla wyniku częściowego, lista przeskanowanych celów, zmiana `TargetCount == 0`, `.fsproj`/`.vbproj`, lock-i dla npm.

## Architecture / Approach

Generator zwraca `UnscannedItem` (ścieżka locka, kod powodu, szczegół) dla projektów, których nie udało się przeskanować, a `TrivyScanner` łączy je z listą `FindMissing` (domyślnie `NoLockFile`). `ScanJobRunner` zapisuje pozycje w tej samej transakcji co wynik, oczyszczając szczegół tokenem Git. API zwraca `unscanned`, a UI mapuje kody na polskie etykiety.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Detektor i generator | Powody dla każdej niewygenerowanej ścieżki, pomijanie katalogów z wieloma projektami | Zmiana zawartości `FindDotnetProjectsWithoutLock` łamie istniejące testy |
| 2. Wynik skanu | `Incomplete` niesie `Unscanned`, `TrivyScanner` łączy powody | Tymczasowe mapowanie w `ScanJobRunner` trzeba usunąć w fazie 3 |
| 3. Zapis i API | Tabela, migracja z kopią danych, oczyszczanie, kontrakt API | Migracja usuwająca kolumnę (potrzebny sprawdzony `Down`) |
| 4. UI i dokumentacja | Lista z powodami po polsku, `AGENTS.md` | UI i API zmieniają kształt razem, więc wdrożenie razem |

**Prerequisites:** brak; zmiana `scan-rules-change` jest zarchiwizowana, generator lock-ów istnieje.
**Estimated effort:** ~3 sesje, 4 fazy.

## Open Risks & Assumptions

- Komunikat restore może zawierać nazwę wewnętrznego hosta lub pakietu; widzą go tylko zalogowani użytkownicy (portal nie ma stron publicznych), token Git jest maskowany.
- Migracja jest jednokierunkowo bezstratna dla ścieżek; powód dla starych skanów pozostaje nieznany.
- Zmiana kształtu pola API wymaga wdrożenia API i UI razem (UI jest serwowane przez API).

## Success Criteria (Summary)

- Skan z nieudanym restore pokazuje przy projekcie powód i pierwszą linię komunikatu.
- Katalog z kilkoma `.csproj` jest wymieniony jako nieprzeskanowany, a skan jest `Incomplete`.
- Stare skany nadal się wyświetlają, a migracja ma sprawdzony `Down`.
