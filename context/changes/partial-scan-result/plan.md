# Powód nieprzeskanowania przy skanie Incomplete (partial-scan-result) Implementation Plan

## Overview

Skan `Incomplete` pokazuje dziś tylko ścieżki brakujących lock-ów, bez powodu (restore NuGet nieudany, npm bez locka, kilka projektów w katalogu), a ostatnia z tych sytuacji w ogóle nie trafia na listę. Plan zapisuje i pokazuje powód przy każdej nieprzeskanowanej pozycji: kod powodu i, przy nieudanym restore, oczyszczoną pierwszą linię komunikatu `dotnet`. Stan `Incomplete` i jego warunki zostają bez zmian. Katalogi z kilkoma `.csproj` nie dostają wygenerowanego locka, tylko trafiają na listę z powodem, co usuwa fałszywe „Completed" z ustalenia F2 przeglądu `scan-rules-change`.

## Current State Analysis

- `ScanOutcome.Incomplete` niesie `IReadOnlyList<string> MissingLockFiles` ([ScanOutcome.cs:22](core/Scanning/ScanOutcome.cs:22)); `TrivyScanner` buduje je z `LockFileDetector.FindMissing` ([TrivyScanner.cs:156-159](core/Scanning/TrivyScanner.cs:156)).
- `DotnetLockFileGenerator.GenerateAsync` loguje powód nieudanego restore i nic nie zwraca ([DotnetLockFileGenerator.cs](core/Scanning/DotnetLockFileGenerator.cs)).
- Katalog z kilkoma `.csproj`: każdy projekt jest restore'owany osobno i wszystkie zapisują ten sam `packages.lock.json`; potem `FindMissing` widzi lock i katalog nie trafia na listę (ustalenie F2, `context/archive/2026-10-03-scan-rules-change/reviews/impl-review.md`).
- Model: `Scan.MissingLockFiles` (`text[]`, [Scan.cs:75](core/Data/Scan.cs:75)), zapis w `ScanJobRunner.FinishAsync` ([ScanJobRunner.cs:218,246](core/Scanning/ScanJobRunner.cs:218)), kontrakt API `ScanDetails.MissingLockFiles` ([ScanContracts.cs:48](api/Scans/ScanContracts.cs:48)), UI [scan-details.tsx](web/app/routes/scan-details.tsx).
- Wzorzec dla danych strukturalnych: encja potomna `ScanFinding` (konfiguracja w `PortalDbContext`, kaskadowe usuwanie, enum jako tekst z `EnumTextMaxLength`; [PortalDbContext.cs:88-106](core/Data/PortalDbContext.cs:88)). Oczyszczanie komunikatów: `ScanJobRunner.Sanitize` (maska tokenu Git, pierwsza linia, limit długości; [ScanJobRunner.cs:270](core/Scanning/ScanJobRunner.cs:270)).
- Frame: problemem jest brak powodu, nie stan wyniku (`context/changes/partial-scan-result/frame.md`, Confidence HIGH). Wcześniejsza decyzja: „brak wyników" tylko dla `Completed` (`context/archive/2026-09-30-manual-version-scan/plan.md:51`).

## Desired End State

Strona skanu `Incomplete` wymienia każdą nieprzeskanowaną pozycję z powodem po polsku, np. „restore NuGet zakończył się błędem" z linią komunikatu, „brak pliku lock (npm)", „kilka projektów .csproj w jednym katalogu". Stare skany (sprzed zmiany) pokazują swoje ścieżki z powodem „nieznany". Katalogi z wieloma `.csproj` bez locka są wymienione jako nieprzeskanowane, a skan jest `Incomplete`. Weryfikacja: testy jednostkowe + skan prawdziwego repozytorium z nieudanym restore i z kilkoma `.csproj` w katalogu.

### Key Discoveries:

- Tabela potomna z enumem jako tekstem i kaskadą to ustalony wzorzec w repo (`ScanFinding`), więc nie wprowadzamy mapowania JSON.
- Ścieżka pozycji zachowuje dotychczasowy format (`src/App/packages.lock.json`), dzięki czemu stare wpisy da się skopiować bez zmiany.
- `Sanitize` działa w `ScanJobRunner` (zna token Git), więc oczyszczanie szczegółu odbywa się przy zapisie, nie w generatorze.

## What We're NOT Doing

- Zmiany stanu skanu na `Completed` dla wyniku częściowego (rama ją odrzuciła).
- Listy przeskanowanych celów (użytkownik nie wskazał tego jako braku; wymagałaby danych z Trivy dla celów bez podatności).
- Zmiany warunku `TargetCount == 0` ani jego komunikatu.
- Wykrywania `.fsproj`/`.vbproj` (istniejące ograniczenie detektora).
- Generowania lock-ów dla npm ani jawnych poświadczeń do feedów.

