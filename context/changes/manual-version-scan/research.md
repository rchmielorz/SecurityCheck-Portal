---
date: 2026-09-30
researcher: Claude (claude-sonnet-5-5), dla Rafal Chmielorz
branch: feature/S-02
repository: SecurityCheck-Portal
topic: "S-02 manual-version-scan: dostępne skanery i biblioteki zgodne z tech-stack.md"
tags: [research, s-02, trivy, osv-scanner, cliwrap, background-jobs]
status: draft
last_updated: 2026-10-01
last_updated_by: Claude (claude-sonnet-5-5)
last_updated_note: "Follow-up 2: kolejka zadań dla S-02 (Hangfire i alternatywy); wcześniej follow-up zgodności cliwrap-docs.md z kodem (git_commit 1ba18be)"
---

# Research: S-02 manual-version-scan — skanery i biblioteki

Źródło: wyszukiwanie web (Exa), bez przeglądu kodu repo. Kontekst: .NET 10, EF Core/Npgsql, PostgreSQL, self-host na Windows (`tech-stack.md`); Trivy wskazany w `infrastructure.md`.

## Skaner (CLI wywoływany z workera)

| Narzędzie | Ocena dla S-02 | Uwagi |
|---|---|---|
| Trivy | domyślny wybór | Jedna binarka: NuGet i npm. Dla .NET czyta `packages.lock.json` (tranzytywne + dev), `*.deps.json` (bez dev), `packages.config`, `Directory.Packages.props`. Wyjście JSON. |
| OSV-Scanner (Google) | kandydat na drugi skaner | Mniej fałszywych alarmów przy lockfile (test na monorepo: 39/41 znalezionych, 1 FP; Trivy 38/41, 3 FP). Dla NuGet historycznie tylko `packages.lock.json` — stan obecny do sprawdzenia. |
| Grype + Syft | nie polecany | Mocny przy SBOM/obrazach; dodatkowy krok SBOM nie pasuje do celu `speed`. |
| OWASP Dependency-Check | nie polecany | Dopasowanie CPE: wiele FP (22 w tym samym teście), wolna synchronizacja NVD. |
| `dotnet list package --vulnerable --include-transitive` | uzupełnienie tylko dla .NET | Wbudowane w SDK; wymaga `restore` i feedu NuGet. Nie pokrywa npm. |

## Biblioteki .NET

