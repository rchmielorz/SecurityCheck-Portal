# Ręczny skan wersji i lista podatności (S-02) — Plan Brief

> Full plan: `context/changes/manual-version-scan/plan.md`
> Research: `context/changes/manual-version-scan/research.md`

## What & Why

Użytkownik klika „Skanuj" przy wzorcu wersji i dostaje wiarygodną listę podatności dla kodu faktycznie stojącego u klienta: posortowaną od critical do low, z jawnym „brak wyników" tylko wtedy, gdy skan jest kompletny. To gwiazda przewodnia M-1 (S-02). Fałszywe poczucie bezpieczeństwa jest gorsze niż brak narzędzia, więc zasadniczo rozróżniamy skan czysty, niepełny i nieudany.

## Starting Point

Po S-01 portal ma logowanie, repozytoria, wzorce wersji i rozwiązywanie wzorca przez `git ls-remote`, z utwardzonym launcherem procesów w `core/Git`. Nie ma hosta w tle, projektu workera, kodu klonowania ani żadnego modelu skanu. Frontend ma gotową stronę repozytorium, `Badge` istotności i tokeny.

## Desired End State

Na stronie repozytorium każdy aktywny wzorzec ma „Skanuj" i skrót ostatniego skanu. Strona skanu odświeża stan i pokazuje tag, commit, wiek bazy Trivy oraz listę podatności, albo „brak wyników", ostrzeżenie „niepełny" z brakującymi plikami blokady, albo jawny błąd z przyczyną.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Skaner | Trivy (`trivy fs`, JSON), bez własnej analizy CVE | Wymóg PRD (Non-Goals) i decyzja z `infrastructure.md` | Research |
| Uruchamianie procesów | Wspólny runner na `Process` wyciągnięty z `GitCliTagSource`, bez CliWrap | Zachowuje utwardzenie i istniejące testy, zero nowych zależności | Plan |
| Kolejka i host | Nowy `worker/` (usługa Windows) + tabela `Scans` jako kolejka, bez Hangfire | IIS zabija skany, jeden zestaw migracji EF, biblioteka dopiero przy S-05 | Plan |
| Który commit | Ponowne rozwiązanie wzorca na starcie skanu, tag i SHA zapisane w skanie | Wynik dotyczy tego, co stoi dziś, i pokazuje dokładnie co przeskanowano | Plan |
| Brak plików blokady | Status `incomplete` z wynikami i listą braków | Nigdy „brak wyników" bez dowodu, a znalezione CVE nie przepadają | Plan |
| Baza Trivy | Aktualizacja przed skanem; przy porażce cache tylko do `MaxDbAgeDays` (domyślnie 7), inaczej failed | Brak cichego użycia starej bazy | Plan |
| Wyniki | Scalone duplikaty (biblioteka+wersja+CVE), `Unknown` na końcu, sort po bibliotece i CVE | Czytelna, deterministyczna lista bez udawania niskiej istotności | Plan |
| Współbieżność | Jeden aktywny skan na wzorzec (częściowy unikalny indeks), przerwany restartem = failed | Brak zdublowanych wyników i niewidocznych ponowień | Plan |
| Izolacja Trivy | Środowisko z białej listy, bez PAT-a, zakaz wersji 0.69.4 | Ochrona poświadczenia przed skompromitowanym Trivy | Research |
| Skan i dane | Snapshot bez FK do wzorca, wyniki w EF | Skan przeżywa usunięcie wzorca (S-03) | Plan |
| Zapis wzorca | Worker nie zapisuje `LastResolved*`, skan trzyma własny tag i commit | `VersionPattern` nie ma tokenu współbieżności, więc unikamy wyścigu z endpointem `resolve` | Plan review |
| Limity klonu | Osobny `Scan:CloneTimeoutMinutes` i łagodniejszy `lowSpeedTime` dla `clone` | Domyślne 30 s i 20 s bezruchu z `ls-remote` przerwałyby realny klon | Plan review |

