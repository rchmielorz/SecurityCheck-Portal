# Podstawowy wygląd aplikacji — Implementation Plan

## Overview

Definiujemy wygląd SecurityCheck Portal jako system: własna paleta marki, tokeny semantyczne (w tym ważność podatności i status skanu), tryb ciemny z ręcznym przełącznikiem, wspólne komponenty i konwencje dostępności. Następnie stosujemy go na istniejących ekranach (`web/`), żeby nadchodzące ekrany skanu budowały na gotowych regułach, a nie kopiowały klasy Tailwind.

## Current State Analysis

Wygląd nie został nigdzie zdecydowany; obecny jest efektem ubocznym dwóch zarchiwizowanych zmian (`research.md`). Domyślna paleta Tailwind (gray, `blue-600`, red), OS-owy dark mode, brak tokenów poza `--font-sans` (`web/app/app.css:3-6`), brak katalogu komponentów, style skopiowane między plikami tras (przycisk główny w 3 miejscach, pole tekstowe w 5). Stany dopasowania wzorca są tekstem bez koloru (`web/app/lib/patterns.ts:82-106`). Brak stylów fokusu i breakpointów, usuwanie potwierdza `window.confirm` (`web/app/routes/repo-details.tsx:137-141`).

## Desired End State

- `web/app/app.css` zawiera tokeny marki i semantyczne w wariancie jasnym i ciemnym; `dark` działa przez klasę na `<html>`, a wybór (system / jasny / ciemny) jest zapamiętywany w `localStorage`.
- `web/app/components/` zawiera wspólne komponenty; żadna trasa nie duplikuje klas przycisku, pola, alertu ani karty.
- Wszystkie pary kolorów tokenów spełniają kontrast WCAG AA w obu motywach; każdy element interaktywny ma widoczny fokus.
- Ważność i status skanu mają tokeny i `Badge` z tekstem oraz ikoną (nigdy sam kolor), gotowe dla ekranów skanu.
- Istniejące ekrany (login, home, repo-details, 404, błąd, ładowanie) używają nowego wyglądu; `window.confirm` zastąpiono dialogiem.
- Weryfikacja: `npm run typecheck`, `npm run build` i ręczny przegląd na stronie `/styleguide` (tylko dev) oraz na ekranach w obu motywach.

### Key Discoveries:

- SPA (`ssr: false`) renderuje `Layout` do statycznego `index.html`, więc skrypt w `<head>` może ustawić motyw przed pierwszym renderem (`web/react-router.config.ts:4-7`, `web/app/root.tsx:14-31`).
- Wariant `dark:` Tailwinda opiera się dziś na ustawieniu systemu (`web/app/app.css:8-15`); przełącznik wymaga `@custom-variant dark` opartego na klasie.
- `routes.ts` działa w Node i jest tablicą, więc trasę `/styleguide` można dodać warunkowo tylko poza produkcją (`web/app/routes.ts:9-17`).
- Jedyne współdzielone style to stałe `secondaryButton` i `dangerButton` (`web/app/routes/repo-details.tsx:143-146`); przenosimy je do `Button`.
- Ograniczenia: brak nowych zależności i zewnętrznych zasobów (`context/archive/2026-09-26-authenticated-app-shell/plan.md:30`), UI po polsku bez i18n, URL-e bez kropki w ostatnim segmencie (`AGENTS.md:8`), każda trasa w `web/app/routes.ts` (`AGENTS.md:19`).
- PRD: nieudany skan nie może wyglądać jak czysty wynik (`context/foundation/prd.md:38`); nowe podatności odróżniane od zaakceptowanego ryzyka (`prd.md:87-91`).

## What We're NOT Doing

- Ekranów skanu, tabeli wyników, sortowania i filtrów (osobne zmiany); tylko tokeny i `Badge` pod nie.
- Nawigacji poza nagłówkiem, panelu bocznego, breadcrumbs, modelu „klient”.
- Nowych zależności (biblioteki UI, ikon, fontów, test runnera); ikony jako inline SVG.
- Zmian w `api/`, `core/`, bazie i endpointach.
- Testów automatycznych UI; weryfikacja to typecheck, build i przegląd ręczny.
- i18n, wielu języków, zmian w treści komunikatów poza tym, co wymusza nowy komponent.

## Implementation Approach

