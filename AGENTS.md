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
- `core/` — `securitycheck-portal.Core` (namespace `securitycheck_portal.Core`), referenced by the API and shared with the scan worker (`worker/`, which now exists): `Data/` (`PortalDbContext`, entities, migrations in `Data/Migrations`), `Git/` (repository URL validation, `X.Y.*` pattern resolution against tags from `git ls-remote`), `Scanning/` (scan options, Trivy runner, parser, job runner, queue) and `Processes/` (shared process runner). `dotnet build api` builds it too.
- `worker/` — `securitycheck-portal.Worker`, the scan worker (`Program.cs`, `ScanWorker.cs`, `appsettings.json`). It polls the database for queued scans, checks out the resolved commit and runs Trivy (`core/Scanning/`, `core/Processes/`). It has its own `UserSecretsId`, separate from the API's.
- New .NET projects (`*.Tests`) get their own top-level folder next to `api/`.
- `web/app/` — UI. Register every route in `web/app/routes.ts` (`/` repository list + add, `/repos/:repoId` details, `/repos/:repoId/scans/:scanId` scan result).
- `context/foundation/` — PRD, tech stack, infrastructure, shape notes, roadmap. Read @context/foundation/prd.md before implementing a feature (FR-001…FR-008); pick the next work item from @context/foundation/roadmap.md.
- `web/.agents/skills/react-router/` — React Router reference skill. Before using a React Router API that isn't already used in `web/app/`, read @web/.agents/skills/react-router/references/framework-mode.md.

## Build and Development Commands

