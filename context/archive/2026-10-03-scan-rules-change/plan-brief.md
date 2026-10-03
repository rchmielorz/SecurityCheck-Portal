# Generowanie lock-ów .NET przed skanem — Plan Brief

> Full plan: `context/changes/scan-rules-change/plan.md`
> Research: `context/changes/scan-rules-change/research.md`

## What & Why

Założenie, że projekty w GitLabie mają `packages.lock.json`, jest w tej organizacji nierealne, a Trivy nie czyta `<PackageReference>` z `.csproj`. Bez zmiany prawie każdy skan .NET jest „niepełny". Worker będzie więc sam generował brakujące lock-i przed skanem, tak by wynik był wiarygodny bez wymagań wobec repozytoriów.

## Starting Point

`LockFileDetector` zgłasza brak lock-a, a `TrivyScanner` (`core/Scanning/TrivyScanner.cs:148`) zamienia to w `Incomplete`. Pole `MissingLockFiles` przechodzi przez bazę, API i UI. Procesy potomne dostają tylko białą listę zmiennych środowiskowych, bo worker trzyma `Git__Token`.

## Desired End State

Skan wersji repozytorium .NET bez lock-a kończy się `Completed` (z listą podatności lub jawnym „brak wyników"), o ile `dotnet restore` powiódł się dla każdego projektu. Projekt, którego restore się nie udał, jest wymieniony w UI, a skan jest `Incomplete`. Nigdy nie ma fałszywego „czysto".

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Czy Trivy skanuje bez lock-ów | Nie (.csproj nie jest czytany) | Dokumentacja Trivy: tylko lock, packages.config, *Packages.props, deps.json | Plan |
| Podejście | Generować lock w workerze | Wybór użytkownika; jedyny sposób na realny skan projektów bez lock-ów | Plan |
| Ekosystemy | Tylko .NET | Brak wymagania Node na serwerze i skryptów npm | Plan |
| Feedy NuGet | NuGet.config konta workera | Zero sekretów w portalu, zgodnie z zasadą „worker nie dostaje PAT-a" | Plan |
| Ryzyko MSBuild | Akceptacja + utwardzone środowisko | Repozytoria z wewnętrznego GitLaba; wyczyszczone env, limit czasu | Plan |
| Błąd restore | Projekt na liście, skan `Incomplete` | Zachowuje zasadę „nigdy czysto bez dowodu" | Research |
| `TargetCount == 0` | Zostaje `Incomplete` | Chroni przed „czysto" dla repo bez żadnych zależności | Plan |
| Model danych | Bez migracji; `MissingLockFiles` zmienia znaczenie | Najmniejsza zmiana kontraktu | Plan |

## Scope

**In scope:** detektor (`packages.config`, lista projektów), `DotnetLockFileGenerator`, nowe opcje `Scan:*`, integracja w `TrivyScanner`, komunikaty UI, testy, dokumentacja i wymagania serwera.

**Out of scope:** npm, izolacja kontenerowa, poświadczenia do feedów z portalu, wiele `.csproj` w jednym katalogu, zmiany schematu bazy i API.

## Architecture / Approach

Po bramce wieku bazy `TrivyScanner` woła `DotnetLockFileGenerator`, który dla każdego `.csproj` bez lock-a uruchamia `dotnet restore --use-lock-file` w utwardzonym środowisku. Potem działa istniejący detektor i `trivy fs`; projekty bez lock-a (restore nieudany) trafiają na listę i dają `Incomplete`.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Detektor | `packages.config` uznany, lista projektów do restore | Pomylenie projektów starego i nowego typu |
| 2. Generator | `dotnet restore --use-lock-file`, opcje, środowisko bez sekretów | Środowisko NuGet (profil, proxy) na serwerze |
| 3. Integracja | Krok w przepływie skanu, komunikaty UI | Wydłużenie skanu, restore częściowy |
| 4. Dokumentacja | AGENTS.md, infrastructure.md, roadmap | Brak SDK/feedów na serwerze |

**Prerequisites:** SDK .NET i skonfigurowane feedy NuGet w profilu konta workera (poza kodem; do ustalenia z adminem).
**Estimated effort:** ~2–3 sesje, 4 fazy.

## Open Risks & Assumptions

- Restore wykonuje MSBuild z repozytorium; zakładamy, że wewnętrzne repozytoria są zaufane.
- Prywatne feedy NuGet muszą być dostępne z konta workera, inaczej projekty z pakietami firmowymi zostaną „nieskanowane".
- Kilka `.csproj` w jednym katalogu współdzieli jeden `packages.lock.json`.

## Success Criteria (Summary)

- Skan repozytorium .NET bez lock-a daje `Completed` z realnymi wynikami.
- Nieudany restore jest widoczny w UI i skutkuje `Incomplete`.
- `Git__Token` nie występuje w środowisku procesu restore.