Od fundamentu do ekranów, z punktem kontrolnym po fazie 2: tokeny i motyw, potem komponenty razem ze stroną `/styleguide` (użytkownik zatwierdza paletę, zanim ruszy migracja), potem powłoka, potem migracja ekranów, na końcu dokumentacja. Paletę proponuję ja (kierunek: głęboki morski/indygo jako kolor główny, neutralne powierzchnie w odcieniu łupka), a kontrast sprawdzam obliczeniowo i wypisuję na stronie styleguide.

## Faza 1: Tokeny i motyw

### Overview

Fundament: paleta, tokeny semantyczne w dwóch motywach, dark mode oparty na klasie i przełącznik.

### Changes Required:

#### 1. Tokeny i wariant dark

**File**: `web/app/app.css`

**Intent**: Zastąpić domyślną paletę nazwanymi tokenami: skala marki (primary), powierzchnie i obramowania, tekst, ważność (critical/high/medium/low), status (success/warning/danger/info/neutral) oraz „new” i „accepted-risk”. Dark mode ma działać przez klasę, a nie ustawienie systemu.

**Contract**: `@theme` z kolorami jako zmienne CSS, warianty ciemne przez nadpisanie zmiennych pod `.dark`; `@custom-variant dark (&:where(.dark, .dark *));`. Reguła `html, body` używa tokenów powierzchni i `color-scheme` zależnego od klasy.

#### 2. Inicjalizacja motywu przed renderem

**File**: `web/app/root.tsx`

**Intent**: Zapobiec błyskowi złego motywu: krótki inline-skrypt w `<head>` odczytuje wybór z `localStorage` (w try/catch), a przy braku lub wartości „system” używa `prefers-color-scheme` i ustawia klasę `dark` na `<html>`. Body przechodzi na tokeny.

**Contract**: Wartość klucza `theme` ∈ `system | light | dark`; brak wpisu = `system`. Skrypt nie może rzucić błędu przy zablokowanym storage. `<html>` dostaje `suppressHydrationWarning`, jeśli klasa jest ustawiana przed hydracją.

#### 3. Przełącznik motywu

**File**: `web/app/components/theme-toggle.tsx`

**Intent**: Przycisk w nagłówku przełączający system → jasny → ciemny, zapamiętujący wybór i reagujący na zmianę ustawienia systemu, gdy wybrano „system”.

**Contract**: `ThemeToggle` bez propsów; ma czytelną etykietę dla czytników ekranu i aktualny stan; zapis w `localStorage` w try/catch.

### Success Criteria:

#### Automated Verification:

- Typecheck przechodzi: `npm run typecheck` (w `web/`)
- Build przechodzi: `npm run build` (w `web/`)

#### Manual Verification:

- Przełącznik przechodzi system, jasny, ciemny, a wybór zostaje po przeładowaniu strony bez błysku złego motywu
- Bez zapisanego wyboru aplikacja przy ciemnym motywie systemu jest ciemna, a przy jasnym jasna

**Implementation Note**: Po tej fazie i przejściu weryfikacji automatycznej wstrzymaj się na ręczne potwierdzenie przez człowieka, zanim przejdziesz dalej. Odpowiadające pola wyboru są w sekcji `## Progress`.

---

## Faza 2: Komponenty wspólne i styleguide

### Overview

Wspólne komponenty na tokenach, z dostępnością na poziomie AA, oraz strona `/styleguide` do przeglądu palety. Punkt kontrolny: zatwierdzenie palety przez użytkownika.

### Changes Required:

#### 1. Komponenty

**File**: `web/app/components/` (nowy katalog: `button.tsx`, `field.tsx`, `alert.tsx`, `card.tsx`, `badge.tsx`, `confirm-dialog.tsx`)

**Intent**: Zastąpić skopiowane klasy jednym miejscem. `Button` ma warianty primary/secondary/danger, `Field` łączy etykietę, pole i błąd, `Alert` zastępuje pięciokrotnie powtórzony akapit błędu, `Badge` wyraża ważność i status tekstem oraz ikoną, `ConfirmDialog` zastępuje `window.confirm`.

