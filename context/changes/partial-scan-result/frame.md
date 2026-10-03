# Frame Brief: Informacja o częściowo udanym skanie

> Framing step before /10x-plan. This document captures what is *actually*
> at issue, separated from what was initially assumed.

## Reported Observation

Po skanie, w którym restore projektów .NET się nie powiódł (błędny serwer NuGet), a przeskanowano tylko projekty React/npm, wynik pokazał ogólne „Skan niepełny" i listę brakujących lock-ów. Nie było informacji, co zostało przeskanowane ani dlaczego restore się nie udał.

## Initial Framing (preserved)

- **User's stated cause or approach**: Stan takiego skanu powinien być `Completed`, a nie `Incomplete`, z informacją o częściowym powodzeniu.
- **User's proposed direction**: Osobna zmiana (`partial-scan-result`), która to wprowadza.
- **Pre-dispatch narrowing**: Brakowało **powodu** nieprzeskanowania (nie listy przeskanowanych celów, nie samej etykiety). Stan `Incomplete` jest „uczciwy, brakło szczegółów". Zakres „częściowego powodzenia": restore .NET nieudany, npm bez locka, kilka `.csproj` w jednym katalogu.

## Dimension Map

The observation could originate at any of these dimensions:

1. **Stan wyniku (`Completed` vs `Incomplete`)** — zakłada, że sama etykieta `Incomplete` jest błędem. ← initial framing
2. **Dane o powodzie** — czy system w ogóle zapisuje, dlaczego dany projekt nie został przeskanowany.
3. **Prezentacja** — czy UI pokazuje to, co system wie.
4. **Zakres „nieprzeskanowanych"** — które sytuacje trafiają na listę, a które znikają.
5. **Lista przeskanowanych celów** — czy brakowało informacji, co zostało sprawdzone.

## Hypothesis Investigation

| Hypothesis | Evidence | Verdict |
| --- | --- | --- |
| 1. Stan: partial → `Completed` | Dla `Completed` z zerem podatności UI pokazuje „Brak podatności" (`web/app/lib/scan.ts:109-112`). Wcześniejsza decyzja: „brak wyników" tylko dla `Completed` (`context/archive/2026-09-30-manual-version-scan/plan.md:51`). Użytkownik: `Incomplete` jest uczciwy. | NONE (framing nie wytrzymuje) |
| 2. Powód nie jest zapisywany | Generator loguje powód tylko do logu workera i nic nie zwraca (`core/Scanning/DotnetLockFileGenerator.cs`, `GenerateAsync`). Model ma tylko `MissingLockFiles` (`core/Data/Scan.cs:74-75`), API tylko to pole (`api/Scans/ScanContracts.cs:48`). Istnieje `FailureDetail` ze sanityzacją (`Scan.cs:53`), ale dotyczy skanu `Failed`, nie pozycji listy. | STRONG |
| 3. UI nie pokazuje powodu | Sekcja wypisuje same ścieżki (`web/app/routes/scan-details.tsx:122-130`), komunikat jest ogólny („…lub nie udało się ich wygenerować (NuGet restore zakończył się błędem)"), identyczny dla każdego przypadku. Skutek hipotezy 2: UI nie ma czego pokazać. | WEAK (przyczyna wtórna) |
| 4. Część nieprzeskanowanych nie jest listowana | Kilka `.csproj` w katalogu: restore każdego zapisuje ten sam lock, potem `FindMissing` widzi lock i katalog nie trafia na listę (ustalenie F2 z `scan-rules-change`, pominięte). Skan może być `Completed` bez sprawdzenia wcześniejszych projektów. | STRONG |
| 5. Brakuje listy przeskanowanych | Użytkownik nie wskazał tego jako braku. Dodatkowo parser zapisuje cel tylko przy podatności (`core/Scanning/TrivyReportParser.cs:106-108`), więc taka lista wymagałaby nowych danych. | NONE (brak potrzeby) |

## Narrowing Signals

- „Dlaczego nie przeskanowano" jest tym, czego brakowało. „Co przeskanowano" i „inna etykieta" nie zostały wybrane.
- Stan `Incomplete` oceniony jako „uczciwy, brakło szczegółów" — przeczy pierwotnemu założeniu o `Completed`.
- Zakres obejmuje trzy sytuacje, w tym kilka `.csproj` w katalogu, która dziś nie daje żadnego wpisu na liście.

## Cross-System Convention

Skan, który pomija część celów, zwykle raportuje je osobno wraz z powodem pominięcia, a nie podnosi stanu wyniku. W tym projekcie już obowiązuje decyzja, że „czysto" wolno pokazać tylko dla skanu kompletnego. Zmiana stanu na `Completed` byłaby sprzeczna z tą zasadą i z infrastructure.md:159.

## Reframed (or Confirmed) Problem Statement

> **The actual problem to plan around is**: skan `Incomplete` nie mówi, dla każdego nieprzeskanowanego projektu, dlaczego nie został sprawdzony, a jedna klasa takich projektów (kilka `.csproj` w katalogu) w ogóle nie jest na liście.

Stan `Incomplete` jest poprawny i zostaje. Brakuje powodu przy każdej pozycji (restore nieudany, npm bez locka, kilka projektów w jednym katalogu) oraz uczciwego wykrycia, że ostatni przypadek jest nieprzeskanowany. Zmiana na `Completed` nie rozwiązałaby problemu i przywróciłaby ryzyko fałszywego „brak podatności".

## Confidence

**HIGH** — dowody z kodu (powód nie jest zapisywany, kilka `.csproj` niewidoczne), zgodność z wcześniejszą decyzją o `Completed` oraz bezpośrednia odpowiedź użytkownika, że `Incomplete` jest uczciwy. Investigacja wykonana lokalnie, bez subagentów: zakres jednoobszarowy, wszystkie dowody to bezpośrednie odczyty kodu i dokumentów.

## What Changes for /10x-plan

Plan nie dotyczy zmiany stanu na `Completed`, tylko zapisania i pokazania powodu przy każdej nieprzeskanowanej pozycji oraz dopisania do listy projektów z kilkoma `.csproj` w katalogu. Stan `Incomplete` i jego warunki zostają. Decyzje do podjęcia w planie: kształt danych (pozycja = ścieżka + powód, migracja `MissingLockFiles`), jak oczyścić komunikat restore z sekretów, oraz sposób postępowania z katalogami z wieloma `.csproj`.

## References

- Source files: `core/Scanning/TrivyScanner.cs:154-162`, `core/Scanning/DotnetLockFileGenerator.cs`, `core/Scanning/TrivyReportParser.cs:106-108`, `core/Data/Scan.cs:53,74-75`, `api/Scans/ScanContracts.cs:48`, `web/app/lib/scan.ts:109-114`, `web/app/routes/scan-details.tsx:106-135`
- Related research: `context/changes/partial-scan-result/research.md`
- Prior decisions: `context/archive/2026-09-30-manual-version-scan/plan.md:51`, `context/archive/2026-10-03-scan-rules-change/reviews/impl-review.md` (F2)
- Investigation tasks: none (lokalnie)
