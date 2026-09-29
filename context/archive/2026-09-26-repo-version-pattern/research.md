---
date: 2026-09-27T21:25:14Z
researcher: Claude (claude-opus-5-5), dla Rafal Chmielorz
git_commit: 0dedd49
branch: feature/research
repository: SecurityCheck-Portal (rchmielorz/SecurityCheck-Portal)
topic: "S-01 repo-version-pattern: co trzeba wiedzieć, żeby zaplanować dodawanie repozytorium Git i wzorca wersji oraz podgląd rozwiązanej wersji"
tags: [research, s-01, git, version-pattern, ef-core, api, spa]
status: complete
last_updated: 2026-09-27
last_updated_by: Claude (claude-opus-5-5)
---

# Research: S-01 repo-version-pattern

**Date**: 2026-09-27T21:25:14Z
**Researcher**: Claude (claude-opus-5-5), dla Rafal Chmielorz
**Git Commit**: 0dedd49
**Branch**: feature/research
**Repository**: SecurityCheck-Portal

## Research Question

Co trzeba wiedzieć przed `/10x-plan repo-version-pattern`, żeby zaplanować S-01: zalogowany użytkownik dodaje repozytorium Git i wzorzec wersji (np. `2.1.*`, gdzie `*` to numer hotfixa) i widzi, na jaką konkretną wersję wzorzec się obecnie rozwiązuje (`context/foundation/roadmap.md:109`, `context/foundation/prd.md:62-64`)?

Zakres: fakty z kodu i dokumentów projektu, techniczne sposoby rozwiązania wzorca w Git z .NET na Windows oraz decyzje, które plan musi podjąć. Bez projektu implementacji.

## Summary

1. **Rozstrzygnięte przez użytkownika 2026-09-27 (ta sesja):**
   - wersje są oznaczane **tagami**, nie gałęziami;
   - tagi mają format **`2.1.7` bez prefiksu `v`**;
   - wewnętrzny serwer Git jest dostępny przez **HTTPS z tokenem PAT** (tylko do odczytu).

   Zamyka to pierwsze Unknown S-01 (`roadmap.md:116`, gdzie jedynym przykładem był `v2.1.7`). Częściowo zamyka też drugie (`roadmap.md:117`): rodzaj poświadczenia jest znany, właściciel i proces nie.
2. **W repo nie ma warstwy danych.** Grep `EntityFramework|Npgsql|DbContext|Migrations|ConnectionStrings` po `*.cs`, `*.csproj`, `*.json` w `api/` i `api.Tests/` nic nie zwraca, a `AGENTS.md:3` mówi „planned but not yet installed”. Żaden dokument nie przypisuje wprowadzenia EF Core ani migracji do konkretnego elementu roadmapy. S-01 jest pierwszym slice'em, który zapisuje dane (repozytorium i wzorzec), więc w praktyce to on wprowadzi PostgreSQL/EF Core. To wniosek, nie zapis w dokumentach.
3. **Rozwiązanie wzorca bez klonowania:** `git ls-remote --tags <url>` bez `--refs`.
   - Commit to linia `refs/tags/X^{}`, jeśli istnieje (tag adnotowany); w przeciwnym razie linia `refs/tags/X` (tag lekki).
   - Filtrowanie i sortowanie robimy w .NET: ścisły regex `^2\.1\.(0|[1-9][0-9]*)$` i porównanie numeru hotfixa jako liczby (2.1.10 > 2.1.9).
   - Nie opieramy się na glob `git ls-remote` ani na `--sort=version:refname`. Szczegóły i źródła niżej.
4. **Najważniejsza decyzja architektoniczna dla planu:** kto rozmawia z Git w S-01.
   - Dokumenty zakładają, że poświadczenie Git ma konto usługi workera (`infrastructure.md:118`, `:161`).
   - Wyjątkiem jest sekcja Secrets (`infrastructure.md:138`), która kładzie „git PAT/SSH key” także na app pool.
   - Worker jeszcze nie istnieje (`infrastructure.md:186` planuje projekt `SecurityCheck.Worker`), a S-01 ma pokazać rozwiązanie „obecnie”, przed skanerem (`roadmap.md:118`).
