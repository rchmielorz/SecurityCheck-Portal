---
date: 2026-10-01
source: web_search_exa (NuGet, dokumentacja Quartz.NET / TickerQ / Wolverine, porównania z 2025-2026)
topic: "Alternatywy dla Hangfire pod S-02, zgodne z tech-stack.md (.NET 10, EF Core 10 + Npgsql, PostgreSQL, usługa Windows)"
tags: [s-02, hangfire, quartz, tickerq, wolverine, background-jobs]
status: draft
---

# Alternatywy dla Hangfire (S-02)

Uzupełnia [hangfire-docs.md](hangfire-docs.md). Źródła to wyniki wyszukiwania, głównie dokumentacja projektów i NuGet; porównania z blogów traktuję jako opinie. Czego nie zweryfikowałem, jest oznaczone.

## Wymagania z repo

- .NET 10, EF Core 10 + `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 (`core/securitycheck-portal.Core.csproj`), PostgreSQL.
- Skan trwa minuty i musi przeżyć restart (roadmapa S-02, `infrastructure.md:158-159`): kolejka lub harmonogram z trwałym magazynem, albo własna tabela skanów.
- Worker jako usługa Windows, API w IIS, wspólny schemat (`infrastructure.md:113`, `:186`).
- Migracje schematu robi ręcznie `dotnet ef` (`AGENTS.md`), więc ważne jest, kto tworzy tabele biblioteki.
- Brak publicznych stron: dashboard biblioteki wymaga uwierzytelnienia (`NoPublicEndpointsTests`).

## Porównanie

| Opcja | Wsparcie .NET 10 / PostgreSQL | Model | Magazyn i schemat | Uwagi |
|---|---|---|---|---|
| **Hangfire + Hangfire.PostgreSql** (punkt odniesienia) | `Hangfire.PostgreSql` 1.21.1 (2026-02-11) celuje w .NET Standard 2.0, zależy od `Npgsql >= 6.0.11` i `Hangfire.Core >= 1.8.0`; Npgsql 10.0.3 spełnia dolną granicę, działania w runtime nie sprawdzałem | kolejka zadań, ponowienia domyślnie 10 prób | własne tabele Hangfire poza EF | dashboard wbudowany, edycja LGPL, batche tylko w Pro |
| **Quartz.NET** | `Quartz.Extensions.Hosting` 3.20.1 ma cel `net10.0`; 3.x jest utrzymywane, 4.x to bieżące wydanie (4.1 w porównaniu Quartz) | harmonogram (trigger + job); jednorazowe zadanie to trigger | `UsePostgres(...)` (sterownik Npgsql dodajesz sam), tabele `QRTZ_*` ze skryptów SQL; w 4.x `ProvisionSchema()` jest opisane dla środowiska deweloperskiego | brak dashboardu; ponowienia opt-in; `WaitForJobsToComplete`; job z `IJobExecutionContext.CancellationToken`; `ShutdownJobInterruption` domyślnie nigdy |
| **TickerQ** | `TickerQ.EntityFrameworkCore` 10.4.0 celuje w `net10.0`, obsługuje PostgreSQL; zależy od `FlexLabs.EntityFrameworkCore.Upsert` 10.x | zadania jednorazowe i cron, atrybut + source generator | przez EF Core; może współdzielić `DbContext` aplikacji, czyli jeden zestaw migracji; tabele w schemacie `ticker` | młody projekt (źródło porównawcze podaje „2+ lata"); w issue #800 migracje z własnym kontekstem wymagały fabryki design-time i ustawienia migrations assembly; dashboard SignalR to osobny pakiet |
| **Wolverine** (`WolverineFx.Postgresql`) | `PersistMessagesWithPostgresql()`, współpracuje z EF Core; wersja w porównaniu Quartz: 6.35.0 | magistrala komunikatów z trwałą lokalną kolejką, outbox | tabele w PostgreSQL (schemat konfigurowalny), opcja `AutoProvision()` | najcięższa zmiana modelu (handlery, konwencje); lokalna kolejka nie dzieli pracy między węzłami; nie weryfikowałem licencji i kosztu |
| **Coravel**, **NCronJob** | brak trwałości (w pamięci) | harmonogram w procesie | brak | utrata stanu przy restarcie, więc nie spełniają wymogu S-02 bez własnej tabeli |
| **MassTransit** | wg jednego bloga wersja 9 przechodzi na licencję komercyjną z darmową warstwą dla małych zespołów (niezweryfikowane) | magistrala komunikatów | zależnie od transportu | zbyt ciężki dla pojedynczego zadania |
| **Temporal / Elsa** | osobna infrastruktura | orkiestracja workflow | własny serwer | przesada dla jednego kroku „skanuj" |

## Opcja bez biblioteki (propozycja, nie wynik wyszukiwania)

`BackgroundService` w workerze + własna tabela `Scans` (statusy `Queued` / `Running` / `Completed` / `Incomplete` / `Failed`) przez EF Core. Worker odpytuje tabelę i claimuje wiersz atomowo (`UPDATE ... WHERE status='Queued'`, ewentualnie `FOR UPDATE SKIP LOCKED`). Zalety w tym repo:
- zero nowych pakietów i jeden zestaw migracji EF (zgodnie z `AGENTS.md`),
- tabela skanów i tak jest potrzebna jako źródło prawdy o stanie (`infrastructure.md:158-159`, UI ma pokazywać failed/incomplete),
- jest to wprost „hosted background services" z `tech-stack.md`,
- restart usługi nie gubi pracy, bo kolejką jest tabela.

Koszty: sam piszesz odzyskiwanie po awarii (skan w stanie `Running` po restarcie), ponowienia i, w S-05, harmonogram cron (można dodać Quartz lub małą bibliotekę parsującą cron dopiero wtedy).

## Wnioski

- Wymóg „przeżyć restart" i „wspólny schemat bez dodatkowych migracji" najlepiej spełniają: **własna tabela + `BackgroundService`** (najmniej zależności) i **TickerQ** (EF Core, wspólny `DbContext`).
- **Quartz.NET** ma najmocniejszą dojrzałość i wsparcie PostgreSQL, ale jest harmonogramem, nie kolejką, nie ma dashboardu i tworzy własne tabele poza EF. Naturalnie pasuje do S-05 (skany cykliczne), mniej do S-02.
- **Hangfire** pozostaje poprawną opcją (dojrzały, domyślne ponowienia i dashboard), ale jego magazyn PostgreSQL to osobny pakiet społecznościowy z własnymi tabelami poza EF.
- **Wolverine** i **MassTransit** są nieproporcjonalne do zakresu S-02.
- Niezależnie od wyboru stan skanu dla UI powinien żyć w tabeli portalu, a nie w stanie zadania biblioteki.

## Niezweryfikowane

- Zgodność runtime Hangfire.PostgreSql 1.21.1, TickerQ 10.4.0 i Quartz 3.20 / 4.1 z EF Core 10 + Npgsql 10.0.3 w tym repo (sprawdzono tylko metadane NuGet i dokumentację).
- Dojrzałość i model licencyjny TickerQ oraz Wolverine.
- Zachowanie przerwanych zadań po restarcie usługi Windows w każdej z bibliotek.
