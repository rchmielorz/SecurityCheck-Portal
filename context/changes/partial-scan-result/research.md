---
date: 2026-10-03T14:10:00+02:00
researcher: Rafal Chmielorz
git_commit: 8eabd011a47ecba045eb85e1fc079afbfaf5dc8c
branch: feature/S-02
repository: SecurityCheck-Portal (M1L1)
topic: "Czy informacja o częściowo udanym skanie (stan Completed + lista przeskanowanych i nieprzeskanowanych projektów) została już zaimplementowana podczas przeglądu scan-rules-change?"
tags: [research, codebase, scanning, ScanOutcome, TrivyScanner, scan-details]
status: complete
last_updated: 2026-10-03
last_updated_by: Rafal Chmielorz
---

# Research: czy częściowy wynik skanu został już zaimplementowany?

**Date**: 2026-10-03
**Git Commit**: 8eabd01 (HEAD; archiwum `scan-rules-change` zakomitowane)
**Branch**: feature/S-02

## Research Question

Użytkownik zakłada, że funkcjonalność z `partial-scan-result` „została już chyba zaimplementowana podczas review" zmiany `scan-rules-change`. Sprawdzenie, czy kod już (a) pokazuje, co zostało przeskanowane i co nie, (b) podaje powód nieudanego restore, (c) traktuje skan częściowo udany jako `Completed`.

## Summary

**Nie, w kodzie na `8eabd01` nic z tego nie jest zaimplementowane.** Sprawdzony zakres: commity zmieniające `core`, `api` i `web` po planie (`git log 473dbec..HEAD`: `e38f785`, `c80f8b7`, `3068922`, `698f05f`) oraz kod ścieżki wyniku skanu wymieniony niżej.

- Poprawki z przeglądu (`698f05f`) dotyczyły: komentarza i dokumentacji ryzyka restore (F1), łącznego limitu czasu restore `Scan:RestoreTotalTimeoutMinutes` (F3), kasowania połowicznego `packages.lock.json` (F4), testów (F5) i opcji w `worker/appsettings.json` (F6). Żadna nie zmienia modelu wyniku ani UI. F2 (kilka `.csproj` w jednym katalogu) zostało pominięte.
- Decyzja o statusie nadal wynika z jednego warunku: `missing.Count > 0 || report.TargetCount == 0` → `Incomplete`, inaczej `Completed` (`core/Scanning/TrivyScanner.cs:156-162`). Skan, w którym restore .NET się nie powiódł, a przeskanowano tylko npm, dalej kończy się `Incomplete`.
- Baza przechowuje tylko `MissingLockFiles` (`core/Data/Scan.cs:74-75`), a API tylko to pole w kontrakcie (`api/Scans/ScanContracts.cs:48`). Nie ma pola z listą przeskanowanych celów ani z powodem niepowodzenia restore.
- Parser raportu Trivy liczy cele (`TargetCount`) i zapisuje nazwę celu tylko przy znalezionej podatności (`core/Scanning/TrivyReportParser.cs:9`, `:56`, `:106-108`; `ScanFinding.Targets`, `core/Data/ScanFinding.cs:40`). Cel bez podatności nie jest nigdzie zapisywany, więc lista „przeskanowano" nie da się odtworzyć z bazy.
- UI dla `Incomplete` pokazuje etykietę „Niepełny" (`web/app/lib/scan.ts:113-114`) oraz alert i listę brakujących lock-ów (`web/app/routes/scan-details.tsx`), bez podziału na przeskanowane i nieprzeskanowane.

Jedyna zmiana w UI z `scan-rules-change` to zmiana brzmienia komunikatu i nagłówka na „Brakujące lub niewygenerowane pliki lock" (`3068922`). To nie jest informacja o częściowym powodzeniu.

## Detailed Findings

### Decyzja o statusie
- `TrivyScanner.ScanAsync`, krok 4: `Incomplete(report.Findings, missing, ...)` przy brakujących lock-ach lub zerze celów, w przeciwnym razie `Completed` (`core/Scanning/TrivyScanner.cs:154-162`).
- `ScanOutcome.Incomplete` niesie `Findings` i `MissingLockFiles` (`core/Scanning/ScanOutcome.cs`); `ScanJobRunner` zapisuje je do `Scan` (`core/Scanning/ScanJobRunner.cs`, przypadek `Incomplete`).

### Co wiadomo o częściowym wyniku
- `DotnetLockFileGenerator` loguje powód nieudanego restore tylko do logów workera (jedna linia na projekt) i nie zwraca wyniku (`core/Scanning/DotnetLockFileGenerator.cs`, metoda `GenerateAsync`). O kompletności decyduje ponowne `FindMissing`.
- Po stronie bazy nie ma kolumny na powód ani na listę celów; dodanie jej wymaga migracji (obecne: `InitialRepositories`, `AddScans`).

## Code References

- `core/Scanning/TrivyScanner.cs:154-162` — decyzja `Completed` / `Incomplete`
- `core/Scanning/TrivyReportParser.cs:9,56,106-108` — `TargetCount`, nazwa celu tylko przy podatności
- `core/Data/Scan.cs:74-75` — jedyne pole o brakach: `MissingLockFiles`
- `api/Scans/ScanContracts.cs:48` — kontrakt API zawiera tylko `MissingLockFiles`
- `web/app/lib/scan.ts:113-114` — etykieta „Niepełny"
- `core/Scanning/DotnetLockFileGenerator.cs` — powód nieudanego restore tylko w logu

## Historical Context (from prior changes)

- `context/archive/2026-10-03-scan-rules-change/reviews/impl-review.md` — przegląd; F2 (kilka `.csproj` w jednym katalogu nadpisuje wspólny lock → fałszywe `Completed`) pominięte i dopisane do `partial-scan-result/change.md`.
- `context/archive/2026-09-30-manual-version-scan/research.md:81` — wymóg trzeciego stanu `incomplete` (brak lock-a lub brak celów nie może znaczyć „brak podatności").

## Open Questions

1. Co ma znaczyć `Completed` przy częściowym skanie, żeby nie wrócić do fałszywego „czysto" (zob. Notes w `change.md`)?
2. Czy lista „przeskanowano / nie przeskanowano" ma być w bazie (migracja), czy wyliczana na żądanie?
3. Jak potraktować F2 (kilka `.csproj` w jednym katalogu) przy nowej definicji `Completed`?