## Implementation Approach

Faza po fazie od środka na zewnątrz: najpierw detektor i generator produkują powody (z testami), potem `ScanOutcome` i `TrivyScanner` je niosą, potem zapis i API, na końcu UI i dokumentacja. Każda faza zostawia zielone testy; `ScanJobRunner` w fazie 2 tymczasowo mapuje nowe pozycje na stare pole `MissingLockFiles` (jedna linia), a faza 3 to usuwa.

## Faza 1: Powody po stronie detektora i generatora

### Overview

Detektor wyodrębnia katalogi z wieloma `.csproj`, a generator zwraca powód dla każdej ścieżki locka, której nie udało się wytworzyć.

### Changes Required:

#### 1. Typ pozycji i kody powodów

**File**: `core/Scanning/UnscannedItem.cs` (nowy)

**Intent**: Wspólny typ opisujący nieprzeskanowaną pozycję, używany przez generator, wynik skanu i zapis.

**Contract**: `enum UnscannedReason { NoLockFile, RestoreFailed, RestoreTimedOut, DotnetNotStarted, RestoreBudgetExceeded, MultipleProjects, Unknown }`; `record UnscannedItem(string Path, UnscannedReason Reason, string? Detail = null)`; stałe `PathMaxLength = 1024`, `DetailMaxLength = 300`. `Path` ma format ścieżki locka jak w `FindMissing` (np. `src/App/packages.lock.json`).

#### 2. Detektor: katalogi z wieloma projektami

**File**: `core/Scanning/LockFileDetector.cs`

**Intent**: `FindDotnetProjectsWithoutLock` zwraca tylko `.csproj` z katalogów z dokładnie jednym projektem (bez locka i bez `packages.config`); nowa metoda zwraca ścieżki locka katalogów z kilkoma `.csproj` bez locka i bez `packages.config`.

**Contract**: `FindDotnetProjectsWithoutLock(string)` bez zmiany sygnatury, zmienia zawartość wyniku; nowa `FindDirectoriesWithMultipleProjects(string checkoutDirectory)` zwraca posortowane ordinal ścieżki `.../packages.lock.json`. `FindMissing` bez zmian.

#### 3. Generator zwraca powody

**File**: `core/Scanning/DotnetLockFileGenerator.cs`

**Intent**: `GenerateAsync` zwraca listę `UnscannedItem` dla ścieżek locka, których nie wytworzył: nieudany restore (`RestoreFailed` z pierwszą linią komunikatu, stderr, a gdy pusty stdout), przekroczony czas (`RestoreTimedOut`), brak `dotnet` (`DotnetNotStarted`), projekty pominięte po wyczerpaniu łącznego budżetu (`RestoreBudgetExceeded`), katalogi z wieloma `.csproj` (`MultipleProjects`, bez uruchamiania `dotnet`). Udany restore nie daje pozycji. Kasowanie połowicznego locka i logowanie zostają.

**Contract**: `Task<IReadOnlyList<UnscannedItem>> GenerateAsync(string checkoutDirectory, CancellationToken)`; `Detail` obcięty do `UnscannedItem.DetailMaxLength`, bez sekretów (maska tokenu robi zapis, patrz faza 3).

### Success Criteria:

#### Automated Verification:

- Testy detektora przechodzą: `dotnet test api.Tests -c Release --filter LockFileDetectorTests`
- Testy generatora przechodzą: `dotnet test api.Tests -c Release --filter DotnetLockFileGeneratorTests`
- Nowe testy detektora: katalog z dwoma `.csproj` bez locka trafia do `FindDirectoriesWithMultipleProjects` i nie do `FindDotnetProjectsWithoutLock`; katalog z dwoma `.csproj` i `packages.config`/lockiem nie jest zgłaszany
- Nowe testy generatora: powód i szczegół dla kodu wyjścia ≠ 0, timeoutu, `StartFailed`, wyczerpanego budżetu; katalog z wieloma projektami daje `MultipleProjects` bez wywołania `dotnet`; udany restore nie daje pozycji

#### Manual Verification:

- Brak (zmiana logiczna pokryta testami)

**Implementation Note**: Po automatycznej weryfikacji pauza na potwierdzenie przed Fazą 2.

---

## Faza 2: Wynik skanu niesie powody

### Overview

`ScanOutcome.Incomplete` i `TrivyScanner` operują na pozycjach z powodem zamiast na samych ścieżkach.

### Changes Required:

