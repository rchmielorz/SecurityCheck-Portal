<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Repozytorium i wzorzec wersji

- **Plan**: context/changes/repo-version-pattern/plan.md
- **Scope**: Full plan
- **Reviewed phases**: 1, 2, 3, 4, 5
- **Date**: 2026-09-28
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 3 warnings, 5 observations
- **Triage (2026-09-28)**: Fixed F1 (Fix A), F2, F3, F7; Accepted F5; Skipped F4, F6, F8.

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

## Success criteria evidence

- `dotnet build api`: 0 warnings, 0 errors.
- `dotnet ef migrations has-pending-model-changes --project core --startup-project api`: exit 0.
- Git unit tests: 100/100. `dotnet test api.Tests` with Docker running: 181/181, 0 skipped.
- The PAT (`settings.Token`) appears only in `GIT_CONFIG_VALUE_0` (`core/Git/GitCliTagSource.cs:135`).
- `git grep "planned but not yet installed" -- AGENTS.md`: no matches.
- `NoPublicEndpointsTests`: only two new `InlineData` rows; the allow-list is unchanged.
- `cd web && npm run typecheck` and `npm run build:api`: both pass.
- 1.4 (tests without Docker: 44 passed, 5 skipped) was verified during implementation and not re-run here.
- Manual 1.5–1.6, 2.5–2.6, 3.4–3.5, 4.4–4.9 and 5.3–5.4 were all confirmed by the user in this session. 2.6 was checked with the `.git` URL; 2.5 and 3.4 were checked through the UI, not the `.http` file.
- Nothing in "What We're NOT Doing" was violated.

## Findings

### F1 — git inherits the full API environment and system/global git config

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: core/Git/GitCliTagSource.cs:126-135
- **Detail**: `startInfo.Environment` starts as a copy of the API process environment, and the code only adds keys to it. Two consequences:
  - `git.exe` and `git-remote-https` receive every app secret held in environment variables. On the server that is `ConnectionStrings__Portal`, `Auth__Jwt__SigningKey` and `Git__Token`, per `infrastructure.md:138`.
  - Inherited `GIT_*` and proxy variables, plus the system and global gitconfig, still apply. `-c credential.helper=` resets only the credential helpers. For example, `http.sslVerify=false` in the service account's global config would silently send the PAT header over unverified TLS, and `url.*.insteadOf` could reroute the request.

  The plan did not require isolation, but research §4 recommended a controlled config.
- **Fix A ⭐ Recommended**: Isolate git config and pin TLS verification. Set `GIT_CONFIG_NOSYSTEM=1`, set `GIT_CONFIG_GLOBAL` to an empty file (or `NUL` on Windows), add `-c http.sslVerify=true`, remove inherited `GIT_*` variables, and extend `GitCliTagSourceTests`.
  - Strength: Closes the TLS downgrade and URL-rewrite paths with a few lines in the pure `CreateStartInfo` helper, which already has tests.
  - Tradeoff: A proxy the company might need would then have to be set explicitly (e.g. a `Git:Proxy` option), because inherited git config is ignored.
  - Confidence: HIGH — these are standard git environment switches; git 2.38 supports them.
  - Blind spot: Whether the server needs a proxy for GitLab has not been verified.
- **Fix B**: Also start from a cleared environment and copy only `PATH`, `SystemRoot`, `TEMP`/`TMP` and `USERPROFILE`/`HOME`.
  - Strength: The app secrets no longer reach child processes at all.
  - Tradeoff: More risk of breaking git on Windows (schannel and certificate store lookups need some system variables); it needs a manual smoke test on the server.
  - Confidence: MED — works in principle, but the minimal set of variables Git for Windows needs is unverified.
  - Blind spot: Behaviour under the IIS app-pool identity.
- **Decision**: FIXED via Fix A — `-c http.sslVerify=true`; inherited `GIT_*` variables removed; `GIT_CONFIG_NOSYSTEM=1` and `GIT_CONFIG_GLOBAL=/dev/null` set (verified locally: git then reads no system or global config); new test added (182/182). Re-check "Sprawdź" against GitLab once, since the system gitconfig is no longer read.

### F2 — A URL near the length limit gives a 500 instead of a 400

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: core/Git/RepositoryUrl.cs:39, :92; api/Repositories/RepositoryEndpoints.cs:445-453
- **Detail**: The limit checks the input length (`input.Length > Repository.UrlMaxLength`, 2048), but the canonical form appends `.git` afterwards. An input of 2045–2048 characters without `.git` therefore becomes up to 2052 characters, which exceeds `varchar(2048)`. PostgreSQL error 22001 is not mapped (only 23505 and 23503 are), so the `DbUpdateException` escapes as a 500.
- **Fix**: In `TryNormalize`, reject when `candidate.Length > Repository.UrlMaxLength`, and add a boundary test to `RepositoryUrlTests`.
- **Decision**: FIXED — the canonical length is checked after `.git` is appended; a boundary test covers exactly 2048 (accepted) and 2049 (rejected); break-check passed.

