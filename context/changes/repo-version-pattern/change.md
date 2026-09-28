---
change_id: repo-version-pattern
title: Dodanie repozytorium i wzorca wersji z podglądem rozwiązanej wersji
status: implemented
created: 2026-09-26
updated: 2026-09-28
archived_at: null
---

## Notes

S-01 from @context/foundation/roadmap.md — GitHub issue [#3](https://github.com/rchmielorz/SecurityCheck-Portal/issues/3).

### Odstępstwa w implementacji

- 2026-09-28, Phase 2.2 (`RepositoryUrl`): postać kanoniczna to `https://<host>/<ścieżka>.git`, cała małymi literami, z `.git` dopisywanym, gdy go brakuje (plan: bez `.git`, wielkość liter ścieżki bez zmian). Powód: firmowy GitLab (`gitlab-do.coig.app`) odpowiada 301 na adres bez `.git`, a git ma wyłączone podążanie za przekierowaniami; GitLab dopasowuje ścieżki bez względu na wielkość liter. Decyzja użytkownika. Sprawdzone ręcznie: `ls-remote` z `.git` i nagłówkiem Basic (`pat:<PAT>`) zwraca tagi (kryterium 2.6).
- Kryterium 2.5 (wynik usługi zgodny z ręcznym `ls-remote`) sprawdzamy przez portal razem z 3.4.