#### 1. ScanOutcome

**File**: `core/Scanning/ScanOutcome.cs`

**Intent**: `Incomplete` niesie `IReadOnlyList<UnscannedItem> Unscanned` zamiast `IReadOnlyList<string> MissingLockFiles`; zaktualizować dokumentację typu.

**Contract**: `Incomplete(IReadOnlyList<ScanFinding> Findings, IReadOnlyList<UnscannedItem> Unscanned, string TrivyVersion, DateTimeOffset DbUpdatedAt)`.

#### 2. TrivyScanner

**File**: `core/Scanning/TrivyScanner.cs`

**Intent**: Wynik `GenerateAsync` przechowywany do kroku 4. Dla każdej ścieżki z `FindMissing` pozycją jest wpis z generatora o tej ścieżce, a gdy go nie ma, `NoLockFile` (npm). Warunek `Incomplete` (`missing.Count > 0 || TargetCount == 0`) bez zmian.

**Contract**: ścieżki z generatora, których `FindMissing` już nie zgłasza, są ignorowane; brak duplikatów po `Path`.

#### 3. Tymczasowe mapowanie w ScanJobRunner

**File**: `core/Scanning/ScanJobRunner.cs`

**Intent**: Do czasu fazy 3 zapisuje `incomplete.Unscanned.Select(u => u.Path)` w istniejącym polu `MissingLockFiles`, żeby build i testy zostały zielone.

**Contract**: Jedna linia w przypadku `Incomplete`; usuwana w fazie 3.

### Success Criteria:

#### Automated Verification:

- Testy scanera przechodzą: `dotnet test api.Tests -c Release --filter TrivyScannerTests`
- Nowe testy scanera: npm bez locka → `NoLockFile`; nieudany restore → `RestoreFailed` z szczegółem; katalog z wieloma `.csproj` → `MultipleProjects`; udany restore → `Completed` jak dotąd
- Pełny zestaw: `dotnet test api.Tests -c Release`

#### Manual Verification:

- Brak (zmiana logiczna pokryta testami)

**Implementation Note**: Pauza na potwierdzenie przed Fazą 3.

---

## Faza 3: Zapis i API

### Overview

Pozycje z powodem są zapisywane w nowej tabeli i zwracane przez API; kolumna `MissingLockFiles` znika po skopiowaniu danych.

### Changes Required:

#### 1. Encja i konfiguracja

**File**: `core/Data/ScanUnscannedItem.cs` (nowy), `core/Data/Scan.cs`, `core/Data/PortalDbContext.cs`

**Intent**: Encja potomna wzorowana na `ScanFinding`; `Scan` dostaje kolekcję `UnscannedItems`, traci `MissingLockFiles`.

**Contract**: `ScanUnscannedItem { Id, ScanId, Scan?, Path (max 1024), Reason (UnscannedReason, tekst z EnumTextMaxLength), Detail? (max 300) }`; FK do `Scans` z kaskadą; indeks unikalny `(ScanId, Path)`; `DbSet<ScanUnscannedItem>`.

#### 2. Migracja

**File**: `core/Data/Migrations/<timestamp>_AddScanUnscannedItems.cs` (generowana przez `dotnet ef migrations add ... --output-dir Data/Migrations`)

**Intent**: Tworzy tabelę, kopiuje stare wpisy z `Scans.MissingLockFiles` (po jednym wierszu na ścieżkę, powód `Unknown`, bez szczegółu), potem usuwa kolumnę. `Down` odtwarza kolumnę z wierszy tabeli i usuwa tabelę.

**Contract**: Kopiowanie przez SQL (`unnest`) w `Up` przed `DropColumn`; wynik migracji zgodny z `PortalDbContextModelSnapshot`.

#### 3. Zapis wyniku

**File**: `core/Scanning/ScanJobRunner.cs`

**Intent**: W przypadku `Incomplete` tworzy `ScanUnscannedItem` dla każdej pozycji, w tej samej transakcji co zmiana statusu i `Findings`; `Detail` przechodzi przez `Sanitize` i jest obcinany do 300 znaków, `Path` do 1024. Usuwa tymczasowe mapowanie z fazy 2.

**Contract**: Pozycje zapisywane obok `ScanFinding` (wspólny `SaveChangesAsync`); odrzucony skan (`updated == 0`) nie zostawia pozycji.

#### 4. API

**File**: `api/Scans/ScanContracts.cs`, `api/Scans/ScanEndpoints.cs`

**Intent**: `ScanDetails` zwraca `Unscanned` zamiast `MissingLockFiles`; zapytanie dołącza pozycje.

