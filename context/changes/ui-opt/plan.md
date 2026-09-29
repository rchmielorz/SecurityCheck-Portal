# Poprawki graficzne: hierarchia i układ listy repozytoriów Implementation Plan

## Overview

Widok listy repozytoriów (`web/app/routes/home.tsx`) dostaje czytelną hierarchię: widoczny tytuł strony, lista jako główna treść, formularz dodawania jako akcja drugorzędna pod listą oraz wiersze, które pokazują stan (liczba aktywnych wzorców) i mają znak linku. Wszystko na istniejącym systemie projektowym (tokeny i komponenty z zarchiwizowanej zmiany `in-context`), bez nowych zależności.

## Current State Analysis

Audyt z `research.md` (status `partial`, bez zrzutu z danymi, bo API wymaga LDAP i bazy). Kontrakt jest zdrowy: skan twardych wartości daje 0 trafień, `home.tsx` używa komponentów i tokenów. Zarzuty dotyczą układu:

- Karta „Dodaj repozytorium” z jedynym pomarańczowym przyciskiem stoi nad listą (`home.tsx:74-95`), a lista zaczyna się poniżej (`:97-122`).
- Tytuł karty i tytuł listy mają ten sam styl `text-lg font-semibold` (`home.tsx:75,97`), widocznego tytułu strony nie ma (`h1` jest sr-only, `:73`), a komponentu nagłówka nie ma (ten sam styl w `repo-details.tsx:311,346`).
- Wiersz listy ma licznik jako zwykły szary tekst (`:117`); repozytorium z 0 aktywnych wzorców wygląda jak skonfigurowane (`patternCountLabel`, `:56-64`). `RepositorySummary` ma tylko pola `id`, `url`, `name`, `activePatternCount` (`web/app/lib/patterns.ts:5-10`).
- Kontener `container mx-auto` jest wspólny dla nagłówka i trzech widoków (`app-layout.tsx:59`, `home.tsx:72`, `repo-details.tsx:277`, `not-found.tsx:15`).

## Desired End State

- Ekran po zalogowaniu pokazuje widoczny tytuł „Repozytoria”, od razu listę repozytoriów, a pod nią kartę „Dodaj repozytorium”. Z tytułu prowadzi link „Dodaj repozytorium” przewijający do formularza.
- Każdy wiersz ma nazwę, adres, `Badge` z liczbą aktywnych wzorców (ostrzegawczy „Brak aktywnych wzorców” dla 0) i strzałkę jako znak linku.
- Widok jest złożony ze współdzielonych komponentów (`PageHeading`, `SectionHeading`, `RepositoryList`); `/styleguide` (dev) pokazuje je z danymi przykładowymi w obu motywach, co jest bramką wizualną bez API.
- `AGENTS.md` mówi, których komponentów używać do nagłówków i list repozytoriów, i podaje komendę skanu twardych wartości.

### Key Discoveries:

- Wszystkie potrzebne tokeny i komponenty istnieją: `Card`, `Button`, `Field`/`Input`, `Alert`, `Badge` z rodzajami `neutral` i `warning` (`web/app/components/badge.tsx`), `focusRing`, `linkClass`, `cx` (`web/app/components/styles.ts`).
- `/styleguide` ma komponent `Section` z opcjonalnym `id` i galerię komponentów (`web/app/routes/styleguide.tsx:107,242`); jest rejestrowany tylko poza produkcją, a build ma skrypt wykrywający wyciek (`web/scripts/copy-to-api.mjs`).
- Loader i akcja `home.tsx` (`:23-54`) nie wymagają zmian; zmienia się tylko prezentacja.
- Ograniczenia z `AGENTS.md`: UI po polsku i na sztywno, czcionka systemowa, brak nowych zależności i zewnętrznych zasobów, kontrast AA dla każdej nowej pary tokenów.

## What We're NOT Doing

