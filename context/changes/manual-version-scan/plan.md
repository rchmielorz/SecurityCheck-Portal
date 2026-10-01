# Ręczny skan wersji i lista podatności (S-02) Implementation Plan

## Overview

Użytkownik klika „Skanuj" przy wzorcu wersji (np. `2.1.*`) i po kilku minutach widzi podatności (biblioteka, CVE, istotność) posortowane od critical do low. Widzi też jawne „brak wyników", gdy skan jest kompletny i czysty, ostrzeżenie „niepełny", gdy brakuje plików blokady, albo jawny błąd. API tylko zapisuje żądanie w tabeli `Scans`. Nowy proces `worker` (usługa Windows) je podejmuje, ponownie rozwiązuje wzorzec, klonuje dokładny commit, uruchamia Trivy i zapisuje wynik. To gwiazda przewodnia M-1 (roadmapa S-02, US-01, FR-004, FR-006).

## Current State Analysis

- **Rozwiązywanie wzorca istnieje.** `PatternResolutionService` jest bezstanowy i można go uruchomić ponownie w chwili skanu ([research.md](research.md), follow-up 1; `core/Git/PatternResolutionService.cs`). Zapis wyniku do wzorca (`ResolveAsync`, `api/Repositories/RepositoryEndpoints.cs:396-414`) zostaje w API bez zmian: worker nie zapisuje `LastResolved*`, bo `VersionPattern` nie ma tokenu współbieżności, a skan trzyma własny tag i commit.
- **Jest jeden utwardzony launcher procesów**: `core/Git/GitCliTagSource.cs` (Process, czyszczenie zmiennych `GIT_*`, PAT w env zawężony do URL, zamknięty stdin, `Kill(entireProcessTree: true)`, timeout łączony z tokenem wywołującego). Testy sprawdzają `ProcessStartInfo` ze statycznego `CreateStartInfo` (`api.Tests/Git/GitCliTagSourceTests.cs:25-78`).
- **Brak** hosta w tle, projektu workera, pliku `.sln`, CI, kodu klonowania i jakiegokolwiek modelu skanu. Jedna migracja EF (`core/Data/Migrations/20260927222900_InitialRepositories.cs`).
- **API**: minimalne endpointy z fallbackiem autoryzacji, tożsamość z claimu `sub` (`GetActor`, `RepositoryEndpoints.cs:356-360`), błędy jako gołe kody statusu, wzorzec zapisu `TrySaveAsync` / `InsertWithEventAsync` (`:421-460`).
- **UI**: `web/app/routes/repo-details.tsx` używa `clientLoader` / `clientAction` z `intent`, `Badge` ma wszystkie poziomy istotności i tokeny, brak odpytywania i brak runnera testów UI.

## Desired End State

- Na stronie `/repos/:repoId` każdy aktywny wzorzec ma przycisk „Skanuj" i skrót ostatniego skanu (status, kiedy). Kliknięcie przenosi na `/repos/:repoId/scans/:scanId`, gdzie widać status, przeskanowany tag i commit, wiek bazy Trivy oraz listę podatności.
- Skan kończy się jednym z trzech stanów: **completed** (kompletny; zero wyników to jawne „brak wyników"), **incomplete** (wyniki pokazane z ostrzeżeniem i listą brakujących plików blokady, nigdy „brak wyników"), **failed** (przyczyna jawna, bez wyników).
- Weryfikacja: `dotnet test api.Tests` zielone, `dotnet build api`, `dotnet build worker`, `cd web && npm run typecheck`; lokalnie z prawdziwym Trivy skan repozytorium z lock file zwraca podatności, a bez lock file kończy się `incomplete`.

### Key Discoveries:

- Tabele wyników muszą być w EF Core i w jednym zestawie migracji (`AGENTS.md`, migracje ręcznie `dotnet ef`); `AuditEvent` jest wzorcem „bez FK, ze snapshotem" (`core/Data/AuditEvent.cs`).
- Trivy widzi zależności tylko z plików blokady; brak targetów w JSON znaczy „brak pliku blokady", nie „czysto" (`infrastructure.md:117`, `:130-132`, `:159`).
- Trivy jest celem ataku na łańcuch dostaw (v0.69.4); jego proces nie może dziedziczyć PAT-a z workera (`infrastructure.md:44`, `:160`).
- IIS recykluje pule i zabija skany, więc skan musi działać poza API (`infrastructure.md` Pre-Mortem, `:186`).
- Ścieżki UI bez kropki w ostatnim segmencie (`AGENTS.md` Hard Rules); `/repos/:repoId/scans/:scanId` używa samych identyfikatorów.

## What We're NOT Doing

- Biblioteki kolejki (Hangfire, TickerQ, Quartz) i CliWrap. Kolejką jest tabela `Scans`, a procesy idą przez wspólny runner. Wybór biblioteki wraca przy S-05.
- Historia skanów w UI i trend (S-03); skany są zapisywane, ale UI pokazuje ostatni skan wzorca i stronę pojedynczego skanu.
- Akceptacja ryzyka i filtrowanie (S-04), skany cykliczne (S-05), dashboard wielu klientów.
- Zależności dev (`--include-dev-deps`), submoduły git, skanowanie obrazów, drugi skaner (OSV-Scanner), analiza osiągalności.
- Automatyczne ponowienia, automatyczne wznowienie skanu po restarcie (przerwany skan = failed).
- Odpytywanie przez SSE lub WebSocket (wystarczy okresowe odświeżanie w UI).
- Wdrożenie i usługa Windows na serwerze, weryfikacja cosign i przypięcie Trivy w CI (F-02, kroki administracyjne z `infrastructure.md`); plan dodaje tylko opcje i sprawdzenie wersji.
- Job Object na Windows (zostaje `Kill(entireProcessTree: true)` jak w S-01); ryzyko opisane w „Open Risks".
- Retencja i czyszczenie starych skanów.

