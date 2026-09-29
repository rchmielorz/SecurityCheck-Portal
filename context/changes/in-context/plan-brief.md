# Podstawowy wygląd aplikacji — Plan Brief

> Full plan: `context/changes/in-context/plan.md`
> Research: `context/changes/in-context/research.md`

## What & Why

Definiujemy wygląd SecurityCheck Portal jako system (paleta marki, tokeny, komponenty, dark mode, dostępność) i stosujemy go na istniejących ekranach. Dziś wygląd jest przypadkowym efektem dwóch wcześniejszych zmian, a ekrany skanu, które wymagają kolorów ważności i jednoznacznych stanów, są następne w kolejce.

## Starting Point

Tailwind 4 z domyślną paletą (`blue-600`, gray), dark mode zależny od systemu, jedna zmienna tematu (czcionka), brak wspólnych komponentów; te same klasy są skopiowane w kilku trasach. Stany dopasowania wzorca to tekst bez koloru, brakuje stylów fokusu, a usuwanie używa `window.confirm`.

## Desired End State

Aplikacja ma własną paletę i tokeny w jasnym i ciemnym wariancie, przełącznik motywu (system/jasny/ciemny) z zapamiętanym wyborem oraz wspólne komponenty w `web/app/components/`. Ważność i status mają `Badge` z tekstem i ikoną. Wszystkie obecne ekrany używają nowego wyglądu, a `/styleguide` (tylko dev) pokazuje całość do przeglądu.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Zakres | Zdefiniować i zmigrować istniejące ekrany | Wygląd ma być realny i sprawdzalny, a duplikaty znikają | Plan |
| Tożsamość | Własna paleta marki, proponuje ją agent, zatwierdza użytkownik | Brak wytycznych firmowych i nie chcemy zamrażać domyślnego niebieskiego | Plan |
| Ważność i status | Tokeny semantyczne plus `Badge` z tekstem i ikoną | PRD wymaga, by nieudany skan nie wyglądał jak czysty wynik, i by kolor nie był jedynym sygnałem | Research + Plan |
| Dark mode | Domyślnie system, ręczny przełącznik z `localStorage` | Kontrola użytkownika bez zmiany domyślnego zachowania | Plan |
| Dostępność | Zestaw WCAG AA: kontrast, fokus, `aria-*` w formularzach, dialog zamiast `window.confirm` | Konkretne, sprawdzalne luki z researchu | Research + Plan |
| Weryfikacja | Typecheck, build i strona `/styleguide` tylko w dev | Brak runnera UI i brak nowych zależności | Plan |
| Zależności | Żadnych nowych; ikony jako inline SVG | Ograniczenie infrastruktury i terminu | Research |

## Scope

**In scope:** tokeny i motyw, przełącznik, komponenty wspólne, strona styleguide, nagłówek i znak marki, login/404/błąd/ładowanie, favicon, migracja home i repo-details, dokumentacja UI.

**Out of scope:** ekrany skanu i tabela wyników, nawigacja poza nagłówkiem, model „klient”, testy UI, zmiany w API i bazie, nowe zależności, i18n.

## Architecture / Approach

Tokeny w `web/app/app.css` (`@theme` plus nadpisania pod `.dark`, wariant `dark` przez klasę), skrypt w `<head>` ustawia motyw przed renderem (SPA renderuje `Layout` do statycznego `index.html`), a komponenty w `web/app/components/` zastępują skopiowane klasy. Trasa `/styleguide` jest dodawana w `routes.ts` tylko poza produkcją. Po fazie 2 jest punkt kontrolny: użytkownik zatwierdza paletę.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Tokeny i motyw | Paleta, tokeny semantyczne, dark przez klasę, przełącznik | Błysk złego motywu przy starcie |
| 2. Komponenty i styleguide | Wspólne komponenty AA, dialog, strona styleguide, zatwierdzenie palety | Trasa dev-only mogłaby trafić do buildu produkcyjnego |
| 3. Powłoka i ekrany bazowe | Nagłówek z marką, login, błąd/404/ładowanie, favicon | Nagłówek na wąskich ekranach |
| 4. Migracja ekranów | Home i repo-details na komponentach, badge stanów, dialog usuwania | Regresje w przepływach bez testów UI |
| 5. Dokumentacja | Konwencje UI w `AGENTS.md`, poprawiony `web/README.md` | Rozjazd dokumentacji z kodem |

**Prerequisites:** Node i zależności w `web/` zainstalowane; Twoja akceptacja palety po fazie 2.
**Estimated effort:** ~3-4 sesje w 5 fazach.

## Open Risks & Assumptions

- Zakładamy, że `react-router build` ustawia `NODE_ENV=production`; faza 2 sprawdza to, szukając `styleguide` w `build/client`.
- Paleta jest propozycją agenta; jeśli firma ma własne barwy, trzeba je podać przed fazą 2.
- Brak runnera UI, więc regresje wychwytuje głównie przegląd ręczny.

## Success Criteria (Summary)

- Wszystkie ekrany wyglądają spójnie w obu motywach, a wybór motywu jest zapamiętany.
- Kontrast AA i widoczny fokus wszędzie; stany wyrażone tekstem i ikoną, nie tylko kolorem.
- Ekrany skanu mogą używać gotowych tokenów i `Badge` bez własnych reguł wyglądu.
