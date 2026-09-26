---
project: securitycheck-portal
researched_at: 2026-09-23
recommended_platform: On-prem IIS (Windows Server) + Windows Service worker
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

Two company-policy constraints were treated as hard filters. First, everything must run on company infrastructure. Second, the scanner must clone Git repos that exist only on the internal network. Together they eliminate every cloud PaaS in the default pool. Among the on-prem candidates, IIS scores slightly lower than Docker Compose and Kamal on the raw agent-friendly criteria. It wins after the interview weights are applied: minimizing cost was the top priority, the team already publishes internal tools to IIS, and the project needs single-region, internal-only hosting. It also adds no licensing cost, no new OS to own, and built-in Windows SSO.

Interview answers (2026-09-23):
- Persistent processes: "Don't know", but `has_background_jobs: true` and minutes-long scans make the answer effectively *yes*.
- Cost: minimize.
- Familiarity: IIS for internal tools.
- Geography: single region.
- Co-location: external providers fine.
- Hosting policy: company infra only.
- Git hosting: internal network.

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

It costs nothing extra on the existing server, and it matches how the team already ships internal tools. Windows Authentication (Negotiate/Kerberos) is GA and gives company SSO with no identity provider work. Microsoft documents that IIS app pools kill long jobs on recycle (default `shutdownTimeLimit` 90s, 20-min idle timeout, recycle every 1740 min). Its explicit guidance is to host background work outside IIS, which leads to the two-process topology: API in IIS, scanner as a Windows Service under a gMSA or service account. That split also gives FR-005 (scheduled scans) a clean home. The UI moves to React Router SPA mode (`ssr: false`), because Node SSR hosting on IIS is poor: iisnode is unmaintained and HttpPlatformHandler's status is unclear.

#### 2. Docker Compose on internal Linux VM

It has the most deterministic deploy and rollback of the three, keeps React Router SSR as scaffolded (the web Dockerfile already exists), and gives the same setup in development and production. The gap vs. IIS is a new Linux server the team must patch and back up. The scanner image must be `mcr.microsoft.com/dotnet/sdk:10.0` plus git and Node, not chiseled. Docker group access is root-equivalent.

#### 3. Kamal on internal Linux VM

It has the best agent-driven deploy semantics: one command to deploy and one to roll back, with health-gated zero downtime. The gaps vs. IIS are the same new-VM burden as Compose, plus a Ruby toolchain, no MCP, and unfamiliarity. It also needs `stop_timeout` raised, or the scanner run as a non-proxied `job` role (the default is to kill 10s after SIGTERM).

## Anti-Bias Cross-Check: IIS + Windows Service

### Devil's Advocate — Weaknesses

1. **Scanners depend on external services.** `dotnet package list --vulnerable` reads advisories from nuget.org (and any internal feeds), and `npm audit` calls registry.npmjs.org. If the server's outbound access is blocked or proxied, the tools can produce empty output. The portal could then show "no vulnerabilities" for a scan that actually failed, which breaks the primary guardrail against false reassurance.
2. **Two deployables share one schema.** The API runs in IIS and the worker is a Windows Service, both on one PostgreSQL schema. A partial deploy leaves them on different EF Core model versions, and migrations don't roll back automatically.
3. **The UI must change rendering mode.** The React Router scaffold is `ssr: true`. On IIS it becomes an SPA (`ssr: false`): only the root route may have a `loader`, and there are no `action`/`headers` exports. All data goes through the API over same-origin fetch.
4. **Shared server blast radius.** A GitHub Actions self-hosted runner with IIS admin rights on a server hosting other internal apps can recycle or break those apps during a deploy.
5. **PostgreSQL on Windows is unfamiliar.** If the company normally runs SQL Server, installing, patching and backing up Postgres (`pg_dump` schedule, restore test) is new, unowned work.

### Pre-Mortem — How This Could Fail