## Implementation Approach

Logika leży w `core/` (runner, checkout, Trivy, pipeline skanu), a `worker/` jest cienkim hostem z pętlą claimowania. Dzięki temu testy w `api.Tests` pokrywają pipeline z atrapami i prawdziwą bazą (Testcontainers), a worker i API dzielą model i opcje. Kolejność faz idzie od najniższej warstwy: runner, dane, Trivy, worker, API, UI. Każda faza jest samodzielnie weryfikowalna.

Decyzje z wywiadu (szczegóły w [plan-brief.md](plan-brief.md)): wspólny runner na `Process`; worker + tabela jako kolejka; ponowne rozwiązanie wzorca na starcie skanu; stan `incomplete` z wynikami; aktualizacja bazy Trivy z limitem wieku; scalanie duplikatów i `UNKNOWN` na końcu; jeden aktywny skan na wzorzec, przerwany = failed.

## Critical Implementation Details

- **State sequencing.** Przejścia statusu: `Queued → Running → {Completed | Incomplete | Failed}`; `Running → Failed(Interrupted)` tylko przy starcie workera. Częściowy unikalny indeks (jeden `Queued`/`Running` na `PatternId`) jest jedyną ochroną przed zdublowanym skanem, więc API musi traktować jego naruszenie jako wyścig (zwrócić istniejący skan), a nie błąd 500.
- **Izolacja Trivy.** Proces Trivy dostaje środowisko z białej listy (PATH, SystemRoot, TEMP/TMP, zmienne proxy, katalog cache), nigdy kopię środowiska workera, bo ta zawiera PAT, gdy konfiguracja idzie przez zmienne środowiskowe.
- **Nigdy „brak wyników" bez dowodu.** UI pokazuje „brak wyników" tylko dla `Completed` z zerem podatności. `Incomplete` z zerem podatności pokazuje ostrzeżenie, że braku podatności nie można potwierdzić.

## Faza 1: Wspólny runner procesów i checkout

### Overview

Wyciągnąć z `GitCliTagSource` utwardzone uruchamianie procesu do wspólnego komponentu w `core/` i na nim zbudować klonowanie na konkretny commit z weryfikacją SHA. Zachowanie `ls-remote` ma się nie zmienić.

### Changes Required:

#### 1. Runner procesów

**File**: `core/Processes/IProcessRunner.cs`, `core/Processes/ProcessRunner.cs` (nowe)

**Intent**: Jedno miejsce, które uruchamia gotowy `ProcessStartInfo` z limitem czasu, anulowaniem, zamkniętym stdin, równoległym odczytem stdout i stderr oraz zabijaniem całego drzewa procesów. Logika przeniesiona z `GitCliTagSource.ListTagsAsync` i `KillTree`.

**Contract**: `RunAsync(ProcessStartInfo, TimeSpan timeout, int maxCapturedChars, CancellationToken)` zwraca `ProcessRunResult` z wynikiem `Exited(ExitCode)` / `TimedOut` / `StartFailed` oraz przyciętymi `Stdout` i `Stderr`. Anulowanie tokenem wywołującego rzuca `OperationCanceledException` (jak dziś), timeout nie. Rejestracja w DI jako singleton.

#### 2. Fabryka utwardzonego git

**File**: `core/Git/GitProcessFactory.cs` (nowe), `core/Git/GitCliTagSource.cs`

**Intent**: Przenieść zawartość `CreateStartInfo` (konfiguracja `-c`, czyszczenie `GIT_*`, `GIT_CONFIG_*`, nagłówek PAT zawężony do URL) do fabryki parametryzowanej argumentami podkomendy. `GitCliTagSource` używa fabryki i runnera; publiczne `CreateStartInfo` zostaje jako cienki wrapper, żeby istniejące testy działały bez zmian.

**Contract**: fabryka przyjmuje `GitOptions`, kanoniczny URL i argumenty (`ls-remote`, `clone`, `rev-parse`) i zwraca `ProcessStartInfo`. Dla klonowania dodatkowo `core.longpaths=true`; `--end-of-options` przed URL. Krótkie limity `ls-remote` (`Git:TimeoutSeconds` = 30 s, `http.lowSpeedLimit=1000` i `http.lowSpeedTime=20`) nie dotyczą klonu: klon dostaje osobny limit `Scan:CloneTimeoutMinutes` i łagodniejszy `lowSpeedTime` (rzędu minut), bo pakowanie po stronie serwera nie wysyła danych.

#### 3. Checkout na commit

**File**: `core/Git/IGitCheckout.cs`, `core/Git/GitCliCheckout.cs` (nowe)

**Intent**: Sklonować płytko tag rozwiązany przez wzorzec do katalogu roboczego i sprawdzić, że `HEAD` równa się commitowi z rozwiązania. Rozbieżność (tag przesunięty) to osobny wynik, nie skan innego kodu.

