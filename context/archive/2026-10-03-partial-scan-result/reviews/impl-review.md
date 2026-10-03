<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Powód nieprzeskanowania przy skanie Incomplete (partial-scan-result)

- **Plan**: context/changes/partial-scan-result/plan.md
- **Scope**: Full plan (4 of 4 phases)
- **Reviewed phases**: 1, 2, 3, 4
- **Date**: 2026-10-03
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 4 warnings, 3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

Success criteria run during the review: full `dotnet test api.Tests -c Release` 374 pass / 3 skipped (real Trivy/Git tests; DB tests ran in a Postgres container), `web` typecheck and build OK, `dotnet build api` and `worker` OK, no unchecked Progress rows (manual rows 3.5, 4.3, 4.4, 4.5 confirmed by the user). Plan adherence reviewer: all planned changes match; no violation of "What We're NOT Doing".

Verified during triage of reviewer claims: with a real `dotnet restore` of a project with a missing package, stdout starts with the banner "Trwa określanie projektów do przywrócenia..." and stderr is empty, and the error line carries the absolute project path (F1, F3 confirmed). No `AsSplitQuery`/`UseQuerySplittingBehavior` exists in core or api (F2 confirmed). Dismissed: the "Detail not clipped to 300 in the generator" drift (`TextHelpers.FirstLine` already caps at 300, so end state is correct).

## Findings

### F1 — Detail takes the restore banner instead of the error line

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: core/Scanning/DotnetLockFileGenerator.cs (branch `Exited when ExitCode != 0`, `TextHelpers.FirstLine(...)`)
- **Detail**: `dotnet restore` writes MSBuild/NuGet errors to stdout and leaves stderr empty. The first stdout line is the progress banner ("Trwa określanie projektów do przywrócenia..."), so for a typical NU1101 failure the stored `Detail` is the banner and the UI shows a useless line under "restore NuGet zakończył się błędem" (reproduced with a missing package). The same line selection also feeds the worker log. The earlier manual check passed only because that failure wrote to stderr (workload error).
- **Fix**: Pick the first stdout/stderr line that contains `error ` (MSBuild/NuGet format, e.g. `error NU1101`, `error MSB...`), falling back to the last non-empty line, then the first line. Add a test with realistic stdout (banner + error line).
  - Strength: Restores the intent of the plan ("the line that says why") for the most common failure class.
  - Tradeoff: Heuristic on the message format; localized banners are skipped because the match is on `error `, not on text.
  - Confidence: HIGH — reproduced against the real `dotnet`.
  - Blind spot: Other MSBuild languages: the `error` token itself is not localized in MSBuild output.
- **Decision**: FIXED (DescribeFailure picks the first line with error, falls back to the last non-empty line; tests)

### F2 — Two collection Includes in one query (Findings × UnscannedItems)

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: api/Scans/ScanEndpoints.cs:99-102 (`GetScanAsync`)
- **Detail**: `.Include(s => s.Findings).Include(s => s.UnscannedItems)` is a single SQL query with no split behavior configured anywhere, so rows multiply (findings × unscanned items, repeating `Targets` and `Title`). A scan with thousands of findings and dozens of unscanned manifests returns a very wide result for one GET, which the UI polls every few seconds while a scan is active.
- **Fix**: Add `.AsSplitQuery()` to this query.
- **Decision**: FIXED (AsSplitQuery on the details query)

### F3 — Detail can expose worker paths and feed URL userinfo

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: core/Scanning/DotnetLockFileGenerator.cs (Detail), core/Scanning/ScanJobRunner.cs:267-281 (`Sanitize` masks only the Git PAT), core/Data/ScanUnscannedItem.cs:20 (XML doc "never contains secrets")
- **Detail**: Restore error lines begin with the absolute project path (confirmed: `C:\...\src\App\App.csproj : error NU1101: ...`), which leaks `Scan:WorkRoot` and the worker account layout to every portal user via `detail`; messages can also contain feed URLs with userinfo (`https://user:pass@feed/...`) or `token=`/`sig=` query values from a repo-controlled or worker NuGet.config. The doc comment overclaims.
- **Fix**: In the generator, strip the checkout directory prefix from the message (leaves `src\App\App.csproj : error ...`), and mask URL userinfo (`://user:pass@` → `://***@`) and `token|sig|key|password=` values; soften the XML doc to "secrets are masked best-effort". Add tests for each masking rule.
  - Strength: Removes the confirmed leak without changing what the user needs to read (relative project path + error).
  - Tradeoff: Best-effort masking by pattern cannot be proven complete.
  - Confidence: HIGH for paths (reproduced), MED for URL/query patterns.
  - Blind spot: Other secret shapes in feed error messages.
- **Decision**: FIXED (checkout prefix removed, URL userinfo and token-like values masked, doc softened; tests)

### F4 — Deploy ordering: dropped column and renamed API field

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: core/Data/Migrations/20261003143134_AddScanUnscannedItems.cs, AGENTS.md (database / deploy notes)
- **Detail**: The migration drops `Scans.MissingLockFiles`; if it is applied while the old API or worker still runs, those processes fail on every `Scans` read/write. The API does not migrate on start. The API field `missingLockFiles` is renamed to `unscanned`; a browser holding the old SPA bundle throws on Incomplete scans (`scan.missingLockFiles.length`) until a hard reload.
- **Fix**: Add one sentence to AGENTS.md: stop the API and the worker, apply the migration, then start both on the new version together (hard-reload browsers).
- **Decision**: SKIPPED (user: deploy-order note not added)

### F5 — NoLockFile label is wrong when restore succeeded without producing a lock

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: web/app/lib/scan.ts:73-74, core/Scanning/TrivyScanner.cs:160-170
- **Detail**: When `dotnet restore` exits 0 but no `packages.lock.json` appears (e.g. an SDK/targets quirk), the generator emits no item and the scanner falls back to `NoLockFile`, whose label says "dla npm nie jest generowany", which is wrong for a restored .NET project.
- **Fix**: Reword the label to "brak pliku lock (nie został wygenerowany)".
- **Decision**: FIXED (label reworded)

### F6 — Clip can cut a surrogate pair or keep a NUL character

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: core/Scanning/ScanJobRunner.cs:259-282 (`Clip`, `Sanitize`)
- **Detail**: Clipping at a fixed length can split a surrogate pair and a NUL char in `Detail` is rejected by PostgreSQL; either fails the whole result transaction, leaving the scan Running until the sweep and losing the findings. The same weakness already exists for `FailureDetail`.
- **Fix**: Surrogate-safe clip (cut one char earlier when the last kept char is a high surrogate) and remove control characters (including `\0`) in `Sanitize`.
- **Decision**: FIXED (surrogate-safe Clip, control characters removed in Sanitize; tests)

### F7 — A few Success Criteria are weakly asserted

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: api.Tests/ScanEndpointsTests.cs:318-333, api.Tests/Scanning/TrivyScannerTests.cs, api.Tests/Scanning/DotnetLockFileGeneratorTests.cs
- **Detail**: (a) "API sorted by path ordinal": the test data sort the same way ordinal and culture-sensitively; (b) no test that generator items whose path `FindMissing` no longer reports are ignored and that duplicates by path collapse; (c) generator `Detail` has no test of the 300 cap; (d) the `Down` migration and the `unnest` copy are covered only by the manual step.
- **Fix**: Add an ordinal-vs-culture case (e.g. `Zeta` vs `alpha`), a scanner test for the ignored/duplicate generator items, and a generator test for a 500-character message.
- **Decision**: FIXED (ordinal sort case, ignored generator item, 300 cap; duplicate-collapse test not added: needs a production seam)
