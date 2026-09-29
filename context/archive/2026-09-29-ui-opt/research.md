---
date: 2026-09-29T21:16:47+02:00
researcher: Rafal Chmielorz
git_commit: 7e2f82a7b98a0109b7e7ee906d70efed6f34fb50
branch: feature/ui
repository: M1L1 (securitycheck-portal)
topic: "Audyt widoku listy repozytoriów (home.tsx): hierarchia i układ"
tags: [research, codebase, web, ui, home, layout, hierarchy]
status: partial
last_updated: 2026-09-29
last_updated_by: Rafal Chmielorz
---

# Research: Audyt widoku listy repozytoriów (home.tsx): hierarchia i układ

**Date**: 2026-09-29T21:16:47+02:00
**Researcher**: Rafal Chmielorz
**Git Commit**: 7e2f82a7b98a0109b7e7ee906d70efed6f34fb50
**Branch**: feature/ui
**Repository**: M1L1 (securitycheck-portal)

## Research Question

Które zarzuty (3–5) dotyczące hierarchii i układu ma widok `web/app/routes/home.tsx`, każdy z plikiem, linią i wpływem na użytkownika? Zakres z `change.md`: jeden widok, focus na hierarchię i układ, istniejący system projektowy do rozszerzenia.

## Summary

- **Status: partial.** Audyt wykonany z kodu. Widoku nie renderowałem z danymi: API wymaga LDAP i bazy, a `api/appsettings.Development.json` nie zawiera trybu deweloperskiego bez uwierzytelniania. Zarzuty dotyczące szerokości i proporcji opierają się na klasach Tailwind i nie zostały zmierzone w przeglądarce.
- **Kontrakt jest zdrowy.** Skan twardych wartości na `web/app/routes/*.tsx` i `web/app/root.tsx` dał 0 trafień; `home.tsx` importuje 5 komponentów (`Alert`, `Button`, `Card`, `Field`/`Input`, `cx`/`focusRing`). Zarzutów „brakujące tokeny” nie ma; zarzuty dotyczą układu, brakującego komponentu nagłówka i architektury wejścia.
- **Pięć zarzutów** (sekcja `## Charges`): szerokość treści, priorytet formularza nad listą, płaska hierarchia nagłówków bez współdzielonego komponentu, ubogi wiersz listy, stany błędu/pustki (odroczony).

## Detailed Findings

### Struktura widoku (this inspected file only)
- `main` z `container mx-auto p-4` (`web/app/routes/home.tsx:72`), sr-only `h1` „Panel repozytoriów” (`:73`).
- Karta „Dodaj repozytorium” (`Card className="mb-8"`, `:74`): `h2` (`:75`), dwa `Field` (adres URL, nazwa opcjonalna, `:77-89`), opcjonalny `Alert` (`:90`), przycisk główny „Dodaj” (`:91-93`).
- `h2` „Repozytoria” (`:97`), następnie pusty stan (`:98-101`) albo `ul` z wierszami-linkami (`:103-121`). Wiersz: nazwa (`font-medium text-primary-text`, `:114`), URL (`text-sm text-text-muted`, `:115`), po prawej licznik wzorców (`text-sm text-text-muted`, `:117`).
- Dane wiersza: `RepositorySummary` ma dokładnie pola `id`, `url`, `name`, `activePatternCount` (`web/app/lib/patterns.ts:5-10`); widok nie ma więc innych danych do sygnalizowania stanu.

### Powłoka
- Nagłówek aplikacji używa tego samego `container mx-auto` (`web/app/routes/app-layout.tsx:59`); tego samego kontenera używają `repo-details.tsx:277` i `not-found.tsx:15`. Zmiana szerokości kontenera dotyka więc co najmniej cztery miejsca.