**Contract**: `CheckoutAsync(canonicalUrl, tag, expectedCommit, targetDirectory, ct)` zwraca `Success` / `CommitMismatch` / `Failure(GitErrorKind)`. Klon: `clone --depth 1 --branch <tag>` bez submodułów; weryfikacja przez `rev-parse HEAD`. Sprzątanie katalogu robi wywołujący (faza 4).

### Success Criteria:

#### Automated Verification:

- Budowanie przechodzi: `dotnet build api`
- Istniejące testy Git przechodzą bez zmian: `dotnet test api.Tests --filter "FullyQualifiedName~Git"`
- Testy runnera przechodzą (kod wyjścia, kod niezerowy, nieistniejący plik, timeout zabija drzewo procesów, ucięcie wyjścia): `dotnet test api.Tests --filter "FullyQualifiedName~ProcessRunner"`
- Testy checkoutu z atrapą runnera przechodzą (argumenty, token tylko w env, brak krótkiego limitu transferu w klonie, `CommitMismatch`, błąd klonu): `dotnet test api.Tests --filter "FullyQualifiedName~GitCheckout"`

#### Manual Verification:

- Test integracyjny pomijalny (zmienna `SCAN_IT_REPO_URL` + tag) klonuje prawdziwe repozytorium z wewnętrznego GitLaba i `HEAD` równa się commitowi z rozwiązania

**Implementation Note**: Po tej fazie i zielonych testach zatrzymaj się na ręczne potwierdzenie przed fazą 2.

---

## Faza 2: Model danych i migracja

### Overview

Dodać tabele `Scans` i `ScanFindings`, statusy, częściowy unikalny indeks aktywnego skanu i migrację.

### Changes Required:

#### 1. Encje

**File**: `core/Data/Scan.cs`, `core/Data/ScanFinding.cs` (nowe)

**Intent**: Skan jako samodzielny rekord ze snapshotem (jak `AuditEvent`), żeby usunięcie wzorca nie kasowało historii skanów.

**Contract**: `Scan`: `Id`; `PatternId`, `RepositoryId` (zwykłe kolumny, bez FK); snapshoty `RepositoryUrl`, `Pattern`; `Status` (`Queued/Running/Completed/Incomplete/Failed`); `FailureReason` (`PatternNotResolved/GitFailed/CommitMismatch/ScannerUnavailable/DatabaseTooOld/Timeout/ScannerFailed/Interrupted`, nullable) i krótki `FailureDetail`; `RequestedBy`, `RequestedAt`, `StartedAt`, `FinishedAt`; `ScannedTag`, `ScannedCommit` (40 znaków), `TrivyVersion`, `TrivyDbUpdatedAt`; `MissingLockFiles` (`string[]`). `ScanFinding`: `Id`, `ScanId` (FK, cascade), `Library`, `InstalledVersion`, `VulnerabilityId`, `Severity` (`Critical/High/Medium/Low/Unknown`), `FixedVersion?`, `Title?`, `Targets` (`string[]`). Enumy jako tekst o długości `EnumTextMaxLength`, stałe długości jako `public const` na encji (konwencja repo).

#### 2. Konfiguracja EF i audyt

**File**: `core/Data/PortalDbContext.cs`, `core/Data/AuditEvent.cs`

**Intent**: Zmapować encje i dodać wartość `ScanRequested` do `AuditAction` (kolumna jest tekstowa, więc bez zmiany schematu audytu).

**Contract**: częściowy unikalny indeks na `Scans(PatternId)` z filtrem `Status IN ('Queued','Running')`; indeks `Scans(PatternId, RequestedAt)`; unikalny `ScanFindings(ScanId, Library, InstalledVersion, VulnerabilityId)`.

#### 3. Migracja

**File**: `core/Data/Migrations/*_AddScans.cs` (+ Designer, snapshot)

**Intent**: Wygenerować komendą z `AGENTS.md`; migracja wyłącznie addytywna (wymóg `infrastructure.md:162`).

**Contract**: `dotnet ef migrations add AddScans --project core --startup-project api --output-dir Data/Migrations`.

### Success Criteria:

#### Automated Verification:

- Migracja nie zostawia niezatwierdzonych zmian modelu: `dotnet ef migrations has-pending-model-changes --project core --startup-project api`
- Testy schematu przechodzą (drugi aktywny skan tego samego wzorca odrzucony, po zakończeniu pierwszego dozwolony, unikalność wyników, kaskada usuwania wyników, skan przeżywa usunięcie wzorca): `dotnet test api.Tests --filter "FullyQualifiedName~SchemaTests"`
- Budowanie przechodzi: `dotnet build api`

#### Manual Verification:

- `dotnet ef database update --project core --startup-project api` na lokalnej bazie tworzy tabele `Scans` i `ScanFindings`

---

## Faza 3: Trivy — uruchomienie, baza, parser

### Overview

Komponent, który dla katalogu z checkoutem zwraca jawny wynik skanu: lista podatności po scaleniu, brakujące pliki blokady, wersja Trivy i wiek bazy albo przyczyna niepowodzenia.

### Changes Required:

#### 1. Opcje skanu

**File**: `core/Scanning/ScanOptions.cs`, `core/Scanning/ScanServiceCollectionExtensions.cs` (nowe)