The team shipped the portal on the shared IIS server and left the scanner inside the app pool "for the first release". IIS recycled the pool nightly and on idle, killing in-flight scans after 90 seconds. Those scans were persisted as completed with zero findings, and nobody questioned the green screen for weeks. The fix moved scanning to a Windows Service, but it ran under a virtual account with no user profile. Git could not find credentials, `%TEMP%` pointed at `C:\Windows\Temp`, and clones failed intermittently in ways that looked like network problems. Windows Defender real-time scanning inspected every file of every clone, and scans went from four minutes to twenty-five. Then a proxy change silently blocked nuget.org. The .NET audit returned no advisory data, the parser treated it as "clean", and a critical CVE at a customer went unreported for two months. Finally, a deploy from the admin-rights runner restarted IIS and took down two unrelated internal apps. The platform was fine. The failures came from assuming IIS familiarity covered long-running processes, service identities, and the scanners' external dependencies.

### Unknown Unknowns

- **MAX_PATH (260 chars).** Deep repos and `node_modules` trees exceed it. Set `git config --system core.longpaths true`, enable Win32 long paths, and keep the scan working directory short (e.g. `D:\scw\<id>`).
- **Defender on scan folders.** Real-time protection slows clones dramatically and can quarantine files from known-vulnerable packages. A scan-directory exclusion must be agreed with the security team.
- **Data Protection keys.** Without persisted keys (`PersistKeysToFileSystem` to a folder the app pool can write to), every app-pool recycle invalidates login cookies and logs everyone out.
- **Windows SSO prerequisites.** Silent sign-in needs the site in the browser's Local Intranet zone. A custom hostname needs an HTTP SPN on the app-pool or service account, or browsers fall back to NTLM or a prompt.
- **.NET 10 CLI noun-first commands.** `dotnet package list --vulnerable` is the .NET 10 form; `dotnet list package` still works as an alias. NuGetAudit also runs during `restore` and emits warnings. The scanner must parse the `--format json` output of the list command and not scrape restore warnings. Pin the SDK with `global.json` so output formats don't shift.

## Operational Story

- **Preview deploys**: none on the platform. Previews run locally (`dotnet run` + `npm run dev`). If needed, add a second IIS site `securitycheck-staging` on another port, deployed from `main` before a manual promote. It is internal-only by network, so no extra access layer is needed.
- **Secrets**:
  - Production connection string and git PAT/SSH key live on the server: ACL'd environment variables on the app pool and the service, or `appsettings.Production.json` outside the repo, readable only by the app-pool identity, the service account and admins.
  - CI-side secrets (runner registration) live in GitHub Secrets.
  - Rotation: an admin updates the value on the server, then runs `Restart-WebAppPool securitycheck` and `Restart-Service SecurityCheck.Worker`.
- **Rollback**: each deploy goes to `D:\apps\securitycheck\releases\<git-sha>\{api,worker}`.
  - To revert, point the site `physicalPath` at the previous release, re-point the service with `sc.exe config SecurityCheck.Worker binPath=...`, then restart both. This takes about 1 minute.
  - Caveat: EF Core migrations do **not** roll back. Migrations must be additive and backward-compatible for one release. A down-migration is a human decision.
