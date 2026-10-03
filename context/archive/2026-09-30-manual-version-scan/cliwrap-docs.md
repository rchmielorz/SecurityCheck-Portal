---
date: 2026-09-30
source: Context7 (/tyrrrz/cliwrap; Readme.md, wiki, _autodocs/api-reference)
topic: "CliWrap — fragmenty dokumentacji potrzebne do S-02 (uruchomienie Trivy z workera)"
tags: [s-02, cliwrap, docs]
status: draft
---

# CliWrap — dokumentacja pod S-02 manual-version-scan

Uzupełnia [research.md](research.md). Fragmenty pochodzą z Context7, sekcja „Przykład dla S-02" to szkic autora notatki, nieskompilowany.

## 1. Komenda, argumenty, katalog roboczy, środowisko

```csharp
using CliWrap;

var result = await Cli.Wrap("path/to/exe")
    .WithArguments(["--foo", "bar"])
    .WithWorkingDirectory("work/dir/path")
    .ExecuteAsync();
// result.ExitCode, IsSuccess, StartTime, ExitTime, RunTime
```

- `WithArguments` przyjmuje `string`, `IEnumerable<string>` albo builder `a => a.Add(..)`. Wersja z kolekcją unika ręcznego escapowania (zalecana, przekazuje ścieżki i adresy repo bezpiecznie).
- Zmienne środowiskowe:

```csharp
Cli.Wrap("git").WithEnvironmentVariables(env => env
    .Set("GIT_AUTHOR_NAME", "John")
    .Set("GIT_AUTHOR_EMAIL", "john@email.com"));
```

## 2. Przekierowanie wyjścia (duże raporty JSON)

Domyślnie stdout i stderr trafiają do `PipeTarget.Null` (odrzucane). Cele: `ToStream`, `ToFile`, `ToStringBuilder`, `ToDelegate` (linia po linii), `Merge`.

```csharp
var errorBuffer = new StringBuilder();

var cmd = Cli.Wrap("curl").WithArguments(["-s", "https://api.example.com/data"])
    | (
        PipeTarget.ToFile("response.json"),
        PipeTarget.ToStringBuilder(errorBuffer)
    );

await cmd.ExecuteAsync();
```

- Odpowiednik bez operatorów: `.WithStandardOutputPipe(...)` i `.WithStandardErrorPipe(...)`.
- `ExecuteBufferedAsync()` trzyma całe wyjście w pamięci — dokumentacja ostrzega przed użyciem przy dużych lub binarnych danych.
- `ToStringBuilder` używa `Encoding.Default`; istnieją przeciążenia z kodowaniem.

## 3. Timeout i anulowanie

```csharp
using var forcefulCts = new CancellationTokenSource();
using var gracefulCts = new CancellationTokenSource();

forcefulCts.CancelAfter(TimeSpan.FromSeconds(10)); // twardy limit (kill)
gracefulCts.CancelAfter(TimeSpan.FromSeconds(7));  // najpierw sygnał przerwania (SIGINT / Ctrl+C)

try
{
    await Cli.Wrap("ffmpeg")
        .WithArguments(["-i", "input.mp4", "output.webm"])
        .ExecuteAsync(forcefulCts.Token, gracefulCts.Token);
}
catch (OperationCanceledException) { /* anulowano */ }
```

- Anulowanie zabija proces i `ExecuteAsync` rzuca `OperationCanceledException` (lub `TaskCanceledException`). Metoda kończy się dopiero po zakończeniu procesu.
- Pojedynczy token = od razu kill. Dwa tokeny = łagodne przerwanie z awaryjnym kill.
- Dotyczy też `ExecuteBufferedAsync`, `ListenAsync`, `Observe`.
- Uwaga spoza Context7 (issue #37 w CliWrap, z wcześniejszego wyszukiwania): na Windows procesy potomne mogą przeżyć kill rodzica.

## 4. Kod wyjścia i błędy (skan nieudany vs czysty)

Domyślnie niezerowy kod wyjścia powoduje `CommandExecutionException` (`ExitCode`, `Command`). `CommandResultValidation`: `None` (0) lub `ZeroExitCode` (1, domyślne).

```csharp
var result = await Cli.Wrap("git")
    .WithValidation(CommandResultValidation.None)
    .ExecuteAsync();

if (result.IsSuccess) { /* ... */ }
if (result.ExitCode == 42) { /* ... */ }
```

Dla S-02: z `WithValidation(None)` sami mapujemy kod wyjścia na stan skanu. Jawnie odróżniamy `failed` (niezerowy kod, timeout, brak pliku raportu) od `completed` z pustą listą („brak wyników").

## 5. Przykład dla S-02 (szkic)

```csharp
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
var stderr = new StringBuilder();

var result = await Cli.Wrap(trivyPath)
    .WithArguments(["fs", "--format", "json", "--scanners", "vuln",
                    "--output", reportPath, repoCheckoutPath])
    .WithStandardErrorPipe(PipeTarget.ToStringBuilder(stderr))
    .WithValidation(CommandResultValidation.None)
    .ExecuteAsync(timeout.Token);   // OperationCanceledException => skan nieudany (timeout)

// result.IsSuccess && File.Exists(reportPath) => parsuj raport (System.Text.Json)
// w przeciwnym razie => status failed + stderr.ToString() jako przyczyna
```

Do potwierdzenia w `/10x-plan`: flagi Trivy, ścieżka binarki na serwerze, limit czasu, sprzątanie katalogu checkoutu oraz Job Object na Windows.