**Intent**: Sekcja `Scan` z walidacją na starcie, tak jak `GitOptions`.

**Contract**: `TrivyExecutablePath` (domyślnie `trivy`), `CacheDirectory` i `WorkRoot` (wymagane), `ScanTimeoutMinutes` (1-120, domyślnie 15), `CloneTimeoutMinutes` (domyślnie 10), `DbUpdateTimeoutMinutes` (domyślnie 5), `MaxDbAgeDays` (1-90, domyślnie 7), `DbRepository` (opcjonalne, mirror), `ExpectedTrivyVersion` (opcjonalne), `PollIntervalSeconds` (domyślnie 5).

#### 2. Wykrywanie plików blokady

**File**: `core/Scanning/LockFileDetector.cs` (nowe)

**Intent**: Przejść po checkoucie (pomijając `.git`, `node_modules`, `bin`, `obj`) i wskazać katalogi z `*.csproj` bez `packages.lock.json` oraz z `package.json` bez żadnego z `package-lock.json` / `yarn.lock` / `pnpm-lock.yaml`.

**Contract**: zwraca listę ścieżek względnych brakujących plików blokady; reguła jest zachowawcza (może zgłosić `incomplete` częściej niż trzeba, nigdy rzadziej).

#### 3. Skaner Trivy

**File**: `core/Scanning/TrivyScanner.cs`, `core/Scanning/TrivyReportParser.cs` (nowe)

**Intent**: (a) odczytać wersję i wiek bazy (`trivy version --format json`), (b) próbować zaktualizować bazę, a przy porażce użyć cache tylko poniżej `MaxDbAgeDays`, (c) uruchomić `trivy fs --scanners vuln --format json --output <plik>` z `--cache-dir` i `--skip-db-update`, (d) sparsować raport. Env z białej listy, bez PAT-a. Odrzucić wersję `0.69.4` i wersję inną niż `ExpectedTrivyVersion`, jeśli ustawiona.

**Contract**: wynik `ScanOutcome`: `Completed(findings, trivyVersion, dbUpdatedAt)` / `Incomplete(findings, missingLockFiles, …)` / `Failed(reason, detail)`. Parser czyta `Results[].Target` i `Results[].Vulnerabilities[]` (`VulnerabilityID`, `PkgName`, `InstalledVersion`, `FixedVersion`, `Severity`, `Title`); scala wiersze o tej samej parze (biblioteka, wersja, CVE) w jeden z listą plików. Brak targetów lub niezerowy kod lub niepoprawny JSON: `Incomplete` (brak targetów) albo `Failed(ScannerFailed)`. Flagi Trivy potwierdzić względem przypiętej wersji przy implementacji (nie weryfikowane w badaniach). Przy plikach `.jar` w checkoucie Trivy może wymagać osobnej bazy Java z innego rejestru (niezweryfikowane); sprawdzić `--skip-java-db-update` i `--java-db-repository` i zapisać decyzję w kodzie i w `AGENTS.md`, żeby blokada proxy nie kończyła skanu błędem.

### Success Criteria:

#### Automated Verification:

- Testy kontraktowe parsera na przykładowych JSON przechodzą (z podatnościami, czysty, brak targetów, `UNKNOWN`, duplikat w dwóch plikach, niepoprawny JSON): `dotnet test api.Tests --filter "FullyQualifiedName~TrivyReportParser"`
- Testy detektora plików blokady przechodzą: `dotnet test api.Tests --filter "FullyQualifiedName~LockFileDetector"`
- Testy skanera z atrapą runnera przechodzą (baza zbyt stara, aktualizacja nieudana z cache w limicie, timeout, zakazana wersja, środowisko bez PAT-a): `dotnet test api.Tests --filter "FullyQualifiedName~TrivyScanner"`
- Opcje `Scan` walidują się na starcie (brak `CacheDirectory` zatrzymuje aplikację): `dotnet test api.Tests --filter "FullyQualifiedName~ScanOptions"`

#### Manual Verification:

- Test integracyjny pomijalny (`TRIVY_PATH`) uruchomiony z prawdziwym Trivy na repozytorium z `packages.lock.json` zwraca oczekiwane podatności, a bez pliku blokady zwraca `Incomplete`

---

## Faza 4: Worker i pipeline skanu

### Overview

Pipeline skanu w `core/` i cienki host `worker/` z pętlą claimowania, odzyskiwaniem po restarcie i `AddWindowsService`.

### Changes Required:

#### 1. Pipeline

**File**: `core/Scanning/ScanJobRunner.cs` (nowe)

**Intent**: Dla zaclaimowanego skanu: załadować wzorzec, rozwiązać go ponownie przez `PatternResolutionService` (tag i commit zapisane w skanie, **bez zapisu do wzorca**: `LastResolved*` należy do endpointu `resolve`, a `VersionPattern` nie ma tokenu współbieżności), zrobić checkout do `WorkRoot/<scanId>`, sprawdzić lock files, uruchomić Trivy, zapisać wyniki i status, zawsze usunąć katalog. Każda porażka mapuje się na jawny `FailureReason`, a `FailureDetail` to jedna przycięta linia bez sekretów (wartość PAT usuwana z tekstu).

