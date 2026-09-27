# Repozytorium i wzorzec wersji — Implementation Plan

## Overview

S-01 z roadmapy ([#3](https://github.com/rchmielorz/SecurityCheck-Portal/issues/3)). Zalogowany użytkownik dodaje repozytorium Git (URL HTTPS z dozwolonego serwera wewnętrznego) i wzorce wersji w postaci `X.Y.*`. Portal pokazuje, na który tag `X.Y.N` i commit wzorzec się obecnie rozwiązuje. Wzorce można dezaktywować (zostają z wynikami), aktywować i usuwać. Każda zmiana trafia do dziennika zdarzeń.

S-01 wprowadza pierwszą warstwę danych (PostgreSQL przez EF Core) i wspólny komponent Git, z którego w S-02 skorzysta worker skanujący. Poprawne rozwiązanie wzorca to warunek wiarygodności całego produktu (PRD, Guardrails), dlatego wynik jest jawny, datowany i nigdy nie jest zgadywany.

## Current State Analysis

- **Brak warstwy danych.** Nie ma EF Core, Npgsql, `DbContext`, migracji ani `ConnectionStrings` w `api/` i `api.Tests/` (research §2; `AGENTS.md:3` „planned but not yet installed”). Żaden element roadmapy nie wprowadził bazy wcześniej; F-01 świadomie niczego nie zapisywał.
- **API z F-01 daje gotowe wzorce:**
  - opcje z walidacją przy starcie (`api/Program.cs:14-24`);
  - moduł z metodą rozszerzającą `Map…Endpoints` i `MapGroup("/api")` (`api/Auth/AuthEndpoints.cs:13-26`);
  - kody statusu bez treści (`AuthEndpoints.cs:40-93`);
  - unia wyników zależności zewnętrznej mapowana na kody (`LdapAuthResult`, `AuthEndpoints.cs:70-75`);
  - `TimeProvider` zarejestrowany jako singleton (`Program.cs:26`);
  - catch-all `/api/{**rest}` musi zostać za nowymi mapowaniami (`Program.cs:132-140`).
- **Testy:**
  - `PortalFactory` nadpisuje konfigurację i usługi (`api.Tests/PortalFactory.cs:73-95`), a fałszywka LDAP jest wzorcem dla fałszywki Git (`:140-164`);
  - `NoPublicEndpointsTests` pilnuje, że nowe endpointy są chronione (`:18-22`);
  - `LoginAsync` jest prywatny w `AuthEndpointsTests.cs:174`.
- **SPA:**
  - trasy chronione są dziećmi layoutu (`web/app/routes.ts:10-15`);
  - `apiFetch(path, init, request)` przekierowuje na logowanie przy 401 (`web/app/lib/api.ts:15-42`);
  - wzorzec formularza z `clientAction` i mapą polskich komunikatów jest w `web/app/routes/login.tsx:6-57`;
  - `home.tsx:12` ma placeholder pustej listy repozytoriów.
- **Środowisko lokalne (sprawdzone 2026-09-27):**
  - Docker CLI (Rancher Desktop 29.0.2) jest zainstalowany, ale demon nie odpowiadał;
  - działa usługa `postgresql-x64-13`;
  - globalne `dotnet-ef` 6.0.14 jest za stare dla EF Core 10;
  - git 2.38.1.

## Desired End State

- **Strona główna** pokazuje listę repozytoriów i formularz dodawania.
  - Poprawny URL HTTPS z dozwolonego hosta tworzy repozytorium i przenosi na `/repos/<id>`.
  - Zły URL albo duplikat (po normalizacji) daje polski komunikat.
- **`/repos/<id>`:**
  - pokazuje URL, nazwę, listę aktywnych wzorców z ostatnim wynikiem i datą sprawdzenia;
  - ma przełącznik „Pokaż nieaktywne”, formularz dodawania wzorca i sekcję „Historia zmian”;
  - przycisk „Usuń repozytorium” działa tylko wtedy, gdy repozytorium nie ma żadnych wzorców.
- **Dodanie wzorca `2.1.*`** od razu go rozwiązuje. „Sprawdź” rozwiązuje ponownie.
  - Wynik to jeden z czterech stanów: rozwiązany (tag `2.1.N` o najwyższym N i pełny commit), brak dopasowania, niejednoznaczny, błąd.
  - Tagi spoza formatu `X.Y.N` są pomijane.
- **Wzorzec nieaktywny:**
  - jest ukryty domyślnie i pokazuje ostatni zapisany wynik;
  - nie da się go sprawdzić;
  - ponowne dodanie tego samego wzorca kończy się komunikatem, że istnieje jako nieaktywny.
- **Każde dodanie, usunięcie, aktywacja i dezaktywacja** trafia do dziennika zdarzeń z autorem i czasem, także po usunięciu repozytorium.
- **Testy:** `dotnet test api.Tests` przechodzi. Bez Dockera testy bazy są oznaczone jako pominięte, a reszta przechodzi.

### Key Discoveries:

- **`git ls-remote --tags` bez `--refs`:** commit tagu adnotowanego jest w linii `refs/tags/X^{}`. Opcja `--refs` ją ukrywa (research §3).
- **Glob `ls-remote` łapie też `2.1.7-rc1` i `2.1.7.1`.** Filtrujemy ścisłym regexem w .NET i porównujemy numer hotfixa jako liczbę (research §3).
- **PAT tylko w środowisku procesu potomnego**, nie w URL ani argv, bo tam trafia do audytu procesów (research §4).
- **URL użytkownika:** lista dozwolonych hostów, tylko https, `--end-of-options`, `GIT_ALLOW_PROTOCOL=https` (research §4).
- **`AGENTS.md:8`:** wzorzec z kropką nie może być ostatnim segmentem adresu trasy UI. Stąd `/repos/<id>`, a wzorce identyfikowane po ID.

## What We're NOT Doing

- Skan, klonowanie, worker i weryfikacja SHA przy klonie (S-02). S-01 tylko zapisuje tag i commit, z którymi S-02 porówna klon.
- Encja ani pole klienta; edycja repozytorium lub wzorca (poprawka = usuń i dodaj).
- Składnia wzorca inna niż `X.Y.*` (dokładna wersja `X.Y.Z`, `X.*`, prerelease).
- Pokazywanie tagów pominiętych.
- Odświeżanie wzorców nieaktywnych oraz automatyczne lub cykliczne odświeżanie wyników (S-05).
- Globalny widok dziennika zdarzeń; historia jest widoczna tylko na stronie repozytorium.
- Import repozytoriów z organizacji Git (PRD Non-Goals) i uwierzytelnianie Git inne niż HTTPS z PAT.
- Limit wywołań „Sprawdź” na użytkownika. Chronią timeout procesu i to, że dostęp mają tylko zalogowani.
- Automatyczne migracje przy starcie API. Produkcyjny sposób wdrażania migracji (`efbundle`) należy do F-02.
- Paginacja list (skala: kilka osób, dziesiątki repozytoriów).

## Implementation Approach

Kolejność od danych do UI. Każda faza kończy się zielonym buildem i testami:

1. Wspólny projekt `core/` z warstwą danych i infrastrukturą testów bazy.
2. Rozwiązywanie wzorca w `core/`: walidacja URL, parsery, klient git, rozwiązywacz. Niezależne od HTTP, żeby worker użył tego samego kodu.
3. Endpointy API z dziennikiem zdarzeń zapisywanym w tej samej transakcji co zmiana.
4. Ekrany SPA.
5. Dokumentacja.

## Critical Implementation Details

- **Dwie fabryki testowe.** Samo zarejestrowanie `DbContext` nie otwiera połączenia. Bazowy `PortalFactory` podaje więc fikcyjny connection string i fałszywkę Git, a testy F-01 dalej działają bez Dockera. Testy dotykające bazy używają fabryki pochodnej, która startuje kontener PostgreSQL i stosuje migracje. Bez Dockera te testy są pomijane z komunikatem, a nie oblewane.
- **Naruszenia ograniczeń z bazy to 409, nie 500.** Unikalność URL i wzorca oraz blokada usuwania repozytorium z wzorcami są też pilnowane indeksami i kluczem obcym (`Restrict`), bo dwa żądania naraz mogą ominąć sprawdzenie w kodzie. Naruszenie unikalności (PostgreSQL `23505`) i klucza obcego (`23503`) mapujemy na 409.
- **Błąd Git nie blokuje zapisu wzorca.** Dodanie wzorca zapisuje go, a potem rozwiązuje. Stan `Error` zapisujemy jako wynik, a odpowiedź to nadal 201. Wzorzec istnieje i można go później sprawdzić.

## Phase 1: Wspólny projekt i warstwa danych

### Overview

Nowa biblioteka `core/` z EF Core 10 i Npgsql, encjami repozytorium, wzorca i zdarzenia audytu oraz pierwszą migracją. API rejestruje `DbContext` z connection stringiem walidowanym przy starcie. Testy dostają fabrykę z kontenerem PostgreSQL.

### Changes Required:

#### 1. Projekt `core`

**File**: `core/securitycheck-portal.Core.csproj` (nowy), `api/securitycheck-portal.csproj`, `api.Tests/securitycheck-portal.Tests.csproj`

**Intent**: Biblioteka współdzielona przez API i przyszłego workera (`infrastructure.md:186`). Folder najwyższego poziomu obok `api/` (`AGENTS.md:17`), z nazewnictwem jak istniejące projekty.

**Contract**:
- `net10.0` classlib, `RootNamespace` `securitycheck_portal.Core`, Nullable i ImplicitUsings włączone.
- Pakiety `Microsoft.EntityFrameworkCore` 10.0.x, `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.x i `Microsoft.EntityFrameworkCore.Design` 10.0.x (PrivateAssets all).
- `api` i `api.Tests` dostają `ProjectReference` do `core`.

#### 2. Encje i `PortalDbContext`

**File**: `core/Data/PortalDbContext.cs`, `core/Data/Repository.cs`, `core/Data/VersionPattern.cs`, `core/Data/AuditEvent.cs` (nowe)

**Intent**: Schemat pod decyzje z planowania: wiele wzorców na repozytorium, dezaktywacja zachowująca dane, ostatni wynik rozwiązania przy wzorcu i dziennik zdarzeń, który przetrwa usunięcie repozytorium.

**Contract**:
- **`Repository`:**
  - `Id` (long, identity), `Url` (kanoniczny, unikalny), `Name` (opcjonalna);
  - `CreatedAt` (`DateTimeOffset`), `CreatedBy` (login z claimu `sub`).
- **`VersionPattern`:**
  - `Id`, `RepositoryId` (FK z `DeleteBehavior.Restrict`), `Pattern` (kanoniczny tekst, np. `2.1.*`);
  - unikalny indeks (`RepositoryId`, `Pattern`), obejmujący także wzorce nieaktywne;
  - `IsActive`, `CreatedAt`, `CreatedBy`;
  - ostatni wynik: `LastResolutionState` (null albo `Resolved`/`NoMatch`/`Ambiguous`/`Error`, zapisany jako tekst), `LastResolvedTag`, `LastResolvedCommit` (40 znaków hex), `LastResolvedAt`.
- **`AuditEvent`:**
  - `Id`, `OccurredAt`, `Actor`;
  - `Action` (tekst: `RepositoryAdded`, `RepositoryDeleted`, `PatternAdded`, `PatternDeleted`, `PatternActivated`, `PatternDeactivated`);
  - `RepositoryId` i `PatternId` jako zwykłe kolumny **bez kluczy obcych**, żeby zdarzenie przetrwało usunięcie;
  - migawki `RepositoryUrl` i `Pattern` (tekst w chwili zdarzenia);
  - indeks (`RepositoryId`, `OccurredAt`).

#### 3. Rejestracja w API i konfiguracja

**File**: `api/Program.cs`, `api/appsettings.json`, `core/Data/DataServiceCollectionExtensions.cs` (nowy)

**Intent**: API korzysta z bazy przez `ConnectionStrings:Portal`. Brak wartości zatrzymuje start z czytelnym błędem, tak jak brak `Auth:*` (`AGENTS.md:27`).

**Contract**:
- Metoda rozszerzająca w `core` rejestruje `PortalDbContext` z Npgsql.
- Brak lub pusty `ConnectionStrings:Portal` kończy start wyjątkiem walidacji opcji.
- W `appsettings.json` nie ma connection stringa; lokalnie ustawia się go przez user-secrets.
- API nie migruje bazy przy starcie.

#### 4. Migracja i narzędzie EF

**File**: `core/Data/Migrations/*` (nowe, generowane), `.config/dotnet-tools.json` (nowy)

**Intent**: Pierwsza migracja tworzy trzy tabele. Lokalny manifest przypina `dotnet-ef` 10.0.x, bo globalne 6.0.14 nie obsługuje EF Core 10.

**Contract**:
- Migracja `InitialRepositories`.
- Komendy: `dotnet tool restore`, `dotnet ef migrations add <Name> --project core --startup-project api`, `dotnet ef database update --project core --startup-project api`.
- Manifest narzędzi w `.config/` nie jest plikiem projektu, więc nie łamie `AGENTS.md:12`.

#### 5. Fabryka testowa z bazą

**File**: `api.Tests/PortalFactory.cs`, `api.Tests/DatabasePortalFactory.cs` (nowy), `api.Tests/TestAuth.cs` (nowy), `api.Tests/securitycheck-portal.Tests.csproj`

**Intent**: Testy F-01 działają dalej bez Dockera. Testy bazy dostają prawdziwy PostgreSQL. Helper logowania jest współdzielony.

**Contract**:
- `PortalFactory` podaje fikcyjny `ConnectionStrings:Portal`, którego nikt nie otwiera.
- `DatabasePortalFactory : PortalFactory`:
  - startuje kontener `Testcontainers.PostgreSql` (obraz PostgreSQL 17) raz na klasę testów;
  - podmienia connection string i stosuje migracje (`Database.MigrateAsync`).
  - Gdy Docker jest niedostępny, testy są pomijane przez `Xunit.SkippableFact` (`Skip.If`) z komunikatem „Docker niedostępny — testy bazy pominięte”.
- `TestAuth.LoginAsync(HttpClient, userName, password)` przejmuje prywatny helper z `AuthEndpointsTests.cs:174`, a `AuthEndpointsTests` go używa.
- Pakiety testowe: `Testcontainers.PostgreSql`, `Xunit.SkippableFact`.

#### 6. Test schematu

**File**: `api.Tests/SchemaTests.cs` (nowy)

**Intent**: Potwierdza, że migracja odpowiada modelowi i że ograniczenia działają w bazie.

**Contract**: Na `DatabasePortalFactory`:
- duplikat `Url` i duplikat (`RepositoryId`, `Pattern`) rzucają naruszenie unikalności;
- usunięcie repozytorium z wzorcem rzuca naruszenie klucza obcego;
- usunięcie repozytorium zostawia jego `AuditEvent`.

### Success Criteria:

#### Automated Verification:

- API i `core` kompilują się bez ostrzeżeń: `dotnet build api`
- Model nie ma zmian bez migracji: `dotnet ef migrations has-pending-model-changes --project core --startup-project api` kończy się kodem 0
- Wszystkie testy przechodzą przy działającym Dockerze: `dotnet test api.Tests`
- Przy wyłączonym Dockerze testy F-01 przechodzą, a testy bazy są raportowane jako pominięte: `dotnet test api.Tests`

#### Manual Verification:

- Bez `ConnectionStrings:Portal` API odmawia startu z czytelnym błędem walidacji
- `dotnet ef database update --project core --startup-project api` tworzy tabele w lokalnym PostgreSQL

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 2: Rozwiązywanie wzorca

### Overview

W `core/Git/`: walidacja i normalizacja URL, parser wzorca `X.Y.*`, parser wyjścia `git ls-remote`, klient `git.exe` z PAT i rozwiązywacz zwracający jeden z czterech stanów. Kod nie zależy od HTTP, żeby worker w S-02 użył go bez zmian.

### Changes Required:

#### 1. Opcje Git

**File**: `core/Git/GitOptions.cs` (nowy), `api/Program.cs`

**Intent**: Ustawienia dostępu do serwera Git walidowane przy starcie. Token wyłącznie w user-secrets lub zmiennej środowiskowej.

**Contract**: Sekcja `Git`:
- `ExecutablePath` (domyślnie `git`; na serwerze ścieżka bezwzględna);
- `AllowedHosts` (co najmniej jeden host);
- `UserName` (nazwa do Basic auth, domyślnie `pat`);
- `Token` (wymagany, nigdy w `appsettings*.json`);
- `TimeoutSeconds` (domyślnie 30, > 0).

`ValidateOnStart` jak w `api/Program.cs:14-24`.

#### 2. URL repozytorium

**File**: `core/Git/RepositoryUrl.cs` (nowy)

**Intent**: Jedno miejsce, które odrzuca niebezpieczne i obce adresy oraz daje kanoniczną postać do zapisu, porównań unikalności i wywołań git.

**Contract**: `TryNormalize(string? input, IReadOnlyCollection<string> allowedHosts)` zwraca kanoniczny URL albo błąd.
- **Akceptuje tylko:**
  - absolutny URI ze schematem dokładnie `https`;
  - host z listy (porównanie bez rozróżniania wielkości liter);
  - port domyślny;
  - brak userinfo, query i fragmentu;
  - ścieżkę ze znaków `[A-Za-z0-9._~/-]`, bez segmentów `.` i `..`.
- **Postać kanoniczna:** `https://<host małymi literami>/<ścieżka>`, bez końcowego `/` i bez końcowego `.git`.
- Wejście zaczynające się od `-`, `ext::`, `file:`, ścieżki lokalne, UNC, `ssh://` i postać scp są odrzucane samą regułą schematu i hosta.

#### 3. Wzorzec wersji

**File**: `core/Git/VersionPatternSpec.cs` (nowy)

**Intent**: Ścisła składnia `X.Y.*` i dopasowanie tagów `X.Y.N`, zgodne z decyzją „tylko `X.Y.*`, tagi bez prefiksu”.

**Contract**:
- `TryParse("2.1.*")` zwraca `Major`, `Minor` i postać kanoniczną.
- `TryMatch(tagName, out int hotfix)`.
- Przyjmujemy liczby bez zer wiodących (`0` albo `[1-9]` i do 8 cyfr). Hotfix porównujemy jako `int`.

```
wzorzec:  ^(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})\.\*$
tag:      ^{Major}\.{Minor}\.(0|[1-9][0-9]{0,8})$
```

#### 4. Parser `ls-remote` i rozwiązywacz

**File**: `core/Git/LsRemoteParser.cs`, `core/Git/PatternResolver.cs`, `core/Git/PatternResolution.cs` (nowe)

**Intent**: Zamiana wyjścia `git ls-remote --tags` na tagi z commitami i wybór najwyższego pasującego tagu. Wszystko, co nie jest jednoznaczne, daje stan zamiast zgadywania.

**Contract**:
- **`PatternResolution`** to jeden z wyników:
  - `Resolved(tag, commitSha)`;
  - `NoMatch`;
  - `Ambiguous(reason)`;
  - `Error(kind)`, gdzie `kind` to `Timeout` albo `Failed`.
- **Parser:**
  - linia `<40 hex>\t<ref>`; bierze tylko refy `refs/tags/…`;
  - commit tagu to wartość z linii `^{}`, jeśli jest, w przeciwnym razie z linii bazowej.
  - Niejednoznaczność: linia w złym formacie, ten sam ref dwa razy z różnym SHA, linia `^{}` bez linii bazowej.
- **Rozwiązywacz:**
  - filtruje tagi przez `VersionPatternSpec.TryMatch` i bierze najwyższy hotfix;
  - brak dopasowań daje `NoMatch`;
  - tagi spoza formatu pomija bez śladu w wyniku.

#### 5. Klient git

**File**: `core/Git/IGitTagSource.cs`, `core/Git/GitCliTagSource.cs`, `core/Git/PatternResolutionService.cs`, `core/Git/GitServiceCollectionExtensions.cs` (nowe)

**Intent**: Uruchomienie `git ls-remote` tak, żeby token nie wyciekł, proces nie zawisł i tekst użytkownika nie trafił do argv jako opcja. Za interfejsem, żeby testy HTTP używały fałszywki.

**Contract**:
- `IGitTagSource.ListTagsAsync(string canonicalUrl, CancellationToken)` zwraca stdout albo błąd (`Timeout`/`Failed`, kod wyjścia).
- `PatternResolutionService.ResolveAsync(url, spec, ct)` składa klienta, parser i rozwiązywacz.
- **Uruchomienie procesu:**
  - `ProcessStartInfo.ArgumentList`, `UseShellExecute=false`, asynchroniczny odczyt stdout i stderr;
  - `WaitForExitAsync` z timeoutem z opcji, a przy timeoucie `Kill(entireProcessTree: true)`.
- **Argumenty i środowisko:**
  - argumenty: `-c credential.helper=`, `-c http.followRedirects=false`, `-c http.lowSpeedLimit=1000`, `-c http.lowSpeedTime=20`, na Windows `-c http.sslBackend=schannel`, potem `ls-remote --tags --exit-code --end-of-options <url>`;
  - środowisko: `GIT_TERMINAL_PROMPT=0`, `GCM_INTERACTIVE=false`, `GIT_ALLOW_PROTOCOL=https`;
  - nagłówek `Authorization: Basic base64(UserName:Token)` przez `GIT_CONFIG_COUNT=1`, `GIT_CONFIG_KEY_0=http.<url>.extraHeader`, `GIT_CONFIG_VALUE_0=…`.
- **Kody wyjścia:** 0 → lista tagów, 2 → brak tagów (`NoMatch`), inny → `Error(Failed)`.
- Logowanie: kod wyjścia i pierwsza linia stderr, nigdy token ani nagłówek.

#### 6. Testy jednostkowe i fałszywka

**File**: `api.Tests/Git/RepositoryUrlTests.cs`, `api.Tests/Git/VersionPatternSpecTests.cs`, `api.Tests/Git/LsRemoteParserTests.cs`, `api.Tests/Git/PatternResolverTests.cs` (nowe), `api.Tests/PortalFactory.cs`

**Intent**: Pokrycie reguł bezpieczeństwa i wyboru wersji bez sieci. Testy HTTP nie wołają prawdziwego git.

**Contract**:
- **URL — akceptowane:** `https://git.internal/team/app.git` → `https://git.internal/team/app`; wielkie litery w hoście.
- **URL — odrzucane:** `http://…`, host spoza listy, `https://user:pat@…`, `?x`, `#x`, `..`, `-oProxyCommand=x`, `ext::sh`, `file:///c:/x`, `\\srv\share`, `git@git.internal:team/app`, port niestandardowy.
- **Wzorzec:** przyjmuje `2.1.*` i `0.0.*`; odrzuca `2.*`, `2.1.7`, `v2.1.*`, `02.1.*`, `2.1.x`.
- **Parser** (próbki tekstu):
  - tag lekki;
  - tag adnotowany (commit z `^{}`, nie SHA obiektu tagu);
  - refy spoza `refs/tags/` ignorowane;
  - linia w złym formacie → `Ambiguous`.
- **Rozwiązywacz:**
  - `2.1.9` i `2.1.10` → `2.1.10`;
  - `2.1.7-rc1`, `2.1.7.1`, `2.1.07`, `v2.1.8` pominięte;
  - tylko tagi innej linii → `NoMatch`.
- **Fałszywka:** `FakeGitTagSource` w `PortalFactory` zwraca wyjście zależne od URL (np. `…/app` → tagi `2.1.9`, `2.1.10`; `…/down` → `Error`; `…/empty` → brak tagów) i liczy wywołania.

### Success Criteria:

#### Automated Verification:

- Kompilacja bez ostrzeżeń: `dotnet build api`
- Testy jednostkowe URL, wzorca, parsera i rozwiązywacza przechodzą: `dotnet test api.Tests --filter "FullyQualifiedName~securitycheck_portal.Tests.Git"`
- Wszystkie testy przechodzą: `dotnet test api.Tests`
- Token nie trafia do argumentów procesu: `git grep -n "Token" -- core/Git/GitCliTagSource.cs` pokazuje token tylko przy budowaniu zmiennych środowiskowych, nie w `ArgumentList`

#### Manual Verification:

- Z `Git:*` w user-secrets `PatternResolutionService` rozwiązuje prawdziwe repozytorium firmowe na tag i commit zgodne z `git ls-remote --tags` uruchomionym ręcznie (np. z małego testu lub endpointu z Phase 3)
- Serwer Git firmy akceptuje kanoniczny URL bez `.git` i nagłówek Basic z PAT

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 3: Endpointy API

### Overview

Minimalne API dla repozytoriów, wzorców i historii zmian. Każda zmiana i jej zdarzenie audytu trafiają do bazy w jednym `SaveChanges`. Wszystkie endpointy są chronione polityką domyślną.

### Changes Required:

#### 1. Endpointy repozytoriów i wzorców

**File**: `api/Repositories/RepositoryEndpoints.cs`, `api/Repositories/RepositoryContracts.cs` (nowe), `api/Program.cs`

**Intent**: Operacje z decyzji planowania, z kodami rozróżniającymi przypadki, żeby UI pokazało właściwy komunikat. Wzorzec jak `AuthEndpoints`: metoda rozszerzająca, `MapGroup("/api")`, rekordy, `IResult`.

**Contract**:
- `GET /api/repos` → 200, lista `{ id, url, name, activePatternCount }` posortowana po nazwie lub URL.
- `POST /api/repos` `{ url, name? }`:
  - 201 + repozytorium;
  - 400 przy niepoprawnym URL (`RepositoryUrl.TryNormalize`) lub nazwie dłuższej niż 200 znaków;
  - 409 przy duplikacie kanonicznego URL.
- `GET /api/repos/{id}` → 200 `{ id, url, name, createdAt, createdBy, patterns[] }` (wszystkie wzorce z `isActive` i ostatnim wynikiem); 404.
- `DELETE /api/repos/{id}` → 204; 409, gdy ma jakikolwiek wzorzec; 404.
- `GET /api/repos/{id}/events` → 200, zdarzenia repozytorium i jego wzorców, od najnowszych, maksymalnie 200; 404 dla nieistniejącego repozytorium.
- `POST /api/repos/{id}/patterns` `{ pattern }`:
  - 201 + wzorzec z wynikiem pierwszego rozwiązania;
  - 400 przy złej składni;
  - 404 przy nieistniejącym repozytorium;
  - 409 `{ "conflict": "exists" }` przy duplikacie aktywnego wzorca i `{ "conflict": "inactive" }` przy duplikacie nieaktywnego.
- `DELETE /api/patterns/{id}` → 204; 404.
- `POST /api/patterns/{id}/deactivate` i `/activate` → 200 + wzorzec. Operacja na wzorcu, który już jest w docelowym stanie, zwraca 200 bez nowego zdarzenia.
- `POST /api/patterns/{id}/resolve` → 200 + wzorzec z nowym wynikiem (także `Error`); 409 dla nieaktywnego; 404.
- Autor zmian to claim `sub` (`JwtIssuer.UserNameClaim`). Czas pochodzi z `TimeProvider`.
- Mapowanie naruszeń bazy: `23505` → 409, `23503` → 409.

#### 2. Testy integracyjne

**File**: `api.Tests/RepositoryEndpointsTests.cs` (nowy), `api.Tests/NoPublicEndpointsTests.cs`

**Intent**: Pokrycie każdej gałęzi kontraktu na `DatabasePortalFactory` z fałszywką Git. Potwierdzenie, że nowe endpointy nie są anonimowe.

**Contract**:
- **Repozytoria:**
  - dodanie → 201, a potem jest na liście;
  - `…/App.git/` i `…/app` → drugie daje 409;
  - URL spoza listy hostów → 400.
- **Wzorce:**
  - dodanie `2.1.*` → 201 ze stanem `Resolved`, tagiem `2.1.10` i commitem z fałszywki;
  - repozytorium „down” → 201 ze stanem `Error`;
  - repozytorium bez tagów → 201 ze stanem `NoMatch`;
  - duplikat → 409 `exists`; duplikat po dezaktywacji → 409 `inactive`;
  - `resolve` nieaktywnego → 409, a fałszywka Git nie została wywołana;
  - aktywacja → `resolve` znowu działa.
- **Usuwanie:**
  - repozytorium z wzorcem → 409;
  - po usunięciu wzorców → 204, a potem 404.
- **Dziennik:**
  - po sekwencji dodaj repo → dodaj wzorzec → dezaktywuj → aktywuj → usuń wzorzec → `/events` zawiera pięć zdarzeń we właściwej kolejności z autorem `alice`;
  - po usunięciu repozytorium zdarzenia zostają w bazie.
- **Ochrona:** `NoPublicEndpointsTests` bez zmian w liście wyjątków. Theory 401 rozszerzona o `GET /api/repos` i `POST /api/patterns/1/resolve`.

### Success Criteria:

#### Automated Verification:

- Kompilacja bez ostrzeżeń: `dotnet build api`
- Wszystkie testy przechodzą przy działającym Dockerze: `dotnet test api.Tests`
- Test ochronny nie wymaga zmiany listy wyjątków: `git diff --stat -- api.Tests/NoPublicEndpointsTests.cs` pokazuje tylko nowe `InlineData`

#### Manual Verification:

- Sekwencja z `api/securitycheck-portal.http` (dodanie repozytorium firmowego, wzorca, sprawdzenie, dezaktywacja) daje oczekiwane kody i poprawny tag i commit
- Po wyłączeniu sieci do serwera Git `resolve` zwraca w ciągu około `Git:TimeoutSeconds` wzorzec ze stanem `Error`, a API nie wisi

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 4: Ekrany SPA

### Overview

Strona główna z listą repozytoriów i formularzem dodawania. Strona `/repos/<id>` ze szczegółami, wzorcami, akcjami, przełącznikiem nieaktywnych i historią zmian. Teksty po polsku.

### Changes Required:

#### 1. Trasy

**File**: `web/app/routes.ts`

**Intent**: Nowa trasa szczegółów jako dziecko chronionego layoutu, z ID w ścieżce (`AGENTS.md:8`).

**Contract**: `route("repos/:repoId", "routes/repo-details.tsx")` w tablicy layoutu, przed catch-allem `*`.

#### 2. Strona główna

**File**: `web/app/routes/home.tsx`

**Intent**: Zastąpić placeholder listą repozytoriów i formularzem dodawania.

**Contract**:
- `clientLoader` pobiera `GET /api/repos` z przekazanym `request`.
- `clientAction` wysyła `POST /api/repos` i po 201 przekierowuje na `/repos/<id>`.
- Komunikaty:
  - 400 — „Podaj poprawny adres HTTPS repozytorium z dozwolonego serwera Git.”
  - 409 — „To repozytorium jest już dodane.”
- Pusty stan „Nie dodano jeszcze żadnego repozytorium.” zostaje, gdy lista jest pusta.
- Element listy: nazwa (albo ścieżka URL), URL i liczba aktywnych wzorców; link do szczegółów.

#### 3. Szczegóły repozytorium

**File**: `web/app/routes/repo-details.tsx` (nowy), `web/app/lib/patterns.ts` (nowy)

**Intent**: Wszystkie operacje na wzorcach i repozytorium w jednym miejscu. S-02 dopisze tu uruchamianie skanu.

**Contract**:
- **`clientLoader`:**
  - równolegle `GET /api/repos/:id` i `/events` (z `request`);
  - 404 → rzuca odpowiedź 404, a root `ErrorBoundary` pokazuje „Nie znaleziono strony”.
- **Przełącznik nieaktywnych:** parametr `?nieaktywne=1`. Bez niego lista pokazuje tylko aktywne wzorce. Nieaktywne są oznaczone „nieaktywny” i nie mają przycisku „Sprawdź”.
- **`clientAction`** rozróżnia pole `intent`: `addPattern`, `resolve`, `deactivate`, `activate`, `deletePattern`, `deleteRepo`.
  - Usuwanie (wzorca i repozytorium) wymaga potwierdzenia `window.confirm` w `onSubmit`.
  - Po `deleteRepo` przekierowanie na `/`.
- **Komunikaty:**
  - 400 wzorca — „Wzorzec musi mieć postać X.Y.*, np. 2.1.*.”
  - 409 `exists` — „Ten wzorzec już istnieje.”
  - 409 `inactive` — „Ten wzorzec istnieje jako nieaktywny — pokaż nieaktywne i aktywuj go.”
  - 409 przy usuwaniu repozytorium — „Najpierw usuń wszystkie wzorce tego repozytorium.”
  - błąd sieci — „Wystąpił nieoczekiwany błąd.”
- **Wynik wzorca** (formatowanie w `lib/patterns.ts`):
  - `Resolved` — „2.1.10 · commit a1b2c3d” (pełny SHA w `title`) + „sprawdzono <data i czas lokalny>”;
  - `NoMatch` — „Brak tagu pasującego do 2.1.*”;
  - `Ambiguous` — „Wynik niejednoznaczny — sprawdź tagi w repozytorium”;
  - `Error` — „Nie udało się sprawdzić (serwer Git niedostępny lub przekroczony czas)”;
  - brak wyniku — „Nie sprawdzono”.
- **Historia zmian:** lista zdarzeń od najnowszych: data i czas, autor, opis po polsku (np. „dodał wzorzec 2.1.*”, „dezaktywował wzorzec 2.1.*”).

### Success Criteria:

#### Automated Verification:

- Typy i trasy są poprawne: `cd web && npm run typecheck`
- Build SPA z kopiowaniem przechodzi: `cd web && npm run build:api`
- API nadal się kompiluje i testy przechodzą: `dotnet build api` oraz `dotnet test api.Tests`

#### Manual Verification:

- Dodanie repozytorium firmowego i wzorca pokazuje tag i commit zgodne z `git ls-remote --tags`
- Duplikat URL (np. z `.git`) i zły wzorzec pokazują właściwe polskie komunikaty
- Dezaktywacja ukrywa wzorzec; „Pokaż nieaktywne” go pokazuje bez przycisku „Sprawdź”; ponowne dodanie daje komunikat o nieaktywnym; aktywacja przywraca „Sprawdź”
- Usunięcie repozytorium z wzorcami jest zablokowane z komunikatem; po usunięciu wzorców działa i wraca na listę
- Historia zmian pokazuje wszystkie wykonane operacje z Twoim loginem
- Wejście na `/repos/999999` pokazuje „Nie znaleziono strony”

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 5: Dokumentacja

### Overview

Dokumenty opisują to, co powstało: nowy projekt, komendy bazy, konfigurację Git, wymóg Dockera dla testów bazy i to, że PAT ma także app pool API.

### Changes Required:

#### 1. AGENTS.md

**File**: `AGENTS.md`

**Intent**: Nowy agent ma od razu wiedzieć, jak uruchomić bazę, testy i rozwiązywanie wzorca.

**Contract**:
- **Project Structure:** `core/` (dane i Git współdzielone z przyszłym workerem).
- **Build and Development Commands:**
  - `dotnet tool restore`;
  - migracje (`dotnet ef migrations add … --project core --startup-project api`, `dotnet ef database update …`);
  - user-secrets: `ConnectionStrings:Portal` i `Git:Token`, `Git:AllowedHosts:0`, opcjonalnie `Git:UserName`, `Git:ExecutablePath`.
- **Testing:** testy bazy wymagają działającego Dockera i bez niego są pomijane; zastąpić zdanie o braku sieci dokładnym opisem.
- Zdanie o PostgreSQL „planned but not yet installed” (`AGENTS.md:3`) → zainstalowane.

#### 2. Infrastruktura

**File**: `context/foundation/infrastructure.md`

**Intent**: Usunąć niespójność: PAT używa także API (IIS app pool), nie tylko worker.

**Contract**:
- Ryzyko 7 (`:118`) i wiersz Risk Register o skompromitowanym Trivy wymieniają także tożsamość app pool jako posiadacza PAT (tylko do odczytu).
- Secrets (`:138`) wymienia `Git:Token` i `Git:AllowedHosts` z nazwy.
- Getting Started, weryfikacja: dodanie wzorca pokazuje rozwiązany tag.

#### 3. Roadmapa

**File**: `context/foundation/roadmap.md`

**Intent**: Zamknąć Unknowns S-01 decyzjami z tego planu.

**Contract**: W S-01 → Unknowns oba wiersze dostają adnotację „rozstrzygnięte 2026-09-28”:
- tagi `X.Y.N` bez prefiksu, najwyższy hotfix liczony numerycznie;
- HTTPS + PAT tylko do odczytu, używany przez API i worker (plan `repo-version-pattern`).

Pola `Status` nie ruszamy.

#### 4. Plik smoke testu

**File**: `api/securitycheck-portal.http`

**Intent**: Dopisać sekwencję S-01 po zalogowaniu.

**Contract**: Zmienne `@repoUrl` i `@pattern`, a potem żądania: dodaj repozytorium → dodaj wzorzec → `resolve` → dezaktywuj → `resolve` (409) → aktywuj → historia zdarzeń. Bez prawdziwych danych w pliku.

### Success Criteria:

#### Automated Verification:

- W AGENTS.md nie ma już „planned but not yet installed”: `git grep -n "planned but not yet installed" -- AGENTS.md` nic nie zwraca
- Build, testy i typecheck przechodzą: `dotnet build api`, `dotnet test api.Tests`, `cd web && npm run typecheck`

#### Manual Verification:

- Przegląd `AGENTS.md` i `infrastructure.md` potwierdza zgodność z tym, co powstało
- Sekwencja z `api/securitycheck-portal.http` przechodzi na prawdziwym serwerze Git

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Testing Strategy

### Unit Tests:

- `RepositoryUrl`: reguły akceptacji i odrzucania, normalizacja (Phase 2.6).
- `VersionPatternSpec`: składnia wzorca i dopasowanie tagów (Phase 2.6).
- `LsRemoteParser`: tagi lekkie i adnotowane, niejednoznaczności (Phase 2.6).
- `PatternResolver`: numeryczne sortowanie, pomijanie tagów spoza formatu, `NoMatch` (Phase 2.6).

### Integration Tests:

- Schemat: unikalność, blokada usuwania, zdarzenia po usunięciu (Phase 1.6, prawdziwy PostgreSQL).
- Kontrakt endpointów: wszystkie kody, stany rozwiązania, reguły wzorca nieaktywnego, dziennik zdarzeń (Phase 3.2, PostgreSQL + fałszywka Git).
- Ochrona: nowe endpointy nie są anonimowe (Phase 3.2).
- `GitCliTagSource` z prawdziwym serwerem Git sprawdzamy tylko ręcznie.

### Manual Testing Steps:

1. Uruchomić Rancher Desktop, a potem `dotnet test api.Tests`: testy bazy przechodzą, a nie są pomijane.
2. Ustawić `ConnectionStrings:Portal` i `Git:*` w user-secrets, zrobić `dotnet ef database update`, uruchomić API i `npm run dev`.
3. Dodać repozytorium firmowe i wzorzec: wynik zgodny z ręcznym `git ls-remote --tags`.
4. Przejść dezaktywację, aktywację, usuwanie i blokadę usuwania repozytorium.
5. Wyłączyć sieć do serwera Git i kliknąć „Sprawdź”: stan błędu w ciągu około 30 s.
6. Sprawdzić historię zmian.

## Performance Considerations

`git ls-remote --tags` pobiera listę refów bez obiektów. Przy repozytoriach z tysiącami tagów to setki KB i zwykle ułamek sekundy do kilku sekund. Wywołanie jest synchroniczne w żądaniu HTTP, a limit czasu pilnuje `Git:TimeoutSeconds`. Lista repozytoriów i szczegóły nie wołają git; pokazują zapisany wynik.

## Migration Notes

Pierwsza migracja tworzy trzy puste tabele. Nie ma danych do przenoszenia. Na serwerze bazę i migracje wdraża F-02 (`efbundle`, kolejność z `infrastructure.md:191-195`). Lokalnie wystarczy `dotnet ef database update`.

## References

- Research: `context/changes/repo-version-pattern/research.md`
- Roadmapa: `context/foundation/roadmap.md` (S-01 `repo-version-pattern`)
- PRD: `context/foundation/prd.md` (FR-002, FR-003, Guardrails)
- Infrastruktura: `context/foundation/infrastructure.md` (`:33` commit przy skanie, `:138` sekrety, `:186` wspólny projekt z workerem)
- Wzorzec modułu i testów z F-01: `api/Auth/AuthEndpoints.cs`, `api.Tests/PortalFactory.cs`
- Poprzedni plan: `context/archive/2026-09-26-authenticated-app-shell/plan.md`
- git ls-remote: https://git-scm.com/docs/git-ls-remote

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Wspólny projekt i warstwa danych

#### Automated

- [ ] 1.1 API i `core` kompilują się bez ostrzeżeń: `dotnet build api`
- [ ] 1.2 Model nie ma zmian bez migracji: `dotnet ef migrations has-pending-model-changes`
- [ ] 1.3 Wszystkie testy przechodzą przy działającym Dockerze: `dotnet test api.Tests`
- [ ] 1.4 Przy wyłączonym Dockerze testy F-01 przechodzą, a testy bazy są pominięte

#### Manual

- [ ] 1.5 Bez `ConnectionStrings:Portal` API odmawia startu z czytelnym błędem walidacji
- [ ] 1.6 `dotnet ef database update` tworzy tabele w lokalnym PostgreSQL

### Phase 2: Rozwiązywanie wzorca

#### Automated

- [ ] 2.1 Kompilacja bez ostrzeżeń: `dotnet build api`
- [ ] 2.2 Testy jednostkowe URL, wzorca, parsera i rozwiązywacza przechodzą
- [ ] 2.3 Wszystkie testy przechodzą: `dotnet test api.Tests`
- [ ] 2.4 Token nie trafia do argumentów procesu

#### Manual

- [ ] 2.5 Prawdziwe repozytorium firmowe rozwiązuje się na tag i commit zgodne z ręcznym `git ls-remote --tags`
- [ ] 2.6 Serwer Git firmy akceptuje kanoniczny URL bez `.git` i nagłówek Basic z PAT

### Phase 3: Endpointy API

#### Automated

- [ ] 3.1 Kompilacja bez ostrzeżeń: `dotnet build api`
- [ ] 3.2 Wszystkie testy przechodzą przy działającym Dockerze: `dotnet test api.Tests`
- [ ] 3.3 Test ochronny nie wymaga zmiany listy wyjątków

#### Manual

- [ ] 3.4 Sekwencja z `api/securitycheck-portal.http` daje oczekiwane kody i poprawny tag i commit
- [ ] 3.5 Bez sieci do serwera Git `resolve` zwraca stan `Error` w czasie limitu, a API nie wisi

### Phase 4: Ekrany SPA

#### Automated

- [ ] 4.1 Typy i trasy są poprawne: `cd web && npm run typecheck`
- [ ] 4.2 Build SPA z kopiowaniem przechodzi: `cd web && npm run build:api`
- [ ] 4.3 API nadal się kompiluje i testy przechodzą

#### Manual

- [ ] 4.4 Dodanie repozytorium firmowego i wzorca pokazuje tag i commit zgodne z `git ls-remote --tags`
- [ ] 4.5 Duplikat URL i zły wzorzec pokazują właściwe polskie komunikaty
- [ ] 4.6 Dezaktywacja, „Pokaż nieaktywne”, ponowne dodanie i aktywacja działają zgodnie z regułami
- [ ] 4.7 Usunięcie repozytorium z wzorcami jest zablokowane; po usunięciu wzorców działa
- [ ] 4.8 Historia zmian pokazuje wszystkie wykonane operacje z loginem autora
- [ ] 4.9 `/repos/999999` pokazuje „Nie znaleziono strony”

### Phase 5: Dokumentacja

#### Automated

- [ ] 5.1 W AGENTS.md nie ma już „planned but not yet installed”
- [ ] 5.2 Build, testy i typecheck przechodzą

#### Manual

- [ ] 5.3 Przegląd `AGENTS.md` i `infrastructure.md` potwierdza zgodność z tym, co powstało
- [ ] 5.4 Sekwencja z `api/securitycheck-portal.http` przechodzi na prawdziwym serwerze Git