- **C1, szerokość kontenera:** bez zmian (decyzja użytkownika); `container mx-auto` w powłoce i widokach zostaje.
- **C5, stany wejścia:** błąd ładowania listy (ogólny `ErrorBoundary`), pusty stan z wezwaniem do działania i stan ładowania są odroczone; teksty pustego stanu zostają.
- `repo-details.tsx` i inne widoki nie są migrowane na nowe nagłówki (jeden widok na zmianę); styl `h2` tam pozostaje do osobnej zmiany.
- Mobile poza jedną szerokością zrzutu, paleta i tokeny (bez zmian w `app.css`), nowe zależności, zmiany w API i w danych.
- Nowe pola danych w wierszu (np. czas ostatniego sprawdzenia); widok ma tylko `activePatternCount`.

## Implementation Approach

Od komponentów do widoku, z bramką na danych przykładowych. Najpierw nowe komponenty i ich galeria w `/styleguide` (tam można obejrzeć układ bez API), potem przepięcie `home.tsx` bez zmiany logiki, potem macierz stanów ze zrzutami i skanem, na końcu reguła dla kolejnych agentów. Fazy „środowisko/biblioteka” i „wartości tokenów” z `/10x-ui` są tu puste, bo audyt nie wykazał braków w kontrakcie.

## Faza 1: Komponenty i galeria

### Overview

Nowe komponenty na istniejących tokenach oraz ich sekcje w `/styleguide` z danymi przykładowymi.

### Changes Required:

#### 1. Nagłówki

**File**: `web/app/components/headings.tsx`

**Intent**: Jedno miejsce dla stylu nagłówka strony (`h1`) i sekcji (`h2`), żeby widoki nie kopiowały klas `text-2xl` i `text-lg font-semibold`. Nagłówek strony ma miejsce na akcję po prawej.

**Contract**: `PageHeading({ children, action? })` renderuje `h1` (`text-2xl font-semibold`) w wierszu z `action` (`flex flex-wrap items-center justify-between gap-2`). `SectionHeading({ children, id? })` renderuje `h2` (`text-lg font-semibold`). Tylko tokeny i `cx`.

#### 2. Lista repozytoriów

**File**: `web/app/components/repository-list.tsx`, `web/app/lib/patterns.ts`

**Intent**: Prezentacyjny komponent listy (bez pobierania danych), który zawiera również pusty stan. Wiersz to link z nazwą, adresem, `Badge` z liczbą wzorców i strzałką. `patternCountLabel` przenosi się z `home.tsx` do `patterns.ts` (bez zmiany zachowania), bo potrzebują go komponent i widok.

**Contract**: `RepositoryList({ repositories: RepositorySummary[] })`. Dla `[]` renderuje istniejący pusty blok (dashed border, ten sam tekst „Nie dodano jeszcze żadnego repozytorium.”). Wiersz: `Link` do `/repos/${id}` z `focusRing`, `hover:bg-surface-hover`, nazwa w `text-primary-text`, adres muted `break-all`. Licznik: `activePatternCount > 0` → `<Badge kind="neutral">{patternCountLabel(n)}</Badge>`; `0` → `<Badge kind="warning">Brak aktywnych wzorców</Badge>`. Strzałka: inline SVG `aria-hidden`, `text-text-subtle`, bez nowych zależności. Wiersz zawija się na wąskim ekranie (`flex-wrap`).

#### 3. Galeria w styleguide

**File**: `web/app/routes/styleguide.tsx`

**Intent**: Sekcje z nagłówkami i listą repozytoriów na danych przykładowych, żeby dało się ocenić układ bez API.

**Contract**: Sekcja „Nagłówki” (PageHeading z akcją i SectionHeading) oraz „Lista repozytoriów” z przypadkami: brak, jedno, kilka z różnymi liczbami (w tym 0), długie nazwy i adresy. Kontener sekcji blokuje nawigację linków (`onClickCapture` z `preventDefault` dla `a`), żeby kliknięcie nie wychodziło z galerii.

### Success Criteria:

#### Automated Verification:

- Typecheck przechodzi: `npm run typecheck` (w `web/`)
- Build przechodzi: `npm run build` (w `web/`)
- Build nie zawiera trasy styleguide: wyszukanie `styleguide` w `web/build/client` nie zwraca trafień

#### Manual Verification:

- `/styleguide` (dev) pokazuje nagłówki i listę repozytoriów w wariantach (brak, jedno, kilka, długie nazwy i adresy) w obu motywach
- Wiersz z 0 aktywnych wzorców ma ostrzegawczy `Badge` z tekstem i ikoną, odróżnialny od wierszy z wzorcami

**Implementation Note**: Po tej fazie i przejściu weryfikacji automatycznej wstrzymaj się na ręczne potwierdzenie przez człowieka, zanim przejdziesz dalej. Odpowiadające pola wyboru są w sekcji `## Progress`.

---

## Faza 2: Widok home

### Overview

`home.tsx` składa ekran z nowych komponentów: lista przed formularzem, widoczny tytuł, link do formularza.

### Changes Required:

#### 1. Przepięcie widoku

**File**: `web/app/routes/home.tsx`

**Intent**: Zmiana kolejności i użycie komponentów; loader, akcja, komunikaty błędów i pola formularza zostają bez zmian.

**Contract**: `main` zawiera kolejno `PageHeading` „Repozytoria” z akcją (zwykły `<a href="#dodaj-repozytorium">` w stylu `linkClass`, tekst „Dodaj repozytorium”), `RepositoryList repositories={repositories}` i `Card id="dodaj-repozytorium"` z `SectionHeading` „Dodaj repozytorium” oraz dotychczasowym formularzem (`Field`/`Input`, `Alert`, `Button`, `noValidate`). Sr-only `h1` i lokalna funkcja `patternCountLabel` znikają z pliku. Odstępy między blokami przez `space-y-*` lub istniejące klasy, bez nowych stałych wartości.

### Success Criteria:

#### Automated Verification:

- Typecheck przechodzi: `npm run typecheck`
- Build przechodzi: `npm run build`
- Brak twardych wartości w plikach zmiany: `grep -cE '#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(|oklch\(|-\[[0-9.]+(px|rem)\]|\b(bg|text|border|ring|outline|from|via|to|fill|stroke|shadow|divide)-(slate|gray|zinc|neutral|stone|red|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose|white|black)\b'` na `home.tsx`, `headings.tsx` i `repository-list.tsx` daje 0 w każdym pliku
- Brak ukrytego nagłówka w widoku: wyszukanie `sr-only` w `web/app/routes/home.tsx` nie zwraca trafień

#### Manual Verification:

- Po zalogowaniu widać tytuł „Repozytoria”, listę, a pod nią kartę „Dodaj repozytorium”
- Dodanie repozytorium działa jak dotąd (przekierowanie do szczegółów, komunikat błędu przy złym adresie i duplikacie)
- Link „Dodaj repozytorium” przewija do formularza

**Implementation Note**: Po tej fazie wstrzymaj się na ręczne potwierdzenie przez człowieka.

---

## Faza 3: Stany i bramka wizualna

### Overview

Macierz siedmiu stanów dla widoku, zrzuty na danych przykładowych i kontrola dostępności.

### Changes Required:

#### 1. Macierz stanów i zrzuty

**File**: `web/app/routes/styleguide.tsx` (dopracowanie sekcji z fazy 1) oraz uzupełnienia `RepositoryList` / `home.tsx`, jeśli przegląd wykaże braki

**Intent**: Każda komórka macierzy jest pokazana albo oznaczona N/A z uzasadnieniem, a wynik oceniony na zrzutach.

