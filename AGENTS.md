# Repository Guidelines

SecurityCheck Portal is an internal tool that scans dependencies of customer-deployed repository versions for known vulnerabilities. Stack: ASP.NET Core minimal API (.NET 10) at the repo root, React Router 8 + Tailwind 4 UI in `web/`. PostgreSQL via EF Core/Npgsql is planned but not yet installed.

## Hard Rules

- Every API endpoint and UI route must require authentication; the PRD forbids any public page (@context/foundation/prd.md, Non-Functional Requirements).
- Do not write CVE/dependency-analysis logic. Detection must delegate to an external scanning tool (PRD Non-Goals).
- A scan must run against the code matching the declared version pattern (e.g. `2.1.*`), never the default branch.
- Never write under `context/archive/`; it is read-only. Change-scoped docs go in `context/changes/<change-id>/`; foundation docs are edited in place (@context/foundation/README.md).
- The root `securitycheck-portal.csproj` currently globs `web/**` JSON files into `Content`, so `dotnet publish` ships them. Add a `web/**` exclude before adding publish/deploy steps, and keep all `.cs` files outside `web/`.

## Project Structure

- `Program.cs`, `securitycheck-portal.csproj`, `appsettings*.json`, `Properties/launchSettings.json` — the API (still the `weatherforecast` template; replace, do not extend it). Root namespace: `securitycheck_portal`.
- `web/app/` — UI. Register every route in `web/app/routes.ts`; route modules live in `web/app/routes/` and import generated types from `./+types/<route>`. Import app code via the `~/` alias (maps to `web/app/`).
- `context/foundation/` — PRD, tech-stack decision, shape notes. Read @context/foundation/prd.md before implementing a feature (FR-001…FR-008).
- `web/.agents/skills/react-router/` — React Router reference skill; consult it instead of guessing v8 APIs.

## Build and Development Commands

- `dotnet run --launch-profile http` — API on `http://localhost:5143` (https profile adds `:7009`). Smoke-test with @securitycheck-portal.http.
- `dotnet build` — compile the API (nullable reference types are enabled).
- `cd web && npm run dev` — UI dev server with HMR.
- `cd web && npm run typecheck` — runs `react-router typegen && tsc`; run after adding or renaming routes.
- `cd web && npm run build` — production SSR build.

## Testing

No test project or UI test runner exists yet. When adding the first tests, create a separate `*.Tests` project outside `web/` and note the command here.

## Commits and Pull Requests

History uses short, free-form subjects in Polish or English without prefixes (e.g. `Tech stack`). Default branch is `main`; CI is planned on GitHub Actions with auto-deploy on merge, so keep `main` buildable.