- `dotnet run --project api --launch-profile http` — API on `http://localhost:5143` (https profile adds `:7009`). Smoke-test with @api/securitycheck-portal.http.
- Dev loop: run the API as above plus `cd web && npm run dev` — UI on `http://localhost:5173`, with `/api` proxied to `:5143` (@web/vite.config.ts).
- `cd web && npm run build:api` — build the SPA and copy `web/build/client` into `api/wwwroot` (gitignored); then the API alone serves UI and `/api` on one origin.
- Auth config: set `Auth:*` with `dotnet user-secrets set <key> <value>` in `api/`: `Auth:Jwt:SigningKey` (≥ 32 bytes) and `Auth:Ldap:Host`, `Auth:Ldap:UpnSuffix`, `Auth:Ldap:SearchBase`, `Auth:Ldap:AllowedGroupDn` (@api/Auth/AuthOptions.cs). Use the full `Auth:Ldap:` prefix. Options are validated on start; a missing value stops the app with `OptionsValidationException`.
- Git config (user-secrets in `api/`, validated on start): `Git:Token` (read-only PAT; never in `appsettings*.json` or a URL) and `Git:AllowedHosts:0` (e.g. `gitlab-do.coig.app`); optional `Git:UserName` (default `pat`), `Git:ExecutablePath` (default `git`; absolute path to `git.exe` on the server), `Git:TimeoutSeconds` (default 30). Repository URLs are stored as `https://<host>/<path>.git`, lower case (@core/Git/RepositoryUrl.cs).
- Database: set `ConnectionStrings:Portal` with `dotnet user-secrets set` in `api/`; start fails without it. The API does not migrate on start: run `dotnet tool restore` once (dotnet-ef pinned in @.config/dotnet-tools.json), then `dotnet ef database update --project core --startup-project api`. Add a migration with `dotnet ef migrations add <Name> --project core --startup-project api --output-dir Data/Migrations`.
- Scan worker: `dotnet run --project worker` (run it next to the API; without it scans stay `Queued` and the scan page warns after 2 minutes). Its secrets are separate from the API's: `dotnet user-secrets set <key> <value> --project worker` for `ConnectionStrings:Portal`, `Git:Token`, `Git:AllowedHosts:0`, `Scan:CacheDirectory` and `Scan:WorkRoot`. Exactly one worker may run: it takes a PostgreSQL advisory lock (@core/Scanning/WorkerLock.cs) before recovery and the sweep, and exits with code 1 if another instance holds it. A scan stuck after a database failure keeps its pattern blocked (409 active) until the periodic sweep fails it (sum of the timeouts, ~35+ min); `Queued` rows are never swept, so a stopped worker blocks the pattern (the UI only warns after 2 minutes).
- Scan options (`Scan:*`, @core/Scanning/ScanOptions.cs, validated on start): `TrivyExecutablePath` (default `trivy` from PATH), `CacheDirectory` (required; fixed Trivy DB folder), `WorkRoot` (required; short folder for checkouts, created on first scan), `ScanTimeoutMinutes`, `CloneTimeoutMinutes`, `DbUpdateTimeoutMinutes`, `MaxDbAgeDays`, `DbRepository` (optional DB mirror), `ExpectedTrivyVersion` (optional pin), `PollIntervalSeconds`, `DotnetExecutablePath` (default `dotnet` from PATH; used to generate lock files), `RestoreTimeoutMinutes` (default 10, per project), `RestoreTotalTimeoutMinutes` (default 20, all restores of one scan together; projects not restored in time stay without a lock file and the scan is `Incomplete`).
- Scan rules: a scan always runs on the commit the pattern resolved to. The Git PAT must never reach the Trivy process (Trivy runs with a whitelisted environment). Java DB decision: it is never downloaded, every Trivy call uses `--skip-java-db-update` and no `--java-db-repository`; only NuGet/npm lock files are scanned and `.jar` files in a checkout are not analysed (see the XML doc of @core/Scanning/TrivyScanner.cs). Repositories generally have no lock files, so before `trivy fs` the worker generates missing `packages.lock.json` for .NET projects with `dotnet restore <csproj> --use-lock-file` (@core/Scanning/DotnetLockFileGenerator.cs; needs the .NET SDK and NuGet feeds in the worker account's NuGet.config; no Git token in that process and no credentials from the portal, so `NuGetPackageSourceCredentials_*` variables do not reach restore and feed credentials must live in that NuGet.config). Accepted risk: restore evaluates MSBuild files from the repository and honors a NuGet.config found in the checkout, so repository content runs with the worker account's rights; only our own environment is kept out of the process, and a lock file left by a failed restore is deleted. A project whose restore fails stays in `MissingLockFiles` and the scan is `Incomplete`; npm without a lock file and zero Trivy targets stay `Incomplete` too.
- `dotnet build api` — compile the API (and `core/`). Plain `dotnet build` at the root fails, because there is no project there.
- `cd web && npm run typecheck` — run after adding or renaming routes.
- Other UI scripts: see @web/package.json (run from `web/`).

## UI Conventions

- Design tokens live only in `web/app/app.css`: `@theme static` holds the light values, `.dark` overrides the same variable names. Palette from the tweakcn "qrafthive" theme; primary is darkened (`#ba5c1f`) to reach AA with white text. Use semantic tokens (`bg-surface`, `text-text-muted`, `bg-primary`, `text-danger-fg`, …), not raw `gray-*`/`blue-*`/`red-*` utilities or hex values.
- Dark mode is a `dark` class on `<html>`; the `dark:` variant is class-based (`@custom-variant` in `app.css`). An inline script in `web/app/root.tsx` sets the class before first paint; `ThemeToggle` cycles system → light → dark and stores it in `localStorage` key `theme` (`system` = key removed).
- Shared UI is in `web/app/components/` (`Button`, `Field`/`Input`, `Card`, `Alert`, `Badge`, `ConfirmDialog`, `ThemeToggle`, `BrandMark`, `PageHeading`, `SectionHeading`, `RepositoryList`, plus `styles.ts` with `focusRing`, `linkClass`, `cx`). Use them; do not copy Tailwind class strings between routes. If a pattern repeats, add or extend a shared component and show it on `/styleguide`.
- Page and section titles go through `PageHeading` (h1, optional `action` slot) and `SectionHeading` (h2), never hand-written `text-2xl` / `text-lg font-semibold` classes. A page has exactly one visible h1.
- The repository list is rendered only via `RepositoryList`; it owns the empty state, the pattern-count `Badge` (warning when a repository has no active patterns) and the chevron.
- After any visual change, scan the changed view/component files for hard-coded values (`grep -nE '<regex>' <files>`) and expect 0 matches:
  ```
  #[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(|oklch\(|-\[[0-9.]+(px|rem)\]|\b(bg|text|border|ring|outline|from|via|to|fill|stroke|shadow|divide)-(slate|gray|zinc|neutral|stone|red|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose|white|black)\b
  ```
- Severity and status (critical/high/medium/low, success/warning/danger/info/neutral/new/accepted-risk) are rendered with `Badge` (text + icon); colour must never be the only cue. Inline messages use `Alert`.
- Destructive actions use `ConfirmDialog`, not `window.confirm`.
- Every new colour token needs a light and a `.dark` value and must reach WCAG AA (4.5:1 text on its background); add the pair to `PAIRS` in `web/app/routes/styleguide.tsx` and check it in both themes.
- `/styleguide` (token swatches, contrast table, component gallery) is registered in `web/app/routes.ts` only when `NODE_ENV !== "production"`, so it exists under `npm run dev` and not in the build. It shows no application data.
- New shared components and view states (empty, one item, many, long values) get a gallery section on `/styleguide` with sample data, because the real views need LDAP and a database and cannot be rendered without them.
- UI text is Polish and hard-coded (no i18n). System font stack only (`--font-sans`); no external fonts, icon sets or CDN assets, and no new UI dependencies for styling.

## Testing

- `dotnet test api.Tests` — run the API tests (xUnit, `api.Tests/securitycheck-portal.Tests.csproj`). They host the API in-memory with a fake LDAP authenticator and a fake Git tag source (`api.Tests/PortalFactory.cs`), so they need no network, AD or Git server.
- Database tests (`api.Tests/DatabasePortalFactory.cs`) start PostgreSQL 17 through Testcontainers and need a running Docker (e.g. Rancher Desktop). Without Docker they are reported as skipped ("Docker niedostępny — testy bazy pominięte"), not failed.
- Every new anonymous endpoint must be added on purpose to the allow-list in `api.Tests/NoPublicEndpointsTests.cs`; otherwise that test fails.
- No UI test runner exists yet.

## Commits and Pull Requests

History uses short, free-form subjects in Polish or English without prefixes (e.g. `Tech stack`). Default branch is `main`; CI is planned on GitHub Actions with auto-deploy on merge, so before merging to `main`, `dotnet build api` and `cd web && npm run typecheck` must both exit 0.
