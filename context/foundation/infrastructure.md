---
project: securitycheck-portal
researched_at: 2026-09-23
updated: 2026-09-28
recommended_platform: On-prem IIS (Windows Server) + Windows Service worker
scanner: Trivy (`trivy fs`, JSON output) on a clone checked out at the resolved version
runner_up: Docker Compose on internal Linux VM
context_type: mvp
tech_stack:
  language: C# + TypeScript
  framework: ASP.NET Core (.NET 10) API + React Router 8.4 UI
  runtime: .NET 10 (net10.0) / Node (build-time only after SPA switch)
  database: PostgreSQL (EF Core / Npgsql)
---

## Recommendation

**Deploy on the company's existing IIS server (Windows Server): the ASP.NET Core API in an IIS app pool, the scan engine as a separate Windows Service, and the React Router UI as a static SPA served from the same site.**

Two company-policy constraints were treated as hard filters. First, everything must run on company infrastructure. Second, the scanner must clone Git repos that exist only on the internal network. Together they eliminate every cloud PaaS in the default pool. Among the on-prem candidates, IIS scores slightly lower than Docker Compose and Kamal on the raw agent-friendly criteria. It wins after the interview weights are applied: minimizing cost was the top priority, the team already publishes internal tools to IIS, and the project needs single-region, internal-only hosting. It also adds no licensing cost and no new OS to own. Users sign in inside the application (login + password checked against Active Directory over LDAPS), so authentication does not depend on the host. This replaced the earlier assumption of IIS-provided single sign-on (decision 2026-09-27, change `authenticated-app-shell`).

Interview answers (2026-09-23):
- Persistent processes: "Don't know", but `has_background_jobs: true` and minutes-long scans make the answer effectively *yes*.
- Cost: minimize.
- Familiarity: IIS for internal tools.
- Geography: single region.
- Co-location: external providers fine.
- Hosting policy: company infra only.
- Git hosting: internal network.

## Scanner: Trivy

Decision (2026-09-26, user): the scan engine is **Trivy**. It replaces the earlier assumption of `dotnet package list --vulnerable` + `npm audit`. The worker clones the repo, checks out the exact commit the version pattern resolved to, runs `trivy fs --scanners vuln --format json <dir>`, and records that commit with the result. It does not use `trivy repo`, so the portal owns the checkout and can show which commit was scanned.

Facts that shape the infrastructure (checked 2026-09-26):

- **Trivy finds dependencies only from lock or manifest files.**
  - .NET: it reads `packages.lock.json`, `packages.config`, `*.deps.json` and `*Packages.props`. It does not read a plain `.csproj`. `*Packages.props` has no transitive dependencies.
  - Node: it reads `package-lock.json`, `yarn.lock`, `pnpm-lock.yaml` and `bun.lock`. `package.json` alone is not enough.
  - Development dependencies are excluded by default; `--include-dev-deps` includes them.
  - Consequence: a repo without lock files scans as "0 packages, 0 vulnerabilities". That is the most likely source of false reassurance.
- **The vulnerability DB is downloaded, not bundled.** It is an OCI artifact pulled from `mirror.gcr.io` or `ghcr.io` over HTTPS and cached in `--cache-dir`. `--db-repository` points Trivy at a self-hosted mirror, and `--skip-db-update` scans with whatever DB is cached.
  - Only DB metadata comes in; no source code leaves the company. This is compatible with the "company infra only" filter.
- **Trivy itself was a supply-chain target.** On 2026-03-19 attackers published a malicious binary v0.69.4 for all platforms, including Windows, and hijacked the `trivy-action` and `setup-trivy` tags. They followed with malicious Docker Hub images v0.69.5 and v0.69.6 (CVE-2026-33634, GHSA-69fq-xp46-6x23).
  - Install a pinned version, verify it with cosign against the sigstore bundle, and never use a mutable tag.