**Contract**: `RunAsync(scanId, ct)`; claim atomowy (`UPDATE … WHERE Status='Queued' … FOR UPDATE SKIP LOCKED`, jeden skan naraz); `RecoverInterruptedAsync()` ustawia `Running` na `Failed(Interrupted)` i czyści osierocone katalogi w `WorkRoot`. Wzorzec usunięty lub nieaktywny w chwili podjęcia skanu (`Scans` nie ma FK do wzorców, więc usunięcie jest możliwe między `POST` a workerem) oraz stan `NoMatch`/`Ambiguous`/`Error` kończą skan jako `Failed(PatternNotResolved)` z detalem opisującym przypadek.

#### 2. Projekt workera

**File**: `worker/securitycheck-portal.Worker.csproj`, `worker/Program.cs`, `worker/ScanWorker.cs` (nowe)

**Intent**: Host `BackgroundService`, który co `PollIntervalSeconds` claimuje i wykonuje skan; `AddWindowsService()`; rejestracja `AddPortalData`, `AddGitResolution`, `AddScanning`. Nazwa projektu zgodna z konwencją repo, nazwa usługi Windows `SecurityCheck.Worker` zgodna z `infrastructure.md`.

**Contract**: osobny `UserSecretsId`; konfiguracja `ConnectionStrings:Portal`, `Git:*`, `Scan:*`; folder najwyższego poziomu `worker/` (`AGENTS.md`). `api.Tests` **nie** referencjonuje `worker`: logika pipeline'u jest w `core`, a dwa projekty z instrukcjami najwyższego poziomu dałyby niejednoznaczny typ `Program` (api ma `public partial class Program`).

### Success Criteria:

#### Automated Verification:

- Budowanie przechodzi: `dotnet build worker` oraz `dotnet build api`
- Testy pipeline'u z prawdziwą bazą i atrapami przechodzą (completed, incomplete, każdy `FailureReason`, wzorzec usunięty lub nieaktywny, sprzątanie katalogu, brak PAT w `FailureDetail`, wiersz wzorca niezmieniony po ponownym rozwiązaniu): `dotnet test api.Tests --filter "FullyQualifiedName~ScanJobRunner"`
- Claim jest atomowy (dwa równoległe claimy, jeden wygrywa) i odzyskiwanie po restarcie działa: `dotnet test api.Tests --filter "FullyQualifiedName~ScanQueue"`
- Istniejący test `resolve` nadal przechodzi: `dotnet test api.Tests --filter "FullyQualifiedName~RepositoryEndpointsTests"`

#### Manual Verification:

- `dotnet run --project worker` podejmuje wiersz `Queued` wstawiony ręcznie do bazy i kończy go statusem `Completed` lub `Incomplete` z prawdziwym Trivy
- Zatrzymanie workera w trakcie skanu i ponowne uruchomienie ustawia skan na `Failed` z przyczyną „przerwany", a katalog w `WorkRoot` znika

---

## Faza 5: API skanów

### Overview

Endpointy do uruchomienia skanu i odczytu wyniku oraz skrót ostatniego skanu w szczegółach repozytorium.

### Changes Required:

#### 1. Kontrakty i endpointy

**File**: `api/Scans/ScanContracts.cs`, `api/Scans/ScanEndpoints.cs` (nowe), `api/Program.cs`

**Intent**: `POST /api/patterns/{id:long}/scans` tworzy skan `Queued` razem ze zdarzeniem audytu `ScanRequested`; `GET /api/scans/{id:long}` zwraca skan z wynikami. Styl jak w `RepositoryEndpoints` (statyczna klasa, `XxxAsync`, gołe kody statusu, rekordy pozycyjne, enumy jako tekst).

**Contract**: `POST` → `202` z `ScanResponse` (nagłówek `Location`); `404` nieznany wzorzec; `409` wzorzec nieaktywny; `409` z ciałem `{ "reason": "active", "scanId": N }` przy aktywnym skanie (także przy wyścigu na indeksie unikalnym); `401` bez ciasteczka. `GET` → `200` `ScanDetails` z wynikami posortowanymi: `Critical`, `High`, `Medium`, `Low`, `Unknown`; wewnątrz poziomu po nazwie biblioteki, potem po identyfikatorze podatności (porównanie porządkowe); `404` nieznany. Sortowanie w pamięci (wyniki jednego skanu są małe).

#### 2. Skrót ostatniego skanu

**File**: `api/Repositories/RepositoryContracts.cs`, `api/Repositories/RepositoryEndpoints.cs`

**Intent**: `PatternResponse` zyskuje opcjonalny `LatestScan` (id, status, zakończono, liczba podatności), żeby strona repozytorium nie robiła zapytania na wzorzec.

**Contract**: nowe pole nullable, bez zmiany istniejących pól. „Ostatni" skan wzorca to ten z najwyższym `Id` na `PatternId` (przy równym `RequestedAt` rozstrzyga `Id`). `LatestScan` liczy jedno zapytanie grupujące, wywoływane tylko w `GET /api/repos/{id}`; `PatternResponse.From` dostaje opcjonalny parametr, a odpowiedzi `add`, `activate`, `deactivate` i `resolve` zwracają `LatestScan = null` (UI i tak je rewaliduje po akcji).

### Success Criteria:

#### Automated Verification:

