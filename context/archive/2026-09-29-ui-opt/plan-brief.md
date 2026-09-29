# Poprawki graficzne: hierarchia i układ listy repozytoriów: Plan Brief

> Full plan: `context/changes/ui-opt/plan.md`
> Research: `context/changes/ui-opt/research.md`

## What & Why

Lista repozytoriów po zalogowaniu ma dziś płaską hierarchię: formularz dodawania waży więcej niż lista, dwa nagłówki mają ten sam styl, brak widocznego tytułu strony, a wiersz nie sygnalizuje, że repozytorium nie ma aktywnych wzorców. Poprawiamy to na istniejącym systemie projektowym, bez nowych zależności.

## Starting Point

System z poprzedniej zmiany (`in-context`): tokeny w `web/app/app.css`, komponenty w `web/app/components/`, skan twardych wartości daje 0 trafień. Widok `home.tsx` składa się z tych komponentów, ale kolejność bloków i style nagłówków są zduplikowane w plikach tras. Widoku nie da się uruchomić z danymi bez LDAP i bazy.

## Desired End State

Ekran pokazuje tytuł „Repozytoria”, od razu listę, a pod nią kartę dodawania (z linkiem-skrótem w tytule). Każdy wiersz ma `Badge` z liczbą wzorców (ostrzegawczy dla 0) i strzałkę. Komponenty są pokazane w `/styleguide` na danych przykładowych, a `AGENTS.md` mówi, których używać.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Układ C2 | Lista pierwsza, formularz pod listą | Powracający użytkownik przychodzi po listę, a zmiana nie wymaga nowego stanu. | Plan |
| Szerokość C1 | Bez zmiany kontenera | Wspólny kontener dotyka wielu widoków; użytkownik wybrał mniejszy zakres. | Plan |
| Wiersz C4 | `Badge` (neutralny, ostrzegawczy przy 0) plus strzałka | Widać repozytoria, w których nic nie będzie sprawdzane, na istniejących danych. | Plan |
| Bramka wizualna | Lista w `/styleguide` z danymi przykładowymi | API wymaga LDAP i bazy, a galeria daje powtarzalny przegląd przypadków brzegowych. | Research + Plan |
| Nagłówki C3 | `PageHeading` (z akcją) i `SectionHeading` | Usuwa duplikację stylu i daje widoczny tytuł strony. | Research |
| Skrót do formularza | Zwykły link `#dodaj-repozytorium` w tytule strony | Łagodzi koszt przesunięcia formularza pod długą listę bez nowego stanu. | Plan |
| Stany C5 | Odroczone | `change.md` wyłącza stany; loading oznaczony N/A w macierzy. | Research |

## Scope

**In scope:** `PageHeading`, `SectionHeading`, `RepositoryList`, przeniesienie `patternCountLabel`, przepięcie `home.tsx`, sekcje w `/styleguide`, macierz stanów ze zrzutami, reguła w `AGENTS.md`.

**Out of scope:** szerokość kontenera (C1), błąd ładowania i pusty stan z wezwaniem do działania (C5), migracja `repo-details`, paleta i tokeny, mobile poza jedną szerokością zrzutu, nowe dane w wierszu, zmiany API, nowe zależności.

## Architecture / Approach

Prezentacyjny `RepositoryList` (z pustym stanem) dostaje dane z `home.tsx` w widoku i z tablic przykładowych w `/styleguide`. `home.tsx` zachowuje loader i akcję. Nagłówki są wspólnymi komponentami. Fazy `/10x-ui` „środowisko” i „wartości tokenów” są puste, bo kontrakt jest zdrowy.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Komponenty i galeria | Nagłówki, lista i ich sekcje w `/styleguide` | Galeria kliknięciem linku wychodzi z `/styleguide` |
| 2. Widok home | Lista przed formularzem, widoczny `h1`, link do formularza | Regres dodawania repozytorium (brak testów UI) |
| 3. Stany i bramka | Macierz 7 stanów, zrzuty desktop i mobile, fokus | Zrzuty tylko na danych przykładowych, nie na prawdziwych |
| 4. Reguła dla agentów | Konwencje w `AGENTS.md` | Rozjazd reguły z kodem |

**Prerequisites:** Node i zależności w `web/` zainstalowane; do sprawdzenia dodawania repozytorium (2.6) potrzebne uruchomione API z LDAP i bazą.
**Estimated effort:** ~2 sesje w 4 fazach.

## Open Risks & Assumptions

- Zarzuty C1 i C2 opierają się na klasach Tailwind, bez pomiaru w przeglądarce; zrzuty z fazy 3 mają to potwierdzić.
- Ostrzeżenie dla 0 wzorców może być głośne dla świeżo dodanych repozytoriów; do oceny na zrzutach.
- Krok 2.6 zależy od Twojego środowiska LDAP i bazy.

## Success Criteria (Summary)

- Po zalogowaniu widać tytuł i listę, a formularz jest pod nią, z działającym skrótem.
- Repozytoria bez aktywnych wzorców są rozpoznawalne od razu po tekście i ikonie.
- Widok składa się ze wspólnych komponentów, a skan twardych wartości daje 0.