### F3 — Implementation deviations are recorded only in commit messages

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: context/changes/repo-version-pattern/change.md:14-17
- **Detail**: `change.md` records only the `.git`/lower-case URL deviation and the move of check 2.5. The plan text still says "bez końcowego `.git`" (`plan.md:234`, `:300`). Several adaptations exist only in commit messages or chat:
  - P1: the DbContext reads the connection string from `IConfiguration`, while `DatabaseOptions` validates it at start; new migrations need `--output-dir Data/Migrations`.
  - P3: adding a repository or pattern uses an explicit transaction with two `SaveChanges` instead of one; `DbUpdateConcurrencyException` maps to 404; routes use `{id:long}`.
  - P4: event descriptions use gender-neutral nouns instead of the plan's verbs.

  The next reviewer or agent will treat the plan as the source of truth.
- **Fix**: Append these points to "Odstępstwa w implementacji" in `change.md`, adding a note next to the `.git` item that the plan text for Phase 2.2 and 2.6 describes the original assumption.
- **Decision**: FIXED — the P1, P3 and P4 deviations and the plan-text note for 2.2/2.6 were appended to `change.md`.

### F4 — AGENTS.md still describes the .http file as login-only

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: AGENTS.md:16
- **Detail**: The line reads "`securitycheck-portal.http` (login → `me` → logout smoke test)", but Phase 5 added the S-01 sequence to that file.
- **Fix**: Change the description to "login → S-01 repository/pattern sequence → logout smoke test".
- **Decision**: SKIPPED

### F5 — No cap on concurrent git processes

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: api/Repositories/RepositoryEndpoints.cs:35, :234
- **Detail**: Each add-pattern and resolve starts a git process that can run up to `Git:TimeoutSeconds` (30 s by default, 600 max), and nothing limits how many run at once. The plan deliberately excludes a per-user limit ("What We're NOT Doing"). The risk is small because only logged-in team members can call these endpoints.
- **Fix**: Accept as a risk for now; if needed later, add a `SemaphoreSlim` in `GitCliTagSource` or a rate-limiter policy.
- **Decision**: ACCEPTED — risk accepted: internal tool, only logged-in team members, consistent with the plan's "no per-user limit". Revisit if S-02/S-05 add more git traffic.

### F6 — Concurrent activate/deactivate can log duplicate events

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: api/Repositories/RepositoryEndpoints.cs:305-317
- **Detail**: The "already in target state" check reads the row without a concurrency token. Two simultaneous `deactivate` requests can both pass it and write two `PatternDeactivated` events. The stored state stays correct; only the log gets a duplicate row.
- **Fix**: Use `UseXminAsConcurrencyToken()` on `VersionPattern` (it maps to the existing 404/409 handling), or accept the risk.
- **Decision**: SKIPPED

### F7 — Rare 409s show a misleading or generic message in the UI

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: api/Repositories/RepositoryEndpoints.cs:228-231; web/app/routes/repo-details.tsx:74, :311-320
- **Detail**: Two cases:
  - Adding a pattern while another request deletes the repository: the FK violation (23503) returns 409 with no body, and the SPA shows "Ten wzorzec już istnieje."
  - Resolving an inactive pattern returns 409, which falls through to "Wystąpił nieoczekiwany błąd." The UI hides the button in this case, so it is rare.
- **Fix**: Map 23503 during add-pattern to 404 (the SPA then shows the not-found path), and map resolve 409 to "Wzorzec jest nieaktywny — aktywuj go, aby sprawdzić."
- **Decision**: FIXED — 23503 during add-pattern now returns 404; the SPA shows "Wzorzec jest nieaktywny — aktywuj go, aby sprawdzić." for resolve 409. Build, tests (183/183) and typecheck pass; no new test, because the race cannot be reproduced reliably.

### F8 — Git:ExecutablePath is not enforced as an absolute path on the server

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: core/Git/GitOptions.cs:12
- **Detail**: The default is `git`, resolved through PATH. The plan and AGENTS.md say to use an absolute path on the server, but nothing validates it, so a PATH hijack on the server would run another binary with the PAT in its environment.
- **Fix**: Outside Development, validate `Path.IsPathRooted(ExecutablePath)` at start.
- **Decision**: SKIPPED
