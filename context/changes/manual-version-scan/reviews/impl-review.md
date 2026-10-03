<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Ręczny skan wersji i lista podatności (S-02) — przegląd po poprawkach (r2)

- **Plan**: context/changes/manual-version-scan/plan.md
- **Scope**: Full plan (Phases 1-6 of 6) + niezacommitowane poprawki z triage r1
- **Reviewed phases**: 1, 2, 3, 4, 5, 6
- **Date**: 2026-10-03
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 2 warnings, 3 observations
- **Previous report**: reviews/impl-review-r1.md (10 findings; F1, F4, F5, F7, F10 fixed; F2 lesson; F3, F6, F8, F9 skipped — not re-reported)

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

Success criteria evidence: `dotnet test api.Tests` 317 passed / 3 skipped / 0 failed (after r1 fixes); `npm run typecheck` exit 0; no pending model changes; plan-drift agent: 38 items MATCH, 0 MISSING. Invariants re-verified: "Brak wyników" only for Completed; Incomplete never looks clean; Trivy gets no PAT.

## Findings

### F1 — Stale-Running sweep does not prove exclusive ownership (violates lesson "Recovery po restarcie musi zakładać jednego właściciela")

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: core/Scanning/ScanQueue.cs:101-124 (FailAbandonedAsync), worker/ScanWorker.cs:63-80, core/Scanning/ScanJobRunner.cs:28-38; also ScanQueue.cs:64-89 (RecoverInterruptedAsync)
- **Detail**: The sweep fails every Running row older than AbandonedAfter and excludes only `runner.CurrentScanId`, a per-process field. A second worker or an overlapping restart during redeploy can sweep another worker's live scan once it passes the cutoff. Within one process the exclusion is dead code, because the loop is serial (the sweep never runs while RunAsync is active). The same class of bug as r1-F2 (recovery at start), now also in a periodic path. The cutoff (clone + scan + DB-update + 5 min) also omits the Git ls-remote timeout and two `trivy version` calls; the margin covers them only with default settings.
- **Fix**: Enforce one worker: take `pg_try_advisory_lock` on a connection held for the process lifetime and refuse to start without it (settles r1-F2 and this finding together); keep `inFlightScanId` only as a defensive parameter or drop it; include the ls-remote timeout in the cutoff.
  - Strength: One mechanism closes both recovery and sweep hazards and satisfies the accepted lesson.
  - Tradeoff: A new worker will not start while the old instance still holds the lock; needs a clear startup message.
  - Confidence: MED — lock must live as long as the connection; Windows Service restart ordering untested.
  - Blind spot: How the redeploy script stops/starts the service.
- **Decision**: FIXED (Fix now) — WorkerLock (pg_try_advisory_lock na dedykowanym połączeniu) przy starcie workera, fail-fast gdy zajęty; limit sweepa uwzględnia timeouty git i trivy version; testy

### F2 — FinishAsync is read-then-write; a late finish can overwrite a swept Failed row

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: core/Scanning/ScanJobRunner.cs:195-232
- **Detail**: The `Status == Running` filter is applied only at read time; SaveChanges then issues `UPDATE ... WHERE Id = @id` with no Status predicate or concurrency token (verified: no IsRowVersion/xmin/ConcurrencyToken in core/). If the sweep or RecoverInterruptedAsync flips the row to Failed(Interrupted) between the SELECT and the UPDATE, the late write sets Completed/Incomplete and inserts findings while FailureReason/FailureDetail from the sweep remain — a Completed scan with an Interrupted failure reason. The window is milliseconds and needs two processes today; it becomes reachable once F1 is realised.
- **Fix**: Make the final write conditional: either add a Postgres xmin/row-version concurrency token on Scan and treat DbUpdateConcurrencyException as "result dropped", or do the status change as `ExecuteUpdate ... WHERE Id=@id AND Status='Running'` in the same transaction as the findings insert. Add a test that flips the row to Failed between read and write.
  - Strength: Makes Failed terminal regardless of who wrote it; independent of F1.
  - Tradeoff: Touches the write path of every scan; needs a migration if a token column is added (xmin needs none).
  - Confidence: HIGH — read-then-write confirmed by reading the code.
  - Blind spot: Whether Npgsql xmin mapping interacts with the partial index/migration snapshot.
- **Decision**: FIXED (Fix now) — warunkowy ExecuteUpdate WHERE Status=Running w jednej transakcji z wynikami (bez migracji); test wyścigu sweep/finish

### F3 — Plan text, docs and small couplings are stale after the sweep

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: plan.md "Critical Implementation Details" (state sequencing), worker/ScanWorker.cs:62-87, core/Scanning/TrivyScanner.cs:94,129,237
- **Detail**: (a) The plan says `Running -> Failed(Interrupted)` only at worker start; the sweep now does it periodically too. (b) A scan stuck at the DB-failure point holds its pattern with 409 "active" for ~35 min before the sweep acts, and Queued rows are never swept (worker down = pattern blocked; UI only warns after 2 min) — undocumented. (c) TrivyScanner now calls GitCliTagSource.FirstLine, so scanning code depends on a Git class; a shared helper would be cleaner.
- **Fix**: Add a one-line addendum to the plan's state-sequencing note and a note in AGENTS.md about the ~35 min hold and unswept Queued; optionally move FirstLine to a shared helper.
- **Decision**: FIXED (Fix now) — addendum w plan.md (sekwencjonowanie stanów), notka w AGENTS.md, wspólny TextHelpers.FirstLine

### F4 — describeAgeDays renders "NaN dni temu" for an invalid timestamp

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: web/app/lib/scan.ts:85-90
- **Detail**: An unparseable ISO string gives NaN; `Math.max(0, Math.floor(NaN))` is NaN, so the label reads "NaN dni temu" (verified). It also counts 24-hour units and depends on the browser clock; a future timestamp is clamped to "dziś". The age is a freshness hint for the vulnerability DB, so it should fail closed (omit) rather than show garbage.
- **Fix**: Return an empty string when the days value is not finite and have the caller omit the parenthesis in that case.
- **Decision**: FIXED (Fix now) — describeAgeDays zwraca pusty tekst dla niepoprawnej daty, wywołujący pomija nawias

### F5 — Scan 409/202 handling in repo-details is not fully fail-safe

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: web/app/routes/repo-details.tsx:84-90, 101-104, 161-173
- **Detail**: The 409 body is correctly read once, but errorMessage treats anything other than reason "active" as "inactive": a 409 with an empty or non-JSON body (e.g. from a proxy) tells the user to activate the pattern. Separately, `await response.json()` on the 202 path (line 165) is unguarded; a malformed 202 body throws into the route error boundary although the scan was queued.
- **Fix**: Map reason "inactive" to SCAN_INACTIVE and everything else to the generic unexpected-error message; wrap the 202 parse in try/catch and fall back to a reload message.
- **Decision**: FIXED (Fix now) — 409 mapowane po reason (inne → ogólny błąd), parsowanie 202 w try/catch z komunikatem odśwież stronę
