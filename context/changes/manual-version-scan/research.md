---
date: 2026-09-30
researcher: Claude (claude-sonnet-5-5), dla Rafal Chmielorz
branch: feature/S-02
repository: SecurityCheck-Portal
topic: "S-02 manual-version-scan: dostępne skanery i biblioteki zgodne z tech-stack.md"
tags: [research, s-02, trivy, osv-scanner, cliwrap, background-jobs]
status: draft
last_updated: 2026-09-30
last_updated_by: Claude (claude-sonnet-5-5)
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