**Contract**: default (lista z wieloma wierszami), hover (`hover:bg-surface-hover` na wierszu), focus-visible (pierścień na wierszu, linku-kotwicy, polach i przycisku), disabled (przycisk „Dodaj” w trakcie zapisu, `disabled:opacity-60`), error (`Alert` w formularzu, pokazany istniejącą galerią `Alert`), empty (lista pusta w galerii). **loading: N/A**, bo ładowanie listy obsługuje route-level `HydrateFallback`, a stan ładowania jest odroczony (C5). Zrzuty `/styleguide`: desktop i jedna szerokość mobilna, w obu motywach, wykonane w podglądzie przeglądarki.

### Success Criteria:

#### Automated Verification:

- Typecheck przechodzi: `npm run typecheck`
- Build przechodzi: `npm run build`
- Brak twardych wartości w plikach zmiany: skan z fazy 2 na `home.tsx`, `headings.tsx`, `repository-list.tsx` daje 0 w każdym pliku

#### Manual Verification:

- Zrzuty `/styleguide` (desktop i jedna szerokość mobilna) w jasnym motywie przejrzane i zaakceptowane
- Zrzuty w ciemnym motywie przejrzane i zaakceptowane
- Nawigacja klawiaturą: fokus jest widoczny na wierszu listy, linku „Dodaj repozytorium”, polach i przycisku „Dodaj”
- Macierz stanów: hover, focus-visible, disabled, error i empty pokazane, loading oznaczone N/A z uzasadnieniem

**Implementation Note**: Po tej fazie wstrzymaj się na ręczne potwierdzenie przez człowieka.

---

## Faza 4: Reguła dla agentów

### Overview

Zapis konwencji, żeby kolejne widoki używały nowych komponentów i nie kopiowały klas.

### Changes Required:

#### 1. Konwencje UI

**File**: `AGENTS.md`

**Intent**: Rozszerzyć istniejącą sekcję „UI Conventions” (bez nowej sekcji i bez duplikowania reguł) o nagłówki i listę repozytoriów oraz komendę skanu.

**Contract**: Dopisać: tytuły stron i sekcji przez `PageHeading` / `SectionHeading` (nie ręczne `text-2xl` / `text-lg font-semibold`); listę repozytoriów przez `RepositoryList`; skan twardych wartości (komenda z fazy 2) uruchamiany na plikach widoku po każdej zmianie wizualnej; galeria stanów widoku w `/styleguide`. Styl i język zgodne z sekcją (angielski).

### Success Criteria:

#### Automated Verification:

- Typecheck przechodzi: `npm run typecheck`
- `AGENTS.md` wskazuje nowe komponenty: wyszukanie `PageHeading` w `AGENTS.md` zwraca trafienie

#### Manual Verification:

- Reguła w `AGENTS.md` jest zgodna z rzeczywistym stanem kodu

**Implementation Note**: Po tej fazie zmiana jest gotowa do przeglądu implementacji.

---

## Testing Strategy

### Unit Tests:

- Brak runnera UI (`AGENTS.md`, sekcja Testing); nie dodajemy go.

### Integration Tests:

- Testy backendu (`api.Tests`) nie są dotknięte; API bez zmian.

### Manual Testing Steps:

1. `npm run dev` w `web/`, otworzyć `/styleguide` i przejrzeć sekcje „Nagłówki” i „Lista repozytoriów” w obu motywach.
2. Sprawdzić fokus klawiaturą (Tab) na wierszu listy, linku „Dodaj repozytorium”, polach i przycisku.
3. Z uruchomionym API i zalogowanym użytkownikiem: dodać repozytorium poprawne, ze złym adresem i duplikat.
4. `npm run build` i sprawdzić, że `build/client` nie zawiera trasy styleguide.

## Performance Considerations

Brak nowych zależności ani zasobów; strzałka i ikony to inline SVG. Zmiana jest czysto prezentacyjna.

## Migration Notes

Brak danych do migracji. `patternCountLabel` zmienia lokalizację (z `home.tsx` do `web/app/lib/patterns.ts`) bez zmiany zachowania.

## Addendum (po implementacji)

