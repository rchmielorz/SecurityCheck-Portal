<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Ręczny skan wersji i lista podatności (S-02)

- **Plan**: context/changes/manual-version-scan/plan.md
- **Scope**: Full plan (Phases 1-6 of 6)
- **Reviewed phases**: 1, 2, 3, 4, 5, 6
- **Date**: 2026-10-01
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 6 warnings, 4 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

Success criteria evidence: `dotnet test api.Tests --no-build` 310 passed / 3 skipped / 0 failed (C# unchanged since phase 5; a live API process locks api\bin, so a fresh build was not possible); `npm run typecheck` exit 0; `has-pending-model-changes`: none; all manual items checked by the user.

## Findings

### F1 — Claimed scan can stay Running forever after a transient DB failure

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: worker/ScanWorker.cs (TryRunNextAsync), core/Scanning/ScanJobRunner.cs:54-65
- **Detail**: If the DB fails after a claim (load fails, or FinishAsync and TryFinishAsync both fail), the row stays `Running`. Only RecoverInterruptedAsync fixes it, and it runs only at worker start. The partial unique index then answers 409 "active" for that pattern and the UI polls "W trakcie" until someone restarts the worker.
- **Fix**: Add a periodic sweep in the worker that fails `Running` scans older than the sum of timeouts (~35 min) that are not the one currently processed (or retry the finish with backoff). Add a test.
  - Strength: Closes the only path where a pattern can be blocked without a restart.
  - Tradeoff: Another background step; must not touch the scan being processed.
  - Confidence: HIGH — reasoning follows from the code paths cited.
  - Blind spot: Real frequency of DB blips on the target server.
- **Decision**: FIXED (Fix now) — sweep porzuconych skanów Running w ScanQueue/ScanWorker + test

### F2 — Recovery is not worker-aware (second worker / overlapping restart kills a live scan)

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: core/Scanning/ScanQueue.cs:69, core/Scanning/WorkDirectory.cs (DeleteOrphans)
- **Detail**: RecoverInterruptedAsync fails every `Running` scan and deletes every numeric folder in WorkRoot. A second worker, or a restart overlapping a still-live process, marks the first worker's active scan Interrupted and deletes its checkout mid-scan. The plan scopes to one worker, but nothing enforces it.
- **Fix**: Enforce a single worker with a Postgres advisory lock taken at worker start (fail fast if held), and document it in AGENTS.md.
  - Strength: Cheap, makes the single-worker assumption true instead of implied.
  - Tradeoff: Worker refuses to start while an old instance still holds the lock.
  - Confidence: MED — advisory-lock lifetime tied to the connection must be held for the process lifetime.
  - Blind spot: How the Windows Service restart sequence interleaves old and new process.
- **Decision**: ACCEPTED-AS-RULE: Recovery po restarcie musi zakładać jednego właściciela (kod bez zmian)

### F3 — `git clone --branch <tag>` may resolve a same-named branch first

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: core/Git/GitCliCheckout.cs:68
- **Detail**: From git's behaviour as recalled (not tested here), `--branch X` prefers `refs/heads/X` over `refs/tags/X`. A repository with a branch and a tag both named `2.1.3` would clone the branch tip; the commit check then returns CommitMismatch with a misleading "tag moved" message, so that scan can never succeed. Fail-safe, but no test covers it.
- **Fix**: Clone `--depth 1` without `--branch`, then `git fetch --depth 1 origin tag <tag>` (or `refs/tags/<tag>`) and check out FETCH_HEAD; add a branch-and-tag collision test against a local bare repo.
  - Strength: Resolves the exact tag ref the pattern resolution used.
  - Tradeoff: Two git invocations instead of one.
  - Confidence: MED — behaviour recalled from git source; verify with a local repo first.
  - Blind spot: Not reproduced on the installed git version.
- **Decision**: SKIPPED

### F4 — One bad character in a Trivy report loses the whole scan result

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: core/Scanning/TrivyReportParser.cs:148 (Cut), ScanJobRunner.FinishAsync
- **Detail**: `Cut` truncates by UTF-16 index and can split a surrogate pair; a NUL (U+0000) in a title, library or target (possible in real advisories) is rejected by PostgreSQL (22021). SaveChangesAsync then throws and the outer catch stores `Failed(ScannerFailed, "Unexpected error: PostgresException")`: the result is not shown as clean, but a complete scan is discarded.
- **Fix**: Add one `Clean` helper in the parser (strip NUL, replace lone surrogates) used by Text/Cut, and cut on a rune boundary. Add parser tests: NUL, emoji at the length limit, over-long title/library/target.
  - Strength: Small, local, covered by parser contract tests.
  - Tradeoff: Slightly alters stored text for pathological input.
  - Confidence: HIGH for NUL (Postgres rejects it); MED for surrogates.
  - Blind spot: Whether Npgsql rejects lone surrogates in this version.
- **Decision**: FIXED (Fix now) — TrivyReportParser.Clean + cięcie bez rozcinania surogatów + testy

### F5 — Scan page fails open for an unknown status

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: web/app/routes/scan-details.tsx:131-138
- **Detail**: The final return in `Results` (the Completed rendering with "Brak wyników") is reached by any status that is not Queued/Running/Failed/Incomplete. A new enum value added to the API without a UI update would show "no results" without proof, breaking the plan's central invariant.
- **Fix**: Render the "Brak wyników" branch only when `status === "Completed"`; for anything else show a neutral "Nieznany status skanu" Alert.
- **Decision**: FIXED (Fix now) — gałąź "Brak wyników" tylko dla Completed, Alert dla nieznanego statusu

### F6 — Unbounded Trivy report and findings payload

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: core/Scanning/TrivyScanner.cs:137-138, api/Scans/ScanEndpoints.cs (GetScanAsync)
- **Detail**: The whole report JSON is read into one string, and the API returns every finding unpaginated, sorted in memory. A large npm monorepo with thousands of findings yields tens of MB in the report, the API response and the React list; the 3 s poll refetches the full payload around completion.
- **Fix**: Fail with ScannerFailed when the report file exceeds a size cap (e.g. 50-100 MB) before reading it; leave API pagination for S-03 but note the limit.
  - Strength: Bounds worker memory with a one-line check.
  - Tradeoff: A very large legitimate report fails instead of completing.
  - Confidence: MED — real sizes for this team's repos are unknown.
  - Blind spot: Typical report size for the customer repositories.
- **Decision**: SKIPPED

### F7 — UI copy and edge states are slightly misleading

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: web/app/routes/scan-details.tsx:109 and :54, web/app/routes/repo-details.tsx (scan branch of clientAction)
- **Detail**: (a) The Incomplete alert always says "Brakuje plików lock (NuGet i npm)", even when Trivy found no target and MissingLockFiles is empty. (b) The page shows the Trivy DB date, not its age (plan: "wiek bazy"). (c) A 409 `active` without `scanId` (winner finished before re-query) falls through to the "inactive" message. (d) The Completed text does not say the scan covers NuGet/npm lock files only, so a mixed repository (e.g. requirements.txt plus one lock file) shows a green "brak podatności".
- **Fix**: Vary the Incomplete text when MissingLockFiles is empty; show "(N dni)" next to the DB date; map the 409 by `reason` with a "spróbuj ponownie" message for active-without-id; add a "NuGet/npm" qualifier to the Completed text.
- **Decision**: FIXED (Fix now) — teksty Incomplete, wiek bazy, mapowanie 409 po reason (jednokrotny odczyt ciała), kwalifikator NuGet/npm

### F8 — WorkDirectory.Delete follows reparse points

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: core/Scanning/WorkDirectory.cs:24
- **Detail**: The attribute-clearing walk uses EnumerateFiles(AllDirectories) without reparse-point protection, unlike LockFileDetector. A symlink in an untrusted checkout could make it clear attributes outside the checkout (.NET recursion behaviour not verified).
- **Fix**: Use EnumerationOptions with AttributesToSkip = ReparsePoint and keep Directory.Delete(recursive) as the final step.
- **Decision**: SKIPPED

### F9 — Process-tree cleanup after a hard worker crash

- **Severity**: 💡 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: core/Processes/ProcessRunner.cs, worker/Program.cs
- **Detail**: A hard crash leaves orphaned git/Trivy processes (no Windows Job Object — consciously out of scope in the plan), which may still write into WorkRoot while recovery deletes the folder.
- **Fix**: Leave as is (documented in the plan's Open Risks); revisit with a kill-on-close Job Object in S-05.
- **Decision**: SKIPPED (zgodnie z planem — Job Object przy S-05)

### F10 — Small drift and tidy-ups

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: core/Git/GitCliTagSource.cs:18,26; AGENTS.md:16; core/Scanning/TrivyScanner.cs (FirstLine)
- **Detail**: (a) ls-remote output is now capped at 8 MiB while the plan said ls-remote behaviour must not change (practically unreachable). (b) AGENTS.md line 16 still describes `core/` as holding only Data/ and Git/ and does not list Scanning/ and Processes/. (c) `FirstLine` is duplicated in TrivyScanner and GitCliTagSource. (d) GitCliCheckout depends on IOptions<ScanOptions>, which only AddScanning validates (documented in a comment).
- **Fix**: Update the AGENTS.md structure line; share one FirstLine helper; leave the 8 MiB cap and the options coupling documented.
- **Decision**: FIXED (Fix now) — AGENTS.md (core/Scanning, Processes), wspólny FirstLine; limit 8 MiB i sprzężenie opcji zostają udokumentowane
