---
project: SecurityCheck Portal
version: 1
status: draft                    # draft | active | locked
created: 2026-09-26
updated: 2026-09-27
prd_version: 1
main_goal: speed
top_blocker: time
milestone_id: first-trustworthy-scan
milestone_seq: 1
milestone_status: open           # open | done
---

# Roadmap: SecurityCheck Portal

> Źródło: `context/foundation/prd.md` (v1) + `tech-stack.md` + `infrastructure.md` + automatyczny przegląd kodu.
> Edycja w miejscu; archiwizować przy zastąpieniu.
> Elementy poniżej są w kolejności zależności. Tabela „W skrócie” jest indeksem.
> Backlog: GitHub Issues, milestone [M-1](https://github.com/rchmielorz/SecurityCheck-Portal/issues?q=milestone%3A%22M-1%3A+Pierwszy+wiarygodny+skan+wersji+klienta%22) — etykiety `foundation`/`slice`, `must-have`/`nice-to-have`, `north-star`, `status: ready`/`status: blocked`; zależności jako natywne „blocked by”.

## Milestone

**M-1: Pierwszy wiarygodny skan wersji klienta** — Status: open

- **Intent:** Zalogowany członek zespołu dodaje repozytorium i wzorzec wersji, uruchamia skan i dostaje wiarygodną listę podatności dla kodu faktycznie stojącego u klienta; po ścieżce koniecznej dochodzą historia, akceptacja ryzyka i skany cykliczne, o ile starczy miejsca.
- **Source materials:** `context/foundation/prd.md` (v1)
- **Done when:** każdy element F-NN i S-NN poniżej ma status `done`. Minimalny próg zgłoszenia MVP: F-01, S-01, S-02 (Kryterium sukcesu — główne); S-03…S-05 można odciąć, przenosząc je do kolejnego milestone'u.
- **Scope anchors:** FR-001…FR-008, US-01, NFR (brak publicznej strony), Business Logic (trwałość zaakceptowanego ryzyka).

## Vision recap

Zespół maintenance nie sprawdza regularnie podatności w bibliotekach starszych, stabilnych wersji wdrożonych u klientów, bo ręczne przechodzenie repo × wersja × klient jest zbyt pracochłonne. Portal wykorzystuje wiedzę, której generyczne skanery nie mają — który wzorzec wersji (np. `2.1.*`, gdzie `*` to numer hotfixa) stoi u którego klienta — i skanuje zależności dokładnie tej wersji. Wynik musi być wiarygodny: fałszywe poczucie bezpieczeństwa jest gorsze niż brak narzędzia.

## North star

**S-02: Użytkownik uruchamia skan wersji i widzi podatności posortowane wg istotności** — gwiazda przewodnia, czyli najmniejszy przepływ od początku do końca, którego dostarczenie dowodzi, że produkt działa; przy celu `speed` to jedyny kawałek, bez którego zgłoszenie MVP nie ma sensu.

> Gwiazda przewodnia stoi tak wcześnie, jak pozwalają jej zależności (F-01 → S-01), bo wszystko inne ma wartość tylko wtedy, gdy ten przepływ działa.

## At a glance

| ID   | Issue | Change ID              | Outcome (user can …)                                                                 | Prerequisites | PRD refs                          | Status   |
| ---- | ----- | ---------------------- | ------------------------------------------------------------------------------------ | ------------- | --------------------------------- | -------- |
| F-01 | [#1](https://github.com/rchmielorz/SecurityCheck-Portal/issues/1) | authenticated-app-shell | (foundation) każda strona i endpoint wymagają zalogowania; UI i API działają pod jednym originem | —             | NFR, Access Control, FR-001       | done |
| F-02 | [#2](https://github.com/rchmielorz/SecurityCheck-Portal/issues/2) | deploy-skeleton        | (foundation) merge do `main` buduje, sprawdza i wdraża portal na serwer docelowy     | F-01, przygotowany serwer | NFR, Access Control               | proposed |
| S-01 | [#3](https://github.com/rchmielorz/SecurityCheck-Portal/issues/3) | repo-version-pattern   | zalogować się, dodać repozytorium i wzorzec wersji oraz zobaczyć, na jaką wersję wzorzec się rozwiązuje | F-01          | FR-001, FR-002, FR-003            | proposed |
| S-02 | [#4](https://github.com/rchmielorz/SecurityCheck-Portal/issues/4) | manual-version-scan    | ręcznie uruchomić skan wersji i zobaczyć podatności posortowane wg istotności albo jawne „brak wyników” | S-01          | US-01, FR-004, FR-006             | proposed |
| S-03 | [#5](https://github.com/rchmielorz/SecurityCheck-Portal/issues/5) | scan-history           | przejrzeć historię poprzednich skanów danej wersji i trend liczby/poziomu podatności | S-02          | FR-007                            | proposed |
| S-04 | [#6](https://github.com/rchmielorz/SecurityCheck-Portal/issues/6) | accepted-risk-triage   | oznaczyć podatność jako zaakceptowane ryzyko z komentarzem i odfiltrować takie pozycje w kolejnych skanach | S-02          | FR-008, FR-006, Business Logic    | proposed |
| S-05 | [#7](https://github.com/rchmielorz/SecurityCheck-Portal/issues/7) | scheduled-scans        | skonfigurować cykliczny, automatyczny skan danej wersji                               | S-02          | FR-005                            | proposed |

## Streams

Pomoc nawigacyjna — grupuje elementy o wspólnym łańcuchu zależności. Kanoniczna kolejność jest w grafie zależności poniżej.

| Stream | Theme                    | Chain                              | Note                                                                 |
| ------ | ------------------------ | ---------------------------------- | -------------------------------------------------------------------- |
| A      | Ścieżka konieczna        | `F-01` → `S-01` → `S-02`           | Minimalny próg zgłoszenia MVP; przy celu `speed` idzie pierwszy.     |
| B      | Wdrożenie                | `F-02`                             | Dołącza do A po `F-01`; równolegle z `S-01`/`S-02`, weryfikuje skan na serwerze. |
| C      | Rozszerzenia po skanie   | `S-03` · `S-04` · `S-05` (równolegle) | Wszystkie wychodzą z `S-02`; kolejność = kolejność odcinania przy braku czasu. |

## Baseline

Stan kodu na `2026-09-26` (przegląd automatyczny + potwierdzenie użytkownika).
Fundamenty poniżej zakładają ten stan i nie tworzą ponownie tego, co istnieje.

- **Frontend:** partial — React Router 8 + Tailwind 4 w `web/`, tylko ekran powitalny (`web/app/routes/home.tsx`); `ssr: true`, a `infrastructure.md` wymaga trybu SPA.
- **Backend / API:** partial — ASP.NET Core (.NET 10) wydzielony do `api/` (odseparowany od frontendu, więc problem z globowaniem `web/**` w projekcie API znika); wciąż szablon `/weatherforecast`.
- **Data:** absent — brak sterownika bazy, ORM i migracji.
- **Auth:** absent — brak uwierzytelniania i middleware autoryzacji.
- **Deploy / infra:** partial — plan w `infrastructure.md` (IIS + osobna usługa Windows dla skanera); brak workflow CI i skryptu wdrożenia; `web/Dockerfile` pochodzi ze startera i nie jest używany.
- **Observability:** absent — tylko domyślne logowanie frameworka.

## Foundations

### F-01: Uwierzytelniona powłoka aplikacji

- **Outcome:** (foundation) szablon API usunięty; UI serwowane pod tym samym originem co API; każda trasa UI i każdy endpoint API odrzuca niezalogowanego użytkownika, a zalogowany widzi pustą powłokę portalu ze swoją tożsamością.
- **Change ID:** authenticated-app-shell
- **PRD refs:** NFR (brak publicznej strony), Access Control, FR-001
- **Unlocks:** S-01 (pierwszy ekran z danymi musi być od początku chroniony); weryfikacja NFR „żadna strona nie jest publiczna” dla wszystkich kolejnych slice'ów.
- **Prerequisites:** —
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:**
  - Metoda logowania: firmowe SSO (rekomendacja `infrastructure.md`) czy email/hasło (PRD dopuszcza oba)? — Owner: user. Block: no (domyślnie SSO). — rozstrzygnięte 2026-09-27: login + hasło przez LDAPS, JWT w ciasteczku HttpOnly (plan `authenticated-app-shell`).
- **Risk:** Sequenced first, bo NFR zakazuje publicznej strony — każda funkcja zbudowana przed bramką musiałaby być potem zabezpieczana wstecz. Zakres ograniczony do bramki i powłoki; żadnych ekranów domenowych.
- **Status:** done

### F-02: Szkielet wdrożenia

- **Outcome:** (foundation) merge do `main` uruchamia build i sprawdzenie typów oraz wdraża API, UI i proces skanera na serwer docelowy; smoke test potwierdza, że strona wymaga logowania.
- **Change ID:** deploy-skeleton
- **PRD refs:** NFR (brak publicznej strony), Access Control
- **Unlocks:** ścieżka weryfikacji dla S-02 — skan działa na serwerze docelowym (dostęp do Git w sieci wewnętrznej, baza podatności skanera, uprawnienia konta usługi), a nie tylko lokalnie.
- **Prerequisites:** F-01, przygotowany serwer (pula aplikacji, baza danych, konto usługi — kroki administracyjne z `infrastructure.md`)
- **Parallel with:** S-01, S-02
- **Blockers:** Przygotowanie serwera przez administratora (pula IIS, PostgreSQL, konto usługi, wyjątek w Defenderze dla katalogu skanów).
- **Unknowns:**
  - Kto jest właścicielem bazy danych na serwerze (aktualizacje, kopie zapasowe)? — Owner: team. Block: no.
- **Risk:** Równolegle z S-01/S-02, żeby problemy środowiskowe (proxy, konto usługi, długie ścieżki) wyszły przed terminem, a nie w dniu zgłoszenia; nie blokuje lokalnej pracy nad gwiazdą przewodnią.
- **Status:** proposed

## Slices

### S-01: Repozytorium i wzorzec wersji

- **Outcome:** użytkownik może zalogować się, dodać repozytorium Git i wzorzec wersji (np. `2.1.*`) oraz zobaczyć, na jaką konkretną wersję wzorzec się obecnie rozwiązuje.
- **Change ID:** repo-version-pattern
- **PRD refs:** FR-001, FR-002, FR-003
- **Prerequisites:** F-01
- **Parallel with:** F-02
- **Blockers:** —
- **Unknowns:**
  - Jak wzorzec mapuje się na repozytorium: tagi (np. `v2.1.7`) czy gałęzie hotfixowe, i czy wybieramy najwyższy pasujący numer? — Owner: user. Block: no (do rozstrzygnięcia w `/10x-plan`).
  - Jakie poświadczenia portal używa do dostępu do repozytoriów w sieci wewnętrznej? — Owner: team. Block: no.
- **Risk:** Wydzielony przed skanem, bo poprawne rozwiązanie wzorca to warunek wiarygodności całego wyniku (Guardrail: „nie na złej wersji/branchu”); widoczne rozwiązanie wzorca pozwala to sprawdzić przed podłączeniem skanera.
- **Status:** proposed

### S-02: Ręczny skan wersji i lista podatności

- **Outcome:** użytkownik może ręcznie uruchomić skan dla wybranej wersji i po jego zakończeniu zobaczyć podatności (biblioteka, CVE, istotność) posortowane od critical do low, jawne „brak wyników”, gdy nic nie wykryto, oraz jawny błąd, gdy skan się nie powiódł.
- **Change ID:** manual-version-scan
- **PRD refs:** US-01, FR-004, FR-006
- **Prerequisites:** S-01
- **Parallel with:** F-02
- **Blockers:** —
- **Unknowns:**
  - Czy skanowane repozytoria mają pliki blokady zależności? Bez nich zewnętrzny skaner (Trivy) może pominąć część bibliotek — ryzyko fałszywego „brak wyników”. — Owner: user. Block: no.
  - Skąd serwer pobiera bazę podatności skanera (bezpośrednio przez proxy czy lokalny mirror)? — Owner: team. Block: no.
- **Risk:** Gwiazda przewodnia; skan trwa minuty, więc musi działać poza cyklem życia żądania HTTP i odróżniać „nieudany” od „czysty” — największe ryzyko wiarygodności, dlatego nic poza S-01 go nie poprzedza.
- **Status:** proposed

### S-03: Historia skanów wersji

- **Outcome:** użytkownik może przejrzeć listę poprzednich skanów danej wersji i zobaczyć, czy liczba i poziom podatności rośnie czy maleje.
- **Change ID:** scan-history
- **PRD refs:** FR-007
- **Prerequisites:** S-02
- **Parallel with:** S-04, S-05
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Najmniejsze rozszerzenie (wyniki są już zapisywane w S-02), więc przy celu `speed` idzie pierwsze po ścieżce koniecznej; łatwe do odcięcia.
- **Status:** proposed

### S-04: Akceptacja ryzyka

- **Outcome:** użytkownik może oznaczyć podatność (biblioteka + CVE + wersja) jako zaakceptowane ryzyko z komentarzem, zobaczyć ten status przy każdym kolejnym skanie tej wersji, odfiltrować takie pozycje i cofnąć akceptację.
- **Change ID:** accepted-risk-triage
- **PRD refs:** FR-008, FR-006, Business Logic
- **Prerequisites:** S-02
- **Parallel with:** S-03, S-05
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Jedyna reguła domenowa w PRD i powód istnienia logowania (odpowiedzialność za decyzję); PRD wskazuje ją jako kandydata do odcięcia przy braku czasu.
- **Status:** proposed

### S-05: Skany cykliczne

- **Outcome:** użytkownik może skonfigurować automatyczne, harmonogramowane uruchamianie skanu dla danej wersji.
- **Change ID:** scheduled-scans
- **PRD refs:** FR-005
- **Prerequisites:** S-02
- **Parallel with:** S-03, S-04
- **Blockers:** —
- **Unknowns:**
  - Czy zmieści się w MVP zgłaszanym na 2026-11-04 (PRD, Otwarte pytania #2)? — Owner: user. Block: no.
- **Risk:** Ostatni, bo nice-to-have; S-02 uruchamia skan poza żądaniem HTTP, więc harmonogram dokłada tylko wyzwalacz — zgodnie z wymogiem PRD, by dodanie go nie wymagało przebudowy.
- **Status:** proposed

## Backlog Handoff

| Roadmap ID | Issue | Change ID               | Suggested issue title                                         | Ready for `/10x-plan` | Notes |
| ---------- | ----- | ----------------------- | ------------------------------------------------------------- | --------------------- | ----- |
| F-01 | [#1](https://github.com/rchmielorz/SecurityCheck-Portal/issues/1) | authenticated-app-shell | Bramka logowania i wspólna powłoka UI + API                   | yes                   | Run `/10x-plan authenticated-app-shell` |
| F-02 | [#2](https://github.com/rchmielorz/SecurityCheck-Portal/issues/2) | deploy-skeleton         | CI + wdrożenie na serwer docelowy ze smoke testem             | no                    | Czeka na F-01 i przygotowanie serwera |
| S-01 | [#3](https://github.com/rchmielorz/SecurityCheck-Portal/issues/3) | repo-version-pattern    | Dodanie repozytorium i wzorca wersji z podglądem rozwiązanej wersji | no                    | Czeka na F-01 |
| S-02 | [#4](https://github.com/rchmielorz/SecurityCheck-Portal/issues/4) | manual-version-scan     | Ręczny skan wersji i lista podatności wg istotności           | no                    | Czeka na S-01; gwiazda przewodnia |
| S-03 | [#5](https://github.com/rchmielorz/SecurityCheck-Portal/issues/5) | scan-history            | Historia skanów wersji z trendem                              | no                    | Czeka na S-02 |
| S-04 | [#6](https://github.com/rchmielorz/SecurityCheck-Portal/issues/6) | accepted-risk-triage    | Oznaczanie podatności jako zaakceptowane ryzyko               | no                    | Czeka na S-02 |
| S-05 | [#7](https://github.com/rchmielorz/SecurityCheck-Portal/issues/7) | scheduled-scans         | Cykliczne skany wersji                                        | no                    | Czeka na S-02; PRD OQ #2 |

## Open Roadmap Questions

1. **Czy i kiedy dodać zbiorczy dashboard wielu klientów?** ([#8](https://github.com/rchmielorz/SecurityCheck-Portal/issues/8)) — Owner: użytkownik/zespół. Block: roadmap-wide (poza M-1; nie blokuje zgłoszenia).
2. **Czy cykliczne skanowanie (FR-005) zmieści się w MVP zgłaszanym na pierwszy termin?** — Owner: użytkownik. By: 2026-11-04. Block: S-05 (nie blokuje planowania).
3. ~~**Aktualizacja `infrastructure.md` pod skaner Trivy**~~ — rozwiązane 2026-09-26: `infrastructure.md` opisuje Trivy (sekcja „Scanner: Trivy”, rejestr ryzyk, kroki instalacji). Otwarte zostają per-slice Unknowns w S-02 (pliki blokady, źródło bazy podatności).

## Parked

- **Zbiorczy dashboard wielu klientów** — Why parked: PRD §Poza zakresem; wartościowy dopiero przy większej skali.
- **Własny mechanizm wykrywania podatności** — Why parked: PRD §Poza zakresem; wykrywanie deleguje do zewnętrznego skanera.
- **Role i uprawnienia poza logowaniem** — Why parked: PRD §Poza zakresem; model płaski.
- **Automatyczny import repozytoriów z organizacji Git** — Why parked: PRD §Poza zakresem; repozytoria dodawane ręcznie.

## Milestone History

## Done

- **F-01: (foundation) szablon API usunięty; UI serwowane pod tym samym originem co API; każda trasa UI i każdy endpoint API odrzuca niezalogowanego użytkownika, a zalogowany widzi pustą powłokę portalu ze swoją tożsamością.** — Archived 2026-09-27 → `context/archive/2026-09-26-authenticated-app-shell/`. Lesson: —.
