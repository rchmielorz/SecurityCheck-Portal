<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Authenticated App Shell Implementation Plan

- **Plan**: context/changes/authenticated-app-shell/plan.md
- **Scope**: Full plan
- **Reviewed phases**: 1, 2, 3, 4
- **Date**: 2026-09-27
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 4 warnings, 6 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | WARNING |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Success criteria evidence

- `dotnet build api`: passed, 0 warnings.
- `git grep -i weatherforecast -- api`: no matches.
- `git grep -n "SigningKey" -- api/appsettings*.json`: no matches.
- `dotnet test api.Tests`: 43/43 passed.
- `cd web && npm run typecheck`: passed.
- `cd web && npm run build:api`: passed and created `api/wwwroot/index.html`.
- `git grep -n "fonts.googleapis" -- web/app`: no matches.
- Windows SSO grep over `infrastructure.md` and `AGENTS.md`: no matches.
- 2.2 (guard test fails when an anonymous endpoint is added): not re-run in this review, because it needs a temporary source edit. Progress marks it `[x]` (2e20e6a).
- Manual 1.4, 1.5, 3.5–3.10, 4.3, 4.4: marked `[x]` with commit SHAs. The real-AD checks cannot be confirmed from the diff. The code supports each claim, so none look rubber-stamped.
- The only change to `plan.md` in the diff is to Progress. No plan content was edited silently.

## Findings

### F1 — LDAP timeout does not cover bind/search

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: api/Auth/LdapAuthenticator.cs:32-65
- **Detail**: The comment at line 32 says "The timeout bounds the whole exchange (connect, TLS handshake, bind, search)", and the code passes a `CancelAfter` token to `BindAsync`/`SearchAsync`/`HasMoreAsync`. In Novell 4.0.0 the reply waits underneath those calls (`LdapMessageQueue.GetResponse`, `Message.WaitForReply`) are synchronous and take no token. `Constraints.TimeLimit` and `SearchConstraints.TimeLimit` default to 0, meaning "no limit". This was confirmed by reflecting over the package DLL. If a DC accepts TCP/TLS but never answers the bind or search, the login request hangs indefinitely and holds a thread-pool thread. The plan's "Unavailable (timeout)" outcome is then never reached.
- **Fix**: Set `connection.Constraints = new LdapConstraints { TimeLimit = ms }` and pass `new LdapSearchConstraints { TimeLimit = ms, ServerTimeLimit = s }` to `SearchAsync`. LDAP code 85 (timeout) then goes through the existing `LdapException` catch to `Unavailable`. Fix the comment.
  - Strength: Uses the library's own timeout mechanism. The error mapping already exists.
  - Tradeoff: No automated test can reproduce a silent DC. It needs a manual check, or a TCP listener that accepts connections and never replies.
  - Confidence: MED — the API surface was confirmed by reflection, but runtime behaviour of `TimeLimit` on bind was not exercised.
  - Blind spot: Whether `TimeLimit` applies to the TLS handshake phase. An outer `Task.WaitAsync(timeout)` would give a hard upper bound.
- **Decision**: PENDING

### F2 — Bind identity (UPN) and authorized identity (sAMAccountName) can diverge

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: api/Auth/LdapAuthenticator.cs:52, :62, :81
- **Detail**:
  - The code binds as `{userName}@{UpnSuffix}` (line 52), but the group check and display name come from a search on `sAMAccountName={userName}` (line 62, `LdapFilter.cs:44`).
  - Where UPN prefixes differ from sAMAccountNames, or `UpnSuffix` is an alternate suffix, user A can bind with their own password. The search can then match a different user B who is in the allowed group, and A receives a JWT carrying B's identity.
  - Separately, `sub` is the raw input (line 81), so `ALICE` and `alice` become different identities. That will split per-user data in later slices.
  - The plan specified this bind/search pair, so this is partly a plan flaw.
