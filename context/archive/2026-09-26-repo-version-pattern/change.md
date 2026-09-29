---
change_id: repo-version-pattern
title: Dodanie repozytorium i wzorca wersji z podglądem rozwiązanej wersji
status: archived
created: 2026-09-26
updated: 2026-09-29
archived_at: 2026-09-29T17:14:47Z
---

## Notes

S-01 from @context/foundation/roadmap.md — GitHub issue [#3](https://github.com/rchmielorz/SecurityCheck-Portal/issues/3).

### Odstępstwa w implementacji

- 2026-09-28, Phase 2.2 (`RepositoryUrl`): postać kanoniczna to `https://<host>/<ścieżka>.git`, cała małymi literami, z `.git` dopisywanym, gdy go brakuje (plan: bez `.git`, wielkość liter ścieżki bez zmian). Powód: firmowy GitLab (`gitlab-do.coig.app`) odpowiada 301 na adres bez `.git`, a git ma wyłączone podążanie za przekierowaniami; GitLab dopasowuje ścieżki bez względu na wielkość liter. Decyzja użytkownika. Sprawdzone ręcznie: `ls-remote` z `.git` i nagłówkiem Basic (`pat:<PAT>`) zwraca tagi (kryterium 2.6).
  Tekst planu w Phase 2.2 i wierszu Progress 2.6 („bez `.git`”) opisuje pierwotne założenie; obowiązuje postać z `.git`.
- Kryterium 2.5 (wynik usługi zgodny z ręcznym `ls-remote`) sprawdzamy przez portal razem z 3.4.
- Phase 1.3: `PortalDbContext` czyta connection string z `IConfiguration`, a `DatabaseOptions` tylko go waliduje przy starcie (dzięki temu `dotnet ef migrations add` działa bez bazy). Nowe migracje wymagają `--output-dir Data/Migrations`.
- Phase 3.1: dodanie repozytorium i wzorca wraz ze zdarzeniem audytu to jawna transakcja z dwoma `SaveChanges` (plan: jeden `SaveChanges`), bo `AuditEvent` nie ma klucza obcego, więc EF nie wpisze wygenerowanego ID w tym samym zapisie. Atomowość zachowana. `DbUpdateConcurrencyException` → 404; trasy mają ograniczenie `{id:long}`.
- Phase 4.3: opisy zdarzeń w historii to rzeczowniki neutralne płciowo („dodanie wzorca 2.1.*”) zamiast przykładów z planu („dodał wzorzec”).
