---
project: "SecurityCheck Portal"
version: 1
status: draft
created: 2026-09-21
context_type: greenfield
product_type: web-app
target_scale:
  users: small
  qps: low
  data_volume: small
timeline_budget:
  mvp_weeks: 3
  hard_deadline: 2026-11-04
  after_hours_only: true
---

## Vision & Problem Statement

Zespół deweloperski odpowiedzialny za utrzymanie wdrożonych u klientów projektów nie ma dziś procesu, który regularnie weryfikowałby podatności w bibliotekach dla stabilnych, starszych wersji — ręczne sprawdzanie każdego repo × wersji × klienta jest zbyt pracochłonne, więc w praktyce nie jest robione, i luka bezpieczeństwa narasta niezauważenie miesiącami.

Firma zna dokładny mapping "która wersja (z wzorcem hotfixa, np. 2.1.*) stoi u którego klienta" — czego żaden generyczny skaner nie wie z automatu, bo działa na poziomie repo/brancha, nie per-klient-wdrożenie. Portal wykorzystuje tę wiedzę domenową do sparametryzowanego, cyklicznego skanowania zależności dopasowanego do rzeczywistego stanu u klienta.

## User & Persona

Developer/zespół maintenance odpowiedzialny za wsparcie wdrożonych u klientów wersji projektów — sięga po portal, gdy trzeba ocenić ryzyko dla konkretnego klienta (np. przy nowym CVE albo okresowym przeglądzie) bez ręcznego przeszukiwania repozytoriów.

## Success Criteria

### Primary
- Użytkownik może: zalogować się, dodać repozytorium i wzorzec wersji, ręcznie uruchomić skan zależności dla tej wersji i zobaczyć listę wykrytych podatności.

### Secondary
- Historia poprzednich skanów dla danej wersji (widać, czy liczba/poziom podatności rośnie czy maleje w czasie).
- Możliwość oznaczenia konkretnej podatności w bibliotece jako zaakceptowanego ryzyka, z komentarzem uzasadniającym.

### Guardrails
- Wynik skanu musi być wiarygodny — skan nie może pomijać podatności ani pokazywać wyników dla złej wersji/brancha; fałszywe poczucie bezpieczeństwa jest gorsze niż brak narzędzia.
- Dane o klientach i repozytoriach nie wyciekają poza firmę — dostęp tylko dla zalogowanych, brak publicznej ekspozycji.

## User Stories

### US-01: Użytkownik uruchamia skan podatności dla wersji u klienta

- **Given** zalogowany użytkownik z dodanym repozytorium i wzorcem wersji (np. 2.1.*)
- **When** ręcznie uruchamia skan dla tej wersji
- **Then** po zakończeniu skanu widzi listę wykrytych podatności w bibliotekach tej wersji

#### Acceptance Criteria
- Skan wykonuje się na kodzie odpowiadającym zadeklarowanemu wzorcowi wersji, nie na innej wersji/branchu
- Wynik zawiera co najmniej: nazwę biblioteki, zidentyfikowaną podatność (CVE), poziom istotności
- Wyniki są posortowane wg istotności (critical → low)
- Brak wykrytych podatności jest jawnie pokazany jako "brak wyników", nie jako pusty/błędny ekran

## Functional Requirements

### Autoryzacja
- FR-001: Użytkownik może zalogować się do portalu. Priority: must-have
  > Socrates: Kontrargument rozważony: "bez logowania traci się odpowiedzialność za decyzje (kto oznaczył ryzyko jako zaakceptowane)". Rozstrzygnięcie: FR zostaje — logowanie jest potrzebne właśnie po to, by FR-008 dało się przypisać do osoby.

### Repozytoria i wersje
- FR-002: Użytkownik może dodać repozytorium GIT do portalu. Priority: must-have
  > Socrates: Brak kontrargumentu; FR zostaje bez zmian.
- FR-003: Użytkownik może zdefiniować wzorzec wersji dla repozytorium (np. 2.1.*, gdzie `*` to numer hotfixa) reprezentujący wersję zainstalowaną u klienta. Priority: must-have
  > Socrates: Brak kontrargumentu; FR zostaje bez zmian.

### Skanowanie
- FR-004: Użytkownik może ręcznie uruchomić skan zależności dla wybranej wersji repozytorium. Priority: must-have
  > Socrates: Brak kontrargumentu; FR zostaje bez zmian.
- FR-005: Użytkownik może skonfigurować cykliczne (automatyczne, harmonogramowane) uruchamianie skanu dla danej wersji. Priority: nice-to-have
  > Socrates: Kontrargument rozważony: "cykliczność to sedno pierwotnego pomysłu — bez niej MVP nie rozwiązuje głównego bólu". Rozstrzygnięcie: FR zostaje nice-to-have na pierwsze zgłoszenie (skan ręczny to już wymierna korzyść dla zespołu), ale architektura MVP musi z góry przewidywać miejsce na tę funkcję, żeby dodanie jej później nie wymagało przebudowy.