- **CliWrap** (`Tyrrrz/CliWrap`, NuGet, brak zależności): uruchamianie `trivy` z tokenem anulowania/timeoutem; `PipeTarget.ToFile` dla dużych raportów (nie `ExecuteBufferedAsync`); `ExitCode` odróżnia skan nieudany od czystego. Uwaga: po anulowaniu procesy potomne mogą dalej żyć na Windows (issue #37) — rozważyć Job Object.
- **`BackgroundService` + `System.Threading.Channels`** (wbudowane): najmniejsza opcja, zgodna z „hosted background services" z `tech-stack.md`. Brak trwałości kolejki — status w PostgreSQL, przerwane skany oznaczać `failed` przy starcie.
- **Hangfire** (+ `Hangfire.PostgreSql`) lub **Quartz.NET**: trwała kolejka/ponowienia. Hangfire = procesor zadań z dashboardem, Quartz = harmonogram (przyda się w S-05). Obie wymagają zadań bezpiecznych przy ponownym uruchomieniu. Zgodność z .NET 10 nie zweryfikowana.
- **`System.Text.Json`**: modele wyniku Trivy (`Results[].Vulnerabilities[]`: `VulnerabilityID`, `PkgName`, `Severity`).

## Wnioski do Unknowns S-02

- **Brak plików blokady**: bez `packages.lock.json` (w .NET wymaga `RestorePackagesWithLockFile`) Trivy może nic nie znaleźć → fałszywe „brak wyników". Portal powinien wykrywać brak lockfile i ostrzegać, a nie pokazywać „czysto".
- **Dev dependencies**: pomijane w `*.deps.json`, uwzględniane w `packages.lock.json`.
- **Self-contained publish**: Trivy gubi bundlowany runtime (`runtimepack`) — dotyczy wyników publikacji, nie repozytoriów źródłowych.
- **Baza podatności offline**: Trivy (`--skip-db-update`, `trivy image --download-db-only`, cache) i OSV-Scanner (`--offline`) obsługują lokalny cache/mirror.
- **Windows**: źródła pośrednio sugerują binarki Windows dla Trivy (`choco install trivy`) — zweryfikować na serwerze docelowym.

## Rekomendacja

Trivy (`trivy fs --format json --scanners vuln`) uruchamiany przez CliWrap z timeoutem w `BackgroundService` z kolejką na `Channel`; stan skanu w PostgreSQL. Hangfire dopiero przy S-05. OSV-Scanner jako drugi skaner, jeśli fałszywe „brak wyników" okaże się realnym problemem.

## Otwarte / niezweryfikowane

- Zgodność Hangfire.PostgreSql i Quartz z .NET 10.
- Aktualne wsparcie NuGet w OSV-Scanner.
- Działanie Trivy na serwerze Windows (proxy, konto usługi, Defender).


---

# Follow-up: czy `cliwrap-docs.md` jest zgodny z kodem repo (S-02)

**Date**: 2026-09-30 · **Git Commit**: 1ba18be · **Branch**: feature/S-02 · **Status**: partial (patrz „Niezweryfikowane")

**Pytanie**: czy [cliwrap-docs.md](cliwrap-docs.md) pasuje do istniejącego kodu? Użytkownik napisał „S-04", ale potwierdził, że chodzi o S-02 (`manual-version-scan`, uruchamianie Trivy). S-04 (`accepted-risk-triage`) nie uruchamia procesów i CliWrap go nie dotyczy.

## Werdykt

**Częściowo zgodny.** Na poziomie platformy i architektury CliWrap pasuje (.NET 10, `core/` współdzielony z workerem, wzorzec „wynik zamiast wyjątków"). Nie jest jednak wpinany w pustkę: repo ma już własny, utwardzony launcher procesów oparty na `System.Diagnostics.Process`, a dokument go nie uwzględnia. Decyzja „CliWrap czy istniejący wzorzec" jest otwarta i należy do `/10x-plan`, nie do researchu.

## Zgodne

- **Platforma**: `core/` i `api/` celują w `net10.0` (`core/securitycheck-portal.Core.csproj:5`, `api/securitycheck-portal.csproj:4`). CliWrap według README (Context7) celuje w .NET Standard 2.0+ / .NET Core 3.0+ bez zależności zewnętrznych. W katalogu głównym nie ma `Directory.Packages.props` ani `Directory.Build.props`, więc wersje pakietów są w pojedynczych `.csproj` (nowy `PackageReference` trafiłby do `core/`).
- **Miejsce**: `AGENTS.md` i plan S-01 mówią, że `core/` jest współdzielone z „przyszłym workerem" (`context/archive/2026-09-26-repo-version-pattern/plan.md:79`, `:202`). CliWrap w `core/` byłby dostępny dla API i workera.
- **Wzorzec błędów**: `GitCliTagSource` zwraca `GitTagListing.Success/Failure` zamiast rzucać (`core/Git/IGitTagSource.cs`). `WithValidation(CommandResultValidation.None)` z sekcji 4 dokumentu daje to samo podejście.
- **Timeout vs anulowanie wywołującego**: istniejący kod łączy token timeoutu z tokenem wywołującego i rozróżnia je po `cancellationToken.IsCancellationRequested` (`core/Git/GitCliTagSource.cs:39-41`, `:65-77`). Sekcja 3 dokumentu (`OperationCanceledException`, tokeny do `ExecuteAsync`) mapuje się na ten sam schemat.
- **Argumenty bez powłoki**: kod używa `ArgumentList` (`GitCliTagSource.cs:112`, `:126`), a dokument zaleca `WithArguments(IEnumerable<string>)` bez ręcznego escapowania.

## Niezgodne lub nieuwzględnione w dokumencie

1. **Istnieje już launcher procesów.** W plikach `.cs` w `core/` i `api/` (grep bez `obj/`) jedyne miejsce uruchamiania procesu to `core/Git/GitCliTagSource.cs:25`. W repo (`.cs`, `.csproj`, `.md` poza folderem tej zmiany) nie ma odwołania do CliWrap. Dokument nie wspomina o tym wzorcu, a plan S-01 zakładał, że worker użyje kodu Git „bez zmian" (`plan.md:202`).
2. **Utwardzenie środowiska, którego dokument nie pokrywa.** `CreateStartInfo` (`GitCliTagSource.cs:84-150`) usuwa odziedziczone zmienne `GIT_*`, ustawia `GIT_CONFIG_NOSYSTEM`, `GIT_CONFIG_GLOBAL`, `GIT_TERMINAL_PROMPT=0`, `GIT_ALLOW_PROTOCOL=https`, przekazuje PAT jako nagłówek w env ograniczony do URL, ustawia `WorkingDirectory` poza repozytorium, a stdin zamyka jawnie (`:37`). Dokument pokazuje tylko dodawanie zmiennych przez `WithEnvironmentVariables(... Set ...)`. Usuwanie odziedziczonych zmiennych i zamykanie stdin w CliWrap nie zostały sprawdzone (patrz niżej).
3. **Zabijanie drzewa procesów.** Kod robi `Kill(entireProcessTree: true)` (`GitCliTagSource.cs:156`). Dokument mówi tylko, że proces „zostanie zabity", a o procesach potomnych zawiera wyłącznie ostrzeżenie z issue #37 (2019). Git i Trivy mogą uruchamiać procesy potomne, więc to istotna luka.
4. **Testowalność.** Testy sprawdzają `ProcessStartInfo` ze statycznego `CreateStartInfo` (`api.Tests/Git/GitCliTagSourceTests.cs:25-78`: token tylko w env, brak tokenu w argv, usuwanie `GIT_SSL_NO_VERIFY`). `Command` z CliWrap wystawia `Arguments` i `EnvironmentVariables` (źródło: `Command.cs` z wyszukiwania Exa), więc da się to testować, ale ten kontrakt testów trzeba by przepisać.
5. **Szkic z sekcji 5 jest węższy niż wymagania S-02.** Sprzeczności z `infrastructure.md` i `AGENTS.md`:
   - Szkic zna tylko stany `failed` i `completed`. `infrastructure.md:159` wymaga trzeciego, **incomplete** (brak lock file lub brak targetów w JSON nie może znaczyć „brak podatności"), a `:158` wymaga oznaczenia skanu jako failed przy zbyt starej bazie (`UpdatedAt`).
   - Szkic nie ma `--cache-dir` ani przypiętej wersji Trivy (`infrastructure.md:128`, `:160`).
   - Szkic skanuje `repoCheckoutPath`, ale repo nie ma jeszcze kodu klonowania i checkoutu na commit z `VersionPattern.LastResolvedCommit` (`core/Data/VersionPattern.cs:44`). S-02 musi dodać kolejne wywołania git (clone/fetch/checkout), a obecne `CreateStartInfo` ma na sztywno `ls-remote --tags` (`GitCliTagSource.cs:124`).
   - `AGENTS.md`: skan musi iść na kod odpowiadający wzorcowi wersji, nigdy na domyślną gałąź. Szkic bez kroku checkoutu tego nie pokazuje.
6. **Brak hosta dla skanu.** `api/Program.cs` nie rejestruje usługi hostowanej, a projektu workera nie ma (`.csproj` w repo: `api`, `core`, `api.Tests`). `infrastructure.md:186` opisuje `SecurityCheck.Worker` z `AddWindowsService()`. Propozycja „`BackgroundService` z `Channel`" z części 1 tego pliku pozostaje propozycją, nie stanem repo.
7. **Sekrety w komunikatach wyjątków.** `CommandExecutionException` CliWrap zawiera `TargetFilePath` i `Arguments` w komunikacie (Context7, `_autodocs/errors.md`). Przy `WithValidation(None)` nie jest rzucany, a PAT i tak nie trafia do argv, więc ryzyko jest niskie, o ile to zostanie zachowane.

## Niezweryfikowane (nie twierdzę, że tak jest)

- Czy `WithEnvironmentVariables` pozwala usunąć odziedziczoną zmienną (wskazówka: `IReadOnlyDictionary<string, string?>` w konstruktorze `Command`, z wyszukiwania Exa; brak potwierdzenia w Context7).
- Czy domyślne `PipeSource.Null` zamyka stdin tak, by prompt kończył się błędem.
- Czy CliWrap 3.10.5 zabija całe drzewo procesów na Windows przy anulowaniu (issue #37 z 2019 opisuje pozostawianie potomków; aktualny stan nie sprawdzony).
- Czy graceful cancellation (Ctrl+C) działa na Windows dla `git` i `trivy` uruchamianych bez konsoli.
- Działanie Trivy na serwerze docelowym (Defender, profil konta usługi), poza zakresem tego researchu.

## Open questions (dla `/10x-plan`)

1. Zostać przy `Process` i wyciągnąć wspólny, utwardzony runner (git + trivy), czy przejść na CliWrap i przenieść na niego także `GitCliTagSource`? Zależy od punktów 2-3 i od pozycji niezweryfikowanych.
2. Gdzie powstaje checkout (katalog, sprzątanie, długie ścieżki wg `infrastructure.md` „Unknown Unknowns") i jak weryfikować SHA klona względem `LastResolvedCommit` (plan S-01, `:63`).
3. Kontrakt trzech stanów skanu (completed / incomplete / failed) oraz źródło „świeżości" bazy Trivy.

---

# Follow-up 2: kolejka zadań dla S-02 (Hangfire i alternatywy)

**Date**: 2026-10-01 · **Git Commit**: 1ba18be · **Branch**: feature/S-02 · **Status**: partial (zgodność runtime nie sprawdzona)

Szczegóły i źródła: [hangfire-docs.md](hangfire-docs.md) (Context7) oraz [hangfire-alternatives.md](hangfire-alternatives.md) (web search). To podsumowanie dla `/10x-plan`.

## Co wiemy

- **Hangfire**: `AddHangfire` + `AddHangfireServer` można rejestrować w osobnych procesach (API kolejkuje, worker wykonuje). Argumenty zadań są serializowane, więc przekazuje się identyfikatory. Zadanie przerwane zamknięciem serwera wraca na początek kolejki, więc musi być reentrant. `CancellationToken` Hangfire nie obejmuje limitu czasu skanu. Domyślny dashboard jest tylko lokalny, a filtr „każdy zalogowany" dokumentacja nazywa potencjalnie niebezpiecznym.
- **Luka Hangfire**: magazyn PostgreSQL to osobny pakiet `Hangfire.PostgreSql` 1.21.1 (netstandard2.0, `Npgsql >= 6.0.11`). Context7 go nie obejmuje. Zgodność z Npgsql 10.0.3 i EF Core 10 z tego repo jest niezweryfikowana. Tabele Hangfire powstają poza migracjami EF (kwestia do sprawdzenia w jego dokumentacji).
- **Alternatywy** (tylko metadane NuGet i dokumentacja): Quartz.NET (`net10.0`, `UsePostgres`, harmonogram bez dashboardu, tabele `QRTZ_*`), TickerQ 10.4.0 (`net10.0`, EF Core, możliwy wspólny `DbContext`, młody projekt), Wolverine (trwała kolejka w PostgreSQL, ciężki model), Coravel i NCronJob (bez trwałości, nie spełniają wymogu przeżycia restartu).
- **Zgodność z repo**: brak hosta w tle w `api/Program.cs`, brak projektu workera (`infrastructure.md:186` przewiduje `SecurityCheck.Worker`). Migracje EF robi się ręcznie (`AGENTS.md`). Każda nowa trasa (np. dashboard) musi przejść przez fallback policy i `NoPublicEndpointsTests`.

## Wnioski dla planu (rekomendacja, decyzja należy do `/10x-plan`)

1. **Stan skanu w tabeli portalu** niezależnie od wyboru biblioteki. `infrastructure.md:158-159` wymaga rozróżnienia failed / incomplete / completed, a stan zadania biblioteki tego nie niesie.
2. **Domyślny wybór dla S-02: własna tabela `Scans` + `BackgroundService` w workerze** (atomowe claimowanie wiersza ze statusem `Queued`). Powody: zero nowych zależności, jeden zestaw migracji EF, zgodność z „hosted background services" z `tech-stack.md`, a tabela jest potrzebna i tak. Koszt: samodzielne odzyskiwanie skanów w stanie `Running` po restarcie i brak wbudowanych ponowień.
3. **Biblioteka kolejki jest uzasadniona dopiero przy S-05 (cron)**: wtedy rozważyć Quartz.NET (dojrzały harmonogram) albo TickerQ. Hangfire pozostaje poprawnym wariantem zapasowym, jeśli zespół chce gotowy dashboard i ponowienia, kosztem osobnego pakietu magazynu i tabel poza EF.
4. Niezależnie od wyboru: `[AutomaticRetry(Attempts = 0)]` lub odpowiednik, żeby ponowienie nie ukrywało błędu skanu ani nie dublowało wyniku; skan idempotentny po `scanId`; limit czasu realizowany osobno od tokenu biblioteki.

## Otwarte pytania (dodatkowe)

1. Czy zespół chce dashboard zadań w MVP, czy wystarczy lista skanów w UI portalu?
2. Czy S-05 ma zadecydować o wyborze biblioteki już teraz, czy dopiero przy planowaniu S-05?
3. Sprawdzić przed wyborem biblioteki: zgodność runtime z Npgsql 10.0.3 / EF Core 10.0.12, model licencyjny TickerQ i Wolverine, zachowanie zadania `Running` po restarcie usługi Windows.