### System projektowy (kontrakt)
- Źródło wartości: `web/app/app.css`; komponenty: `web/app/components/` (`Card` stały `p-6`, `card.tsx:8`; `Button` primary `px-4 py-2`, `button.tsx:8`; `Field` `space-y-1`, `field.tsx:24`). Reguły dla agentów: sekcja „UI Conventions” w `AGENTS.md`; nie zawiera instrukcji zachęcających do jednorazowych wartości (inspected: ta sekcja, 8 punktów).
- Powtórzony styl nagłówka sekcji `text-lg font-semibold` występuje w `home.tsx:75,97`, `repo-details.tsx:311,346` oraz `confirm-dialog.tsx:69`; nie ma komponentu nagłówka sekcji.

## Charges

**C1 — Szerokość treści nie jest ograniczona (układ).**
- Gdzie: `web/app/routes/home.tsx:72` (`container mx-auto`), zależność: `web/app/routes/app-layout.tsx:59`.
- Dowód: klasa `container` w Tailwind v4 ustawia `max-width` wg punktów przełamania (wniosek z definicji Tailwind, niezmierzony w przeglądarce); dwa pola tekstowe w karcie (`:77-89`) wypełniają całą szerokość karty (`inputBase` zawiera `w-full`, `web/app/components/styles.ts:8`).
- Wpływ na użytkownika: na szerokim monitorze pola formularza i wiersze listy rozciągają się na całą szerokość; nazwa repozytorium jest daleko od licznika po prawej stronie wiersza, a oko musi pokonać dużą odległość.
- Fix (do decyzji w planie): ograniczyć szerokość czytania treści widoku (np. wąski kontener dla `main`) i uzgodnić z nagłówkiem, żeby brand i treść były wyrównane.

**C2 — Formularz dodawania waży więcej niż lista (hierarchia).**
- Gdzie: `web/app/routes/home.tsx:74-95` (karta z cieniem i przyciskiem głównym) przed `:97-122` (lista).
- Dowód: karta zawiera dwa pola i jedyny pełny, pomarańczowy przycisk główny na ekranie (`button.tsx:8`); lista jest niżej i ma delikatniejszą ramkę (`border-border-subtle`, `:103`).
- Wpływ na użytkownika: powracający użytkownik przychodzi przede wszystkim po listę; największy wizualnie element to zadanie rzadkie (dodanie), a lista zaczyna się poniżej formularza i przy wielu repozytoriach ląduje poniżej pierwszego ekranu (wniosek z układu pionowego, niezmierzony).
- Fix (do decyzji w planie, realny wybór): lista pierwsza z akcją „Dodaj repozytorium” jako drugorzędną (przycisk otwierający formularz albo formularz pod listą) vs. układ dwukolumnowy na szerokich ekranach vs. zostawić kolejność i zmniejszyć wagę formularza.

**C3 — Płaska hierarchia nagłówków i brak współdzielonego komponentu (brakujący komponent).**
- Gdzie: `web/app/routes/home.tsx:75,97`; ten sam styl w `repo-details.tsx:311,346`.
- Dowód: tytuł karty i tytuł listy mają identyczną klasę `text-lg font-semibold`; widoczny tytuł strony nie istnieje (`h1` jest sr-only, `:73`), a w nagłówku aplikacji jest tylko marka.
- Wpływ na użytkownika: ekran nie mówi, „gdzie jestem”, a dwie sekcje mają tę samą wagę, więc nic nie prowadzi wzroku.
- Fix: komponent nagłówka strony/sekcji (`PageHeader`/`SectionHeading`) w `web/app/components/`, użyty w `home.tsx` i `repo-details.tsx`, dodany do `/styleguide`.