## Scope

**In scope:**
- wspólny runner procesów i checkout na rozwiązany commit z weryfikacją SHA
- tabele `Scans` i `ScanFindings`, migracja, audyt `ScanRequested`
- opcje `Scan:*`, uruchomienie Trivy, parser, wykrywanie braków lock files
- projekt `worker/` z pipeline'em, claimowaniem i odzyskiwaniem po restarcie
- `POST /api/patterns/{id}/scans`, `GET /api/scans/{id}`, `LatestScan` w szczegółach repozytorium
- UI: „Skanuj", strona skanu z odświeżaniem, `VulnerabilityList`, sekcja w `/styleguide`, `AGENTS.md`

**Out of scope:**
- Hangfire/TickerQ/Quartz, CliWrap, historia skanów (S-03), akceptacja ryzyka (S-04), skany cykliczne (S-05)
- zależności dev, submoduły, drugi skaner, ponowienia, Job Object, wdrożenie (F-02), retencja skanów

## Architecture / Approach

API zapisuje skan w stanie `Queued` i kończy. Worker (cienki `BackgroundService`) claimuje wiersz atomowo, a cała logika leży w `core/`: ponowne rozwiązanie wzorca, płytki klon tagu z weryfikacją SHA, wykrycie brakujących lock files, Trivy z odświeżaniem bazy, zapis wyników i sprzątanie. UI odpytuje `GET /api/scans/{id}`, dopóki stan to `Queued`/`Running`.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Runner i checkout | Wspólny runner, utwardzony git, klon na commit z weryfikacją | Regresja zachowania `ls-remote` |
| 2. Dane | `Scans`, `ScanFindings`, indeks aktywnego skanu, migracja | Poprawny filtr częściowego indeksu w EF |
| 3. Trivy | Opcje, baza z limitem wieku, parser, detektor lock files | Flagi Trivy niezweryfikowane względem przypiętej wersji |
| 4. Worker | Pipeline, claim, odzyskiwanie po restarcie, `AddWindowsService` | Atomowość claimu i sprzątanie katalogów |
| 5. API | Uruchomienie i odczyt skanu, skrót w szczegółach repozytorium | Wyścig dwóch `POST` na indeksie unikalnym |
| 6. UI i docs | Skanuj, strona skanu, lista podatności, `AGENTS.md` | Brak mechanizmu odpytywania i runnera testów UI |

**Prerequisites:** zainstalowany Trivy (przypięta wersja) i dostęp do Git z PAT-em lokalnie, lokalny PostgreSQL, Docker dla testów bazy.
**Estimated effort:** ~6 sesji, po jednej na fazę.

## Open Risks & Assumptions

- Wykrywanie lock files jest zachowawcze i może zgłaszać `incomplete` częściej niż trzeba (np. projekty testowe bez lock file).
- Po anulowaniu procesy potomne mogą przeżyć na Windows (jak w S-01); Job Object poza zakresem.
- Dostępność bazy Trivy (proxy, mirror) i działanie Trivy na serwerze docelowym wymagają weryfikacji w F-02.
- Ponowne rozwiązanie wzorca może wskazać inny tag niż ten widoczny w UI przed kliknięciem; skan zawsze pokazuje, co faktycznie przeskanowano (wiersz wzorca nie jest aktualizowany przez worker).
- Trivy może wymagać osobnej bazy Java przy plikach `.jar` (niezweryfikowane, do potwierdzenia przy implementacji).

## Success Criteria (Summary)

- Skan repozytorium z lock files zwraca podatności posortowane od critical do low; bez lock files kończy się `incomplete`, a czysty kompletny pokazuje „brak wyników".
- Awaria (baza zbyt stara, przesunięty tag, błąd Trivy, restart workera) daje jawny `failed` z przyczyną, nigdy cichy pusty wynik.
- `dotnet test api.Tests`, `dotnet build api`, `dotnet build worker` i `npm run typecheck` przechodzą.