- **Fix**: Search on `userPrincipalName={Escape(userName@UpnSuffix)}`, the same name that was bound. Read `sAMAccountName` from the returned entry and use it as `sub` and `userName`.
  - Strength: The authorized entry is then provably the bound account, and the identity becomes canonical.
  - Tradeoff: Adds one attribute and changes the filter. `LdapFilter` tests and the fake need small updates.
  - Confidence: HIGH — this is the standard way to close the bind/search gap in AD.
  - Blind spot: The company's actual UPN/sAMAccountName convention is unknown. If they always match, the exposure today is theoretical.
- **Decision**: PENDING

### F3 — Per-IP-only rate limit still allows AD account lockout

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: api/Program.cs:83-96
- **Detail**: The only partition is `RemoteIpAddress` at 5 per minute, which is 300 failed binds per hour against any chosen `userName` from one IP. That is enough to trip a typical AD lockout threshold (5–10) and lock a colleague out of the whole domain, not just the portal. It matches the plan exactly (Phase 1.6), so the gap is in the plan. The infrastructure.md risk row relies on this limit as its mitigation. Behind IIS in-process hosting the IP is correct. A future ARR or load balancer would need `UseForwardedHeaders`.
- **Fix**: Chain a second fixed-window partition keyed on the lower-cased `userName` (e.g. 5 per 15 minutes, below the AD lockout threshold), and add one test for it.
  - Strength: Directly bounds attempts per account, whatever the source IP.
  - Tradeoff: An attacker can still lock the portal (not AD) for a named user for the window.
  - Confidence: HIGH — `PartitionedRateLimiter.CreateChained` is built in.
  - Blind spot: The actual AD lockout threshold and observation window are not known.
- **Decision**: PENDING

### F4 — Raw, unvalidated username written to logs

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: api/Auth/AuthEndpoints.cs:52-53
- **Detail**: `LogWarning("Failed login for {UserName} ...", request.UserName, ...)` logs the input before any format validation, so it can contain CR/LF (log forging in plain-text sinks), be unbounded in length, or contain a password typed into the login field by mistake.
- **Fix**: Log `request.UserName` only when `LdapFilter.IsValidUserName` passes. Otherwise log a fixed placeholder such as `"<invalid>"`.
- **Decision**: PENDING

### F5 — SPA fallback `{*path:nonfile}` rejects paths with a dot in the last segment

- **Severity**: 💡 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Architecture
- **Location**: api/Program.cs:125
- **Detail**: `MapFallbackToFile("index.html")` only matches paths without a file extension. A deep link like `/repos/x/2.1.*` or `/users/jan.kowalski` gets 401 (anonymous) or 404 (signed in) instead of `index.html`. S-01 (`repo-version-pattern`) is about version patterns such as `2.1.*`, so the next slice is likely to hit this.
- **Fix**: Record a constraint for S-01 planning: keep dots out of the last URL segment (use IDs or put versions in the query string). Only if that is not acceptable, replace the fallback with a custom `/{**path}` route that excludes `/assets`.
  - Strength: No code change now. The decision moves to where the URLs are designed.
  - Tradeoff: It is a constraint S-01 must remember.
  - Confidence: HIGH — the behaviour is visible in `NoPublicEndpointsTests.cs:16`.
  - Blind spot: S-01's URL design does not exist yet.
- **Decision**: PENDING

### F6 — `apiFetch` does not handle 401 as the plan intended

- **Severity**: 💡 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Adherence
- **Location**: web/app/lib/api.ts:13-25
- **Detail**: Plan 3.3 says the shared fetch "on 401 in protected routes redirects to `/login?next=…`". Only `requireUser` does this; `apiFetch` returns the raw `Response`. Today `shouldRevalidate() { return true }` in `app-layout.tsx:15-17` hides the gap by re-checking `/api/me` on every navigation. S-01 loaders that call `apiFetch` directly will receive a 401 and have to handle it themselves.
- **Fix A ⭐ Recommended**: Have `apiFetch` throw `redirect("/login?next=…")` on 401 for any path except `/api/auth/login` and `/api/me`.
  - Strength: Matches the plan's intent. Every future loader is covered automatically.
  - Tradeoff: `apiFetch` becomes router-aware, and it needs the current path (a `request` argument, as `requireUser` has).
  - Confidence: HIGH — the same pattern as `requireUser`.
  - Blind spot: How S-01 will structure its calls.
