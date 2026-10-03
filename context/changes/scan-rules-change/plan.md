# Generowanie lock-ów .NET przed skanem (scan-rules-change) Implementation Plan

## Overview

Repozytoria w GitLabie praktycznie nie mają `packages.lock.json`, a Trivy nie czyta `<PackageReference>` z `.csproj`, więc dziś prawie każdy skan .NET kończy się `Incomplete`. Worker będzie przed uruchomieniem Trivy generował brakujące lock-i (`dotnet restore <csproj> --use-lock-file`) w checkoutcie, który i tak jest kasowany. Projekty, dla których się to nie uda, zostają na liście „nieskanowanych" i nadal dają `Incomplete` — nigdy fałszywe „czysto".

## Current State Analysis

- `LockFileDetector.FindMissing` ([LockFileDetector.cs:21](core/Scanning/LockFileDetector.cs:21)) zgłasza brak dla każdego katalogu z `*.csproj` bez `packages.lock.json` oraz z `package.json` bez `package-lock.json`/`yarn.lock`/`pnpm-lock.yaml`. Nie uwzględnia `packages.config`, który Trivy czyta.
- `TrivyScanner.ScanAsync` ([TrivyScanner.cs:148-153](core/Scanning/TrivyScanner.cs:148)): `missing.Count > 0 || report.TargetCount == 0` → `Incomplete`, inaczej `Completed`.
- Trivy dla .NET czyta: `packages.lock.json`, `packages.config`, `*Packages.props` (tylko wersje bezpośrednie), `*.deps.json`; **nie** czyta `.csproj` (https://trivy.dev/latest/docs/coverage/language/dotnet).
- `dotnet restore <projekt> --use-lock-file` zapisuje `packages.lock.json` obok projektu (flaga przekazuje `RestorePackagesWithLockFile=true`).
- Procesy potomne dostają tylko białą listę zmiennych środowiskowych (`TrivyScanner.InheritedEnvironment`, `CreateStartInfo`), bo środowisko workera niesie `Git__Token`. Uruchamianie procesów: `IProcessRunner` (`core/Processes/ProcessRunner.cs`) z limitem czasu i obcięciem wyjścia.
- `MissingLockFiles` jest zapisywane w `Scan` ([ScanJobRunner.cs:206-246](core/Scanning/ScanJobRunner.cs:206)), wystawione w API (`ScanContracts.cs:48`) i pokazane w UI (`web/app/routes/scan-details.tsx:108-135`).
- `context/foundation/infrastructure.md:102` zakłada, że serwer nie potrzebuje SDK .NET ani Node.

## Desired End State

Skan wersji dla repozytorium .NET bez `packages.lock.json` kończy się `Completed` (lub z podatnościami), jeśli `dotnet restore` powiódł się dla każdego projektu. Gdy restore któregoś projektu się nie powiedzie (feed niedostępny, błąd projektu, timeout), skan jest `Incomplete`, a UI wymienia ten projekt jako „nie udało się wygenerować pliku lock". npm bez lock-a i `TargetCount == 0` zachowują się jak dziś. Weryfikacja: testy jednostkowe + skan na prawdziwym repozytorium .NET bez lock-a.

### Key Discoveries:

- Brak lock-a nie jest „nadmiarowy" dla Trivy — bez niego .NET SDK-style daje zero targetów; zmiana polega na wytworzeniu lock-a, nie na zluzowaniu reguły.
- `MissingLockFiles` zachowuje kształt (lista ścieżek względnych), zmienia znaczenie: „lock nie istnieje i nie dało się go wygenerować". Brak zmiany migracji, kontraktu API i typów w UI.
- Nowy proces musi dostać to samo utwardzone środowisko co Trivy, plus profil użytkownika potrzebny NuGetowi (`NuGet.config`, cache pakietów).

## What We're NOT Doing

- Generowania lock-ów dla npm (`package.json` bez lock-a zostaje `Incomplete`, jak dziś).
- Izolacji restore (kontener / osobne konto); akceptujemy ryzyko wykonania MSBuild z wewnętrznego GitLaba, łagodzone utwardzeniem środowiska.
- Jawnego przekazywania poświadczeń do feedów NuGet z portalu; feedy konfiguruje admin w `NuGet.config` konta workera.
- Obsługi wielu `.csproj` w jednym katalogu (współdzielą jeden `packages.lock.json`, zapisze się ostatni).
- Zmiany schematu bazy, kontraktu API i `TargetCount == 0` → `Incomplete`.

## Implementation Approach

Nowa klasa `DotnetLockFileGenerator` wywoływana z `TrivyScanner` po bramce bazy podatności, tuż przed `trivy fs`. Detektor wskazuje projekty do restore; generator uruchamia restore dla każdego i nie przerywa skanu przy błędzie pojedynczego projektu. Po generowaniu istniejący `FindMissing` decyduje o `Completed`/`Incomplete`, więc nieudane projekty trafiają na listę automatycznie.

## Faza 1: Detektor rozpoznaje projekty do restore

### Overview

`LockFileDetector` przestaje fałszywie zgłaszać projekty `packages.config` i udostępnia listę `.csproj`, dla których można wygenerować lock.

### Changes Required:

#### 1. Reguła dla .NET i lista projektów

**File**: `core/Scanning/LockFileDetector.cs`

**Intent**: Katalog z `.csproj` i `packages.config` jest kompletny (Trivy go czyta). Detektor dodatkowo zwraca ścieżki `.csproj` bez lock-a (i bez `packages.config`), żeby generator wiedział, co restore'ować.

**Contract**: `FindMissing(string)` zachowuje sygnaturę i format wyniku (ścieżki `.../packages.lock.json`). Nowa metoda `FindDotnetProjectsWithoutLock(string checkoutDirectory)` zwraca względne ścieżki `.csproj` (forward slashes, posortowane ordinal), pomijając te same katalogi co `FindMissing` (`.git`, `node_modules`, `bin`, `obj`) i dowiązania.

### Success Criteria:

#### Automated Verification:

- Testy detektora przechodzą: `dotnet test --filter LockFileDetectorTests`
- Nowe testy: `.csproj` + `packages.config` → brak wpisu; `.csproj` bez lock-a → wpis w obu metodach; wiele projektów w zagnieżdżonych katalogach; katalogi pomijane

#### Manual Verification:

- Brak (zmiana czysto logiczna, pokryta testami)

**Implementation Note**: Po automatycznej weryfikacji pauza na potwierdzenie przed Fazą 2.

---

## Faza 2: Generator lock-ów

### Overview

Klasa uruchamiająca `dotnet restore --use-lock-file` dla wskazanych projektów w utwardzonym środowisku, z limitem czasu i opcjami konfiguracji.

### Changes Required:

#### 1. Opcje konfiguracji

**File**: `core/Scanning/ScanOptions.cs`

**Intent**: Ścieżka do `dotnet` i limit czasu restore, tak jak `TrivyExecutablePath` i `ScanTimeoutMinutes`.

**Contract**: `DotnetExecutablePath` (domyślnie `"dotnet"`, `[Required]`), `RestoreTimeoutMinutes` (`[Range(1, 120)]`, domyślnie 10; limit na jeden projekt).

#### 2. DotnetLockFileGenerator

**File**: `core/Scanning/DotnetLockFileGenerator.cs` (nowy), rejestracja w `core/Scanning/ScanServiceCollectionExtensions.cs`

**Intent**: Dla każdego `.csproj` zwróconego przez detektor wykonuje `dotnet restore <csproj> --use-lock-file --nologo` z `WorkingDirectory` w katalogu projektu. Błąd, timeout lub brak `dotnet` jednego projektu jest logowany (jedna linia, bez sekretów) i nie przerywa pozostałych; anulowanie przez token przerywa całość. Środowisko procesu startuje od zera: biała lista jak w `TrivyScanner.InheritedEnvironment` rozszerzona o zmienne profilu potrzebne NuGetowi (`USERPROFILE`, `APPDATA`, `LOCALAPPDATA`, `HOME`, `ProgramData`, `ProgramFiles`) oraz `DOTNET_CLI_TELEMETRY_OPTOUT=1`, `DOTNET_NOLOGO=1`, `DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`. `Git__Token` i inne sekrety nie trafiają do procesu.

**Contract**: `Task GenerateAsync(string checkoutDirectory, CancellationToken cancellationToken)`; zależności: `IOptions<ScanOptions>`, `IProcessRunner`, `LockFileDetector`, `ILogger`. Statyczne `CreateStartInfo(ScanOptions, string projectPath)` publiczne, by testy sprawdzały argumenty i środowisko (wzór: `TrivyScanner.CreateStartInfo`). Nie zwraca wyniku — o kompletności decyduje ponowne `FindMissing`.

### Success Criteria:

#### Automated Verification:

- Testy generatora przechodzą (fałszywy `IProcessRunner`): `dotnet test --filter DotnetLockFileGeneratorTests`
- Testy: argumenty restore; środowisko nie zawiera `Git__Token`; błąd jednego projektu nie blokuje następnych; timeout i `StartFailed` nie rzucają wyjątku; anulowanie propaguje
- Opcje walidują się: `dotnet test --filter ScanOptionsTests`

#### Manual Verification:

- Na maszynie dewelopera: restore małego projektu .NET w katalogu tymczasowym tworzy `packages.lock.json`

**Implementation Note**: Pauza na potwierdzenie przed Fazą 3.

---

## Faza 3: Integracja w TrivyScanner i komunikaty

### Overview

Generowanie lock-ów wchodzi do przepływu skanu, a UI poprawnie opisuje projekty, dla których się nie udało.

### Changes Required:

#### 1. Wywołanie generatora

**File**: `core/Scanning/TrivyScanner.cs`

**Intent**: Po bramce wieku bazy (po kroku 2, przed `trivy fs`) wywołać generator na `checkoutDirectory`. Dalszy przepływ i decyzja `Completed`/`Incomplete` (krok 4) bez zmian.

**Contract**: Konstruktor dostaje `DotnetLockFileGenerator`; aktualizacja komentarza kroku 4 oraz XML doc klasy (decyzja o Java DB zostaje, dopisać krok generowania). Doc `ScanOutcome.Completed`/`Incomplete`: „lock files … obecne lub wygenerowane".

#### 2. Treść w UI

**File**: `web/app/routes/scan-details.tsx`

**Intent**: Komunikat i nagłówek sekcji opisują „pliki lock, których nie udało się wygenerować lub których brakuje", zamiast samego „brakujące pliki lock (NuGet i npm)".

**Contract**: Typy i kontrakt API bez zmian (`missingLockFiles: string[]`).

### Success Criteria:

#### Automated Verification:

- Testy scanera przechodzą: `dotnet test --filter TrivyScannerTests` (konstruktor testowy dostaje generator z fałszywym runnerem; nowe testy: lock wygenerowany → `Completed`; restore nieudany → `Incomplete` z projektem na liście; generator wołany po bramce bazy, nie przy `Failed(DatabaseTooOld)`)
- Pełny zestaw: `dotnet test`
- Web: `cd web && npm run typecheck && npm run build`

#### Manual Verification:

- Skan prawdziwego repozytorium .NET bez lock-a (po ustawieniu `dotnet` i feedów na serwerze) kończy się `Completed` z listą podatności
- Repozytorium z projektem, którego restore się nie udaje (np. niedostępny feed), daje `Incomplete` z tym projektem w sekcji UI

**Implementation Note**: Pauza na potwierdzenie przed Fazą 4.

---

## Faza 4: Dokumentacja i wymagania serwera

### Overview

Dokumenty odzwierciedlają nowe zachowanie i nowe wymagania wobec serwera workera.

### Changes Required:

#### 1. Dokumenty

**File**: `AGENTS.md`, `context/foundation/infrastructure.md`, `context/foundation/roadmap.md`

**Intent**: `AGENTS.md` (reguły skanu): worker generuje `packages.lock.json` dla .NET przez `dotnet restore --use-lock-file`, npm bez lock-a pozostaje `Incomplete`, nowe opcje `Scan:DotnetExecutablePath`/`Scan:RestoreTimeoutMinutes`. `infrastructure.md`: worker wymaga SDK .NET i skonfigurowanych feedów NuGet w profilu konta usługi; poprawić zdanie z :102. `roadmap.md`: odnotować rozstrzygnięcie pytania o pliki blokady (S-02, :130).

**Contract**: Edycje prozy w istniejących sekcjach; bez nowych dokumentów.

### Success Criteria:

#### Automated Verification:

- Całość buduje się i przechodzi testy: `dotnet build && dotnet test`

#### Manual Verification:

- Admin serwera potwierdza, że SDK i feedy NuGet da się skonfigurować dla konta workera zgodnie z opisem

---

## Testing Strategy

### Unit Tests:

- `LockFileDetectorTests`: `packages.config`, lista projektów do restore, pomijane katalogi
- `DotnetLockFileGeneratorTests`: argumenty, środowisko bez sekretów, tolerancja na błąd jednego projektu, timeout, anulowanie
- `TrivyScannerTests`: kolejność kroków, `Completed` po wygenerowaniu, `Incomplete` po nieudanym restore
- `ScanOptionsTests`: nowe pola i walidacja

### Integration Tests:

- Istniejące `ScanJobRunnerTests`/`ScanEndpointsTests` bez zmian zachowania (kontrakt `MissingLockFiles` ten sam)

### Manual Testing Steps:

1. Skan repozytorium .NET bez lock-a → `Completed` z podatnościami
2. Skan z niedostępnym feedem → `Incomplete` z wymienionym projektem
3. Sprawdzić w logach workera, że `Git__Token` nie występuje w środowisku procesu restore

## Performance Considerations

Restore pobiera pakiety, więc skan wydłuża się o czas restore (limit `RestoreTimeoutMinutes` na projekt). Cache pakietów NuGet konta workera przyspiesza kolejne skany. Skan wielu projektów w jednym repozytorium restore'uje je kolejno.

## Migration Notes

Brak migracji bazy. Wymaga zmiany na serwerze: SDK .NET, dostęp do feedów NuGet w profilu konta usługi workera, ewentualnie `Scan:DotnetExecutablePath`.

## References

- Related research: `context/changes/scan-rules-change/research.md`
- Wzór utwardzonego procesu: `core/Scanning/TrivyScanner.cs` (`CreateStartInfo`, `InheritedEnvironment`)
- Detektor: `core/Scanning/LockFileDetector.cs:21-57`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Detektor rozpoznaje projekty do restore

#### Automated

- [ ] 1.1 Testy detektora przechodzą: `dotnet test --filter LockFileDetectorTests`
- [ ] 1.2 Nowe testy: `.csproj` + `packages.config` → brak wpisu; `.csproj` bez lock-a → wpis w obu metodach; wiele projektów w zagnieżdżonych katalogach; katalogi pomijane

### Phase 2: Generator lock-ów

#### Automated

- [ ] 2.1 Testy generatora przechodzą (fałszywy `IProcessRunner`): `dotnet test --filter DotnetLockFileGeneratorTests`
- [ ] 2.2 Testy: argumenty restore; środowisko nie zawiera `Git__Token`; błąd jednego projektu nie blokuje następnych; timeout i `StartFailed` nie rzucają wyjątku; anulowanie propaguje
- [ ] 2.3 Opcje walidują się: `dotnet test --filter ScanOptionsTests`

#### Manual

- [ ] 2.4 Na maszynie dewelopera: restore małego projektu .NET w katalogu tymczasowym tworzy `packages.lock.json`

### Phase 3: Integracja w TrivyScanner i komunikaty

#### Automated

- [ ] 3.1 Testy scanera przechodzą: `dotnet test --filter TrivyScannerTests` (nowe: lock wygenerowany → `Completed`; restore nieudany → `Incomplete` z projektem na liście; generator po bramce bazy)
- [ ] 3.2 Pełny zestaw: `dotnet test`
- [ ] 3.3 Web: `cd web && npm run typecheck && npm run build`

#### Manual

- [ ] 3.4 Skan prawdziwego repozytorium .NET bez lock-a kończy się `Completed` z listą podatności
- [ ] 3.5 Repozytorium z projektem, którego restore się nie udaje, daje `Incomplete` z tym projektem w sekcji UI

### Phase 4: Dokumentacja i wymagania serwera

#### Automated

- [ ] 4.1 Całość buduje się i przechodzi testy: `dotnet build && dotnet test`

#### Manual

- [ ] 4.2 Admin serwera potwierdza, że SDK i feedy NuGet da się skonfigurować dla konta workera zgodnie z opisem