**C4 — Wiersz listy niesie mało informacji i nie da się go „skanować” (hierarchia wiersza).**
- Gdzie: `web/app/routes/home.tsx:106-118`.
- Dowód: licznik to zwykły tekst w kolorze `text-text-muted` (`:117`); dla `activePatternCount` równego 0 kod zwraca „0 aktywnych wzorców” (`patternCountLabel`, `:56-64`, gałąź końcowa), tak samo wyglądający jak wartości dodatnie; całe wiersze są linkami bez znaku (np. strzałki) poza kolorem po najechaniu (`hover:bg-surface-hover`, `:109`). Istnieje komponent `Badge` z rodzajami `neutral`/`warning` (`web/app/components/badge.tsx`), niewykorzystany w tym widoku.
- Wpływ na użytkownika: repozytorium bez aktywnych wzorców (nic nie będzie skanowane) wygląda tak samo jak dobrze skonfigurowane; trzeba otwierać każde, by to sprawdzić.
- Fix: `Badge` z licznikiem, wyróżniający brak aktywnych wzorców (neutral/warning), oraz czytelna afordancja linku; bez nowych danych (tylko `activePatternCount`).

**C5 — Stany wejścia: błąd ładowania listy i pusta lista (architektura wejścia) — ODROCZONY.**
- Gdzie: `web/app/routes/home.tsx:23-29` (loader rzuca `Error` przy odpowiedzi innej niż OK), `:98-101` (pusty stan bez wezwania do działania), `web/app/root.tsx:58` (`ErrorBoundary`).
- Dowód: błąd ładowania trafia do `ErrorBoundary` w root, który nie renderuje nagłówka aplikacji ani akcji ponowienia (inspected: `root.tsx` ErrorBoundary; render nagłówka jest w `app-layout.tsx`, którego ErrorBoundary w root nie obejmuje — wniosek z struktury tras, niezweryfikowany w przeglądarce); pusty stan to sam akapit z ramką przerywaną.
- Wpływ na użytkownika: przy chwilowej awarii API użytkownik widzi ogólną stronę błędu bez ponowienia; przy pustej liście nie ma wskazania, że formularz powyżej jest następnym krokiem.
- Status: **deferred** — zakres z `change.md` to hierarchia i układ; stany (empty/error/loading) zostały świadomie wyłączone.

## Code References

- `web/app/routes/home.tsx:72-123` — cały układ widoku
- `web/app/routes/home.tsx:56-64` — `patternCountLabel`
- `web/app/routes/app-layout.tsx:59` — kontener nagłówka
- `web/app/components/card.tsx:8`, `button.tsx:8`, `field.tsx:24`, `styles.ts:8` — proporcje komponentów
- `web/app/lib/patterns.ts:5-10` — pola `RepositorySummary`
- `web/app/root.tsx:58` — `ErrorBoundary`

## Architecture Insights

- Wszystkie widoki dzielą ten sam `container mx-auto`, więc decyzja o szerokości jest decyzją o powłoce, nie tylko o `home.tsx`.
- Brak komponentów dla nagłówków stron i sekcji jest jedyną luką w komponentach wykrytą w tym widoku; reszta (przycisk, pole, karta, alert, badge) już istnieje.

## Historical Context (from prior changes)

- `context/archive/2026-09-29-in-context/plan.md` — plan systemu projektowego (tokeny, komponenty, migracja `home` i `repo-details`); wyraźnie wykluczał nawigację poza nagłówkiem i ekrany skanu.
- `context/archive/2026-09-29-in-context/reviews/impl-review.md` — F8: sr-only `h1` „Repozytoria” zmieniony na „Panel repozytoriów”; wpływa na C3 (widoczny tytuł strony nadal nie istnieje).

## Related Research

- `context/archive/2026-09-29-in-context/research.md` — badanie wyglądu całej aplikacji (kontekst systemu projektowego).

## Open Questions

- Nie zebrano zrzutu ekranu widoku z danymi (brak uruchomialnego API); proporcje z C1 i C2 warto potwierdzić zrzutem w bramce wizualnej po implementacji.
- Wybór układu w C2 (lista pierwsza / dwie kolumny / zmniejszenie wagi formularza) jest decyzją produktową dla `/10x-plan`.
- Czy zmiana szerokości kontenera (C1) ma obejmować też `repo-details.tsx` i `not-found.tsx` (te same `container`), czy tylko `home.tsx` i nagłówek.