**Contract**:
- `Button`: `variant: "primary" | "secondary" | "danger"`, reszta jak natywny `<button>`.
- `Field`: `label`, `error?`, dziecko-input; ustawia `htmlFor`, `aria-invalid`, `aria-describedby`.
- `Alert`: `role="alert"`; wariant `error | warning | info | success`.
- `Badge`: `kind` ze zbioru ważności (critical/high/medium/low) lub statusu (success/warning/danger/info/neutral/new/accepted-risk); zawsze tekst plus ikona inline SVG.
- `ConfirmDialog`: natywny `<dialog>` z `showModal()`, Esc anuluje, fokus wraca do elementu wywołującego, przyciski Anuluj i Potwierdź (wariant danger).
- Wspólny pierścień fokusu (`focus-visible`) na wszystkich elementach interaktywnych.

#### 2. Strona styleguide (tylko dev)

**File**: `web/app/routes/styleguide.tsx`, `web/app/routes.ts`

**Intent**: Jedna strona pokazująca paletę z współczynnikami kontrastu, typografię, wszystkie komponenty i ich stany w obu motywach, do zatwierdzenia palety i późniejszej regresji wizualnej.

**Contract**: Trasa `styleguide` dodawana do `routes.ts` tylko gdy `process.env.NODE_ENV !== "production"`; bez logowania (nie pokazuje danych). Kontrasty obliczane w kodzie strony z wartości tokenów (bez zależności). Do sprawdzenia w implementacji: czy `react-router build` faktycznie ustawia `NODE_ENV=production`, oraz że w `build/client` nie ma śladu trasy.

### Success Criteria:

#### Automated Verification:

- Typecheck przechodzi: `npm run typecheck`
- Build przechodzi: `npm run build`
- Build produkcyjny nie zawiera trasy styleguide: wyszukanie `styleguide` w `web/build/client` nie zwraca trafień

#### Manual Verification:

- `/styleguide` (dev) pokazuje paletę i wszystkie komponenty w jasnym i ciemnym motywie
- Paleta została przedstawiona z kontrastami AA i zatwierdzona przez użytkownika
- Każdy element interaktywny ma widoczny fokus przy nawigacji klawiaturą
- `ConfirmDialog` zamyka się Esc, a fokus wraca do przycisku wywołującego

**Implementation Note**: Ta faza kończy się punktem kontrolnym: nie przechodź do fazy 3 bez akceptacji palety przez użytkownika.

---

## Faza 3: Powłoka i ekrany bazowe

### Overview

Nagłówek, login, ekrany awaryjne i tożsamość marki.

### Changes Required:

#### 1. Nagłówek i marka

**File**: `web/app/routes/app-layout.tsx`, `web/app/components/brand-mark.tsx`

**Intent**: Nagłówek z znakiem wektorowym i nazwą jako link do strony głównej, przełącznikiem motywu, nazwą użytkownika i wylogowaniem na wspólnych komponentach; zawijanie na wąskich ekranach.

**Contract**: `BrandMark` to inline SVG bez zależności; kontener nagłówka dostaje `flex-wrap`; logika wylogowania bez zmian.

#### 2. Login, ładowanie, błąd, 404

**File**: `web/app/routes/login.tsx`, `web/app/root.tsx`, `web/app/routes/not-found.tsx`

**Intent**: Użyć `Card`, `Field`, `Button`, `Alert`; `ErrorBoundary` i 404 mają spójny wygląd oraz link powrotu na stronę główną; `HydrateFallback` i `ErrorBoundary` dostają tytuł strony.

**Contract**: Komunikaty i logika loginu bez zmian; login nadal nie pokazuje żadnych danych.

#### 3. Favicon

**File**: `web/public/favicon.ico` (lub `favicon.svg` z odpowiednim `<link>` w `root.tsx`)

**Intent**: Zastąpić domyślną ikonę szablonu znakiem marki.

**Contract**: Ikona w `web/public`, odnośnik w `links` w `root.tsx` jeśli format inny niż ico.

### Success Criteria:

#### Automated Verification:

- Typecheck przechodzi: `npm run typecheck`
- Build przechodzi: `npm run build`

#### Manual Verification:

- Nagłówek pokazuje znak i nazwę jako link do strony głównej oraz przełącznik motywu, a na wąskim ekranie nie przepełnia się
- Login, ekran ładowania, błąd i 404 mają nowy wygląd w obu motywach, a 404 i błąd mają link powrotu
- Karta przeglądarki pokazuje nowy favicon

**Implementation Note**: Po tej fazie wstrzymaj się na ręczne potwierdzenie.

---

