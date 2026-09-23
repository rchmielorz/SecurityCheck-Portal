---
bootstrapped_at: 2026-09-22T19:41:10Z
starter_id: dotnet
starter_name: .NET (ASP.NET Core webapi)
project_name: securitycheck-portal
language_family: multi
package_manager: dotnet
cwd_strategy: subdir-then-move
bootstrapper_confidence: verified
phase_3_status: ok
audit_command: "null"
---

## Hand-off

```yaml
starter_id: dotnet
package_manager: dotnet
project_name: securitycheck-portal
hints:
  language_family: multi
  team_size: solo
  deployment_target: self-host
  ci_provider: github-actions
  ci_default_flow: auto-deploy-on-merge
  bootstrapper_confidence: verified
  path_taken: custom
  quality_override: false
  self_check_answers:
    typed: true
    from_official_starter: true
    conventions: true
    docs_current: true
    can_judge_agent: false
  has_auth: true
  has_payments: false
  has_realtime: false
  has_ai: false
  has_background_jobs: true
```

### Why this stack

A solo developer building an internal vulnerability-scanning portal in 3 after-hours weeks chose a split stack: an ASP.NET Core (.NET) backend as the scaffolded starter and a React Router (TypeScript) frontend as the UI layer, with PostgreSQL via EF Core/Npgsql. The .NET backend owns the load-bearing work — login, repository and version-pattern management, and long-running dependency scans — and its hosted background services give scheduled scans (FR-005) a natural home without re-architecture. React Router adds a convention-based, typed TypeScript UI; both starters pass all four agent-friendly gates and have verified scaffolding. Self-host was chosen because it is common to both starters and keeps customer and repository data inside the company, matching the PRD guardrail. CI runs on GitHub Actions with auto-deploy on merge. The React Router UI (`create-react-router`) must be scaffolded alongside the .NET API as a second step, since the hand-off carries a single starter.

**Divergence noted**: hand-off `hints.language_family` is `multi`, while the registry card's `language_family` is `dotnet`. The hand-off value was used (user confirmed "Proceed"), which routes the audit slot to the `multi` skip.

## Pre-scaffold verification

| Signal      | Value   | Severity | Notes                                                                                   |
| ----------- | ------- | -------- | --------------------------------------------------------------------------------------- |
| npm package | not run | —        | non-JS starter; `cmd_template` invokes `dotnet new`, not an npm `create-*` CLI           |
| GitHub repo | not run | —        | `docs_url` is `https://learn.microsoft.com/aspnet/core` (not GitHub) — no recency signal |

Local toolchain observed (informational): .NET SDK 10.0.204 (default); `webapi` template available.

## Scaffold log

**Card cmd_template**: `dotnet new webapi -n {name} --no-restore`
**Resolved invocation**: `dotnet new webapi -n securitycheck-portal -o .bootstrap-scaffold --no-restore`
**Substitution deviation**: literal `{name}=.bootstrap-scaffold` would have made `dotnet new` name the project `.bootstrap-scaffold.csproj` with namespace `_bootstrap_scaffold`. Instead `-n` kept `project_name` and `-o .bootstrap-scaffold` pointed output at the temp dir — equivalent directory semantics, correct project/namespace naming (`RootNamespace: securitycheck_portal`, `TargetFramework: net10.0`).
**Strategy**: subdir-then-move (default — `dotnet` not listed in bootstrapper-config.yaml)
**Exit code**: 0
**Files moved**: 6 (`Program.cs`, `appsettings.json`, `appsettings.Development.json`, `Properties/launchSettings.json`, `securitycheck-portal.csproj`, `securitycheck-portal.http`)
**Conflicts (.scaffold siblings)**: none
**.gitignore handling**: absent in scaffold (and absent in cwd) — `bin/` and `obj/` are not ignored yet
**.bootstrap-scaffold cleanup**: deleted

## Post-scaffold audit

**Tool**: skipped — no built-in audit tool for multi
**Recommended external tool**: no single audit tool covers this multi-language stack. Per component: `dotnet list package --vulnerable --include-transitive` for the .NET API (after `dotnet restore`); `npm audit` for the React Router UI once scaffolded.

## Hints recorded but not acted on

| Hint                                   | Value                |
| -------------------------------------- | -------------------- |
| bootstrapper_confidence                | verified             |
| quality_override                       | false                |
| path_taken                             | custom               |
| self_check_answers.typed               | true                 |
| self_check_answers.from_official_starter | true               |
| self_check_answers.conventions         | true                 |
| self_check_answers.docs_current        | true                 |
| self_check_answers.can_judge_agent     | false                |
| team_size                              | solo                 |
| deployment_target                      | self-host            |
| ci_provider                            | github-actions       |
| ci_default_flow                        | auto-deploy-on-merge |
| has_auth                               | true                 |
| has_payments                           | false                |
| has_realtime                           | false                |
| has_ai                                 | false                |
| has_background_jobs                    | true                 |

## Follow-up: React Router UI (`web/`)

Second step named in the hand-off, done after the bootstrap run:
- `web/` scaffolded by the user with `create-react-router` (npm latest 8.4.0, modified 2026-09-15 — fresh); root `.gitignore` added via `dotnet new gitignore`.
- `npm install` in `web/`: 181 packages added, `npm audit` 0 vulnerabilities.
- `npm run typecheck` (`react-router typegen && tsc`): exit 0.

## Next steps

Next: a future skill will set up agent context (CLAUDE.md, AGENTS.md). For now, your project is scaffolded and verified — happy hacking.

Useful manual steps in the meantime:
- `git init` (if you have not already) to start your own repo history.
- Review any `.scaffold` siblings the conflict policy created and decide which version of each file to keep.
- Address audit findings per your project's risk tolerance — the full breakdown is in this log.
- Add a `.gitignore` for .NET (`dotnet new gitignore`) before committing, so `bin/` and `obj/` stay out of history.
- Scaffold the React Router UI as the second step named in the hand-off (e.g. `npx create-react-router@latest <ui-dir> --yes --package-manager npm`).
