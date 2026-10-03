<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Generowanie lock-ów .NET przed skanem (scan-rules-change)

- **Plan**: context/changes/scan-rules-change/plan.md
- **Scope**: Full plan (4 of 4 phases)
- **Reviewed phases**: 1, 2, 3, 4
- **Date**: 2026-10-03
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 4 warnings, 2 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

Success criteria run during the review: LockFileDetectorTests 25 pass, DotnetLockFileGeneratorTests 6 pass, ScanOptionsTests 11 pass, TrivyScannerTests 22 pass, full suite 343 pass / 3 skipped (real Trivy/Git tests), `web` typecheck and build OK, `dotnet build api` OK. No unchecked Progress rows remain; manual rows 2.4, 3.4, 3.5, 4.2 were confirmed by the user.

Triaged out before reporting: a reviewer finding that exceptions can escape `DotnetLockFileGenerator.GenerateAsync` (dismissed: `ScanJobRunner` catches `Exception` around the pipeline and stores `Failed`, `ScanJobRunner.cs:73`); a reviewer CRITICAL on running repo MSBuild (downgraded to F1: the plan explicitly accepted this risk, the real defect is the inaccurate doc comment).

## Findings

### F1 — Doc claims feeds come only from the worker account's NuGet.config, but a repo NuGet.config is honored

- **Severity**: ⚠️ WARNING
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Safety & Quality
- **Location**: core/Scanning/DotnetLockFileGenerator.cs:8-16 (class doc), AGENTS.md (Scan rules), context/foundation/infrastructure.md (worker setup step)
- **Detail**: NuGet walks up from the project folder, so a `NuGet.config` (and `Directory.Build.targets`, `global.json` msbuild-sdks) inside the checkout is honored and runs/loads repo-controlled content with the worker account's rights, with USERPROFILE/APPDATA passed through. The plan accepted running repo MSBuild ("akceptacja + utwardzone środowisko"), but the class doc overstates the isolation ("feeds come only from the NuGet.config of the worker account").
- **Fix A ⭐ Recommended**: Correct the class doc, AGENTS.md and infrastructure.md to state the accepted risk plainly (repo NuGet.config and MSBuild files are honored; only the Git PAT is kept out of the environment; secrets in the worker profile are reachable).
  - Strength: Matches the decision already taken with the user; no behavior change; the risk is documented where the next agent looks.
  - Tradeoff: The exposure remains.
  - Confidence: HIGH — wording only.
  - Blind spot: None significant.
- **Fix B**: Pass `--configfile <worker-owned NuGet.config>` so repo NuGet.config is ignored, and give restore an isolated HOME/NUGET_PACKAGES.
  - Strength: Removes the attacker-feed path and shields the profile's NuGet credentials.
  - Tradeoff: Changes how feeds are configured on the server (a file path option), does not stop MSBuild targets, and was not agreed.
  - Confidence: MEDIUM — only half of the exposure goes away.
  - Blind spot: Whether internal repos rely on their own repo-level NuGet.config for private feeds.
- **Decision**: FIXED (Fix A: class doc, AGENTS.md and infrastructure.md now state the accepted risk)

### F2 — Two or more .csproj in one directory overwrite one packages.lock.json, giving a false "Completed"

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: core/Scanning/LockFileDetector.cs:49-53 (`dotnetProjects.AddRange`), core/Scanning/DotnetLockFileGenerator.cs (loop)
- **Detail**: Each project in the directory is restored separately but all write the same `<dir>/packages.lock.json`; the last one wins. Afterwards `FindMissing` sees a lock file and the scan can be `Completed` while the earlier projects' dependencies were never scanned — exactly the false "clean" the whole change is meant to prevent. The plan listed this case as "not doing" (last write wins), but that contradicts its own principle.
- **Fix**: Generate only for directories with exactly one `.csproj`; leave multi-project directories without a generated lock so they stay in `MissingLockFiles` (scan `Incomplete`). Add a detector test and a generator test.
  - Strength: Preserves "never clean without proof"; small change in `FindDotnetProjectsWithoutLock`.
  - Tradeoff: Such directories stay unscanned until a lock exists in the repo.
  - Confidence: HIGH — follows from NuGet's one-lock-per-directory behavior.
  - Blind spot: How common multi-csproj directories are in your repositories.