**Contract**: `record UnscannedItemResponse(string Path, string Reason, string? Detail)`; `ScanDetails.Unscanned : IReadOnlyList<UnscannedItemResponse>` posortowane po ścieżce; `Reason` jako nazwa enuma (jak `Status`).

### Success Criteria:

#### Automated Verification:

- Budowanie: `dotnet build api -c Release` i `dotnet build worker -c Release`
- Testy zapisu i API przechodzą: `dotnet test api.Tests -c Release --filter "ScanJobRunnerTests|ScanEndpointsTests|SchemaTests"`
- Nowe testy: zapis pozycji z `Detail` oczyszczonym z tokenu i obciętym; kaskadowe usuwanie pozycji ze skanem (`SchemaTests`); odpowiedź API z `Unscanned`
- Pełny zestaw: `dotnet test api.Tests -c Release`

#### Manual Verification:

- Migracja na lokalnej bazie z istniejącym skanem `Incomplete` przechodzi (`dotnet ef database update --project core --startup-project api`), a stare ścieżki są w nowej tabeli z powodem `Unknown`; `database update <poprzednia migracja>` też przechodzi

**Implementation Note**: Pauza na potwierdzenie przed Fazą 4.

---

## Faza 4: UI i dokumentacja

### Overview

Strona skanu pokazuje powód przy każdej nieprzeskanowanej pozycji; dokumentacja opisuje zmianę.

### Changes Required:

#### 1. Typy i etykiety

**File**: `web/app/lib/scan.ts`

**Intent**: Typ `UnscannedItem { path, reason, detail }` w `ScanDetails.unscanned` zamiast `missingLockFiles`; mapa kodów powodów na polskie etykiety.

**Contract**: Etykiety: `NoLockFile` „brak pliku lock (dla npm nie jest generowany)", `RestoreFailed` „restore NuGet zakończył się błędem", `RestoreTimedOut` „restore NuGet przekroczył limit czasu", `DotnetNotStarted` „nie udało się uruchomić dotnet", `RestoreBudgetExceeded` „wyczerpany łączny limit czasu restore", `MultipleProjects` „kilka projektów .csproj w jednym katalogu (wspólny plik lock)", `Unknown` „powód nieznany (skan sprzed zapisu powodów)"; nieznany kod wyświetlany jako surowa wartość.

#### 2. Widok skanu

**File**: `web/app/routes/scan-details.tsx`

**Intent**: Lista „Nieprzeskanowane" z powodem po polsku i opcjonalnym szczegółem (czcionka mono, zawijanie) przy każdej ścieżce; ogólnikowy komunikat o restore zastąpiony neutralnym, a wariant `Unscanned` puste + `Incomplete` (brak celów) bez zmian.

**Contract**: Używa istniejących komponentów (`Alert`, `SectionHeading`); bez nowych komponentów.

#### 3. Dokumentacja

**File**: `AGENTS.md`

**Intent**: Zdanie w regułach skanu: nieprzeskanowane pozycje mają powód zapisany w `ScanUnscannedItems`, katalogi z wieloma `.csproj` nie dostają generowanego locka, `Detail` jest oczyszczany tokenem Git.

**Contract**: Edycja prozy w istniejącej sekcji.

### Success Criteria:

#### Automated Verification:

- Typy i build: `cd web && npm run typecheck && npm run build`
- Całość: `dotnet build api -c Release && dotnet test api.Tests -c Release`

#### Manual Verification:

- Skan repozytorium z nieudanym restore (np. błędny feed) pokazuje przy projekcie powód „restore NuGet zakończył się błędem" i pierwszą linię komunikatu
- Skan z npm bez locka pokazuje „brak pliku lock", a z kilkoma `.csproj` w jednym katalogu „kilka projektów .csproj w jednym katalogu"
- Skan sprzed migracji pokazuje swoje ścieżki z powodem „nieznany"

---

## Testing Strategy

### Unit Tests:

- `LockFileDetectorTests`: katalogi z wieloma projektami, `packages.config`, lock obecny
- `DotnetLockFileGeneratorTests`: powody i szczegóły dla każdego wyniku procesu, budżet, wiele projektów bez uruchamiania `dotnet`
- `TrivyScannerTests`: łączenie `FindMissing` z powodami, `NoLockFile` dla npm
- `ScanJobRunnerTests`: zapis pozycji, oczyszczenie i obcięcie szczegółu, brak pozycji dla odrzuconego skanu
- `ScanEndpointsTests`: `Unscanned` w odpowiedzi
- `SchemaTests`: kaskada pozycji

### Integration Tests:

- Istniejące testy z prawdziwym Trivy/Git bez zmian zachowania (pomijane bez zmiennych środowiskowych)

### Manual Testing Steps:

1. Zastosować migrację na lokalnej bazie ze starym skanem `Incomplete` i sprawdzić kopię oraz `Down`
2. Zeskanować repozytorium z nieudanym restore, z npm bez locka i z kilkoma `.csproj` w katalogu
3. Otworzyć stary skan i sprawdzić powód „nieznany"

## Performance Considerations

Dodatkowe wiersze (kilka na skan) i jedno dodatkowe `Include` w szczegółach skanu; pomijalne.

## Migration Notes

Migracja przenosi dane i usuwa kolumnę `MissingLockFiles` (z `Down`). API zmienia kształt pola (`missingLockFiles` → `unscanned`), a UI jest serwowane razem z API, więc wdrażać razem. Kopia danych jest jednokierunkowo bezstratna dla ścieżek; powód dla starych skanów to `Unknown`.

## References

- Related research: `context/changes/partial-scan-result/research.md`
- Frame: `context/changes/partial-scan-result/frame.md`
- Wzorzec encji potomnej: `core/Data/ScanFinding.cs`, `core/Data/PortalDbContext.cs:88-106`
- Oczyszczanie komunikatów: `core/Scanning/ScanJobRunner.cs:270`
- Ustalenie F2: `context/archive/2026-10-03-scan-rules-change/reviews/impl-review.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Powody po stronie detektora i generatora

#### Automated

- [ ] 1.1 Testy detektora przechodzą: `dotnet test api.Tests -c Release --filter LockFileDetectorTests`
- [ ] 1.2 Testy generatora przechodzą: `dotnet test api.Tests -c Release --filter DotnetLockFileGeneratorTests`
- [ ] 1.3 Nowe testy detektora: katalog z dwoma `.csproj` bez locka trafia do `FindDirectoriesWithMultipleProjects` i nie do `FindDotnetProjectsWithoutLock`; katalog z dwoma `.csproj` i `packages.config`/lockiem nie jest zgłaszany
- [ ] 1.4 Nowe testy generatora: powód i szczegół dla kodu wyjścia ≠ 0, timeoutu, `StartFailed`, wyczerpanego budżetu; katalog z wieloma projektami daje `MultipleProjects` bez wywołania `dotnet`; udany restore nie daje pozycji

### Phase 2: Wynik skanu niesie powody

#### Automated

- [ ] 2.1 Testy scanera przechodzą: `dotnet test api.Tests -c Release --filter TrivyScannerTests`
- [ ] 2.2 Nowe testy scanera: npm bez locka → `NoLockFile`; nieudany restore → `RestoreFailed` z szczegółem; katalog z wieloma `.csproj` → `MultipleProjects`; udany restore → `Completed` jak dotąd
- [ ] 2.3 Pełny zestaw: `dotnet test api.Tests -c Release`

### Phase 3: Zapis i API

#### Automated

- [ ] 3.1 Budowanie: `dotnet build api -c Release` i `dotnet build worker -c Release`
- [ ] 3.2 Testy zapisu i API przechodzą: `dotnet test api.Tests -c Release --filter "ScanJobRunnerTests|ScanEndpointsTests|SchemaTests"`
- [ ] 3.3 Nowe testy: zapis pozycji z `Detail` oczyszczonym z tokenu i obciętym; kaskadowe usuwanie pozycji ze skanem (`SchemaTests`); odpowiedź API z `Unscanned`
- [ ] 3.4 Pełny zestaw: `dotnet test api.Tests -c Release`

#### Manual

- [ ] 3.5 Migracja na lokalnej bazie z istniejącym skanem `Incomplete` przechodzi (`dotnet ef database update --project core --startup-project api`), a stare ścieżki są w nowej tabeli z powodem `Unknown`; `database update <poprzednia migracja>` też przechodzi

### Phase 4: UI i dokumentacja

#### Automated

- [ ] 4.1 Typy i build: `cd web && npm run typecheck && npm run build`
- [ ] 4.2 Całość: `dotnet build api -c Release && dotnet test api.Tests -c Release`

#### Manual

- [ ] 4.3 Skan repozytorium z nieudanym restore (np. błędny feed) pokazuje przy projekcie powód „restore NuGet zakończył się błędem" i pierwszą linię komunikatu
- [ ] 4.4 Skan z npm bez locka pokazuje „brak pliku lock", a z kilkoma `.csproj` w jednym katalogu „kilka projektów .csproj w jednym katalogu"
- [ ] 4.5 Skan sprzed migracji pokazuje swoje ścieżki z powodem „nieznany"