- **Fix B**: Keep `apiFetch` raw and amend the plan: 401 handling = layout revalidation plus `requireUser`.
  - Strength: No code change, and it keeps a thin client.
  - Tradeoff: A 401 in the middle of an action (e.g. a form post) surfaces as a generic error.
  - Confidence: MED — works for read-only pages, less so for actions.
  - Blind spot: S-01's use of `clientAction`.
- **Decision**: PENDING

### F7 — Unplanned catch-all route and layout revalidation

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: web/app/routes.ts:14, web/app/routes/not-found.tsx, web/app/routes/app-layout.tsx:15-17, web/app/lib/api.ts:33
- **Detail**: These were added beyond the plan: `route("*", "routes/not-found.tsx")` under the protected layout, `shouldRevalidate → true`, and `requireUser(request)` instead of `requireUser()`. All three are justified. The catch-all is needed for manual check 3.8 (an anonymous deep link redirects to login instead of a root 404). The other two fix URL and expiry detection. None of them is documented in the plan.
- **Fix**: Add a short addendum to plan.md (or a change.md note) listing these three deviations and why they were made.
- **Decision**: PENDING

### F8 — Logout ignores a failed response

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: web/app/routes/app-layout.tsx:24-36
- **Detail**: `response.ok` is not checked. If logout fails (network or 5xx), the cookie survives. The code navigates to `/login`, whose `clientLoader` gets 200 from `/api/me` and redirects back to `/`, so the user silently stays logged in. Also, a network error in `requireUser` shows the generic root error with no path back to login.
- **Fix**: When logout fails, show an inline "Nie udało się wylogować" message instead of navigating.
- **Decision**: PENDING

### F9 — `index.html` served without `Cache-Control: no-cache`

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: api/Program.cs:107, :125; web/scripts/copy-to-api.mjs:16
- **Detail**: `index.html` is served with no `Cache-Control` header, so browsers may cache it heuristically. `copy-to-api.mjs` deletes the old hashed assets on each build. After a redeploy, a cached `index.html` references deleted assets and the app breaks until a hard refresh. This matters more once F-02 deploys regularly.
- **Fix**: Set `Cache-Control: no-cache` for `index.html` via `StaticFileOptions.OnPrepareResponse` and on the fallback (can be deferred to F-02).
- **Decision**: PENDING

### F10 — Stale SSR leftovers after the switch to SPA mode

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: web/package.json:9,13-15; web/Dockerfile
- **Detail**: With `ssr: false` there is no `build/server` any more. The following are now dead: `"start": "react-router-serve ./build/server/index.js"`, the Dockerfile `CMD ["npm","run","start"]`, and the `@react-router/node`, `@react-router/serve` and `isbot` dependencies. The plan did not ask for this cleanup, so it is not drift, but the leftovers point future agents at a server that no longer exists.
- **Fix**: Remove the `start` script, the three dependencies and `web/Dockerfile`, or leave them for F-02 (deploy-skeleton).
- **Decision**: PENDING

## Noted, not raised as findings

- Stateless JWT stays valid up to 8 h after logout or group removal. The plan accepts this ("What We're NOT Doing").
- `LdapAuthenticator` maps only LDAP/socket/IO/TLS exceptions to 503. Any other library exception becomes a 500. Acceptable.
- `PortalFactory` temp-dir cleanup catches only `IOException`, not `UnauthorizedAccessException`. Test hygiene only.
- LDAPS certificate revocation checking is off by library default.
- `appsettings.Development.json` is listed in plan 1.1 but unchanged. No change was needed, because user-secrets hold the dev config.
