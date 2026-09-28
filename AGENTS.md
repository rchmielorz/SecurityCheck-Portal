# Repository Guidelines

SecurityCheck Portal is an internal tool that scans dependencies of customer-deployed repository versions for known vulnerabilities. Stack: ASP.NET Core minimal API (.NET 10) in `api/`, shared data and Git code in `core/`, React Router 8 + Tailwind 4 UI in `web/`; the repo root holds no project. Data lives in PostgreSQL via EF Core 10/Npgsql.

## Hard Rules

- Every API endpoint and UI route must require authentication; the PRD forbids any public page (@context/foundation/prd.md, Non-Functional Requirements). The API enforces this with a fallback authorization policy. The only anonymous exceptions are `POST /api/auth/login` and the SPA files (static assets + `index.html` fallback, which render only the login form); `api.Tests/NoPublicEndpointsTests.cs` fails on any other (see Testing).
- UI route URLs must not have a dot in the last path segment (e.g. `/repos/x/2.1.*`, `/users/jan.kowalski`): the API serves `index.html` through `MapFallbackToFile` (`{*path:nonfile}`), so such deep links get 401/404 instead of the SPA. Use IDs in the path and put values like version patterns in the query string, or replace the fallback route on purpose.
- Do not write CVE/dependency-analysis logic. Detection must delegate to the external scanner, Trivy (PRD Non-Goals).
- A scan must run against the code matching the declared version pattern (e.g. `2.1.*`), never the default branch.
- Never write under `context/archive/`; it is read-only. Change-scoped docs go in `context/changes/<change-id>/`; foundation docs are edited in place (@context/foundation/README.md).
- Keep backend and frontend separated: `.cs` files only in .NET project folders (`api/` or a sibling), UI code only in `web/`. Do not add a project file at the repo root.

## Project Structure

- `api/` — the API: `Program.cs`, `securitycheck-portal.csproj`, `appsettings*.json`, `Properties/launchSettings.json`, `securitycheck-portal.http` (login → `me` → logout smoke test), `Auth/` (LDAPS login, JWT cookie). Root namespace: `securitycheck_portal`.
- `core/` — `securitycheck-portal.Core` (namespace `securitycheck_portal.Core`), referenced by the API and meant to be shared with the planned scan worker: `Data/` (`PortalDbContext`, entities, migrations in `Data/Migrations`) and `Git/` (repository URL validation, `X.Y.*` pattern resolution against tags from `git ls-remote`). `dotnet build api` builds it too.
- New .NET projects (the planned scan worker, `*.Tests`) get their own top-level folder next to `api/`.
- `web/app/` — UI. Register every route in `web/app/routes.ts` (`/` repository list + add, `/repos/:repoId` details).
- `context/foundation/` — PRD, tech stack, infrastructure, shape notes, roadmap. Read @context/foundation/prd.md before implementing a feature (FR-001…FR-008); pick the next work item from @context/foundation/roadmap.md.
- `web/.agents/skills/react-router/` — React Router reference skill. Before using a React Router API that isn't already used in `web/app/`, read @web/.agents/skills/react-router/references/framework-mode.md.

## Build and Development Commands

- `dotnet run --project api --launch-profile http` — API on `http://localhost:5143` (https profile adds `:7009`). Smoke-test with @api/securitycheck-portal.http.
- Dev loop: run the API as above plus `cd web && npm run dev` — UI on `http://localhost:5173`, with `/api` proxied to `:5143` (@web/vite.config.ts).
- `cd web && npm run build:api` — build the SPA and copy `web/build/client` into `api/wwwroot` (gitignored); then the API alone serves UI and `/api` on one origin.
- Auth config: set `Auth:*` with `dotnet user-secrets set <key> <value>` in `api/`: `Auth:Jwt:SigningKey` (≥ 32 bytes) and `Auth:Ldap:Host`, `Auth:Ldap:UpnSuffix`, `Auth:Ldap:SearchBase`, `Auth:Ldap:AllowedGroupDn` (@api/Auth/AuthOptions.cs). Use the full `Auth:Ldap:` prefix. Options are validated on start; a missing value stops the app with `OptionsValidationException`.
- Git config (user-secrets in `api/`, validated on start): `Git:Token` (read-only PAT; never in `appsettings*.json` or a URL) and `Git:AllowedHosts:0` (e.g. `gitlab-do.coig.app`); optional `Git:UserName` (default `pat`), `Git:ExecutablePath` (default `git`; absolute path to `git.exe` on the server), `Git:TimeoutSeconds` (default 30). Repository URLs are stored as `https://<host>/<path>.git`, lower case (@core/Git/RepositoryUrl.cs).
- Database: set `ConnectionStrings:Portal` with `dotnet user-secrets set` in `api/`; start fails without it. The API does not migrate on start: run `dotnet tool restore` once (dotnet-ef pinned in @.config/dotnet-tools.json), then `dotnet ef database update --project core --startup-project api`. Add a migration with `dotnet ef migrations add <Name> --project core --startup-project api --output-dir Data/Migrations`.
- `dotnet build api` — compile the API (and `core/`). Plain `dotnet build` at the root fails, because there is no project there.
- `cd web && npm run typecheck` — run after adding or renaming routes.
- Other UI scripts: see @web/package.json (run from `web/`).

## Testing

- `dotnet test api.Tests` — run the API tests (xUnit, `api.Tests/securitycheck-portal.Tests.csproj`). They host the API in-memory with a fake LDAP authenticator and a fake Git tag source (`api.Tests/PortalFactory.cs`), so they need no network, AD or Git server.
- Database tests (`api.Tests/DatabasePortalFactory.cs`) start PostgreSQL 17 through Testcontainers and need a running Docker (e.g. Rancher Desktop). Without Docker they are reported as skipped ("Docker niedostępny — testy bazy pominięte"), not failed.
- Every new anonymous endpoint must be added on purpose to the allow-list in `api.Tests/NoPublicEndpointsTests.cs`; otherwise that test fails.
- No UI test runner exists yet.

## Commits and Pull Requests

History uses short, free-form subjects in Polish or English without prefixes (e.g. `Tech stack`). Default branch is `main`; CI is planned on GitHub Actions with auto-deploy on merge, so before merging to `main`, `dotnet build api` and `cd web && npm run typecheck` must both exit 0.
