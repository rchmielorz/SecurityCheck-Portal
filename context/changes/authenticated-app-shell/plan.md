# Authenticated App Shell Implementation Plan

## Overview

F-01 z roadmapy ([#1](https://github.com/rchmielorz/SecurityCheck-Portal/issues/1)). Portal dostaje logowanie loginem i hasłem, weryfikowane przez LDAPS w Active Directory. Dostęp mają tylko członkowie jednej skonfigurowanej grupy AD, także przez członkostwo zagnieżdżone. API wydaje JWT ważny 8 godzin i zapisuje go w ciasteczku HttpOnly/Secure/SameSite=Strict. Każdy endpoint API poza logowaniem wymaga zalogowania; pilnuje tego automatyczny test ochronny w pierwszym projekcie testowym. UI to polska powłoka SPA serwowana spod tego samego adresu co API. Zmiana odblokowuje S-01 (`repo-version-pattern`), którego pierwszy ekran z danymi musi być chroniony od początku.

## Current State Analysis

- `api/Program.cs` to nietknięty szablon `weatherforecast`: `AddOpenApi`, `UseHttpsRedirection`, jeden endpoint, brak uwierzytelniania i autoryzacji.
- `api/securitycheck-portal.csproj` ma tylko `Microsoft.AspNetCore.OpenApi` 10.0.12 (`net10.0`, `RootNamespace` `securitycheck_portal`).
- `web/react-router.config.ts` ma `ssr: true`. `infrastructure.md` wymaga trybu SPA (`ssr: false`), a klient ma być serwowany z `wwwroot` API.
- `web/app/root.tsx` ładuje Google Fonts: przeglądarka łączy się z zewnętrznym serwerem. `lang="en"`, komunikaty błędów po angielsku.
- `web/app/routes.ts` rejestruje tylko `routes/home.tsx`, który renderuje szablonowy `welcome/`.
- W repo nie ma projektu testowego ani runnera testów UI (AGENTS.md, Testing).
- Poprzednie założenie (`infrastructure.md`, roadmapa F-01) mówiło o Windows SSO przez Negotiate. W tym planowaniu zapadła inna decyzja: logowanie przez LDAP i JWT, niezależne od platformy, bo aplikacja może trafić kiedyś na Linuksa.

## Desired End State

- Wejście na dowolny adres portalu bez ważnej sesji prowadzi do polskiego formularza logowania. Nie da się pobrać ani zobaczyć żadnych danych.
- Poprawny login i hasło członka grupy AD kończą się przekierowaniem do strony, o którą prosił użytkownik. Nagłówek pokazuje nazwę portalu, nazwę wyświetlaną użytkownika i przycisk „Wyloguj”. Strona główna pokazuje pusty stan.
- Złe hasło, konto spoza grupy, niedostępny serwer LDAP i przekroczony limit prób dają cztery różne, zrozumiałe komunikaty po polsku.
- Po 8 godzinach albo po wylogowaniu każde wywołanie API zwraca 401, a SPA wraca do formularza logowania.
- `dotnet test api.Tests` przechodzi. Test ochronny nie przepuszcza żadnego anonimowego endpointu spoza listy wyjątków: `POST /api/auth/login` i fallback SPA.
- Po `cd web && npm run build:api` samo API (`dotnet run --project api`) serwuje UI i `/api` pod jednym adresem.

### Key Discoveries:

- W `api/Program.cs:1-41` nie ma nic do zachowania poza `AddOpenApi`/`MapOpenApi` (tylko w Development). `WeatherForecast` usuwamy w całości.
- `web/react-router.config.ts:5-7`: `ssr: true` → `false`. W trybie SPA serwerowe `loader`/`action` są dostępne tylko w root; w trasach używamy `clientLoader`/`clientAction`. Przed pierwszym użyciem przeczytać `web/.agents/skills/react-router/references/framework-mode.md` (reguła z AGENTS.md).
- Ładowanie Google Fonts w `web/app/root.tsx:13-24` i `--font-sans: "Inter"` w `web/app/app.css:3-6` usuwamy na rzecz systemowego stosu czcionek.
- Katalog `wwwroot/` jest zakomentowany w `.gitignore:40-41`; trzeba dodać `api/wwwroot/`.
- Novell.Directory.Ldap.NETStandard 4.0.0 (marzec 2025) celuje w `net6.0` i `netstandard2.0` i działa na `net10.0` na Windows i Linux.

## What We're NOT Doing

- Windows SSO / Negotiate, OIDC, lokalna tabela użytkowników, rejestracja, reset hasła.
- Tokeny odświeżające i odwoływanie tokenów przed wygaśnięciem. Usunięcie z grupy AD działa najpóźniej po 8 godzinach.
- Sprawdzanie grupy AD przy każdym żądaniu. Sprawdzamy ją tylko przy logowaniu.
- Role i uprawnienia poza jedną grupą (PRD: model płaski).
- Baza danych i EF Core. F-01 niczego nie zapisuje.
- `/health` i endpointy diagnostyczne (należą do F-02 `deploy-skeleton`, podobnie jak CI i skrypt wdrożenia).
- Biblioteka i18n. Teksty UI są po polsku, zapisane na sztywno.
- Nagłówek `Authorization: Bearer` jako alternatywa dla ciasteczka.
- Ekrany domenowe (repozytoria, wzorce). To S-01.

## Implementation Approach

API odpowiada za całe uwierzytelnianie. SPA tylko wyświetla formularz i reaguje na 401. Kolejność:

1. Najpierw API z mechanizmem bezpieczeństwa i abstrakcją LDAP za interfejsem.
2. Potem testy, które wymuszają regułę „nic publicznego poza listą” i pokrywają wszystkie wyniki logowania na fałszywym LDAP.
3. Następnie powłoka SPA.
4. Na końcu dokumentacja, żeby opisywała to, co faktycznie powstało.

Domyślna polityka autoryzacji (`FallbackPolicy` = zalogowany użytkownik) sprawia, że każdy przyszły endpoint jest domyślnie chroniony. Wyjątek wymaga jawnego `AllowAnonymous` i dopisania go do listy w teście ochronnym.

## Critical Implementation Details

- **Puste hasło = anonimowy bind.** AD traktuje simple bind z pustym hasłem jako anonimowy i zwraca sukces. Pusty albo złożony z samych białych znaków login lub hasło odrzucamy odpowiedzią 400, zanim otworzymy połączenie LDAP. Ten przypadek ma własny test.
- **Kolejność middleware.** `UseStaticFiles` stoi przed `UseAuthentication`/`UseAuthorization`, bo pliki SPA muszą być dostępne bez logowania: wyświetlają formularz i nie zawierają danych. Fallback do `index.html` nie może przechwytywać `/api/*`. Najpierw mapujemy fallback `/api/{**rest}`, który zwraca 404 (bez logowania dostaje 401 przez politykę domyślną), a dopiero potem `MapFallbackToFile("index.html").AllowAnonymous()`.
- **Ciasteczko Secure w testach.** Klient `WebApplicationFactory` domyślnie używa `http://localhost`, więc nie odeśle ciasteczka `Secure`. W testach trzeba ustawić `ClientOptions.BaseAddress = https://localhost`. Przeglądarki traktują `http://localhost` jako bezpieczny kontekst, więc w dev przez Vite ciasteczko `Secure` działa.
- **Certyfikat LDAPS.** Walidację certyfikatu serwera LDAP ustawiamy jawnie: akceptujemy tylko `SslPolicyErrors.None`. Nie polegamy na domyślnym zachowaniu biblioteki. Certyfikat wewnętrznego CA musi być zaufany w systemie.

## Phase 1: Uwierzytelnianie w API

### Overview

Usuwamy szablon i dodajemy: logowanie LDAP za interfejsem, JWT w ciasteczku, endpointy `login`/`logout`/`me`, domyślną politykę „wymaga zalogowania”, limit prób logowania, serwowanie plików SPA z fallbackiem oraz walidację konfiguracji przy starcie.

### Changes Required:

#### 1. Pakiety i konfiguracja

**File**: `api/securitycheck-portal.csproj`, `api/appsettings.json`, `api/appsettings.Development.json`

**Intent**: Dodać pakiety `Novell.Directory.Ldap.NETStandard` (4.0.0) i `Microsoft.AspNetCore.Authentication.JwtBearer` (10.0.x), zgodne z wersją OpenAPI. Opisać ustawienia logowania bez sekretów. Włączyć user-secrets dla dev.

**Contract**: Sekcje `Auth:Ldap` i `Auth:Jwt` z polami:

- `Auth:Ldap`:
  - `Host`, `Port` (domyślnie 636);
  - `UpnSuffix` (bind jako `{login}@{UpnSuffix}`);
  - `SearchBase`;
  - `AllowedGroupDn`;
  - `ConnectTimeoutSeconds` (domyślnie 5).
- `Auth:Jwt`:
  - `Issuer`, `Audience`;
  - `LifetimeHours` (8);
  - `SigningKey` — wyłącznie w user-secrets lub zmiennej środowiskowej, nigdy w `appsettings*.json`.

W `appsettings.json` wartości domyślne i puste miejsca na ustawienia specyficzne dla firmy. W `csproj` `UserSecretsId`.

#### 2. Opcje z walidacją przy starcie

**File**: `api/Auth/AuthOptions.cs` (nowy)

**Intent**: Typowane opcje `LdapOptions` i `JwtOptions`. Aplikacja nie startuje przy brakującym lub słabym ustawieniu, zamiast zawieść dopiero przy pierwszym logowaniu.

**Contract**: `ValidateOnStart`. `SigningKey` musi mieć co najmniej 32 bajty (HS256). `Host`, `UpnSuffix`, `SearchBase`, `AllowedGroupDn`, `Issuer` i `Audience` nie mogą być puste. `LifetimeHours` > 0.

#### 3. Abstrakcja uwierzytelniania LDAP

**File**: `api/Auth/ILdapAuthenticator.cs`, `api/Auth/LdapAuthenticator.cs`, `api/Auth/LdapFilter.cs` (nowe)

**Intent**: Jedno miejsce, które zamienia login i hasło na jeden z czterech wyników. Warstwę HTTP da się dzięki temu testować z fałszywą implementacją. Implementacja Novell:

1. łączy się przez LDAPS z jawną walidacją certyfikatu i timeoutem;
2. robi bind jako `{login}@{UpnSuffix}`;
3. uwierzytelnieniem samego użytkownika (bez konta serwisowego) szuka jego wpisu z warunkiem członkostwa w grupie, także zagnieżdżonego.

**Contract**:

- `Task<LdapAuthResult> AuthenticateAsync(string userName, string password, CancellationToken)`.
- `LdapAuthResult` to jeden z wyników:
  - `Success(userName, displayName)`;
  - `InvalidCredentials` (LDAP ResultCode 49);
  - `NotInGroup` (bind się udał, ale filtr nic nie znalazł);
  - `Unavailable` (połączenie, TLS, timeout).
- Login akceptujemy tylko jako `^[A-Za-z0-9._-]{1,64}$`. Inny format daje `InvalidCredentials` bez połączenia z LDAP.
- `LdapFilter.Escape` escapuje wartości filtra zgodnie z RFC 4515 (`\ * ( ) NUL`), jako zabezpieczenie dodatkowe względem walidacji formatu.
- Filtr wyszukiwania:

```
(&(objectClass=user)(sAMAccountName={escaped login})(memberOf:1.2.840.113556.1.4.1941:={escaped AllowedGroupDn}))
```

  Reguła `1.2.840.113556.1.4.1941` (LDAP_MATCHING_RULE_IN_CHAIN) rozwija zagnieżdżone grupy. Pobieramy atrybut `displayName`; gdy jest pusty, używamy loginu.

#### 4. JWT w ciasteczku

**File**: `api/Auth/AuthCookie.cs`, `api/Auth/JwtIssuer.cs` (nowe), `api/Program.cs`

**Intent**: Wydawanie i weryfikacja tokenu. Handler JwtBearer czyta token wyłącznie z ciasteczka.

**Contract**:

- Ciasteczko `sc_auth` z flagami `HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/` i `Expires` równym wygaśnięciu tokenu.
- Token HS256 z claimami `sub` (login), `name` (displayName), `iss`, `aud`, `exp` (+`LifetimeHours`).
- Walidacja sprawdza wystawcę, odbiorcę, czas życia i klucz, z `ClockSkew` 1 minuta.
- `JwtBearerEvents.OnMessageReceived` pobiera token z `sc_auth`.
- Nieudane uwierzytelnienie zwraca 401 bez treści.

#### 5. Endpointy uwierzytelniania

**File**: `api/Auth/AuthEndpoints.cs` (nowy), `api/Program.cs`

**Intent**: Minimalne API do logowania, wylogowania i odczytu bieżącego użytkownika. Kody odpowiedzi rozróżniają przypadki, żeby UI mogło pokazać właściwy komunikat.

**Contract**:

- `POST /api/auth/login` — `AllowAnonymous`, limit prób. Body `{ userName, password }`. Odpowiedzi:
  - `200` + ciasteczko + `{ userName, displayName }` przy sukcesie;
  - `400` przy pustym albo złożonym z samych białych znaków loginie lub haśle (bez kontaktu z LDAP);
  - `401` przy `InvalidCredentials`;
  - `403` przy `NotInGroup`;
  - `503` przy `Unavailable`;
  - `429` po przekroczeniu limitu.
- `POST /api/auth/logout` — wymaga zalogowania. Usuwa `sc_auth`, zwraca `204`.
- `GET /api/me` — wymaga zalogowania. Zwraca `{ userName, displayName }` z claimów.
- Nieudane logowania logujemy z loginem i adresem IP, nigdy z hasłem.

#### 6. Pipeline, polityka domyślna, limit prób, SPA

**File**: `api/Program.cs`

**Intent**: Zastąpić szablon pipeline'em, w którym ochrona jest domyślna, a dostęp anonimowy wymaga jawnego wyjątku.

**Contract**:

- `AddAuthorization` z `FallbackPolicy` wymagającym zalogowanego użytkownika.
- `AddRateLimiter`: polityka `login` typu fixed window, 5 żądań na minutę na adres IP klienta, odrzucenie z `429`.
- Kolejność middleware:
  1. poza Development: `UseHsts` + `UseHttpsRedirection`;
  2. `UseStaticFiles`;
  3. `UseRateLimiter`;
  4. `UseAuthentication`;
  5. `UseAuthorization`;
  6. endpointy `/api`;
  7. fallback `/api/{**rest}` → 404;
  8. `MapFallbackToFile("index.html").AllowAnonymous()`.
- `MapOpenApi` zostaje, tylko w Development, i jest chronione polityką domyślną.
- Na końcu pliku `public partial class Program;` dla `WebApplicationFactory`.
- `WeatherForecast` i jego endpoint usuwamy.

### Success Criteria:

#### Automated Verification:

- API kompiluje się bez ostrzeżeń nullable w nowych plikach: `dotnet build api`
- W repozytorium nie ma już `weatherforecast`: `git grep -i weatherforecast -- api` nic nie zwraca
- `appsettings*.json` nie zawiera `SigningKey` z wartością: `git grep -n "SigningKey" -- api/appsettings*.json` zwraca tylko pusty placeholder albo nic

#### Manual Verification:

- Bez ustawionego `Auth:Jwt:SigningKey` API odmawia startu z czytelnym błędem walidacji opcji
- Z kluczem w user-secrets i konfiguracją AD: `POST /api/auth/login` z prawdziwym kontem z grupy zwraca 200 i ciasteczko `sc_auth` (HttpOnly, Secure, SameSite=Strict), a potem `GET /api/me` zwraca nazwę wyświetlaną

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 2: Projekt testowy i test ochronny

### Overview

Pierwszy projekt testowy w repo. Test ochronny pilnuje, że nic poza listą wyjątków nie jest dostępne bez zalogowania. Testy integracyjne na fałszywym LDAP pokrywają każdy wynik logowania, flagi ciasteczka i limit prób. Testy jednostkowe sprawdzają escapowanie filtra.

### Changes Required:

#### 1. Projekt testowy

**File**: `api.Tests/securitycheck-portal.Tests.csproj` (nowy, osobny folder najwyższego poziomu zgodnie z AGENTS.md)

**Intent**: Projekt xUnit (`dotnet new xunit`) z referencją do `api/securitycheck-portal.csproj` i `Microsoft.AspNetCore.Mvc.Testing` 10.0.x.

**Contract**: `dotnet test api.Tests` uruchamia wszystkie testy bez dostępu do sieci i AD.

#### 2. Fabryka testowa

**File**: `api.Tests/PortalFactory.cs` (nowy)

**Intent**: `WebApplicationFactory<Program>` w środowisku `Testing` z fałszywą konfiguracją, podmienionym `ILdapAuthenticator` i tymczasowym `wwwroot` zawierającym `index.html`. Klient działa na `https://localhost`, żeby ciasteczko `Secure` było odsyłane.

**Contract**:

- Konfiguracja w pamięci:
  - `Auth:Jwt:SigningKey` — losowe 32 bajty;
  - `Issuer`, `Audience`;
  - `Auth:Ldap:*` — fikcyjne wartości spełniające walidację.
- `FakeLdapAuthenticator` zwraca wynik zależny od loginu, np. `alice` + poprawne hasło → `Success`, `bob` → `NotInGroup`, `down` → `Unavailable`, złe hasło → `InvalidCredentials`. Liczy wywołania, żeby test pustego hasła mógł sprawdzić, że LDAP nie został wywołany.

#### 3. Test ochronny

**File**: `api.Tests/NoPublicEndpointsTests.cs` (nowy)

**Intent**: Wymusza NFR „brak publicznej strony” dla każdego przyszłego endpointu. Dodanie anonimowego endpointu bez świadomego dopisania go do listy psuje build testów.

**Contract**:

- Z `EndpointDataSource` pobieramy wszystkie `RouteEndpoint`. Metadane `IAllowAnonymous` mogą mieć wyłącznie:
  - `POST /api/auth/login`;
  - fallback do `index.html`.
- `AuthorizationOptions.FallbackPolicy` nie jest `null` i wymaga zalogowanego użytkownika.
- Żądania bez ciasteczka:
  - `GET /api/me`, `POST /api/auth/logout` i `GET /api/does-not-exist` zwracają `401`; to ostatnie nie zwraca HTML;
  - `GET /` i `GET /repos/42` (głęboki link) zwracają `200` z `index.html`.

#### 4. Testy logowania

**File**: `api.Tests/AuthEndpointsTests.cs` (nowy)

**Intent**: Pokryć wszystkie gałęzie kontraktu `POST /api/auth/login` i cykl logowanie → `me` → wylogowanie.

**Contract**:

- **Sukces:**
  - `200`, body `{ userName, displayName }`;
  - `Set-Cookie: sc_auth` z `HttpOnly`, `Secure`, `SameSite=Strict` i `Expires` około teraz + 8 h;
  - następnie `GET /api/me` → `200` z tą samą nazwą.
- **Wylogowanie:**
  - `POST /api/auth/logout` → `204` i ciasteczko usunięte;
  - następnie `GET /api/me` → `401`.
- **Pozostałe przypadki:**
  - złe hasło → `401`;
  - użytkownik spoza grupy → `403`;
  - LDAP niedostępny → `503`;
  - pusty lub złożony z samych spacji login albo hasło → `400`, a fałszywy LDAP nie został wywołany;
  - szóste żądanie w ciągu minuty z tego samego adresu → `429`;
  - token z innym kluczem albo przeterminowany → `401` na `GET /api/me`.

#### 5. Testy filtra i walidacji loginu

**File**: `api.Tests/LdapFilterTests.cs` (nowy)

**Intent**: Zabezpieczenie przed wstrzyknięciem do filtra LDAP.

**Contract**:

- `LdapFilter.Escape` zamienia `\ * ( )` i `NUL` na sekwencje `\5c \2a \28 \29 \00`.
- Walidacja loginu odrzuca `a*`, `a)(cn=*`, `DOMENA\user`, `user@x` i login dłuższy niż 64 znaki, a przyjmuje `jan.kowalski`.

#### 6. Komenda testów w AGENTS.md

**File**: `AGENTS.md`

**Intent**: Zastąpić zdanie „No test project … exists yet” komendą `dotnet test api.Tests` i zasadą, że każdy nowy wyjątek anonimowy wymaga zmiany listy w `NoPublicEndpointsTests`. Część merytoryczną dokumentacji robi Phase 4; tu tylko to, co zmienia się razem z projektem testowym.

**Contract**: Sekcja `## Testing` w `AGENTS.md`.

### Success Criteria:

#### Automated Verification:

- Wszystkie testy przechodzą: `dotnet test api.Tests`
- Test ochronny zawodzi, gdy tymczasowo dodamy anonimowy endpoint spoza listy (sprawdzić raz i cofnąć zmianę)

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 3: Powłoka SPA

### Overview

UI przechodzi w tryb SPA. Vite w dev przekazuje `/api` do Kestrela. Dodajemy trasę `/login`, chroniony layout z nagłówkiem, pusty ekran główny, teksty po polsku, systemowe czcionki i skrypt kopiujący build do `api/wwwroot`.

### Changes Required:

#### 1. Tryb SPA i proxy dev

**File**: `web/react-router.config.ts`, `web/vite.config.ts`

**Intent**: `ssr: false`, żeby build dawał statyczny `build/client/index.html` serwowany przez API. W dev Vite przekazuje `/api` do API na `http://localhost:5143`, więc przeglądarka widzi jeden adres.

**Contract**: `ssr: false`; `server.proxy` z wpisem `/api` → `http://localhost:5143`.

#### 2. Root: język, czcionki, stany ładowania i błędu

**File**: `web/app/root.tsx`, `web/app/app.css`

**Intent**: Usunąć zależność od Google Fonts, ustawić `lang="pl"`, przetłumaczyć `ErrorBoundary`, dodać `HydrateFallback` („Ładowanie…”) wymagany w trybie SPA.

**Contract**:

- Z `root.tsx` znika eksport `links` z Google Fonts.
- `--font-sans` w `app.css` bez `"Inter"`, tylko stos systemowy.
- `ErrorBoundary`: „Nie znaleziono strony” dla 404, „Wystąpił nieoczekiwany błąd” dla pozostałych.

#### 3. Klient API

**File**: `web/app/lib/api.ts` (nowy)

**Intent**: Wspólny `fetch` do `/api` z JSON i `credentials: "same-origin"`. Przy 401 w chronionych trasach przekierowuje na `/login?next=<bieżąca ścieżka>`.

**Contract**:

- `apiFetch(path, init)` zwraca `Response`.
- `requireUser()` wywołuje `GET /api/me` i zwraca `{ userName, displayName }` albo rzuca `redirect("/login?next=…")`.
- `safeNext(next)` przepuszcza tylko ścieżki względne zaczynające się od `/`, ale nie od `//` ani `/\`; każdą inną wartość zamienia na `/`. Zapobiega to otwartemu przekierowaniu.

#### 4. Trasy

**File**: `web/app/routes.ts`, `web/app/routes/login.tsx` (nowy), `web/app/routes/app-layout.tsx` (nowy), `web/app/routes/home.tsx`, usunięcie `web/app/welcome/`

**Intent**: Trasa logowania dostępna bez sesji. Wszystkie pozostałe trasy siedzą pod layoutem, który w `clientLoader` wymaga użytkownika. S-01 dopisze swoje ekrany jako dzieci tego layoutu.

**Contract**:

- `routes.ts`: `route("login", "routes/login.tsx")` oraz `layout("routes/app-layout.tsx", [index("routes/home.tsx")])`.
- `login.tsx`:
  - formularz z polami „Login” i „Hasło” oraz przyciskiem „Zaloguj”;
  - `clientAction` wysyła `POST /api/auth/login`;
  - komunikaty po polsku:
    - `400` — „Podaj login i hasło.”
    - `401` — „Nieprawidłowy login lub hasło.”
    - `403` — „Twoje konto nie ma dostępu do portalu.”
    - `503` — „Usługa logowania jest niedostępna. Spróbuj później.”
    - `429` — „Zbyt wiele prób logowania. Odczekaj minutę.”
  - po sukcesie przekierowanie na `safeNext(next)`;
  - gdy `GET /api/me` zwraca `200`, zalogowany użytkownik od razu przechodzi na `/`.
- `app-layout.tsx`:
  - `clientLoader` = `requireUser()`;
  - nagłówek z nazwą „SecurityCheck Portal”, `displayName` i przyciskiem „Wyloguj” (`POST /api/auth/logout`, potem `/login`);
  - `<Outlet />`.
- `home.tsx`: tytuł strony „SecurityCheck Portal” i pusty stan „Nie dodano jeszcze żadnego repozytorium.”

#### 5. Build do API

**File**: `web/package.json`, `web/scripts/copy-to-api.mjs` (nowy), `.gitignore`

**Intent**: Jedna komenda buduje SPA i kopiuje `build/client` do `api/wwwroot`, żeby API serwowało UI pod jednym adresem lokalnie i w F-02. Kopiowanie w Node działa niezależnie od platformy.

**Contract**:

- Skrypt `build:api` = `react-router build && node scripts/copy-to-api.mjs`.
- Skrypt czyści `../api/wwwroot` i kopiuje do niego `build/client`.
- `.gitignore` dostaje wpis `api/wwwroot/`.

### Success Criteria:

#### Automated Verification:

- Typy i trasy są poprawne: `cd web && npm run typecheck`
- Build SPA z kopiowaniem przechodzi i tworzy `api/wwwroot/index.html`: `cd web && npm run build:api`
- W `web/app` nie ma odwołań do zewnętrznych czcionek: `git grep -n "fonts.googleapis" -- web/app` nic nie zwraca
- API nadal się kompiluje i testy przechodzą: `dotnet build api` oraz `dotnet test api.Tests`

#### Manual Verification:

- Dev (`dotnet run --project api --launch-profile http` + `cd web && npm run dev`): wejście na `http://localhost:5173/` bez sesji pokazuje formularz logowania
- Zalogowanie kontem z grupy pokazuje nagłówek z nazwą wyświetlaną i pusty stan
- Złe hasło, konto spoza grupy i wyłączony LDAP (np. zły `Host` w user-secrets) pokazują trzy różne polskie komunikaty
- Głęboki link bez sesji (np. `/cokolwiek`) prowadzi na `/login?next=/cokolwiek`, a po zalogowaniu wraca na tę ścieżkę. `?next=//example.com` przekierowuje na `/`
- „Wyloguj” wraca do formularza; przycisk Wstecz nie pokazuje danych bez ponownego logowania
- Po `npm run build:api` samo API na `http://localhost:5143/` serwuje formularz i logowanie działa bez Vite

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 4: Dokumentacja i porządki

### Overview

Dokumenty fundamentów mają opisywać rzeczywisty mechanizm logowania, a nie Windows SSO. Plik `.http` zastępuje szablon.

### Changes Required:

#### 1. Infrastruktura

**File**: `context/foundation/infrastructure.md`

**Intent**: Usunąć założenie Windows SSO i opisać wymagania nowego mechanizmu.

**Contract**:

- **Recommendation / Shortlisted IIS:** zamiast „built-in Windows SSO” — logowanie w aplikacji przez LDAPS, niezależne od hosta.
- **Getting Started, krok 1:** witryna z włączonym Anonymous i wyłączonym Windows Authentication (uwierzytelnia aplikacja). Serwer musi mieć dostęp do AD po LDAPS (636), a certyfikat wewnętrznego CA musi być zaufany.
- **Getting Started, krok 3:** SPA publikowane przez `npm run build:api`.
- **Getting Started, weryfikacja:** anonimowe `GET /api/me` → 401, a formularz logowania jest widoczny.
- **Secrets:** `Auth:Jwt:SigningKey` i ustawienia `Auth:Ldap` specyficzne dla firmy.
- **Risk Register:**
  - wiersz „Users logged out on every recycle” → nieaktualny (klucz JWT pochodzi z konfiguracji); usunąć albo zastąpić;
  - usunąć wiersz „Windows SSO prompts or NTLM fallback”;
  - dodać „LDAPS niedostępne lub certyfikat niezaufany → logowanie 503” (mitygacja: pre-flight na serwerze, zaufane CA);
  - dodać „zgadywanie haseł blokuje konta AD” (mitygacja: limit 5 prób na minutę na IP i polityka blokady w AD);
  - dodać „wyciek `SigningKey` pozwala podrobić sesję” (mitygacja: sekret tylko na serwerze, rotacja wylogowuje wszystkich).
- **Unknown Unknowns:** usunąć punkty „Data Protection keys” i „Windows SSO prerequisites”.

#### 2. AGENTS.md

**File**: `AGENTS.md`

**Intent**: Uzupełnić reguły i komendy o to, co powstało.

**Contract**:

- **Hard Rules:** reguła o uwierzytelnianiu z jedynym wyjątkiem (`POST /api/auth/login` i pliki SPA) oraz odesłaniem do `NoPublicEndpointsTests`.
- **Build and Development Commands:** dev = API + `cd web && npm run dev` (UI na `http://localhost:5173`, `/api` przez proxy); `npm run build:api`; konfiguracja `Auth:*` przez `dotnet user-secrets` w `api/`.

#### 3. Roadmapa

**File**: `context/foundation/roadmap.md`

**Intent**: Zamknąć pytanie F-01 o metodę logowania decyzją z tego planu.

**Contract**: W F-01 → `Unknowns` wiersz o metodzie logowania dostaje adnotację „rozstrzygnięte 2026-09-27: login + hasło przez LDAPS, JWT w ciasteczku HttpOnly (plan `authenticated-app-shell`)”. Pola `Status` nie ruszamy; robią to skille cyklu życia.

#### 4. Plik smoke testu

**File**: `api/securitycheck-portal.http`

**Intent**: Zastąpić `GET /weatherforecast` sekwencją logowanie → `me` → wylogowanie → `me` (401).

**Contract**: Zmienne `@host`, `@userName`, `@password`. Hasło podaje użytkownik lokalnie; plik nie zawiera prawdziwych danych.

### Success Criteria:

#### Automated Verification:

- W dokumentach fundamentów nie zostały nieaktualne założenia o Windows SSO: `git grep -n -i "Windows Authentication on\|Windows SSO\|Negotiate" -- context/foundation/infrastructure.md AGENTS.md` nic nie zwraca albo trafia tylko w notę historyczną o zmianie decyzji
- Build i testy przechodzą: `dotnet build api`, `dotnet test api.Tests`, `cd web && npm run typecheck`

#### Manual Verification:

- Sekwencja z `api/securitycheck-portal.http` przechodzi na prawdziwym AD
- Przegląd zmian w `infrastructure.md` i `AGENTS.md` potwierdza, że opisują zbudowany mechanizm

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Testing Strategy

### Unit Tests:

- `LdapFilter.Escape` i walidacja formatu loginu (Phase 2.5).

### Integration Tests:

- Test ochronny: brak anonimowych endpointów poza listą, polityka domyślna ustawiona, 401 dla `/api/*`, `index.html` dla ścieżek UI (Phase 2.3).
- Kontrakt logowania: wszystkie kody odpowiedzi, flagi ciasteczka, cykl logowanie → `me` → wylogowanie, limit prób, token z obcym kluczem albo przeterminowany (Phase 2.4).
- `LdapAuthenticator` (Novell) z prawdziwym AD sprawdzamy tylko ręcznie. W testach automatycznych zastępuje go fałszywa implementacja.

### Manual Testing Steps:

1. Ustawić `Auth:*` w user-secrets, uruchomić API i `npm run dev`, wejść na `http://localhost:5173/`: widać formularz.
2. Zalogować się kontem z grupy: widać nagłówek z nazwą wyświetlaną i pusty stan.
3. Złe hasło, konto spoza grupy, zły `Host` LDAP: trzy różne polskie komunikaty.
4. Sześć błędnych prób w minutę: komunikat o limicie.
5. Głęboki link bez sesji → logowanie → powrót na link. `?next=//example.com` → `/`.
6. Wylogować się i sprawdzić, że `/api/me` zwraca 401.
7. `npm run build:api`, samo API na `:5143`: logowanie działa pod jednym adresem.

## Performance Considerations

Logowanie to jeden bind i jedno wyszukiwanie LDAP (timeout 5 s). Reguła `IN_CHAIN` przy bardzo rozbudowanych grupach bywa wolna, ale przy jednej grupie zespołu to pomijalne. Pozostałe żądania weryfikują JWT lokalnie, bez LDAP.

## Migration Notes

Nie ma danych ani użytkowników do migrowania. Zmiana decyzji z Windows SSO na LDAP + JWT wymaga innej konfiguracji witryny IIS (Anonymous włączone) i dostępu serwera do LDAPS. Opisuje to Phase 4, a wykonuje F-02.

## References

- Roadmapa: `context/foundation/roadmap.md` (F-01 `authenticated-app-shell`, odblokowuje S-01)
- PRD: `context/foundation/prd.md` (FR-001, NFR, Access Control)
- Infrastruktura: `context/foundation/infrastructure.md` (IIS + SPA z `wwwroot`, pod jednym adresem)
- Issue: https://github.com/rchmielorz/SecurityCheck-Portal/issues/1
- Novell.Directory.Ldap.NETStandard 4.0.0: https://www.nuget.org/packages/Novell.Directory.Ldap.NETStandard
- Dokumentacja Microsoftu, Windows Authentication (powód odrzucenia Negotiate za proxy): https://learn.microsoft.com/en-us/aspnet/core/security/authentication/windowsauth?view=aspnetcore-10.0
- Tryb SPA w React Router: `web/.agents/skills/react-router/references/framework-mode.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Uwierzytelnianie w API

#### Automated

- [ ] 1.1 API kompiluje się bez ostrzeżeń nullable w nowych plikach: `dotnet build api`
- [ ] 1.2 W repozytorium nie ma już `weatherforecast`: `git grep -i weatherforecast -- api` nic nie zwraca
- [ ] 1.3 `appsettings*.json` nie zawiera `SigningKey` z wartością

#### Manual

- [ ] 1.4 Bez `Auth:Jwt:SigningKey` API odmawia startu z czytelnym błędem walidacji
- [ ] 1.5 Logowanie prawdziwym kontem z grupy zwraca 200 i ciasteczko `sc_auth`, `GET /api/me` zwraca nazwę wyświetlaną

### Phase 2: Projekt testowy i test ochronny

#### Automated

- [ ] 2.1 Wszystkie testy przechodzą: `dotnet test api.Tests`
- [ ] 2.2 Test ochronny zawodzi przy tymczasowym anonimowym endpoincie spoza listy

### Phase 3: Powłoka SPA

#### Automated

- [ ] 3.1 Typy i trasy są poprawne: `cd web && npm run typecheck`
- [ ] 3.2 `npm run build:api` przechodzi i tworzy `api/wwwroot/index.html`
- [ ] 3.3 Brak odwołań do zewnętrznych czcionek w `web/app`
- [ ] 3.4 API nadal się kompiluje i testy przechodzą

#### Manual

- [ ] 3.5 Wejście bez sesji na `http://localhost:5173/` pokazuje formularz logowania
- [ ] 3.6 Zalogowanie kontem z grupy pokazuje nagłówek z nazwą wyświetlaną i pusty stan
- [ ] 3.7 Złe hasło, konto spoza grupy i wyłączony LDAP pokazują trzy różne polskie komunikaty
- [ ] 3.8 Głęboki link wraca po zalogowaniu; `?next=//example.com` przekierowuje na `/`
- [ ] 3.9 „Wyloguj” wraca do formularza, a Wstecz nie pokazuje danych
- [ ] 3.10 Po `build:api` samo API serwuje UI i logowanie działa bez Vite

### Phase 4: Dokumentacja i porządki

#### Automated

- [ ] 4.1 Brak nieaktualnych założeń o Windows SSO w `infrastructure.md` i `AGENTS.md`
- [ ] 4.2 Build, testy i typecheck przechodzą

#### Manual

- [ ] 4.3 Sekwencja z `api/securitycheck-portal.http` przechodzi na prawdziwym AD
- [ ] 4.4 Przegląd `infrastructure.md` i `AGENTS.md` potwierdza zgodność z mechanizmem