## Faza 4: Migracja ekranów

### Overview

Przenieść `home` i `repo-details` na wspólne komponenty i naprawić braki dostępności.

### Changes Required:

#### 1. Home

**File**: `web/app/routes/home.tsx`

**Intent**: Karta „Dodaj repozytorium” i lista repozytoriów na `Card`, `Field`, `Button`, `Alert`; usunąć zdublowany nagłówek marki.

**Contract**: Zachowanie i teksty bez zmian; formularz nadal `noValidate`, błąd przez `Field`/`Alert`.

#### 2. Szczegóły repozytorium

**File**: `web/app/routes/repo-details.tsx`, `web/app/lib/patterns.ts`

**Intent**: Wzorce wersji, formularz dodawania i historia na wspólnych komponentach. Stany dopasowania (Resolved / NoMatch / Ambiguous / Error) jako `Badge` z tekstem i ikoną (error i ambiguous odróżnione od poprawnego wyniku). Usunięcie przez `ConfirmDialog` zamiast `window.confirm`. `aria-live` dla „Sprawdzanie…”, a SHA dostępny nie tylko przez `title`.

**Contract**: Mapowanie stanów dopasowania na `Badge kind` żyje obok `describeResolution` w `web/app/lib/patterns.ts`; stałe `secondaryButton` i `dangerButton` znikają na rzecz `Button`.

### Success Criteria:

#### Automated Verification:

- Typecheck przechodzi: `npm run typecheck`
- Build przechodzi: `npm run build`
- Brak `window.confirm` w kodzie: wyszukanie `window.confirm` w `web/app` nie zwraca trafień
- Brak skopiowanego stylu przycisku głównego poza komponentami: wyszukanie `bg-blue-600` w `web/app/routes` nie zwraca trafień

#### Manual Verification:

- Dodanie i lista repozytoriów działają jak dotąd, w obu motywach
- Dodanie, sprawdzenie, dezaktywacja i usunięcie wzorca działają, a usunięcie pyta w oknie dialogowym (Esc anuluje)
- Każdy stan dopasowania jest rozpoznawalny po tekście i ikonie, nie tylko po kolorze
- Błędy formularzy są powiązane z polami (`aria-invalid`, `aria-describedby`) i ogłaszane

**Implementation Note**: Po tej fazie wstrzymaj się na ręczne potwierdzenie.

---

## Faza 5: Dokumentacja

### Overview

Zapisać konwencje, żeby kolejni agenci i zmiany ich trzymali.

### Changes Required:

#### 1. Konwencje UI

**File**: `AGENTS.md`, `web/README.md`

**Intent**: Dodać do `AGENTS.md` sekcję o UI (tokeny w `app.css`, komponenty w `web/app/components/`, zakaz kopiowania klas, dark mode przez klasę, kontrast AA, badge z tekstem, styleguide tylko dev, UI po polsku). Zastąpić nieaktualny tekst szablonu w `web/README.md` (SSR, Docker) opisem SPA i tych konwencji.

**Contract**: Sekcja „UI” w `AGENTS.md` w obecnym stylu dokumentu; `web/README.md` opisuje komendy `dev`, `build`, `build:api`, `typecheck` i wskazuje `/styleguide`.

### Success Criteria:

#### Automated Verification:

- Typecheck przechodzi: `npm run typecheck`
- `AGENTS.md` zawiera sekcję UI: wyszukanie `styleguide` w `AGENTS.md` zwraca trafienie

#### Manual Verification:

- `AGENTS.md` i `web/README.md` są zgodne z rzeczywistym stanem kodu i nie zawierają starego tekstu szablonu

**Implementation Note**: Po tej fazie zmiana jest gotowa do przeglądu implementacji.

---

## Testing Strategy

### Unit Tests:

- Brak runnera UI (`AGENTS.md:40`); nie dodajemy go w tej zmianie.

### Integration Tests:

- Istniejące testy backendu (`api.Tests`) nie są dotknięte; UI nie zmienia API.

### Manual Testing Steps:

1. `npm run dev` w `web/`, otworzyć `/styleguide` i przejrzeć paletę oraz komponenty w obu motywach.
2. Przełączyć motyw i przeładować stronę; sprawdzić brak błysku.
3. Przejść login, home, szczegóły repozytorium, 404 w obu motywach, samą klawiaturą.
4. `npm run build` i sprawdzić, że `build/client` nie zawiera trasy styleguide.

