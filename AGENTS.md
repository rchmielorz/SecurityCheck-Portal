# Repository Guidelines

SecurityCheck Portal is an internal tool that scans dependencies of customer-deployed repository versions for known vulnerabilities. Stack: ASP.NET Core minimal API (.NET 10) in `api/`, React Router 8 + Tailwind 4 UI in `web/`; the repo root holds no project. PostgreSQL via EF Core/Npgsql is planned but not yet installed.

## Hard Rules

- Every API endpoint and UI route must require authentication; the PRD forbids any public page (@context/foundation/prd.md, Non-Functional Requirements). The API enforces this with a fallback authorization policy. The only anonymous exceptions are `POST /api/auth/login` and the SPA files (static assets + `index.html` fallback, which render only the login form); `api.Tests/NoPublicEndpointsTests.cs` fails on any other (see Testing).
- UI route URLs must not have a dot in the last path segment (e.g. `/repos/x/2.1.*`, `/users/jan.kowalski`): the API serves `index.html` through `MapFallbackToFile` (`{*path:nonfile}`), so such deep links get 401/404 instead of the SPA. Use IDs in the path and put values like version patterns in the query string, or replace the fallback route on purpose.
- Do not write CVE/dependency-analysis logic. Detection must delegate to the external scanner, Trivy (PRD Non-Goals).
- A scan must run against the code matching the declared version pattern (e.g. `2.1.*`), never the default branch.
- Never write under `context/archive/`; it is read-only. Change-scoped docs go in `context/changes/<change-id>/`; foundation docs are edited in place (@context/foundation/README.md).
- Keep backend and frontend separated: `.cs` files only in .NET project folders (`api/` or a sibling), UI code only in `web/`. Do not add a project file at the repo root.

## Project Structure

- `api/` — the API: `Program.cs`, `securitycheck-portal.csproj`, `appsettings*.json`, `Properties/launchSettings.json`, `securitycheck-portal.http` (login → `me` → logout smoke test), `Auth/` (LDAPS login, JWT cookie). Root namespace: `securitycheck_portal`.
- New .NET projects (the planned scan worker, `*.Tests`) get their own top-level folder next to `api/`.
- `web/app/` — UI. Register every route in `web/app/routes.ts`.
- `context/foundation/` — PRD, tech stack, infrastructure, shape notes, roadmap. Read @context/foundation/prd.md before implementing a feature (FR-001…FR-008); pick the next work item from @context/foundation/roadmap.md.
- `web/.agents/skills/react-router/` — React Router reference skill. Before using a React Router API that isn't already used in `web/app/`, read @web/.agents/skills/react-router/references/framework-mode.md.

## Build and Development Commands

- `dotnet run --project api --launch-profile http` — API on `http://localhost:5143` (https profile adds `:7009`). Smoke-test with @api/securitycheck-portal.http.
- Dev loop: run the API as above plus `cd web && npm run dev` — UI on `http://localhost:5173`, with `/api` proxied to `:5143` (@web/vite.config.ts).
- `cd web && npm run build:api` — build the SPA and copy `web/build/client` into `api/wwwroot` (gitignored); then the API alone serves UI and `/api` on one origin.
- Auth config: set `Auth:*` with `dotnet user-secrets set <key> <value>` in `api/`: `Auth:Jwt:SigningKey` (≥ 32 bytes) and `Auth:Ldap:Host`, `Auth:Ldap:UpnSuffix`, `Auth:Ldap:SearchBase`, `Auth:Ldap:AllowedGroupDn` (@api/Auth/AuthOptions.cs). Use the full `Auth:Ldap:` prefix. Options are validated on start; a missing value stops the app with `OptionsValidationException`.
- `dotnet build api` — compile the API. Plain `dotnet build` at the root fails, because there is no project there.
- `cd web && npm run typecheck` — run after adding or renaming routes.
- Other UI scripts: see @web/package.json (run from `web/`).

## Testing

- `dotnet test api.Tests` — run the API tests (xUnit, `api.Tests/securitycheck-portal.Tests.csproj`). They host the API in-memory with a fake LDAP authenticator (`api.Tests/PortalFactory.cs`) and need no network or AD.
- Every new anonymous endpoint must be added on purpose to the allow-list in `api.Tests/NoPublicEndpointsTests.cs`; otherwise that test fails.
- No UI test runner exists yet.

## Commits and Pull Requests

History uses short, free-form subjects in Polish or English without prefixes (e.g. `Tech stack`). Default branch is `main`; CI is planned on GitHub Actions with auto-deploy on merge, so before merging to `main`, `dotnet build api` and `cd web && npm run typecheck` must both exit 0.
