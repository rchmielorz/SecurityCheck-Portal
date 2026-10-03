---
date: 2026-10-03
researcher: Rafal Chmielorz
git_commit: aae8fe0fc7508f5e61cb560a22389133d4659d5c
branch: feature/S-02
repository: SecurityCheck-Portal (M1L1)
topic: "Zmiana reguł skanowania – lock files w projektach GitLab nie są wymagane"
tags: [research, codebase, scanning, LockFileDetector, TrivyScanner, ScanOutcome]
status: complete
last_updated: 2026-10-03
last_updated_by: Rafal Chmielorz
---

# Research: Zmiana reguł skanowania – lock files w projektach GitLab nie są wymagane

**Date**: 2026-10-03
**Researcher**: Rafal Chmielorz
**Git Commit**: aae8fe0
**Branch**: feature/S-02
**Repository**: SecurityCheck-Portal

## Research Question

Założenie, że repozytoria w GitLabie mają pliki lock, jest „nadmiarowe" (względy organizacyjne). Gdzie w kodzie to założenie jest zaszyte i co dokładnie zmienia jego usunięcie?

## Summary

- Założenie jest zakodowane w jednym miejscu logiki: `LockFileDetector.FindMissing` wywoływany w kroku 4 `TrivyScanner` (`core/Scanning/TrivyScanner.cs:148-153`). Dla każdego katalogu z `*.csproj` bez `packages.lock.json` albo z `package.json` bez `package-lock.json`/`yarn.lock`/`pnpm-lock.yaml` dodaje wpis do listy braków (`LockFileDetector.cs:35-45`). Przy niepustej liście skan kończy się jako `Incomplete`, nigdy `Completed`.
- Drugi, niezależny warunek `Incomplete`: `report.TargetCount == 0` (Trivy nie znalazł żadnego pliku zależności) — `TrivyScanner.cs:149`. Nie zależy od detektora.
- Konsekwencja dla tego repo (inspected path only): jeśli repozytorium klienta nie ma plików lock, dziś każdy jego skan to `Incomplete` z listą „Brakujące pliki lock" albo z komunikatem „Trivy nie znalazł żadnego pliku z zależnościami" (`web/app/routes/scan-details.tsx:116-119`). Reguła jest więc faktycznie nie do spełnienia, a skan nigdy nie jest „zakończony" — to prawdopodobnie źródło zgłoszenia.
- Ryzyko usunięcia reguły wprost: mieszany checkout (część projektów z lock, część bez) przestaje być `Incomplete` i dostaje `Completed` / „brak podatności", choć projekty bez lock nie zostały przeskanowane. To dokładnie fałszywe „czysto", któremu reguła miała zapobiec (`context/archive/2026-09-30-manual-version-scan/research.md:37`, `context/foundation/infrastructure.md:159` wg cytatu w research.md:81).

## Detailed Findings

### Detektor (jedyne miejsce reguły)
- `core/Scanning/LockFileDetector.cs:10-11` — pomija `.git`, `node_modules`, `bin`, `obj`; akceptowane lock Node: `package-lock.json`, `yarn.lock`, `pnpm-lock.yaml`; dla .NET tylko `packages.lock.json`.
- Doc klasy (`:3-7`) deklaruje regułę jako „konserwatywną": może zgłosić brak, którego Trivy by nie potrzebował. To właśnie ta nadgorliwość jest dziś problemem (np. `.csproj` bez lock, choć Trivy czyta też `packages.config`/`Directory.Packages.props` — `archive/2026-09-30-manual-version-scan/research.md:22`).
- Rejestracja: `core/Scanning/ScanServiceCollectionExtensions.cs`; wstrzyknięty do `TrivyScanner` (`TrivyScanner.cs:38`).

### Decyzja Completed / Incomplete
- `TrivyScanner.cs:148-153`: `missing.Count > 0 || report.TargetCount == 0` → `Incomplete`, inaczej `Completed`.
- `core/Scanning/ScanOutcome.cs:70-82`: `Completed` zdefiniowany jako „lock files dla każdego manifestu były obecne i Trivy znalazł ≥1 target"; `Incomplete` niesie `MissingLockFiles`. Doc trzeba by zmienić razem z regułą.
- `ScanJobRunner.cs:206-246` zapisuje `MissingLockFiles` do `Scan` (`core/Data/Scan.cs:74-75`); kolumna w migracji `AddScans`.