- **Fokus po dodaniu repozytorium (poza pierwotnym zakresem, na prośbę użytkownika):** po dodaniu repozytorium `home.tsx` przekierowuje na `/repos/{id}?nowy=1`, a `repo-details.tsx` ustawia wtedy fokus w polu „Nowy wzorzec” i usuwa parametr z adresu. Zwykłe wejście, odświeżenie i przełączanie „Pokaż nieaktywne” nie ustawiają fokusu. Zmiana dotyka `repo-details.tsx`, mimo że plan obejmuje jeden widok.

## References

- Related research: `context/changes/ui-opt/research.md`
- Poprzednia zmiana (system projektowy): `context/archive/2026-09-29-in-context/plan.md`
- Widok: `web/app/routes/home.tsx:56-123`
- Komponenty i tokeny: `web/app/components/`, `web/app/app.css`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Komponenty i galeria

#### Automated

- [x] 1.1 Typecheck przechodzi: `npm run typecheck` (w `web/`) — ba7793d
- [x] 1.2 Build przechodzi: `npm run build` (w `web/`) — ba7793d
- [x] 1.3 Build nie zawiera trasy styleguide: wyszukanie `styleguide` w `web/build/client` nie zwraca trafień — ba7793d

#### Manual

- [x] 1.4 `/styleguide` (dev) pokazuje nagłówki i listę repozytoriów w wariantach (brak, jedno, kilka, długie nazwy i adresy) w obu motywach — ba7793d
- [x] 1.5 Wiersz z 0 aktywnych wzorców ma ostrzegawczy `Badge` z tekstem i ikoną, odróżnialny od wierszy z wzorcami — ba7793d

### Phase 2: Widok home

#### Automated

- [x] 2.1 Typecheck przechodzi: `npm run typecheck` — 33a5185
- [x] 2.2 Build przechodzi: `npm run build` — 33a5185
- [x] 2.3 Brak twardych wartości w plikach zmiany: skan na `home.tsx`, `headings.tsx` i `repository-list.tsx` daje 0 w każdym pliku — 33a5185
- [x] 2.4 Brak ukrytego nagłówka w widoku: wyszukanie `sr-only` w `web/app/routes/home.tsx` nie zwraca trafień — 33a5185

#### Manual

- [x] 2.5 Po zalogowaniu widać tytuł „Repozytoria”, listę, a pod nią kartę „Dodaj repozytorium” — 33a5185
- [x] 2.6 Dodanie repozytorium działa jak dotąd (przekierowanie do szczegółów, komunikat błędu przy złym adresie i duplikacie) — 33a5185
- [x] 2.7 Link „Dodaj repozytorium” przewija do formularza — 33a5185

### Phase 3: Stany i bramka wizualna

#### Automated

- [x] 3.1 Typecheck przechodzi: `npm run typecheck`
- [x] 3.2 Build przechodzi: `npm run build`
- [x] 3.3 Brak twardych wartości w plikach zmiany: skan na `home.tsx`, `headings.tsx`, `repository-list.tsx` daje 0 w każdym pliku

#### Manual

- [x] 3.4 Zrzuty `/styleguide` (desktop i jedna szerokość mobilna) w jasnym motywie przejrzane i zaakceptowane
- [x] 3.5 Zrzuty w ciemnym motywie przejrzane i zaakceptowane
- [x] 3.6 Nawigacja klawiaturą: fokus jest widoczny na wierszu listy, linku „Dodaj repozytorium”, polach i przycisku „Dodaj”
- [x] 3.7 Macierz stanów: hover, focus-visible, disabled, error i empty pokazane, loading oznaczone N/A z uzasadnieniem

### Phase 4: Reguła dla agentów

#### Automated

- [ ] 4.1 Typecheck przechodzi: `npm run typecheck`
- [ ] 4.2 `AGENTS.md` wskazuje nowe komponenty: wyszukanie `PageHeading` w `AGENTS.md` zwraca trafienie

#### Manual

- [ ] 4.3 Reguła w `AGENTS.md` jest zgodna z rzeczywistym stanem kodu