## Performance Considerations

Brak nowych zależności ani zasobów zewnętrznych; ikony inline SVG. Skrypt motywu ma kilka linijek i działa synchronicznie w `<head>`.

## Migration Notes

Brak danych do migracji. Wartość `theme` w `localStorage` jest nowa; brak wpisu oznacza „system”, czyli zachowanie sprzed zmiany.

## References

- Related research: `context/changes/in-context/research.md`
- Obecne style współdzielone: `web/app/routes/repo-details.tsx:143-146`
- Tokeny i dark mode dziś: `web/app/app.css:1-15`
- Wymagania produktowe dla ważności i zaufania do skanu: `context/foundation/prd.md:38,74,87-91`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Tokeny i motyw

#### Automated

- [x] 1.1 Typecheck przechodzi: `npm run typecheck` (w `web/`) — b997169
- [x] 1.2 Build przechodzi: `npm run build` (w `web/`) — b997169

#### Manual

- [ ] 1.3 Przełącznik przechodzi system, jasny, ciemny, a wybór zostaje po przeładowaniu strony bez błysku złego motywu
- [ ] 1.4 Bez zapisanego wyboru aplikacja przy ciemnym motywie systemu jest ciemna, a przy jasnym jasna

### Phase 2: Komponenty wspólne i styleguide

#### Automated

- [x] 2.1 Typecheck przechodzi: `npm run typecheck`
- [x] 2.2 Build przechodzi: `npm run build`
- [x] 2.3 Build produkcyjny nie zawiera trasy styleguide: wyszukanie `styleguide` w `web/build/client` nie zwraca trafień

#### Manual

- [ ] 2.4 `/styleguide` (dev) pokazuje paletę i wszystkie komponenty w jasnym i ciemnym motywie
- [ ] 2.5 Paleta została przedstawiona z kontrastami AA i zatwierdzona przez użytkownika
- [ ] 2.6 Każdy element interaktywny ma widoczny fokus przy nawigacji klawiaturą
- [ ] 2.7 `ConfirmDialog` zamyka się Esc, a fokus wraca do przycisku wywołującego

### Phase 3: Powłoka i ekrany bazowe

#### Automated

- [ ] 3.1 Typecheck przechodzi: `npm run typecheck`
- [ ] 3.2 Build przechodzi: `npm run build`

#### Manual

- [ ] 3.3 Nagłówek pokazuje znak i nazwę jako link do strony głównej oraz przełącznik motywu, a na wąskim ekranie nie przepełnia się
- [ ] 3.4 Login, ekran ładowania, błąd i 404 mają nowy wygląd w obu motywach, a 404 i błąd mają link powrotu
- [ ] 3.5 Karta przeglądarki pokazuje nowy favicon

### Phase 4: Migracja ekranów

#### Automated

- [ ] 4.1 Typecheck przechodzi: `npm run typecheck`
- [ ] 4.2 Build przechodzi: `npm run build`
- [ ] 4.3 Brak `window.confirm` w kodzie: wyszukanie `window.confirm` w `web/app` nie zwraca trafień
- [ ] 4.4 Brak skopiowanego stylu przycisku głównego poza komponentami: wyszukanie `bg-blue-600` w `web/app/routes` nie zwraca trafień

#### Manual

- [ ] 4.5 Dodanie i lista repozytoriów działają jak dotąd, w obu motywach
- [ ] 4.6 Dodanie, sprawdzenie, dezaktywacja i usunięcie wzorca działają, a usunięcie pyta w oknie dialogowym (Esc anuluje)
- [ ] 4.7 Każdy stan dopasowania jest rozpoznawalny po tekście i ikonie, nie tylko po kolorze
- [ ] 4.8 Błędy formularzy są powiązane z polami (`aria-invalid`, `aria-describedby`) i ogłaszane

### Phase 5: Dokumentacja

#### Automated

- [ ] 5.1 Typecheck przechodzi: `npm run typecheck`
- [ ] 5.2 `AGENTS.md` zawiera sekcję UI: wyszukanie `styleguide` w `AGENTS.md` zwraca trafienie

#### Manual

- [ ] 5.3 `AGENTS.md` i `web/README.md` są zgodne z rzeczywistym stanem kodu i nie zawierają starego tekstu szablonu
