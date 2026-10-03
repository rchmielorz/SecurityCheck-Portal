---
date: 2026-10-01
source: Context7 (/hangfireio/hangfire.documentation)
topic: "Hangfire: fragmenty dokumentacji potrzebne do S-02 (skan poza żądaniem HTTP)"
tags: [s-02, hangfire, background-jobs, docs]
status: draft
---

# Hangfire: dokumentacja pod S-02 manual-version-scan

Uzupełnia [research.md](research.md) i [cliwrap-docs.md](cliwrap-docs.md). Fragmenty pochodzą z Context7. Sekcja „Dopasowanie do repo" to wnioski autora notatki, nie dokumentacja. Hangfire w tym repo jeszcze nie występuje (brak w `.csproj`).

## Czego Context7 nie zwróciło

**Magazyn PostgreSQL.** Zapytanie o `Hangfire.PostgreSql` / `UsePostgreSqlStorage` nie dało wyniku: Context7 nie ma tej biblioteki, a oficjalna dokumentacja Hangfire opisuje tylko SQL Server i Redis. Hangfire.PostgreSql jest osobnym, społecznościowym pakietem (nie od autorów Hangfire). Jego API, wsparcie dla .NET 10 i EF Core/Npgsql 10 trzeba sprawdzić w jego README i na NuGet przed planem. To największa luka tej notatki, bo `tech-stack.md` wymaga PostgreSQL.

## 1. Rejestracja i serwer

```csharp
services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(Configuration.GetConnectionString("HangfireConnection"))); // dla Postgresa: inny pakiet, patrz wyżej

// Serwer przetwarzania jako IHostedService
services.AddHangfireServer();

services.AddHangfireServer(options =>
{
    options.Queues = new[] { "critical", "default", "low" };
    options.WorkerCount = Environment.ProcessorCount * 2;
});
```

- `AddHangfire` konfiguruje magazyn i klienta, `AddHangfireServer` dodaje serwer przetwarzania jako `IHostedService`. Można je rejestrować w różnych procesach: proces z samym `AddHangfire` tylko kolejkuje zadania, a proces z `AddHangfireServer` je wykonuje (dokumentacja pokazuje osobno `BackgroundJobServer` w konsoli: `using (new BackgroundJobServer()) { ... }`).
- Kolejki: `[Queue("name")]` na metodzie, a `Queues` w opcjach serwera ustala priorytet. Dedykowany serwer można ograniczyć do wybranych kolejek (`ServerName`, `Queues`).

## 2. Kolejkowanie skanu (fire-and-forget)

```csharp
public class OrderController : Controller
{
    private readonly IBackgroundJobClient _backgroundJobs;
    public OrderController(IBackgroundJobClient backgroundJobs) => _backgroundJobs = backgroundJobs;

    [HttpPost]
    public IActionResult Create(Order order)
    {
        _backgroundJobs.Enqueue<IOrderService>(x => x.ProcessOrder(order.Id));
        return Ok();
    }
}
```

- `Enqueue` serializuje wywołanie metody i jej argumenty do magazynu i od razu wraca. Serwer pobiera zadanie, ukrywa je przed innymi workerami, wykonuje i usuwa z kolejki po sukcesie.
- Argumenty są serializowane, więc przekazuj identyfikatory (np. `scanId`), nie obiekty.
- Stany: Enqueued, Scheduled, Awaiting, Processing, Failed, Succeeded, Deleted. Kontynuacje: `BackgroundJob.ContinueJobWith(jobId, () => ...)`.

## 3. Anulowanie, zamykanie serwera

```csharp
public async Task LongRunningJob(CancellationToken token)
{
    token.ThrowIfCancellationRequested();
    // ...
}
BackgroundJob.Enqueue<MyService>(x => x.LongRunningJob(CancellationToken.None)); // token jest podmieniany przez Hangfire

services.AddHangfireServer(new BackgroundJobServerOptions { CancellationCheckInterval = TimeSpan.FromSeconds(5) });
new BackgroundJobServerOptions { StopTimeout = TimeSpan.FromSeconds(10) }
```