5. **Gwarancja wiarygodności obejmuje S-02.**
   - Tagi da się przesunąć, więc wynik rozwiązania z S-01 jest podglądem z datą (`resolvedAt`), a nie trwałą prawdą.
   - Worker ma sklonować rozwiązany ref, sprawdzić, czy SHA zgadza się z zapisanym, i zakończyć skan błędem przy niezgodności. Wpisuje się to w `infrastructure.md:33` („checks out the exact commit … and records that commit”).
6. **Ograniczenia z F-01, które S-01 musi respektować:**
   - wszystkie nowe endpointy są chronione polityką domyślną, a lista wyjątków anonimowych jest pilnowana testem (`AGENTS.md:7`, `api.Tests/NoPublicEndpointsTests.cs:18-22`);
   - wzorzec nie może być ostatnim segmentem adresu trasy UI (`AGENTS.md:8`);
   - loadery przekazują `request` do `apiFetch` (`web/app/lib/api.ts:15-42`).

## Detailed Findings

### 1. Stan kodu i konwencje, na których buduje S-01

**API:**
- Opcje rejestrujemy wzorcem `AddOptions<T>().BindConfiguration(T.SectionName).ValidateDataAnnotations().ValidateOnStart()` (`api/Program.cs:14-17`; wariant z dodatkowym `.Validate` w `:19-24`). Dzięki temu brakujące ustawienie zatrzymuje start (`AGENTS.md:27`).
- Endpointy mapuje metoda rozszerzająca, np. `app.MapAuthEndpoints();` (`api/Program.cs:132`). Nowe mapowanie musi stać przed catch-allem `/api/{**rest}` (`:136-137`), a ten przed `MapFallbackToFile` (`:139-140`).
- Moduł `api/Auth/`:
  - jeden plik na odpowiedzialność, namespace `securitycheck_portal.Auth` (`api/Auth/AuthEndpoints.cs:3`);
  - rekordy żądań i odpowiedzi z polami nullable walidowanymi ręcznie (`:5`, `:7`);
  - `MapGroup("/api")` (`:15`);
  - handlery zwracają `IResult` z kodami statusu bez treści: `BadRequest`, `StatusCode(...)`, `Ok(record)`, `NoContent`. ProblemDetails nie ma nigdzie w `api/Auth/`.
  - Wyniki zewnętrznej zależności modeluje unia dyskryminowana (`LdapAuthResult.*`, `api/Auth/ILdapAuthenticator.cs`) mapowana na kody HTTP (`AuthEndpoints.cs:70-75`). To gotowy wzorzec dla stanów rozwiązania wzorca (sekcja 3).
- Pakiety API: JwtBearer 10.0.12, OpenApi 10.0.12, Novell LDAP 4.0.0 (`api/securitycheck-portal.csproj:11-15`). `appsettings.json` nie ma sekcji `ConnectionStrings`.

**Testy:**
- `PortalFactory : WebApplicationFactory<Program>`:
  - konfigurację nadpisuje `AddInMemoryCollection` (`api.Tests/PortalFactory.cs:73-88`);
  - usługi podmienia `RemoveAll<T>()` + `AddSingleton` z fałszywką (`:90-95`).
  - Nowy klient Git i baza będą musiały mieć tu odpowiedniki (fałszywka lub kontener).
- `AGENTS.md:34`: testy „need no network or AD”. Dodanie prawdziwego PostgreSQL do testów zmieniłoby to zdanie (sekcja 5).
- Helper logowania `LoginAsync` jest prywatny w `api.Tests/AuthEndpointsTests.cs:174`. Testy S-01 muszą go skopiować albo przenieść do `PortalFactory`.
- `NoPublicEndpointsTests.cs:61-74` już sprawdza `/repos/42` jako trasę SPA: kształt adresu z ID w ścieżce.