- Testy endpointów przechodzą (202, 404, 409 nieaktywny, 409 aktywny z id, wyścig, sortowanie z `Unknown` na końcu, scalone duplikaty, audyt, `LatestScan` w szczegółach repozytorium jako skan o najwyższym `Id` przy dwóch skanach): `dotnet test api.Tests --filter "FullyQualifiedName~ScanEndpointsTests"`
- Nowe ścieżki mają wiersze „401 bez ciasteczka" w `NoPublicEndpointsTests`: `dotnet test api.Tests --filter "FullyQualifiedName~NoPublicEndpointsTests"`
- Całość testów API zielona: `dotnet test api.Tests`
- Budowanie przechodzi: `dotnet build api`

#### Manual Verification:

- Przez `api/securitycheck-portal.http`: logowanie, `POST` skanu, odpytywanie `GET` aż do `Completed` przy działającym workerze

---

## Faza 6: UI i dokumentacja

### Overview

Akcja „Skanuj", strona wyniku z odświeżaniem stanu, wspólny komponent listy podatności i aktualizacja `AGENTS.md`.

### Changes Required:

#### 1. Akcja i skrót na stronie repozytorium

**File**: `web/app/routes/repo-details.tsx`, `web/app/lib/patterns.ts` (typy), `web/app/lib/scan.ts` (nowe)

**Intent**: Nowy `intent` `scan` w `clientAction` (POST, przekierowanie na stronę skanu; `409 active` przekierowuje na trwający skan) i skrót `LatestScan` w `PatternRow` jako `Badge` statusu. Typy kontraktu obok istniejących, z komentarzem wskazującym plik C#.

