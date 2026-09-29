# Repozytorium i wzorzec wersji — Plan Brief

> Full plan: `context/changes/repo-version-pattern/plan.md`
> Research: `context/changes/repo-version-pattern/research.md`

## What & Why

Zalogowany użytkownik dodaje repozytorium Git i wzorce wersji `X.Y.*`. Portal pokazuje, na który tag `X.Y.N` i commit każdy wzorzec się obecnie rozwiązuje. To S-01 z roadmapy. Poprawne rozwiązanie wzorca to warunek wiarygodności całego produktu: skan w S-02 musi dotyczyć kodu faktycznie stojącego u klienta, a nie złej wersji.

## Starting Point

Po F-01 portal ma logowanie, chronione API i powłokę SPA, ale żadnej warstwy danych: w repo nie ma EF Core, bazy ani migracji. Brakuje też kodu, który rozmawia z Git.

## Desired End State

Strona główna pokazuje listę repozytoriów z formularzem dodawania. Strona `/repos/<id>` pokazuje wzorce z ostatnim wynikiem („2.1.10 · commit a1b2c3d, sprawdzono …”), przyciski Sprawdź, Dezaktywuj, Aktywuj i Usuń oraz historię zmian. Wynik zawsze jest jednym z jawnych stanów: rozwiązany, brak dopasowania, niejednoznaczny, błąd.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Oznaczanie wersji w Git | Tagi `X.Y.N` bez prefiksu | Tak oznaczane są wydania w firmie | Research (użytkownik) |
| Dostęp do Git | HTTPS + PAT tylko do odczytu | Tak działa wewnętrzny serwer | Research (użytkownik) |
| Kto woła git | API przez wspólny komponent w `core/` | Podgląd działa bez workera, a S-02 użyje tego samego kodu | Plan |
| Metoda rozwiązania | `git ls-remote --tags`, ścisły regex, najwyższy hotfix jako liczba | Unika pułapek globu, `--sort` i normalizacji bibliotek wersji | Research |
| Wynik rozwiązania | Na żądanie; ostatni wynik zapisany z datą | Lista nie odpytuje Git, a użytkownik widzi wiek wyniku | Plan |
| Model danych | Repozytorium z wieloma wzorcami, bez pola klienta | Ścisłe FR-002/FR-003 | Plan |
| Składnia wzorca | Tylko `X.Y.*` | Definicja z FR-003, najprostsza semantyka | Plan |
| Operacje | Repozytorium: dodaj, usuń (tylko bez wzorców). Wzorzec: dodaj, usuń, dezaktywuj, aktywuj | Usuń poprawia pomyłkę; dezaktywacja zachowuje wyniki skanów | Plan |
| Wzorzec nieaktywny | Ukryty domyślnie, bez odświeżania; ponowne dodanie → 409 „aktywuj” | Nic nie działa na wersji wyłączonej z użycia; brak zdublowanych rekordów | Plan |
| Duplikaty | URL unikalny po normalizacji; wzorzec unikalny w repozytorium | Bez podwójnych skanów tego samego kodu | Plan |
| Tagi niepasujące | Pomijane po cichu | Prostszy wynik | Plan |
| Audyt | Pełny dziennik zdarzeń (osobna tabela), widoczny na stronie repozytorium | Odpowiedzialność za decyzje także po usunięciu | Plan |
| Testy bazy | Testcontainers PostgreSQL; bez Dockera pominięte | Ten sam silnik co produkcja | Plan |
| Ekrany | Lista repozytoriów + `/repos/<id>` | ID w ścieżce (`AGENTS.md:8`); miejsce na skan w S-02 | Plan |

## Scope

**In scope:**
- projekt `core/` z EF Core 10, Npgsql, trzema tabelami i pierwszą migracją;
- walidacja URL, parser wzorca i `ls-remote`, klient git z PAT;
- endpointy repozytoriów, wzorców i historii;
- dwa ekrany SPA;
- testy jednostkowe i integracyjne;
- dokumentacja.

**Out of scope:**
- skan, worker i weryfikacja SHA przy klonie (S-02);
- klient jako encja; edycja; składnie inne niż `X.Y.*`;
- pokazywanie pominiętych tagów; automatyczne odświeżanie (S-05);
- globalny widok dziennika; limit „Sprawdź” na użytkownika;
- automatyczne migracje przy starcie; paginacja.

## Architecture / Approach

```
SPA (home, /repos/:id) ──apiFetch──▶ API /api/repos, /api/patterns  (chronione polityką domyślną)
                                        │ jeden SaveChanges: zmiana + AuditEvent
                                        ├──▶ core/Data: PortalDbContext ─▶ PostgreSQL
                                        └──▶ core/Git: RepositoryUrl → git ls-remote (PAT w env)
                                                         → LsRemoteParser → PatternResolver → wynik (4 stany)
```

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Wspólny projekt i warstwa danych | `core/`, schemat, migracja, fabryka testów z PostgreSQL | Docker niedostępny lokalnie; stare globalne `dotnet-ef` |
| 2. Rozwiązywanie wzorca | Walidacja URL, parsery, klient git, rozwiązywacz z testami | Serwer firmy może nie akceptować URL bez `.git` albo nagłówka Basic |
| 3. Endpointy API | Repozytoria, wzorce, historia; audyt w transakcji | Wyścigi przy duplikatach (pilnują indeksy → 409) |
| 4. Ekrany SPA | Lista, szczegóły, akcje, przełącznik nieaktywnych, historia | Liczba stanów i komunikatów do pokazania |
| 5. Dokumentacja | AGENTS.md, infrastructure.md, roadmapa, `.http` | — |

**Prerequisites:**
- lokalny PostgreSQL (jest `postgresql-x64-13`);
- działający Docker (Rancher Desktop) dla testów bazy;
- PAT tylko do odczytu i nazwa hosta Git.

**Estimated effort:** ~4–6 sesji w 5 fazach; największe to fazy 2 i 3.

## Open Risks & Assumptions

- Zakładamy, że serwer Git przyjmuje kanoniczny URL bez `.git` i PAT w nagłówku Basic. Sprawdzamy to ręcznie w Phase 2; w razie potrzeby zmieniamy normalizację albo schemat nagłówka.
- PAT trafia także na tożsamość app pool API. Phase 5 aktualizuje opis ryzyka w `infrastructure.md`.
- Tag może zostać przesunięty po rozwiązaniu. S-01 pokazuje wynik z datą, a weryfikacja SHA przy klonie należy do S-02.

## Success Criteria (Summary)

- Użytkownik dodaje repozytorium i wzorzec i widzi tag i commit zgodne z ręcznym `git ls-remote --tags`.
- Żaden przypadek brzegowy (brak tagów, błąd Git, duplikat, wzorzec nieaktywny) nie kończy się zgadywaniem ani błędem 500.
- `dotnet test api.Tests` przechodzi; każda zmiana jest widoczna w historii z autorem.
