---
change_id: partial-scan-result
title: Informacja o częściowo udanym skanie (przeskanowane i nieprzeskanowane projekty)
status: implemented
created: 2026-10-03
updated: 2026-10-03
archived_at: null
---

## Notes

Wynika z weryfikacji zmiany `scan-rules-change` na prawdziwym repozytorium: restore projektów .NET nie powiódł się (błędny serwer NuGet), a przeskanowane zostały tylko projekty React/npm. Wynik pokazał tylko ogólne „Skan niepełny" i listę brakujących lock-ów, bez informacji, co zostało przeskanowane i dlaczego restore się nie udał.

Założenie użytkownika: stan takiego skanu to nie `Incomplete`, a `Completed`, z informacją o częściowym powodzeniu.

Do rozstrzygnięcia (tu leży napięcie): dziś `Incomplete` istnieje po to, by częściowy skan nigdy nie wyglądał jak „czysto" (infrastructure.md:159, `ScanOutcome.cs`). Zmiana stanu na `Completed` musi to zachować w inny sposób, np. przez widoczną listę przeskanowanych celów i nieprzeskanowanych projektów z powodem. Wymaga danych, których baza dziś nie przechowuje: cele z raportu Trivy (`Results[].Target`, parser zna tylko `TargetCount`) oraz powód nieudanego restore (migracja, kontrakt API, UI).

Wejście z przeglądu `scan-rules-change` (ustalenie F2, pominięte, `context/changes/scan-rules-change/reviews/impl-review.md`): gdy w jednym katalogu leży kilka plików `.csproj`, generator restore'uje każdy z nich osobno, a wszystkie zapisują ten sam `packages.lock.json`. Wygrywa ostatni, więc po generowaniu `FindMissing` widzi lock, a skan może być `Completed`, choć zależności wcześniejszych projektów nie zostały sprawdzone. To dokładnie fałszywe „czysto", które ta zmiana musi wykluczyć przy każdej definicji „Completed" dla skanu częściowego. Proponowana poprawka z przeglądu: generować lock tylko dla katalogów z jednym `.csproj`, a katalogi z kilkoma zostawiać jako nieprzeskanowane (z powodem w nowej liście).