### Wyniki i historia
- FR-006: Użytkownik może przeglądać wyniki skanu (listę wykrytych podatności) dla danej wersji, posortowane wg istotności (critical/high/medium/low) i z możliwością odfiltrowania podatności już oznaczonych jako zaakceptowane ryzyko (FR-008). Priority: must-have
  > Socrates: Kontrargumenty rozważone: "surowa lista bez priorytetyzacji będzie nieczytelna" oraz "bez rozróżnienia nowe/zaakceptowane wynik będzie mylący". Rozstrzygnięcie: oba przyjęte — FR rozszerzony o sortowanie wg istotności i filtrowanie wg statusu.
- FR-007: Użytkownik może przeglądać historię poprzednich skanów dla danej wersji. Priority: nice-to-have
  > Socrates: Kontrargument rozważony: "bez historii nie widać trendu — czy sytuacja u klienta się pogarsza". Rozstrzygnięcie: FR zostaje nice-to-have na pierwsze zgłoszenie; wartość trendu uznana, ale nie blokuje MVP.
- FR-008: Użytkownik może oznaczyć podatność w konkretnej bibliotece jako zaakceptowane ryzyko, z komentarzem uzasadniającym. Priority: nice-to-have
  > Socrates: Kontrargument rozważony: "to osobny mini-workflow (triage) — może za duży zakres na MVP". Rozstrzygnięcie: FR zostaje nice-to-have, potwierdzone jako kandydat do ograniczenia/odłożenia jeśli zabraknie czasu.

## Non-Functional Requirements

- Dane o repozytoriach, klientach i wykrytych podatnościach są dostępne wyłącznie dla zalogowanych członków zespołu — brak publicznej ekspozycji jakiejkolwiek strony portalu.

## Business Logic

Gdy podatność w danej bibliotece dla danej wersji zostaje oznaczona jako zaakceptowane ryzyko, aplikacja automatycznie utrzymuje ten status przy każdym kolejnym skanie tej samej wersji — zamiast traktować tę samą podatność jako nową za każdym razem.

Regułę uruchamia oznaczenie konkretnej podatności (biblioteka + CVE + wersja) jako zaakceptowanego ryzyka wraz z komentarzem uzasadniającym. Wynikiem jest trwały status tej podatności, widoczny przy każdym kolejnym przeglądzie wyników skanu tej wersji — dopóki ktoś go nie cofnie.

Użytkownik spotyka tę regułę w przeglądzie wyników skanu (FR-006): nowe/niezaakceptowane podatności są odróżnione od tych już rozpatrzonych i świadomie zaakceptowanych, więc zespół nie musi za każdym razem od nowa oceniać tego samego ryzyka.

## Access Control

Logowanie (email/hasło lub firmowe SSO) — narzędzie wewnętrzne, wieloosobowe. Model płaski: każdy zalogowany członek zespołu widzi te same dane (wszystkie repozytoria, klientów i wyniki skanów). Brak podziału na role w MVP.

## Non-Goals

- Brak zbiorczego dashboardu pokazującego stan wielu klientów naraz — rationale: MVP celuje w garstkę użytkowników i pojedyncze przeglądy skanów; zbiorczy widok staje się wartościowy dopiero przy większej skali (patrz Open Questions).
- Brak własnego mechanizmu wykrywania podatności — rationale: wykrywanie opiera się na zewnętrznym narzędziu do skanowania zależności; żadna własna logika analizy zależności/CVE nie wchodzi w zakres.
- Brak ról i uprawnień poza samym logowaniem — rationale: model płaski (Access Control) jest świadomie najmniejszym sensownym zakresem na MVP.
- Brak automatycznego importu repozytoriów z organizacji GIT — rationale: repozytoria dodawane ręcznie, jedno po drugim; integracja z listą repo to rozszerzenie na później.

## Open Questions

1. **Czy i kiedy dodać zbiorczy dashboard wielu klientów?** — Owner: użytkownik/zespół. Kontekst: przy skali x100 (setki repo/klientów) sama reguła trwałości zaakceptowanego ryzyka wystarcza, ale brakuje zbiorczego widoku stanu — zidentyfikowane jako naturalne rozszerzenie po MVP, nie blokuje pierwszego zgłoszenia.
2. **Czy cykliczne skanowanie (FR-005) zmieści się w MVP zgłaszanym na pierwszy termin?** — Owner: użytkownik. By: 2026-11-04. Nice-to-have — architektura MVP musi zostawić na nie miejsce, ale nie jest wymagane do zgłoszenia.