- `CancellationToken` (od 1.7) jest wspierany; Hangfire odpytuje magazyn co `CancellationCheckInterval`.
- Przy zamknięciu serwera zadania używające tokenu **wracają na początek kolejki** i uruchomią się ponownie. `StopTimeout` daje czas na dokończenie przy zatrzymaniu. `server.Dispose()` blokuje do pełnego, łagodnego zamknięcia.
- Token odzwierciedla zamknięcie serwera lub przerwanie zadania, a nie limit czasu skanu. Timeout trzeba zrobić samemu (np. `CancellationTokenSource.CancelAfter` połączony z tokenem Hangfire).

## 4. Ponowienia i idempotentność

```csharp
[AutomaticRetry(Attempts = 0)]                       // bez ponowień
[AutomaticRetry(Attempts = 5, DelaysInSeconds = new[] { 60, 300, 600, 1800, 3600 })]
GlobalJobFilters.Filters.Add(new AutomaticRetryAttribute { Attempts = 5 });   // globalnie
client.Requeue(jobId);                               // ręczne ponowienie
```

- Best practices: metody w tle mają być **reentrant**, bo zadanie może być uruchomione ponownie po wyjątku lub zamknięciu serwera. Przed wykonaniem sprawdzaj stan operacji.
- Ponowienia są domyślnie włączone (domyślna liczba prób nie padła w pobranych fragmentach).

## 5. Dashboard

```csharp
public class MyAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
        => context.GetHttpContext().User.Identity?.IsAuthenticated ?? false; // "potentially dangerous"
}
app.UseHangfireDashboard("/hangfire", new DashboardOptions { Authorization = new[] { new MyAuthorizationFilter() } });
```

- Domyślnie dashboard jest dostępny tylko z lokalnych żądań. Dokumentacja ostrzega, że wpuszczenie wszystkich zalogowanych jest potencjalnie niebezpieczne (dashboard pozwala zarządzać zadaniami).
- Można ustawić tryb tylko do odczytu (`IsReadOnlyFunc`) na podstawie roli.

## 6. Dopasowanie do repo (wnioski, nie dokumentacja)

- **Wymóg „żadna strona nie jest publiczna"** (`AGENTS.md`, `NoPublicEndpointsTests`): `UseHangfireDashboard` dodaje nową trasę. Musi przejść przez fallback policy `RequireAuthenticatedUser` i test `NoPublicEndpointsTests` albo zostać wyłączony. Dla MVP rozsądne jest pominięcie dashboardu.
- **Dwa wdrażalne procesy** (`infrastructure.md:113`, `:186`): API w IIS tylko kolejkuje, a `SecurityCheck.Worker` jako usługa Windows ma `AddHangfireServer`. Oba muszą wskazywać ten sam magazyn.
- **Migracje**: repo stosuje migracje EF Core ręcznie (`dotnet ef`), API nie migruje przy starcie (`AGENTS.md`). Czy Hangfire.PostgreSql tworzy własny schemat automatycznie i jak to się układa z kolejnością „stop worker, migracja, wymiana API" (`infrastructure.md:162`), jest do sprawdzenia w dokumentacji tego pakietu.
- **Ponowienia vs wiarygodność wyniku**: automatyczne ponowienie skanu mogłoby ukryć błąd lub zdublować wynik. Do rozważenia `[AutomaticRetry(Attempts = 0)]` i jawny status `failed`/`incomplete` w bazie portalu, a nie tylko stan zadania Hangfire.
- **Stan skanu dla użytkownika**: UI powinno czytać stan z własnej tabeli skanów (zgodnie z `infrastructure.md:158-159`), nie z API monitorującego Hangfire. Stan zadania Hangfire może służyć diagnostyce.
- **Restart serwera**: zadanie przerwane zamknięciem wróci do kolejki i wykona się od nowa (sekcja 3), więc skan musi być idempotentny: ten sam `scanId` nie może zapisać wyniku dwa razy ani zostawić półskończonego katalogu.
- **Zakres**: Hangfire dostarcza kolejkę i ponowienia, ale nie zastępuje checkoutu, wywołania Trivy ani parsera (patrz `cliwrap-docs.md`). Harmonogram cykliczny (S-05) w Hangfire to `RecurringJob`. Ta część nie była tematem zapytań.
