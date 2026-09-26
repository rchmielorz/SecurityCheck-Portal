# Repository Guidelines

SecurityCheck Portal is an internal tool that scans dependencies of customer-deployed repository versions for known vulnerabilities. Stack: ASP.NET Core minimal API (.NET 10) in `api/`, React Router 8 + Tailwind 4 UI in `web/`; the repo root holds no project. PostgreSQL via EF Core/Npgsql is planned but not yet installed.

## Hard Rules

- Every API endpoint and UI route must require authentication; the PRD forbids any public page (@context/foundation/prd.md, Non-Functional Requirements).
- Do not write CVE/dependency-analysis logic. Detection must delegate to the external scanner, Trivy (PRD Non-Goals).
- A scan must run against the code matching the declared version pattern (e.g. `2.1.*`), never the default branch.
- Never write under `context/archive/`; it is read-only. Change-scoped docs go in `context/changes/<change-id>/`; foundation docs are edited in place (@context/foundation/README.md).
- Keep backend and frontend separated: `.cs` files only in .NET project folders (`api/` or a sibling), UI code only in `web/`. Do not add a project file at the repo root.

## Project Structure

- `api/` — the API: `Program.cs`, `securitycheck-portal.csproj`, `appsettings*.json`, `Properties/launchSettings.json`, `securitycheck-portal.http` (still the `weatherforecast` template; replace, do not extend it). Root namespace: `securitycheck_portal`.
- New .NET projects (the planned scan worker, `*.Tests`) get their own top-level folder next to `api/`.
- `web/app/` — UI. Register every route in `web/app/routes.ts`.
- `context/foundation/` — PRD, tech stack, infrastructure, shape notes, roadmap. Read @context/foundation/prd.md before implementing a feature (FR-001…FR-008); pick the next work item from @context/foundation/roadmap.md.
- `web/.agents/skills/react-router/` — React Router reference skill. Before using a React Router API that isn't already used in `web/app/`, read @web/.agents/skills/react-router/references/framework-mode.md.

## Build and Development Commands

- `dotnet run --project api --launch-profile http` — API on `http://localhost:5143` (https profile adds `:7009`). Smoke-test with @api/securitycheck-portal.http.
- `dotnet build api` — compile the API. Plain `dotnet build` at the root fails, because there is no project there.
- `cd web && npm run typecheck` — run after adding or renaming routes.
- Other UI scripts: see @web/package.json (run from `web/`).

## Testing

No test project or UI test runner exists yet. When adding the first tests, create a separate `*.Tests` project in its own top-level folder (outside `api/` and `web/`) and note the command here.

## Commits and Pull Requests

History uses short, free-form subjects in Polish or English without prefixes (e.g. `Tech stack`). Default branch is `main`; CI is planned on GitHub Actions with auto-deploy on merge, so before merging to `main`, `dotnet build api` and `cd web && npm run typecheck` must both exit 0.