**Contract**: przycisk „Skanuj" tylko dla aktywnego wzorca; stan zajętości jak dla pozostałych intentów (`busy`). Unia `AuditAction` w `patterns.ts` i `describeEvent` dostają przypadek `ScanRequested` z polską etykietą (inaczej „Historia zmian" pokaże surowy tekst, `describeEvent` ma `default: return event.action`). Skrót skanu w wierszu wzorca pokazuje przeskanowany tag (np. „przeskanowano 2.1.11"), bo wiersz wzorca może pokazywać inny tag z ostatniego „Sprawdź".

#### 2. Strona skanu

**File**: `web/app/routes/scan-details.tsx` (nowe), `web/app/routes.ts`

**Intent**: Trasa `/repos/:repoId/scans/:scanId` wewnątrz layoutu aplikacji, `clientLoader` z `GET /api/scans/{id}`, okresowe odświeżanie (`useRevalidator`) dopóki status to `Queued`/`Running`.

**Contract**: nagłówek strony pokazuje metadane skanu: przeskanowany tag i commit (skrócony), czas zakończenia, wiek bazy Trivy i wersję Trivy. Stan `Queued` dłużej niż 2 minuty (na podstawie `RequestedAt`) pokazuje `Alert` ostrzegawczy „Worker może nie działać"; bez heartbeatu. Widoczne stany: oczekuje/trwa, `Completed` z listą albo „brak wyników", `Incomplete` z `Alert` ostrzegawczym i listą brakujących plików blokady (także gdy wyników zero, wtedy bez „brak wyników"), `Failed` z `Alert` i przyczyną po polsku. Nagłówki przez `PageHeading` / `SectionHeading`.

#### 3. Komponent listy podatności i styleguide

**File**: `web/app/components/vulnerability-list.tsx` (nowe), `web/app/routes/styleguide.tsx`

**Intent**: Lista wierszy (biblioteka, wersja, CVE, `Badge` istotności z ikoną i tekstem, wersja naprawiona, pliki) jako wspólny komponent; `Unknown` jako neutralny `Badge` „Nieznana". Sekcja w galerii ze stanami: pusty, jeden, wiele, długie wartości.

**Contract**: bez nowych tokenów (istnieją `critical`/`high`/`medium`/`low`/`neutral`); wszystkie kolory przez tokeny semantyczne.

#### 4. Dokumentacja agentów

**File**: `AGENTS.md`

**Intent**: Opisać `worker/` w Project Structure, polecenie uruchomienia, opcje `Scan:*` i sekretny `UserSecretsId` workera, zasadę „skan zawsze na rozwiązanym commicie" (już jest) oraz zakaz przekazywania PAT-a do procesu Trivy.

**Contract**: edycja istniejących sekcji, bez nowych sekcji poza potrzebnymi.

### Success Criteria:

#### Automated Verification:

- Typy i trasy są spójne: `cd web && npm run typecheck`
- Brak zakodowanych wartości w zmienionych plikach (regex z `AGENTS.md`, oczekiwane 0 trafień)
- SPA buduje się i trafia do API: `cd web && npm run build:api`
- Backend nadal się buduje: `dotnet build api`

#### Manual Verification:

- `/styleguide` pokazuje listę podatności we wszystkich stanach w trybie jasnym i ciemnym, kontrast AA zachowany
- Pełny przepływ lokalnie (API + worker + UI): „Skanuj" przenosi na stronę skanu, stan odświeża się bez przeładowania, widać listę posortowaną od critical, a repozytorium bez lock file kończy się ostrzeżeniem „niepełny"
- Skan czystego repozytorium z lock file pokazuje „brak wyników", a awaria (np. zły adres bazy Trivy) pokazuje błąd z przyczyną
- Nawigacja klawiaturą i widoczny fokus na nowych elementach; wylogowanie w trakcie przekierowuje na `/login`
- `AGENTS.md` opisuje worker, opcje `Scan:*` i polecenie uruchomienia
- Skan w stanie `Queued` dłużej niż 2 minuty (przy zatrzymanym workerze) pokazuje ostrzeżenie, że worker może nie działać, a „Historia zmian" pokazuje polską etykietę dla zdarzenia skanu

---

## Testing Strategy

### Unit Tests:

- Runner procesów: kod wyjścia, niezerowy kod, brak pliku, timeout z zabiciem drzewa, ucięcie wyjścia.
- Parser raportu Trivy: kontrakt na przykładowych JSON (z podatnościami, czysty, brak targetów, `UNKNOWN`, duplikat w dwóch plikach, uszkodzony).
- Detektor plików blokady, reguły wieku bazy, odrzucenie zakazanej wersji, brak PAT-a w środowisku Trivy.
- Sortowanie i scalanie: dwa `HIGH` w kolejności biblioteki, `Unknown` na końcu, ta sama biblioteka, wersja i CVE z dwóch plików jako jeden wiersz.

### Integration Tests:

- Pipeline z prawdziwym PostgreSQL (Testcontainers, pomijane bez Dockera) i atrapami git i Trivy: wszystkie statusy i przyczyny, sprzątanie, atomowy claim, odzyskiwanie po restarcie.
- Endpointy przez `PortalFactory`, w tym wyścig dwóch `POST` i wiersze „401 bez ciasteczka".
- Pomijalne testy z prawdziwym git i Trivy (zmienne środowiskowe) uruchamiane ręcznie.

### Manual Testing Steps:

1. Zainstaluj przypiętą wersję Trivy lokalnie, ustaw `Scan:*` i `Git:*` w user-secrets workera.
2. Uruchom API, worker i UI; przeskanuj repozytorium z lock files i bez.
3. Zatrzymaj worker w trakcie skanu, uruchom ponownie i sprawdź status „przerwany".
4. Sprawdź stany w `/styleguide` w obu motywach.

## Performance Considerations

Skan trwa minuty; jeden skan naraz (jeden worker) wystarcza dla małej skali z PRD. Wyniki jednego skanu są sortowane w pamięci. Odpytywanie UI co kilka sekund jest akceptowalne przy garstce użytkowników.

## Migration Notes

Migracja `AddScans` jest addytywna (`infrastructure.md:162`). Kolejność wdrożenia (F-02): zatrzymać worker, zmigrować, wymienić API, uruchomić worker. Istniejące dane nie są zmieniane.

## References

- Related research: `context/changes/manual-version-scan/research.md`, `cliwrap-docs.md`, `hangfire-docs.md`, `hangfire-alternatives.md`
- Wzorzec launchera: `core/Git/GitCliTagSource.cs:19-79`, `:84-150`
- Wzorzec endpointów i zapisu: `api/Repositories/RepositoryEndpoints.cs:19-38`, `:327-354`, `:396-414`, `:421-460`
- Encje i konwencje: `core/Data/AuditEvent.cs`, `core/Data/PortalDbContext.cs`
- Wymagania skanera: `context/foundation/infrastructure.md:31-47`, `:112-118`, `:128-132`, `:158-162`, `:186`
- Poprzednia zmiana: `context/archive/2026-09-26-repo-version-pattern/plan.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Wspólny runner procesów i checkout

#### Automated

- [x] 1.1 Budowanie przechodzi: `dotnet build api` — 8196cf3
- [x] 1.2 Istniejące testy Git przechodzą bez zmian: `dotnet test api.Tests --filter "FullyQualifiedName~Git"` — 8196cf3
- [x] 1.3 Testy runnera przechodzą (kod wyjścia, kod niezerowy, nieistniejący plik, timeout zabija drzewo procesów, ucięcie wyjścia): `dotnet test api.Tests --filter "FullyQualifiedName~ProcessRunner"` — 8196cf3
- [x] 1.4 Testy checkoutu z atrapą runnera przechodzą (argumenty, token tylko w env, brak krótkiego limitu transferu w klonie, `CommitMismatch`, błąd klonu): `dotnet test api.Tests --filter "FullyQualifiedName~GitCheckout"` — 8196cf3

#### Manual

- [x] 1.5 Test integracyjny pomijalny (zmienna `SCAN_IT_REPO_URL` + tag) klonuje prawdziwe repozytorium z wewnętrznego GitLaba i `HEAD` równa się commitowi z rozwiązania — 8196cf3

### Phase 2: Model danych i migracja

#### Automated

- [x] 2.1 Migracja nie zostawia niezatwierdzonych zmian modelu: `dotnet ef migrations has-pending-model-changes --project core --startup-project api` — 3816edf
- [x] 2.2 Testy schematu przechodzą (drugi aktywny skan tego samego wzorca odrzucony, po zakończeniu pierwszego dozwolony, unikalność wyników, kaskada usuwania wyników, skan przeżywa usunięcie wzorca): `dotnet test api.Tests --filter "FullyQualifiedName~SchemaTests"` — 3816edf
- [x] 2.3 Budowanie przechodzi: `dotnet build api` — 3816edf

#### Manual

- [x] 2.4 `dotnet ef database update --project core --startup-project api` na lokalnej bazie tworzy tabele `Scans` i `ScanFindings` — 3816edf

### Phase 3: Trivy — uruchomienie, baza, parser

#### Automated

- [x] 3.1 Testy kontraktowe parsera na przykładowych JSON przechodzą (z podatnościami, czysty, brak targetów, `UNKNOWN`, duplikat w dwóch plikach, niepoprawny JSON): `dotnet test api.Tests --filter "FullyQualifiedName~TrivyReportParser"` — fcdbc0f
- [x] 3.2 Testy detektora plików blokady przechodzą: `dotnet test api.Tests --filter "FullyQualifiedName~LockFileDetector"` — fcdbc0f
- [x] 3.3 Testy skanera z atrapą runnera przechodzą (baza zbyt stara, aktualizacja nieudana z cache w limicie, timeout, zakazana wersja, środowisko bez PAT-a): `dotnet test api.Tests --filter "FullyQualifiedName~TrivyScanner"` — fcdbc0f
- [x] 3.4 Opcje `Scan` walidują się na starcie (brak `CacheDirectory` zatrzymuje aplikację): `dotnet test api.Tests --filter "FullyQualifiedName~ScanOptions"` — fcdbc0f

#### Manual

- [x] 3.5 Test integracyjny pomijalny (`TRIVY_PATH`) uruchomiony z prawdziwym Trivy na repozytorium z `packages.lock.json` zwraca oczekiwane podatności, a bez pliku blokady zwraca `Incomplete` — fcdbc0f

### Phase 4: Worker i pipeline skanu

#### Automated

- [x] 4.1 Budowanie przechodzi: `dotnet build worker` oraz `dotnet build api`
- [x] 4.2 Testy pipeline'u z prawdziwą bazą i atrapami przechodzą (completed, incomplete, każdy `FailureReason`, wzorzec usunięty lub nieaktywny, sprzątanie katalogu, brak PAT w `FailureDetail`, wiersz wzorca niezmieniony po ponownym rozwiązaniu): `dotnet test api.Tests --filter "FullyQualifiedName~ScanJobRunner"`
- [x] 4.3 Claim jest atomowy (dwa równoległe claimy, jeden wygrywa) i odzyskiwanie po restarcie działa: `dotnet test api.Tests --filter "FullyQualifiedName~ScanQueue"`
- [x] 4.4 Istniejący test `resolve` nadal przechodzi: `dotnet test api.Tests --filter "FullyQualifiedName~RepositoryEndpointsTests"`

#### Manual

- [x] 4.5 `dotnet run --project worker` podejmuje wiersz `Queued` wstawiony ręcznie do bazy i kończy go statusem `Completed` lub `Incomplete` z prawdziwym Trivy
- [x] 4.6 Zatrzymanie workera w trakcie skanu i ponowne uruchomienie ustawia skan na `Failed` z przyczyną „przerwany", a katalog w `WorkRoot` znika

### Phase 5: API skanów

#### Automated

- [ ] 5.1 Testy endpointów przechodzą (202, 404, 409 nieaktywny, 409 aktywny z id, wyścig, sortowanie z `Unknown` na końcu, scalone duplikaty, audyt, `LatestScan` w szczegółach repozytorium jako skan o najwyższym `Id` przy dwóch skanach): `dotnet test api.Tests --filter "FullyQualifiedName~ScanEndpointsTests"`
- [ ] 5.2 Nowe ścieżki mają wiersze „401 bez ciasteczka" w `NoPublicEndpointsTests`: `dotnet test api.Tests --filter "FullyQualifiedName~NoPublicEndpointsTests"`
- [ ] 5.3 Całość testów API zielona: `dotnet test api.Tests`
- [ ] 5.4 Budowanie przechodzi: `dotnet build api`

#### Manual

- [ ] 5.5 Przez `api/securitycheck-portal.http`: logowanie, `POST` skanu, odpytywanie `GET` aż do `Completed` przy działającym workerze

### Phase 6: UI i dokumentacja

#### Automated

- [ ] 6.1 Typy i trasy są spójne: `cd web && npm run typecheck`
- [ ] 6.2 Brak zakodowanych wartości w zmienionych plikach (regex z `AGENTS.md`, oczekiwane 0 trafień)
- [ ] 6.3 SPA buduje się i trafia do API: `cd web && npm run build:api`
- [ ] 6.4 Backend nadal się buduje: `dotnet build api`

#### Manual

- [ ] 6.5 `/styleguide` pokazuje listę podatności we wszystkich stanach w trybie jasnym i ciemnym, kontrast AA zachowany
- [ ] 6.6 Pełny przepływ lokalnie (API + worker + UI): „Skanuj" przenosi na stronę skanu, stan odświeża się bez przeładowania, widać listę posortowaną od critical, a repozytorium bez lock file kończy się ostrzeżeniem „niepełny"
- [ ] 6.7 Skan czystego repozytorium z lock file pokazuje „brak wyników", a awaria (np. zły adres bazy Trivy) pokazuje błąd z przyczyną
- [ ] 6.8 Nawigacja klawiaturą i widoczny fokus na nowych elementach; wylogowanie w trakcie przekierowuje na `/login`
- [ ] 6.9 `AGENTS.md` opisuje worker, opcje `Scan:*` i polecenie uruchomienia
- [ ] 6.10 Skan w stanie `Queued` dłużej niż 2 minuty (przy zatrzymanym workerze) pokazuje ostrzeżenie, że worker może nie działać, a „Historia zmian" pokazuje polską etykietę dla zdarzenia skanu