### Konsumenci wyniku
- API: `api/Scans/ScanContracts.cs:48`, `api/Scans/ScanEndpoints.cs:142` (pole `MissingLockFiles` w kontrakcie).
- Web: `web/app/lib/scan.ts:66,113-127`, `web/app/routes/scan-details.tsx:108-135` (alert „Skan niepełny", sekcja „Brakujące pliki lock", treść komunikatu zależna od `missingLockFiles.length`).
- Testy: `api.Tests/Scanning/LockFileDetectorTests.cs`, `TrivyScannerTests.cs` (m.in. `Missing_lock_file_is_incomplete_with_the_findings_kept` :91, helper `WithLockFile` :44), `ScanJobRunnerTests.cs:77,115,382`, `ScanEndpointsTests.cs:47,317,329`.
- Dokumentacja: `AGENTS.md:34` („only NuGet/npm lock files are scanned"), doc `TrivyScanner.cs:30-33`, `context/foundation/roadmap.md:130` (otwarte pytanie S-02 „Czy skanowane repozytoria mają pliki blokady" — odpowiedź użytkownika: nie można tego zakładać).

## Code References

- `core/Scanning/LockFileDetector.cs:21-57` — reguła brakujących lock
- `core/Scanning/TrivyScanner.cs:146-153` — decyzja Completed/Incomplete
- `core/Scanning/ScanOutcome.cs:70-82` — kontrakt wyniku
- `core/Scanning/ScanJobRunner.cs:206-246` — persystencja
- `web/app/routes/scan-details.tsx:108-135` — prezentacja
- `core/Scanning/TrivyReportParser.cs:9` — `TargetCount == 0` = „nie czysto"

## Architecture Insights

- Zasada „nigdy czysto bez dowodu" jest rozłożona na dwa sygnały: (a) heurystyka manifest→lock (detektor), (b) fakt z raportu Trivy (TargetCount). Usunięcie (a) zostawia tylko (b), który nie wykryje projektów bez lock w repo, w którym choć jeden lock istnieje.
- Reguła jest jedynym powodem istnienia kolumny/pola `MissingLockFiles` i sekcji UI; zmiana może być miękka (zmiana semantyki) albo twarda (usunięcie pola — migracja + zmiana kontraktu API).
- Ograniczenie Trivy: bez lock/`packages.config`/deps.json nie ma czego skanować, więc „nie wymagamy lock" nie daje skanu takich repo — daje tylko inną etykietę wyniku.

## Historical Context (from prior changes)

- `context/archive/2026-09-30-manual-version-scan/research.md:37` — pierwotne uzasadnienie: brak lockfile → Trivy może nic nie znaleźć → portal ma ostrzegać, nie pokazywać „czysto".
- `context/archive/2026-09-30-manual-version-scan/research.md:81` — wymóg trzeciego stanu `incomplete`.
- `context/foundation/roadmap.md:130` — niezamknięte pytanie o obecność plików lock (Owner: user), teraz de facto rozstrzygnięte „nie zakładamy".
- `context/foundation/lessons.md` — jedyny wpis (recovery workera) nie dotyczy tej zmiany.

## Related Research

- `context/archive/2026-09-30-manual-version-scan/research.md`

## Open Questions (decyzje dla /10x-plan)

1. Co ma znaczyć „nadmiarowe": (A) brak lock to nie powód do `Incomplete` (usunąć detektor), (B) brak lock to informacja/ostrzeżenie, ale skan zostaje `Completed`, (C) reguła wyłączona tylko dla hostów GitLab (konfigurowalnie)? Dziś wszystkie repozytoria przechodzą przez `AllowedHosts` GitLaba, więc C ≈ A bez dodatkowego przełącznika.
2. Czy zachować `Incomplete` dla `TargetCount == 0` (Trivy nic nie znalazł)? Rekomendacja badawcza: tak — inaczej repo bez żadnych lock pokaże „brak podatności".
3. Jak oznaczać projekty bez lock, żeby nie było fałszywego „czysto" (np. informacyjna lista „nieskanowane manifesty" bez wpływu na status)? To decyzja produktowa.
4. Czy usuwać `MissingLockFiles` z modelu/API/UI (migracja), czy zostawić pole i zmienić semantykę?
5. Czy da się dostarczyć lock w inny sposób (np. `dotnet restore --use-lock-file` / `npm install --package-lock-only` w workerze) — poza zakresem tego zgłoszenia, ale zmienia sens „nadmiarowe".
