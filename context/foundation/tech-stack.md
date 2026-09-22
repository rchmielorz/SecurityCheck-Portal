---
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
---

## Why this stack

A solo developer building an internal vulnerability-scanning portal in 3 after-hours weeks chose a split stack: an ASP.NET Core (.NET) backend as the scaffolded starter and a React Router (TypeScript) frontend as the UI layer, with PostgreSQL via EF Core/Npgsql. The .NET backend owns the load-bearing work — login, repository and version-pattern management, and long-running dependency scans — and its hosted background services give scheduled scans (FR-005) a natural home without re-architecture. React Router adds a convention-based, typed TypeScript UI; both starters pass all four agent-friendly gates and have verified scaffolding. Self-host was chosen because it is common to both starters and keeps customer and repository data inside the company, matching the PRD guardrail. CI runs on GitHub Actions with auto-deploy on merge. The React Router UI (`create-react-router`) must be scaffolded alongside the .NET API as a second step, since the hand-off carries a single starter.