Sources: [Trivy .NET coverage](https://trivy.dev/latest/docs/coverage/language/dotnet/), [Trivy Node.js coverage](https://trivy.dev/latest/docs/coverage/language/nodejs/), [Trivy air-gap / DB](https://trivy.dev/latest/docs/advanced/air-gap/), [GHSA-69fq-xp46-6x23](https://github.com/aquasecurity/trivy/security/advisories/GHSA-69fq-xp46-6x23).

## Platform Comparison

**Hard filters applied first**

| Platform | .NET 10 | Long-running jobs | Company-owned infra | Reaches internal Git without data leaving | Result |
|---|---|---|---|---|---|
| Cloudflare Workers/Pages (+ Containers, GA 2026-04-13) | Containers only | Partial | No | Only via Cloudflare Tunnel (data transits Cloudflare) | **Dropped** |
| Vercel | No | No (functions max 800s GA, 1800s beta) | No | No | **Dropped** |
| Netlify | No | No (~15 min background functions) | No | No | **Dropped** |
| Fly.io | Docker | Yes | No (Fly cloud only) | WireGuard/Tailscale, but source is cloned on third-party compute | **Dropped** |
| Railway | Docker | Yes | No (not BYOC per Railway's own blog) | Tailscale template, same problem | **Dropped** |
| Render | Docker | Yes | No | Tailscale/tunnel, same problem | **Dropped** |

Because the default candidate pool fails the policy, the research added on-prem candidates: IIS, Docker Compose, Kamal, Coolify and Dokku.

**Scoring matrix (on-prem candidates)**

| Platform | CLI-first | Managed/Serverless | Agent-readable docs | Stable deploy API | MCP / Integration | Raw | After weights |
|---|---|---|---|---|---|---|---|
| **IIS + Windows Service** | Pass | Partial | Pass | Partial | Partial | 2P/3Pa | **1st** (cost + familiarity) |
| **Docker Compose (Linux VM)** | Pass | Partial | Pass | Pass | Partial | 3P/2Pa | **2nd** |
| **Kamal (Linux VM)** | Pass | Partial | Pass | Pass | Fail | 3P/1Pa/1F | **3rd** |
| Coolify v4 | Pass | Pass | Pass | Partial | Partial | 3P/2Pa | Excluded (security record) |
| Dokku 0.38 | Pass | Partial | Pass | Partial | Fail | 2P/2Pa/1F | Excluded (bus factor, SIGTERM behaviour) |

Notes per platform:

- **IIS + Windows Service**
  - *CLI:* the full loop is scriptable from PowerShell: `dotnet publish`, `app_offline.htm`, `robocopy` (exit code ≥ 8 means failure), IISAdministration cmdlets (`Get-IISSite`, `Set-ItemProperty ... physicalPath`), `New-Service`/`Restart-Service`, and `Get-WinEvent`.
  - *Managed:* Partial. It is a server, but the company already operates it, patches it and knows it.
  - *Docs:* learn.microsoft.com serves markdown, and the sources live on GitHub (`dotnet/AspNetCore.Docs`, `MicrosoftDocs/iis-docs`).
  - *Deploy:* Partial. There is no single platform command; deploy and rollback are a script we own (versioned folders plus a `physicalPath` swap).
  - *MCP:* Partial. The Microsoft Learn MCP Server (public since 2025-06, free) covers docs. There is no official IIS ops MCP; `dotnet-demos/mcp-server-iis` is a demo only.
- **Docker Compose**
  - *Deploy:* `docker compose up -d --wait` blocks on health checks and exits non-zero on failure. Remote control works via `docker context` over SSH. Rollback means re-running with the previous pinned image tag.
  - *Docs:* `docs.docker.com/llms.txt` exists.
  - *MCP:* Partial. The Docker MCP Toolkit is tied to Docker Desktop, and MCP Gateway is invite-only. Neither is usable on a headless server.
  - *Why it's lower here:* it needs a **new Linux VM**, because Windows Server cannot run Linux containers (LCOW is deprecated). The team would own OS patching and Docker upgrades.
- **Kamal v2.12**
  - *Deploy:* `kamal deploy` / `kamal rollback <version>` / `kamal app logs`. It is non-interactive, has a deploy lock and does health-gated zero-downtime deploys via kamal-proxy. It accepts a custom (internal-CA) certificate and needs no daemon on the server.
  - *MCP:* Fail, there is no MCP server.
  - *Why it's lower here:* it is the least familiar option, needs Ruby (WSL or a container on Windows dev machines), and Postgres backups are DIY.
- **Coolify v4**: best "managed" score. It has a UI, built-in Postgres backups, a CLI with JSON output and a read-only built-in MCP. It was excluded because it ran as root and disclosed 11 critical CVEs in Jan 2026, with more through July 2026. Install and updates also expect internet access, which is a poor fit for a security-scanning tool holding customer data.
- **Dokku 0.38**: git-push simple with a solid CLI. It was excluded because it is effectively maintained by one person, and since 0.38 old containers get SIGTERM immediately on deploy, which is hostile to minutes-long scans.

### Shortlisted Platforms

#### 1. IIS + Windows Service (Recommended)

It costs nothing extra on the existing server, and it matches how the team already ships internal tools. Sign-in is handled by the application itself (Active Directory bind over LDAPS, session as a JWT in an HttpOnly cookie), so it needs no identity provider work and does not depend on IIS authentication features. Microsoft documents that IIS app pools kill long jobs on recycle (default `shutdownTimeLimit` 90s, 20-min idle timeout, recycle every 1740 min). Its explicit guidance is to host background work outside IIS, which leads to the two-process topology: API in IIS, scanner as a Windows Service under a gMSA or service account. That split also gives FR-005 (scheduled scans) a clean home. The UI moves to React Router SPA mode (`ssr: false`), because Node SSR hosting on IIS is poor: iisnode is unmaintained and HttpPlatformHandler's status is unclear.

#### 2. Docker Compose on internal Linux VM

It has the most deterministic deploy and rollback of the three, keeps React Router SSR as scaffolded (the web Dockerfile already exists), and gives the same setup in development and production. The gap vs. IIS is a new Linux server the team must patch and back up. The scanner image needs git plus a Trivy binary pinned by version and verified with cosign (or the official image pinned by digest). It does not need Node, because Trivy reads npm lock files directly. It does need the .NET SDK, because the worker generates missing `packages.lock.json` files with `dotnet restore --use-lock-file` before the scan, and the NuGet feeds the projects use must be configured in the NuGet.config of the worker account. Docker group access is root-equivalent.

#### 3. Kamal on internal Linux VM

It has the best agent-driven deploy semantics: one command to deploy and one to roll back, with health-gated zero downtime. The gaps vs. IIS are the same new-VM burden as Compose, plus a Ruby toolchain, no MCP, and unfamiliarity. It also needs `stop_timeout` raised, or the scanner run as a non-proxied `job` role (the default is to kill 10s after SIGTERM).

## Anti-Bias Cross-Check: IIS + Windows Service

### Devil's Advocate — Weaknesses

1. **The scanner depends on an external DB.** Trivy pulls its vulnerability DB from `mirror.gcr.io` / `ghcr.io`. If the server's outbound access is blocked or proxied, a first run fails outright. Later runs silently fall back to whatever DB is cached, which may be weeks old. Either way the portal could show "no vulnerabilities" that are not true, which breaks the primary guardrail against false reassurance.
2. **Two deployables share one schema.** The API runs in IIS and the worker is a Windows Service, both on one PostgreSQL schema. A partial deploy leaves them on different EF Core model versions, and migrations don't roll back automatically.
3. **The UI must change rendering mode.** The React Router scaffold is `ssr: true`. On IIS it becomes an SPA (`ssr: false`): only the root route may have a `loader`, and there are no `action`/`headers` exports. All data goes through the API over same-origin fetch.
4. **Shared server blast radius.** A GitHub Actions self-hosted runner with IIS admin rights on a server hosting other internal apps can recycle or break those apps during a deploy.
5. **PostgreSQL on Windows is unfamiliar.** If the company normally runs SQL Server, installing, patching and backing up Postgres (`pg_dump` schedule, restore test) is new, unowned work.
6. **Trivy is blind without lock files.** An old customer version with only `.csproj` files (no `packages.lock.json`) or only `package.json` produces zero packages. Trivy reports it as a clean result, not as an error.
7. **The scanner runs with the worker's privileges.** It runs as the service account, which holds the git credential (a read-only PAT). The API's app-pool identity holds the same PAT, because the API resolves version patterns with `git ls-remote`. A compromised Trivy release, as in March 2026, would leak that credential and read every repo the PAT can reach.

### Pre-Mortem — How This Could Fail

The team shipped the portal on the shared IIS server and left the scanner inside the app pool "for the first release". IIS recycled the pool nightly and on idle, killing in-flight scans after 90 seconds. Those scans were persisted as completed with zero findings, and nobody questioned the green screen for weeks. The fix moved scanning to a Windows Service, but it ran under a virtual account with no user profile. Git could not find credentials, `%TEMP%` pointed at `C:\Windows\Temp`, and clones failed intermittently in ways that looked like network problems. Windows Defender real-time scanning inspected every file of every clone, and scans went from four minutes to twenty-five. Then a proxy change silently blocked the Trivy DB registry. The worker kept scanning with a months-old cached DB, and every result looked current. Meanwhile, the oldest customer versions had never had `packages.lock.json`. Trivy found zero .NET packages, the parser treated it as "clean", and a critical CVE at a customer went unreported for two months. Finally, a deploy from the admin-rights runner restarted IIS and took down two unrelated internal apps. The platform was fine. The failures came from assuming IIS familiarity covered long-running processes, service identities, and the scanners' external dependencies.

### Unknown Unknowns

- **MAX_PATH (260 chars).** Deep repos and `node_modules` trees exceed it. Set `git config --system core.longpaths true`, enable Win32 long paths, and keep the scan working directory short (e.g. `D:\scw\<id>`).
- **Defender on scan folders.** Real-time protection slows clones dramatically and can quarantine files from known-vulnerable packages. A scan-directory exclusion must be agreed with the security team.
- **Trivy cache under the service account.** Without an explicit `--cache-dir`, the DB lands in the service profile, or it fails if the profile is missing. Use a fixed folder, e.g. `D:\trivy-cache`, that only the worker account can write.
- **Dev dependencies are skipped by default.** This matches "what ships to the customer", but it is a product decision. If build-time tooling should count, add `--include-dev-deps`.
- **Trivy JSON shape.** Each `Results[]` entry is one target (a lock file) with a `Vulnerabilities[]` list of `VulnerabilityID`, `PkgName`, `InstalledVersion` and `Severity`.
  - "No targets" means no lock file was found. "Targets without vulnerabilities" means clean.
  - The parser must tell these apart. Keep a contract test on sample output, because the format can change between Trivy versions.

## Operational Story

- **Preview deploys**: none on the platform. Previews run locally (`dotnet run` + `npm run dev`). If needed, add a second IIS site `securitycheck-staging` on another port, deployed from `main` before a manual promote. It is internal-only by network, so no extra access layer is needed.
- **Secrets**:
  - Production connection string (`ConnectionStrings:Portal`), the read-only git PAT (`Git:Token`, used by the API and the worker) with its host allow-list (`Git:AllowedHosts`, e.g. `gitlab-do.coig.app`), `Auth:Jwt:SigningKey` (at least 32 bytes) and the company-specific `Auth:Ldap` settings (`Host`, `UpnSuffix`, `SearchBase`, `AllowedGroupDn`) live on the server: ACL'd environment variables on the app pool and the service, or `appsettings.Production.json` outside the repo, readable only by the app-pool identity, the service account and admins.
  - CI-side secrets (runner registration) live in GitHub Secrets.
  - Rotation: an admin updates the value on the server, then runs `Restart-WebAppPool securitycheck` and `Restart-Service SecurityCheck.Worker`.
- **Rollback**: each deploy goes to `D:\apps\securitycheck\releases\<git-sha>\{api,worker}`.
  - To revert, point the site `physicalPath` at the previous release, re-point the service with `sc.exe config SecurityCheck.Worker binPath=...`, then restart both. This takes about 1 minute.
  - Caveat: EF Core migrations do **not** roll back. Migrations must be additive and backward-compatible for one release. A down-migration is a human decision.
- **Approval**:
  - *Human only:* publishing to production (manual approval on a GitHub Environment), running a destructive migration, rotating the git credential or DB password, upgrading the pinned Trivy version, dropping or restoring the database, and changing app-pool or service identity.
  - *Agent may do unattended:* build, test, deploy to staging, and read logs and status.
- **Logs** (read-only for the agent, via PowerShell Remoting or the runner):
  - `Get-Content D:\apps\securitycheck\logs\api-stdout*.log -Tail 200` (ANCM stdout log)
  - `Get-WinEvent -LogName Application -MaxEvents 50 | ? ProviderName -match 'IIS|AspNetCore|SecurityCheck'`
  - `Get-Service SecurityCheck.Worker`, `Get-IISAppPool securitycheck`
  - Scan job state and history live in PostgreSQL tables.

## Risk Register

| Risk | Source | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| Scan killed by app-pool recycle and recorded as clean | Pre-mortem | H (if hosted in IIS) | H | Scanner runs only in the Windows Service. Scan status is a state machine (`queued → running → succeeded/failed`), and only `succeeded` shows results. Raise `HostOptions.ShutdownTimeout`. |
| Trivy DB registry unreachable, so the scan fails or silently uses a stale cached DB | Devil's advocate | M | H | Allow `mirror.gcr.io`/`ghcr.io` through the proxy, or host a DB mirror (`--db-repository`). Store the DB `UpdatedAt` with every scan and mark the scan **failed** when the DB is older than the agreed limit. A non-zero exit or empty/invalid JSON also marks it **failed**. |
| Repo version has no supported lock file, so zero packages is reported as clean | Devil's advocate | H | H | Before the scan, detect manifests (`*.csproj`, `package.json`) that have no matching lock file. If any are found, or Trivy returns no targets, mark the result **incomplete**, never "no vulnerabilities". |
| Compromised Trivy release (precedent: v0.69.4, 2026-03-19) steals the git credential, held by both the worker service account and the API app-pool identity | Research finding | L | H | Pin the version, verify with cosign, and upgrade only with human approval. The git credential is a read-only PAT, sent as a header via the environment, never in a URL or argv. Run no Trivy GitHub Action in CI with deploy secrets. |
| Service identity has no profile, so git credentials, HOME and TEMP fail | Pre-mortem | H | M | Run the worker under a gMSA or dedicated domain account. Set explicit `HOME`/`TEMP` and a git credential via env (`GIT_ASKPASS` / header), or an SSH key in the service profile. |
| API/worker version skew or non-reversible migration during deploy | Devil's advocate | M | M | Deploy script stops the worker → migrates → swaps API → starts the worker. Migrations are additive only. One release folder serves both processes. |
| React Router SPA-mode constraints (root-only `loader`, no `action`) | Devil's advocate | H | L | Decide `ssr: false` before building UI routes. Data calls go to same-origin `/api/*`, with the SPA served from ASP.NET Core `wwwroot` or a static IIS app under the same site. |
| Self-hosted runner on shared IIS server breaks other apps | Devil's advocate | M | M | Runner service account can only write `D:\apps\securitycheck` and control its own app pool and service. Production deploy requires GitHub Environment approval. |
| MAX_PATH failures on deep repos | Unknown unknowns | M | M | `core.longpaths=true`, enable `LongPathsEnabled`, short scan root. |
| Defender slows scans or quarantines vulnerable-package files | Unknown unknowns | M | M | Scan-directory exclusion approved by the security team. Clean the working dir after each scan. |
| LDAPS unreachable or its certificate untrusted, so every login returns 503 | Research finding | M | H | Pre-flight on the server before go-live: TCP 636 to the DC and a trusted internal-CA chain for the name in `Auth:Ldap:Host`. |
| Password guessing locks out AD accounts | Research finding | M | M | Login is limited to 5 attempts per minute per client IP, and to 5 failed attempts per account per 15 minutes from any IP (`FailedLoginThrottle`); both return 429 without contacting LDAP. The per-account limit must stay below the AD lockout threshold, which remains the backstop. Behind a reverse proxy the per-IP limit needs forwarded headers (`UseForwardedHeaders` with known proxies), or all clients share one address. |
| Leaked `Auth:Jwt:SigningKey` lets anyone forge a session | Research finding | L | H | The key lives only on the server, never in the repo or `appsettings*.json`. Rotating it invalidates every session and logs everyone out. |
| PostgreSQL on Windows unowned (patching, backups) | Devil's advocate | M | H | Named owner. Nightly `pg_dump` via Task Scheduler to a separate share. Test a restore once before go-live. |
| Trivy JSON output changes between versions and breaks the parser | Unknown unknowns | L | M | Pin the Trivy version, parse `--format json` only, and keep a contract test on sample output. |
| HttpPlatformHandler / iisnode unsuitable for Node SSR (unmaintained, status unclear, checked 2026-09-23) | Research finding | — | — | Avoided by SPA mode. If SSR is ever needed, move to runner-up (Docker Compose). |
| GitHub self-hosted runner fee (announced 2025-12-16, postponed; status checked 2026-09-23) | Research finding | M | L | Low CI minutes at MVP. Re-check pricing before the fee lands. |

## Getting Started

1. **On the server (one-time, admin):**
   - Install the **.NET 10 Hosting Bundle**, then run `iisreset`.
   - Create the app pool: `New-WebAppPool securitycheck`, then set `managedRuntimeVersion ''`, `startMode AlwaysRunning`, `processModel.idleTimeout 00:00:00` and `processModel.loadUserProfile true`.
   - Create the site with Anonymous Authentication enabled and Windows Authentication disabled; the application authenticates users itself.
   - Make sure the server reaches Active Directory over LDAPS (TCP 636) and trusts the internal CA that issued the domain controllers' certificate. `Auth:Ldap:Host` must be a name present in that certificate (DC FQDN or domain name), not an IP address.
   - Install PostgreSQL and create the `securitycheck` database and login.
   - Install git and a pinned Trivy Windows binary. Never use v0.69.4. Verify it with `cosign verify-blob` against the release's sigstore bundle.
   - Install the .NET SDK (the `dotnet` on the worker's PATH, or set `Scan:DotnetExecutablePath`). The worker runs `dotnet restore --use-lock-file` to generate missing `packages.lock.json` files, so the worker account's profile needs a NuGet.config with the internal NuGet feeds and their access configured there. The portal never passes feed credentials. Note that restore evaluates MSBuild files from the repository and honors a NuGet.config found in the checkout, so repository content runs with the worker account's rights (accepted risk: repositories come from the internal GitLab). A project whose restore fails or does not finish within the total restore budget (`Scan:RestoreTotalTimeoutMinutes`) leaves the scan **incomplete**. Node is not needed.
   - Create a cache folder (e.g. `D:\trivy-cache`) writable only by the worker account.
   - Allow `mirror.gcr.io` / `ghcr.io` through the proxy for the worker account, or set up an internal DB mirror.
2. **Split the worker.** Add a `SecurityCheck.Worker` project (Worker Service template) with `Microsoft.Extensions.Hosting.WindowsServices` and `builder.Services.AddWindowsService()`. It shares the EF Core `DbContext` project with the API. Register it once with `New-Service -Name SecurityCheck.Worker -BinaryPathName ...\worker\SecurityCheck.Worker.exe -Credential <gMSA>`.
3. **Publish the UI as an SPA.** The UI runs in SPA mode (`ssr: false` in `web/react-router.config.ts`). `cd web && npm run build:api` builds it and copies `web/build/client` into the API's `wwwroot`, where `app.UseStaticFiles()` + `app.MapFallbackToFile("index.html")` serve it, so UI and `/api` share one origin and one session cookie.
4. **Publish.**
   - Build: `dotnet publish api -c Release -o out/api` and `dotnet publish SecurityCheck.Worker -c Release -r win-x64 -o out/worker`.
   - Deploy by running a script from a self-hosted runner on the server that does, in order:
     1. Copy to `releases\<sha>`.
     2. `Stop-Service SecurityCheck.Worker`.
     3. Apply migrations (EF migration bundle `efbundle.exe`).
     4. Drop `app_offline.htm`, swap the `physicalPath`, remove `app_offline.htm`.
     5. Re-point and `Start-Service`.
5. **Verify.**
   - Check the site: an anonymous `GET https://<host>/api/me` returns 401 (`curl.exe -i https://<host>/api/me`), and `https://<host>/` in a browser shows the login form.
   - Check Git access from the API: after login, add a repository (`https://<git host>/<path>.git`) and a version pattern (e.g. `2.1.*`); the pattern shows the resolved tag, not an error.
   - Check the worker: `Get-Service SecurityCheck.Worker`.
   - Check the scanner as the worker account: `trivy --cache-dir D:\trivy-cache version --format json` shows the pinned version and a fresh vulnerability DB.
   - Trigger one manual scan against a repo with a known-vulnerable package and confirm it is reported (a guardrail smoke test, not just "200 OK").

## Out of Scope

The following were not evaluated in this research:
- Docker image configuration
- CI/CD pipeline setup (only the approval gate and deploy-script shape are noted)
- Production-scale architecture (multi-region, HA, DR)
