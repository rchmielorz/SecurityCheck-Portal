---
change_id: ui-opt
title: Poprawki graficzne
status: implementing
created: 2026-09-29
updated: 2026-09-29
archived_at: null
---

## Notes

Poprawki graficzne

### Zakres (ustalony w /10x-ui)

- **Widok:** lista repozytoriów, `web/app/routes/home.tsx` (ekran po zalogowaniu). Jeden widok na tę zmianę.
- **Focus zmiany:** hierarchia i układ (waga elementów, odstępy, gęstość informacji, czytelność ekranu). Pozostałe wątki (stany, paleta, mobile) poza zakresem, chyba że audyt wykaże, że hierarchia od nich zależy.
- **Wariant kontraktu:** istniejący system projektowy do rozszerzenia (nie tworzyć drugiej palety ani nowej biblioteki).
  - Źródło wartości: `web/app/app.css` (`@theme static`, wartości ciemne pod `.dark`).
  - Komponenty: `web/app/components/` (`Button`, `Field`/`Input`, `Card`, `Alert`, `Badge`, `ConfirmDialog`, `BrandMark`, `ThemeToggle`, `styles.ts`).
  - Reguły dla agentów: sekcja "UI Conventions" w `AGENTS.md`.
- **Pre-audyt (2026-09-29):** skan twardo wpisanych wartości na `web/app/routes/*.tsx` i `root.tsx`: 0 trafień; widoki importują 2-8 komponentów i używają tokenów (najczęściej `text-text-muted`, `border-border-subtle`). Brakujących tokenów nie ma; audyt szuka braków komponentów, architektury (kolejność i wejście do widoku) i hierarchii.
- **Brief audytu dla /10x-research:** przejść widok w obu kierunkach (źródło → widok, widok → źródło); zapisać 3-5 zarzutów (`## Charges`) z plikiem, linią i wpływem na użytkownika; sprawdzić, co widzi użytkownik przy braku danych, po błędzie i z linku bezpośredniego; sprawdzić reguły w `AGENTS.md` pod kątem instrukcji zachęcających do jednorazowych stylów.