- **Decision**: SKIPPED (user: not fixing now)

### F3 — No overall limit on restore time

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: core/Scanning/DotnetLockFileGenerator.cs:34-56, core/Scanning/ScanOptions.cs:32, core/Scanning/ScanQueue.cs:101-105 (`AbandonedAfter`)
- **Detail**: `RestoreTimeoutMinutes` (10) is per project and projects are restored serially, so a monorepo with 30 projects can run up to 300 minutes before Trivy starts, blocking the serial queue and keeping the pattern blocked. `RestoreTimeoutMinutes` is not part of `AbandonedAfter`. The sweep excludes the in-flight scan and there is one serial worker, so a live scan is not swept; the cost is queue blocking and a longer window after a crash, not data corruption.
- **Fix**: Add an overall budget (e.g. `RestoreTotalTimeoutMinutes`, default 20) enforced with a linked cancellation source in `GenerateAsync`; projects not restored in time stay in `MissingLockFiles`. Add the budget to `AbandonedAfter` and a test next to ScanQueueTests.
  - Strength: Bounds scan duration and keeps the sweep window consistent.
  - Tradeoff: Large monorepos may exceed the budget and come out `Incomplete`.
  - Confidence: HIGH — the arithmetic is visible in the code.
  - Blind spot: Typical project counts per repository.
- **Decision**: FIXED (RestoreTotalTimeoutMinutes, default 20; included in AbandonedAfter; tests)

### F4 — A timeout or kill can leave a partial packages.lock.json that counts as present

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: core/Scanning/DotnetLockFileGenerator.cs (switch on `result.Outcome`)
- **Detail**: After `TimedOut` (process tree killed) or a failed restore, a half-written `packages.lock.json` may exist next to the project. `FindMissing` would then treat the directory as complete and Trivy would either fail to parse it or read a partial lock.
- **Fix**: After a `TimedOut` or non-zero restore, delete the project's `packages.lock.json` if the generator created it (it did not exist before the restore). Add a test.
- **Decision**: FIXED (partial lock deleted after a failed or timed-out restore; tests)

### F5 — Tests do not assert some Success Criteria literally

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: api.Tests/Scanning/DotnetLockFileGeneratorTests.cs:61-81, api.Tests/Scanning/TrivyScannerTests.cs:137-148, api.Tests/Scanning/ScanOptionsTests.cs
- **Detail**: (a) The env test plants `SCW_TEST_SECRET`, not `Git__Token`; its `Git__Token` assertion is on the static list only. (b) Only the negative order case is tested (no restore when the DB is too old); nothing asserts restore runs before `trivy fs`. (c) No test that `DotnetExecutablePath` is `[Required]`. (d) The plan's manual step "check worker logs that Git__Token is absent from the restore environment" has no Progress row.
- **Fix**: Plant `Git__Token` instead of the made-up variable, add an order assertion (restore call recorded before the `fs` call), add a `Required` case to `ScanOptionsTests`; the manual log check can be dropped or done once on the server.
- **Decision**: FIXED (Git__Token planted in the env test, restore-before-fs order test, DotnetExecutablePath Required case)

### F6 — Documentation and configuration details for operations

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: worker/appsettings.json:10-12, AGENTS.md (Scan rules), core/Scanning/DotnetLockFileGenerator.cs (environment)
- **Detail**: `worker/appsettings.json` lists the other timeouts but not `RestoreTimeoutMinutes`/`DotnetExecutablePath`. The cleared environment means `NuGetPackageSourceCredentials_*` variables do not reach restore, so feeds must be configured in the profile's NuGet.config (documented in infrastructure.md, not in AGENTS.md); an unauthenticated feed silently gives an `Incomplete` scan. `HTTP_PROXY`/`HTTPS_PROXY` (possibly with credentials) are inherited, as for Trivy. Pre-existing and out of scope: `.fsproj`/`.vbproj` are never flagged by the detector.
- **Fix**: Add the two options to `worker/appsettings.json` with defaults and one sentence in AGENTS.md about feed credentials living in the worker account's NuGet.config.
- **Decision**: FIXED (options added to worker/appsettings.json; AGENTS.md sentence on feed credentials)