**SPA:**
- Nowe trasy chronione trafiają do tablicy layoutu w `web/app/routes.ts:10-15`, przed catch-allem `*`.
- Wzorzec formularza: `clientAction` + `<Form method="post" noValidate>` + mapa polskich komunikatów błędów po kodzie HTTP (`web/app/routes/login.tsx:6-14`, `:35-57`, `:96-100`). Klasy Tailwind i `dark:` jak w `login.tsx:67`, `:81`, `:104`.
- `home.tsx:12` ma placeholder „Nie dodano jeszcze żadnego repozytorium.”, który S-01 prawdopodobnie zastąpi listą.
- Nie ma biblioteki formularzy ani runnera testów UI (`web/package.json`, `AGENTS.md:36`).

**Projekty .NET:**
- Nie ma pliku `.sln`/`.slnx`. Jedyna referencja to `api.Tests` → `api`.
- Nowe projekty .NET dostają własny folder najwyższego poziomu obok `api/` (`AGENTS.md:17`), bez projektu w korzeniu repo (`AGENTS.md:12`).
- Nazewnictwo jest niespójne: istniejące projekty to `securitycheck-portal[.Tests]` z namespace `securitycheck_portal`, a `infrastructure.md:186` planuje `SecurityCheck.Worker`. Plan musi wybrać nazwę projektu danych.

### 2. Wcześniejsze decyzje w dokumentach

- **Definicja wzorca:** `2.1.*`, „gdzie `*` to numer hotfixa”, reprezentuje wersję zainstalowaną u klienta (`prd.md:64`, `shape-notes.md:79`).
- **Skan musi dotyczyć kodu tej wersji**, „never the default branch” (`AGENTS.md:10`, `prd.md:38`, `prd.md:50`).
- **Co musi zawierać „rozwiązana wersja”.** Jedyny wymóg w dokumentach to **commit**, zapisywany przy wyniku skanu (`infrastructure.md:6`, `:33`). Żaden dokument nie wymaga zapisu nazwy tagu, daty ani rozwiązania w bazie; przeszukano pliki wymienione w sekcji „Historical Context”. Nie ustalono też, czy S-01 rozwiązuje wzorzec na żywo przy każdym wyświetleniu, czy zapisuje wynik, ani czy S-02 rozwiązuje go ponownie przy starcie skanu.
- **Klient jako encja.** Wizja i Access Control mówią o klientach jako danych (`prd.md:22`, `prd.md:95`), ale żadne FR nie tworzy klienta. FR-002 i FR-003 obejmują tylko repozytorium i wzorzec.
- **Liczba wzorców na repozytorium** nie jest określona. FR-003 mówi „wzorzec wersji dla repozytorium” w liczbie pojedynczej (`prd.md:64`), co nie jest wykluczeniem.
- **Dostęp do Git:**
  - repozytoria są tylko w sieci wewnętrznej (`infrastructure.md:20`, `:29`);
  - poświadczenie ma dostęp tylko do odczytu (`infrastructure.md:160`), a jego rotacja wymaga człowieka (`:145`);
  - bezprofilowa tożsamość usługi psuje poświadczenia Git, `HOME` i `TEMP` (`:161`, pre-mortem `:122`).
- **Import repozytoriów z organizacji Git jest Non-Goal.** Repozytoria dodaje się ręcznie (`prd.md:102`, `roadmap.md:195`).

### 3. Rozwiązanie wzorca `2.1.*` na tag i commit

Poniższe źródła zewnętrzne zebrał wątek badawczy 2026-09-27. Wersje i daty pochodzą z ich stron; nie weryfikowałem ich lokalnie.

