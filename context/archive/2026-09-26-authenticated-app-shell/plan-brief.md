# Authenticated App Shell — Plan Brief

> Full plan: `context/changes/authenticated-app-shell/plan.md`

## What & Why

To fundament F-01 z roadmapy ([#1](https://github.com/rchmielorz/SecurityCheck-Portal/issues/1)). Przed pierwszym ekranem z danymi o repozytoriach i klientach portal musi mieć logowanie i domyślną ochronę każdego endpointu, bo PRD zabrania jakiejkolwiek publicznej strony. Funkcje zbudowane przed bramką trzeba by zabezpieczać wstecz. Zmiana odblokowuje S-01 (`repo-version-pattern`).

## Starting Point

API w `api/` to szablon `weatherforecast` bez uwierzytelniania. UI w `web/` to ekran powitalny React Router w trybie SSR, który ładuje Google Fonts. W repo nie ma projektu testowego.

## Desired End State

Każde wejście bez sesji kończy się polskim formularzem logowania. Członek skonfigurowanej grupy AD loguje się swoim loginem i hasłem domenowym i widzi powłokę portalu: nagłówek ze swoją nazwą, przycisk „Wyloguj” i pusty stan. Sesja trwa 8 godzin. Automatyczny test pilnuje, że żaden endpoint poza logowaniem nie jest publiczny. UI i API działają pod jednym adresem.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Metoda logowania | Login + hasło, bind LDAPS (Novell.Directory.Ldap.NETStandard), API wydaje JWT | Niezależne od platformy; aplikacja może w przyszłości działać na Linuksie. Zastępuje wcześniejsze założenie Windows SSO. | Plan (user) |
| Kto ma dostęp | Członkowie jednej grupy AD (także zagnieżdżeni), sprawdzani przy logowaniu | PRD: dostęp tylko dla zespołu, a nie dla każdego pracownika; model nadal płaski. | Plan |
| Przechowywanie tokenu | Ciasteczko `sc_auth`: HttpOnly, Secure, SameSite=Strict | Skrypt na stronie nie odczyta tokenu, a SameSite + jeden adres blokują CSRF. | Plan |
| Czas sesji | 8 h, bez odświeżania | Najprostsze rozwiązanie; usunięcie z grupy działa najpóźniej po dniu roboczym. | Plan |
| Jeden adres w dev | Vite przekazuje `/api` do Kestrela | Przy JWT proxy nie psuje uwierzytelniania; standardowy układ z hot reloadem. | Plan |
| AD w dev i w testach | Prawdziwe AD przez LDAPS w dev, fałszywy authenticator w testach | Dev sprawdza prawdziwy bind i grupę, a testy działają bez sieci. | Plan |
| Weryfikacja NFR | Pierwszy projekt `api.Tests` z testem „brak publicznych endpointów” | Chroni każdy przyszły kawałek, nie tylko ten. | Plan |
| Język UI | Polski, teksty zapisane na sztywno | Zgodny z zespołem i PRD; bez biblioteki i18n. | Plan |
| Wyjątki anonimowe | Tylko `POST /api/auth/login` i pliki SPA (formularz) | Formularz logowania musi być widoczny, ale nie zawiera danych. | Plan |
| Tryb UI | SPA (`ssr: false`) serwowane z `api/wwwroot` | Zgodnie z `infrastructure.md` (IIS bez hostingu Node). | Infrastructure |

## Scope

**In scope:**

- logowanie LDAPS z kontrolą grupy;
- JWT w ciasteczku;
- `POST /api/auth/login`, `POST /api/auth/logout`, `GET /api/me`;
- ochrona domyślna i limit prób logowania;
- serwowanie SPA z fallbackiem;
- projekt testowy z testem ochronnym i testami logowania;
- powłoka SPA po polsku;
- skrypt `build:api`;
- aktualizacja `infrastructure.md`, `AGENTS.md`, roadmapy i pliku `.http`.

**Out of scope:**

- Windows SSO i OIDC;
- tokeny odświeżające i odwoływanie sesji;
- role;
- baza danych;
- `/health`, CI i wdrożenie (F-02);
- i18n;
- ekrany repozytoriów (S-01).

## Architecture / Approach

SPA wysyła `POST /api/auth/login`. API robi bind do AD przez LDAPS jako `{login}@{UpnSuffix}` i, z uprawnieniami tego użytkownika, szuka jego wpisu z warunkiem członkostwa w grupie (`LDAP_MATCHING_RULE_IN_CHAIN`). Przy sukcesie wydaje JWT (HS256, 8 h) w ciasteczku `sc_auth`. Kolejne żądania uwierzytelnia handler JwtBearer, który czyta token z ciasteczka. Grupy AD przy każdym żądaniu nie sprawdzamy. Domyślna polityka autoryzacji chroni wszystko. Pliki SPA i `index.html` są anonimowe, a nieznane `/api/*` dają 404 lub 401, nigdy HTML. LDAP siedzi za interfejsem `ILdapAuthenticator`, więc w testach podmieniamy go na fałszywy.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Uwierzytelnianie w API | Login / wylogowanie / `me`, JWT w ciasteczku, ochrona domyślna, limit prób, fallback SPA | Pusty login lub hasło dają anonimowy bind w AD; niezaufany certyfikat LDAPS |
| 2. Projekt testowy i test ochronny | `api.Tests`: brak publicznych endpointów, wszystkie wyniki logowania, escapowanie filtra | Ciasteczko Secure w testach wymaga klienta na `https://localhost` |
| 3. Powłoka SPA | `/login`, chroniony layout z nagłówkiem, pusty stan, PL, `build:api` | Otwarte przekierowanie przez `?next=` |
| 4. Dokumentacja i porządki | `infrastructure.md` i `AGENTS.md` zgodne z mechanizmem, nowy `.http`, zamknięte pytanie F-01 | Nieaktualne ślady Windows SSO w dokumentach |

**Prerequisites:** dostęp z maszyny dev do AD po LDAPS (636) z zaufanym certyfikatem; DN grupy zespołu; konto testowe w grupie i poza nią (do testów ręcznych).
**Estimated effort:** ok. 2–3 sesje w 4 fazach.

## Open Risks & Assumptions

- Zakładamy, że AD przyjmuje bind UPN `{login}@{UpnSuffix}` i że konta mają `sAMAccountName` zgodne z formatem `^[A-Za-z0-9._-]{1,64}$`.
- Wyciek `Auth:Jwt:SigningKey` pozwoliłby podrobić sesję. Klucz jest tylko w user-secrets lub na serwerze; rotacja wylogowuje wszystkich.
- Limit prób na IP za wspólnym NAT może zablokować kilka osób jednocześnie. Przy garstce użytkowników to akceptowalne.
- Formularz logowania jest jedyną świadomie publiczną stroną. To interpretacja NFR „brak publicznej strony”, konieczna przy logowaniu hasłem.

## Success Criteria (Summary)

- Bez zalogowania nie da się pobrać żadnych danych; test ochronny zawodzi przy każdym nowym, niezamierzonym wyjątku.
- Członek grupy AD loguje się i widzi powłokę ze swoją nazwą; osoba spoza grupy dostaje czytelny komunikat o braku dostępu.
- `dotnet test api.Tests`, `dotnet build api` i `npm run typecheck` przechodzą, a po `build:api` samo API serwuje UI pod jednym adresem.