- **Approval**:
  - *Human only:* publishing to production (manual approval on a GitHub Environment), running a destructive migration, rotating the git credential or DB password, dropping or restoring the database, and changing app-pool or service identity.
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
| Advisory source (nuget.org / npm registry / internal feed) unreachable produces a false "no vulnerabilities" | Devil's advocate | M | H | Pre-flight connectivity check per scan. A non-zero exit, empty or invalid JSON, or missing `sources` marks the scan **failed**. Record the proxy config on the service account. |
| Service identity has no profile, so git credentials, HOME and TEMP fail | Pre-mortem | H | M | Run the worker under a gMSA or dedicated domain account. Set explicit `HOME`/`TEMP` and a git credential via env (`GIT_ASKPASS` / header), or an SSH key in the service profile. |
| API/worker version skew or non-reversible migration during deploy | Devil's advocate | M | M | Deploy script stops the worker → migrates → swaps API → starts the worker. Migrations are additive only. One release folder serves both processes. |
| React Router SPA-mode constraints (root-only `loader`, no `action`) | Devil's advocate | H | L | Decide `ssr: false` before building UI routes. Data calls go to same-origin `/api/*`, with the SPA served from ASP.NET Core `wwwroot` or a static IIS app under the same site. |
| Self-hosted runner on shared IIS server breaks other apps | Devil's advocate | M | M | Runner service account can only write `D:\apps\securitycheck` and control its own app pool and service. Production deploy requires GitHub Environment approval. |
| MAX_PATH failures on deep repos | Unknown unknowns | M | M | `core.longpaths=true`, enable `LongPathsEnabled`, short scan root. |
| Defender slows scans or quarantines vulnerable-package files | Unknown unknowns | M | M | Scan-directory exclusion approved by the security team. Clean the working dir after each scan. |
| Users logged out on every recycle | Unknown unknowns | H | L | Persist Data Protection keys to disk (protected with DPAPI). |
| Windows SSO prompts or NTLM fallback | Unknown unknowns | M | L | Register an HTTP SPN for the site hostname. GPO adds the site to the Local Intranet zone. |
| PostgreSQL on Windows unowned (patching, backups) | Devil's advocate | M | H | Named owner. Nightly `pg_dump` via Task Scheduler to a separate share. Test a restore once before go-live. |
| .NET CLI output or verb changes break the parser | Unknown unknowns / Research finding | L | M | Pin the SDK in `global.json`, parse `--format json`, keep a contract test on sample output. |
| HttpPlatformHandler / iisnode unsuitable for Node SSR (unmaintained, status unclear, checked 2026-09-23) | Research finding | — | — | Avoided by SPA mode. If SSR is ever needed, move to runner-up (Docker Compose). |
| GitHub self-hosted runner fee (announced 2025-12-16, postponed; status checked 2026-09-23) | Research finding | M | L | Low CI minutes at MVP. Re-check pricing before the fee lands. |

## Getting Started

1. **On the server (one-time, admin):**
   - Install the **.NET 10 Hosting Bundle**, then run `iisreset`.
   - Create the app pool: `New-WebAppPool securitycheck`, then set `managedRuntimeVersion ''`, `startMode AlwaysRunning`, `processModel.idleTimeout 00:00:00` and `processModel.loadUserProfile true`.
   - Create the site with Windows Authentication on and Anonymous off.
   - Install PostgreSQL and create the `securitycheck` database and login.
2. **Split the worker.** Add a `SecurityCheck.Worker` project (Worker Service template) with `Microsoft.Extensions.Hosting.WindowsServices` and `builder.Services.AddWindowsService()`. It shares the EF Core `DbContext` project with the API. Register it once with `New-Service -Name SecurityCheck.Worker -BinaryPathName ...\worker\SecurityCheck.Worker.exe -Credential <gMSA>`.
3. **Switch the UI to SPA mode.** Set `ssr: false` in `web/react-router.config.ts` and build with `npm run build`. Publish `web/build/client` into the API's `wwwroot`, with `app.UseStaticFiles()` + `app.MapFallbackToFile("index.html")`, so UI and `/api` share one origin and one Windows-auth session.
4. **Publish.**
   - Build: `dotnet publish -c Release -o out/api` and `dotnet publish SecurityCheck.Worker -c Release -r win-x64 -o out/worker`.
   - Deploy by running a script from a self-hosted runner on the server that does, in order:
     1. Copy to `releases\<sha>`.
     2. `Stop-Service SecurityCheck.Worker`.
     3. Apply migrations (EF migration bundle `efbundle.exe`).
     4. Drop `app_offline.htm`, swap the `physicalPath`, remove `app_offline.htm`.
     5. Re-point and `Start-Service`.
5. **Verify.**
   - Check the site: `Invoke-WebRequest https://<host>/health -UseDefaultCredentials`.
   - Check the worker: `Get-Service SecurityCheck.Worker`.
   - Trigger one manual scan against a repo with a known-vulnerable package and confirm it is reported (a guardrail smoke test, not just "200 OK").

## Out of Scope

The following were not evaluated in this research:
- Docker image configuration
- CI/CD pipeline setup (only the approval gate and deploy-script shape are noted)
- Production-scale architecture (multi-region, HA, DR)