**Komenda:** `git ls-remote --tags <url>` ([git-ls-remote](https://git-scm.com/docs/git-ls-remote)).
- Format linii: `<oid>\t<ref>`.
- Tag adnotowany daje dwie linie: `refs/tags/2.1.7` (SHA obiektu tagu) i `refs/tags/2.1.7^{}` (commit). Tag lekki daje jedną linię z SHA commita.
- `--refs` pomija linie `^{}`, więc przy tagach adnotowanych zgubiłby SHA commita. Nie używać.
- `--exit-code`: kod 2, gdy nic nie pasuje. Kod 128 oznacza błąd krytyczny (uwierzytelnienie, sieć, TLS). To różne stany.

**Filtrowanie w .NET, nie w git:**
- Wzorzec przekazany do `ls-remote` to glob dopasowywany do końcówki refa, więc `2.1.*` łapie też `2.1.7-rc1`, `2.1.7.1` i `foo/2.1.3`.
- Zalecenie: pobrać wszystkie tagi (bez wzorca), a potem w .NET dopasować nazwę po `refs/tags/` ścisłym regexem, np. `^2\.1\.(0|[1-9][0-9]*)$` dla wzorca `2.1.*`. Tekst użytkownika nie trafia wtedy do argv.
- Sortujemy po numerze hotfixa jako liczbie całkowitej.
- `--sort=version:refname` zależy od konfiguracji `versionsort.suffix` i domyślnie może postawić prerelease za wydaniem ([git-config versionsort.suffix](https://git-scm.com/docs/git-config#Documentation/git-config.txt-versionsortsuffix)). Nie polegać na nim.

**Biblioteki do porównywania wersji się nie nadają:**
- `System.Version` rzuca wyjątek na `2.1.7-rc1` i uznaje `2.1.7` za mniejsze od `2.1.7.0`.
- NuGet.Versioning normalizuje: `2.1.07` == `2.1.7` i `1.0.0` == `1.0.0.0` ([zasady wersjonowania NuGet](https://learn.microsoft.com/nuget/concepts/package-versioning)). Dwa różne tagi mogłyby więc wyjść równe, co dla wiarygodności skanu jest niejednoznacznością.
- Własny ścisły regex daje najprostszą, sprawdzalną semantykę.

**Tagi niepasujące do formatu** (prerelease, 4 części, zera wiodące) warto pokazać w UI jako „pominięte” zamiast ukrywać. To propozycja, nie wymóg z PRD.

**Jawne stany wyniku**, zgodne z guardrailem „fałszywe poczucie bezpieczeństwa jest gorsze niż brak narzędzia” (`prd.md:38`):
- `Resolved`: tag, commit, `resolvedAt`;
- `NoMatch`: brak pasujących tagów, z listą pominiętych;
- `Ambiguous`: np. ten sam ref z różnymi SHA albo niepoprawny peel. Blokować, nie zgadywać;
- `Error`: uwierzytelnienie, TLS, sieć, timeout, z kodem wyjścia i zredagowanym stderr.

Ten sam kształt co `LdapAuthResult`.

**Przesuwanie tagów (TOCTOU):** tag można przesunąć między rozwiązaniem a klonowaniem. Weryfikacja SHA po stronie workera (klon rozwiązanego refa, `rev-parse "<tag>^{commit}"` równe zapisanemu SHA, twardy błąd przy niezgodności) to zadanie S-02. S-01 powinien jednak zapisać lub pokazać SHA, żeby było z czym porównać.

**Gałęzie** odpadają decyzją użytkownika. Gałąź daje „czubek linii”, nie numer wersji.

### 4. Klient Git z .NET, poświadczenie i bezpieczeństwo

**git.exe przez `Process` (zalecane) czy LibGit2Sharp:**
- LibGit2Sharp: 0.32.0 z 2026-07-24, łatka bezpieczeństwa z libgit2 1.8.6, poprzednia wersja 2024-12-03 ([NuGet](https://www.nuget.org/packages/LibGit2Sharp), [releases](https://github.com/libgit2/libgit2sharp/releases)).
  - `Repository.ListRemoteReferences(url, credentialsProvider)` nie ma parametru timeoutu ani `CancellationToken`.
  - Nie potwierdzono, czy zwraca linie `^{}` dla tagów adnotowanych.
  - Worker i tak klonuje, więc dwa różne klienty Git interpretowałyby refy.
- git.exe daje tę samą semantykę dla rozwiązania i klonu. Git for Windows ma aktualne wydania: 2.55.0.windows.5 z 2026-08-20 ([releases](https://github.com/git-for-windows/git/releases)). Git musi być zainstalowany na serwerze tak czy inaczej (`infrastructure.md:183`).

**Zasady uruchamiania git.exe** (źródła: [ProcessStartInfo.ArgumentList](https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.argumentlist), [Process.Kill](https://learn.microsoft.com/dotnet/api/system.diagnostics.process.kill), [git — zmienne środowiskowe](https://git-scm.com/docs/git)):
- bezwzględna ścieżka do `git.exe`, `ArgumentList` (nie `Arguments`);
- asynchroniczny odczyt stdout i stderr, `WaitForExitAsync` z timeoutem, a przy timeoucie `Kill(entireProcessTree: true)`;
- `GIT_TERMINAL_PROMPT=0` i `-c credential.helper=` (resetuje listę helperów, żeby GCM nie wisiał pod tożsamością bez profilu);
- `-c http.sslBackend=schannel`, żeby certyfikat wewnętrznego CA był brany z magazynu Windows;
- `-c http.lowSpeedLimit/lowSpeedTime`.

**Dostarczenie PAT** (gdzie wycieka):
- **Token w URL:** najgorszy wariant. Trafia do linii poleceń procesu (audyt 4688, Sysmon, EDR), komunikatów git i logów.
- **`-c http.extraHeader=...` w argv:** ten sam wyciek.
- **Bezpieczniejsze warianty:** nagłówek przez zmienne `GIT_CONFIG_COUNT` / `GIT_CONFIG_KEY_0` / `GIT_CONFIG_VALUE_0` albo `--config-env`. Jeszcze lepiej helper poświadczeń czytający token ze zmiennej środowiskowej, albo `GIT_ASKPASS`. Token jest wtedy tylko w środowisku procesu potomnego ([git-config](https://git-scm.com/docs/git-config)). Wersję Git, w której pojawiło się `GIT_CONFIG_COUNT`, wątek podał z pamięci (2.31) i jej nie zweryfikował.
- **Zasięg:** tylko odczyt repozytoriów, dedykowana tożsamość, rotacja.
- **Przechowywanie:** zgodnie z `infrastructure.md:138`, a nie w bazie jawnym tekstem.

**Walidacja URL podanego przez użytkownika:**
- `Uri.TryCreate` (absolutny), schemat dokładnie `https`, host z listy dozwolonych (zamyka też SSRF);
- bez userinfo, query i fragmentu; ścieżka ze ścisłego zbioru znaków, bez `..`;
- do git przekazać URL zserializowany na nowo;
- odrzucać `-…` (wstrzyknięcie opcji, klasa [CVE-2017-1000117](https://access.redhat.com/security/cve/cve-2017-1000117)), `ext::`, `file://`, ścieżki lokalne i UNC, `ssh://` oraz postać scp;
- ochrona warstwowa: `--end-of-options` przed URL i `GIT_ALLOW_PROTOCOL=https` ([protocol.allow](https://git-scm.com/docs/git-config#Documentation/git-config.txt-protocolallow), [gitcli](https://git-scm.com/docs/gitcli)).

Lista dozwolonych hostów pasuje do istniejącego wzorca opcji z walidacją przy starcie (`api/Program.cs:14-17`).

### 5. Dane i testy

- **Stos:** PostgreSQL przez EF Core/Npgsql (`tech-stack.md:29`, `infrastructure.md:13`).
- **Wspólny projekt `DbContext`:** API i przyszły worker mają go współdzielić (`infrastructure.md:186`).
- **Migracje:** addytywne, zgodne wstecz przez jedno wydanie. Wdrożenie: zatrzymać workera, uruchomić migrację (`efbundle`), podmienić API, uruchomić workera (`infrastructure.md:113`, `:143`, `:162`, `:191-195`).
- **Sprzeczność z testami.** Testy API mają działać bez sieci (`AGENTS.md:34`), a wprowadzenie bazy tę zasadę narusza. Opcje dla planu:
  - (a) Testcontainers z PostgreSQL: wierne, ale wymaga Dockera na maszynie deweloperskiej i w CI;
  - (b) SQLite in-memory: szybkie, ale z innym dialektem SQL;
  - (c) interfejsy repozytoriów z fałszywkami w testach HTTP, a osobne testy EF na prawdziwej bazie.

  Tego nie rozstrzygnąłem: to wybór planu, zależny od tego, czy Docker jest dostępny.
- **Klient Git w testach** wymaga fałszywki za interfejsem, analogicznie do `FakeLdapAuthenticator` (`api.Tests/PortalFactory.cs:140-164`). Parser wyjścia `ls-remote` da się przetestować jednostkowo na próbkach tekstu, bez sieci.

## Code References

- `api/Program.cs:14-17` — rejestracja opcji z walidacją przy starcie
- `api/Program.cs:132-140` — mapowanie endpointów, catch-all `/api/{**rest}`, fallback SPA
- `api/Auth/AuthEndpoints.cs:5-26` — rekordy, `MapGroup("/api")`, metoda rozszerzająca
- `api/Auth/AuthEndpoints.cs:70-75` — mapowanie unii wyników na kody HTTP
- `api/securitycheck-portal.csproj:11-15` — obecne pakiety (brak EF Core/Npgsql)
- `api.Tests/PortalFactory.cs:73-95` — nadpisywanie konfiguracji i usług w testach
- `api.Tests/PortalFactory.cs:140-164` — wzorzec fałszywki zależności zewnętrznej
- `api.Tests/NoPublicEndpointsTests.cs:18-22` — lista wyjątków anonimowych
- `api.Tests/AuthEndpointsTests.cs:174` — prywatny helper `LoginAsync`
- `web/app/routes.ts:10-15` — tablica tras chronionych
- `web/app/lib/api.ts:15-42` — `apiFetch(path, init, request?)` z przekierowaniem przy 401
- `web/app/routes/login.tsx:6-14`, `:35-57` — mapa komunikatów i `clientAction`
- `web/app/routes/home.tsx:12` — placeholder pustej listy repozytoriów
- `AGENTS.md:7-12`, `:17`, `:27`, `:34` — reguły wiążące S-01

## Architecture Insights

- **Dwa czasy rozwiązania.** S-01 pokazuje podgląd „na teraz”. Wiarygodność gwarantuje dopiero ponowne rozwiązanie lub weryfikacja SHA w S-02. Kontrakt „tag + commit + resolvedAt” łączy oba slice'y.
- **Jeden komponent Git dla API i workera.** Wspólny moduł (ta sama biblioteka co `DbContext` albo osobny) daje tę samą walidację URL, to samo dostarczanie PAT i ten sam parser dla podglądu i skanu. Rozjazd semantyki między podglądem a skanem naruszyłby guardrail.
- **Unia wyników zamiast wyjątków.** Wzorzec `LdapAuthResult` (F-01) przenosi się wprost na wynik rozwiązania wzorca i daje UI odrębne, polskie komunikaty dla każdego stanu.

## Historical Context (from prior changes)

- `context/archive/2026-09-26-authenticated-app-shell/reviews/impl-review.md:95-107` (F5) wskazał S-01 jako slice, który trafi na ograniczenie `{*path:nonfile}` przy wzorcach z kropką. Skutek: reguła `AGENTS.md:8`. Stwierdzenie aktualne: fallback w `api/Program.cs:139-140` się nie zmienił.
- `impl-review.md:115-126` (F6): loadery S-01 mają przekazywać `request` do `apiFetch`. Aktualne: `web/app/lib/api.ts:32-42`.
- `plan.md` F-01 (archiwum, linia ok. 40): „F-01 niczego nie zapisuje”. Baza była świadomie odłożona i żaden element roadmapy nie przejął jej wprost.
- `roadmap.md:68` i `:71` (Baseline z 2026-09-26) oceniam osobno:
  - `ssr: true` — nieaktualne, bo `web/react-router.config.ts` ma teraz `ssr: false`;
  - „Auth: absent” — nieaktualne, F-01 jest `done` (`roadmap.md:45`);
  - „Data: absent” (`:70`) — nadal zgodne ze stanem kodu.
- `infrastructure.md:138` i `:118`/`:161` różnią się co do tego, kto trzyma poświadczenie Git. Sekcja Secrets obejmuje app pool, a opisy ryzyk mówią tylko o koncie workera. Plan S-01 powinien to ujednolicić.

## Related Research

Nie dotyczy: w `context/changes/**` i `context/archive/**` nie ma innych plików `research.md` (sprawdzone listowaniem obu katalogów 2026-09-27).

## Open Questions

Decyzje dla `/10x-plan` (nie blokują researchu):

1. **Kto rozmawia z Git w S-01?**
   - (a) API synchronicznie: potrzebny PAT na app pool, na co `infrastructure.md:138` już pozwala; najprostsze.
   - (b) Worker: nie istnieje, więc S-01 musiałby go wprowadzić.

   Rekomendacja: (a) ze wspólnym komponentem Git, żeby worker w S-02 użył tego samego kodu. Wymaga zmiany opisu ryzyka w `infrastructure.md:118`.
2. **Czy zapisywać wynik rozwiązania?** Czy S-01 rozwiązuje wzorzec na żywo przy każdym wyświetleniu (z `resolvedAt`), czy zapisuje ostatni wynik? Niezależnie od tego S-02 musi rozwiązać wzorzec ponownie albo zweryfikować SHA przy skanie.
3. **Kształt danych:**
   - Czy repozytorium może mieć kilka wzorców?
   - Czy wzorzec ma pole „klient” (nazwa, bez osobnej encji)?
   - Czy repozytorium ma nazwę wyświetlaną, czy tylko URL?
4. **Dozwolona składnia wzorca:** tylko `X.Y.*` czy też np. `X.*` albo dokładna wersja `X.Y.Z`? Czy pokazywać tagi pominięte (prerelease, 4 części)?
5. **Strategia testów z bazą:** Testcontainers, SQLite czy fałszywki (sekcja 5). Od tego zależy zmiana zdania w `AGENTS.md:34`.
6. **Nazwa i miejsce projektu danych i komponentu Git:** konwencja `securitycheck-portal.*` czy `SecurityCheck.*` (`infrastructure.md:186`).
7. **Poświadczenie Git w środowisku deweloperskim:** jak deweloper dostarcza PAT lokalnie (user-secrets, jak `Auth:*`)? Nie znaleziono w dokumentach.
8. **Lista dozwolonych hostów Git:** konkretna nazwa hosta wewnętrznego serwera, do konfiguracji.

Fakty do sprawdzenia na docelowym serwerze Git (wątek badawczy ich nie potwierdził):
- jaki schemat nagłówka `Authorization` akceptuje serwer dla PAT (Basic czy Bearer);
- czy `GIT_CONFIG_NOSYSTEM` pomija też `C:\ProgramData\Git\config`.
