---
date: 2026-09-29T19:28:09+02:00
researcher: Rafal Chmielorz
git_commit: e3f1a8c0236703d480dbce6ee3a967fbaaf3ccec
branch: feature/ui
repository: M1L1 (securitycheck-portal)
topic: "Określenie podstawowego wyglądu aplikacji — what look-and-feel exists and what is decided"
tags: [research, codebase, web, tailwind, ui, design]
status: complete
last_updated: 2026-09-29
last_updated_by: Rafal Chmielorz
---

# Research: Określenie podstawowego wyglądu aplikacji

**Date**: 2026-09-29T19:28:09+02:00
**Researcher**: Rafal Chmielorz
**Git Commit**: e3f1a8c0236703d480dbce6ee3a967fbaaf3ccec
**Branch**: feature/ui
**Repository**: M1L1 (securitycheck-portal)

## Research Question

What is the current visual look of the web app, and which look-and-feel decisions or constraints already exist in the docs and archived changes? (Input for defining the app's basic look.)

## Summary

- **No visual-design decision exists in the docs inspected** (`context/foundation/*`, both archived changes, `AGENTS.md`, `web/README.md`). Settled items: Polish UI text, Tailwind 4, system font stack, SPA served from the API origin, internal small-team tool.
- **The de facto look is the implicit output of two archived slices**: Tailwind default palette (gray neutrals, `blue-600` primary, red for errors/danger), OS-driven dark mode, `rounded-2xl` bordered cards, a text-only header brand. No custom tokens beyond one `@theme` font entry (`web/app/app.css:3-6`).
- **Styling is copy-pasted**: no `web/app/components/` directory was found among inspected files; the primary button class string appears in 3 places, the text input classes in 5 (inspected routes only).
- **Biggest gaps** for the upcoming product screens: no severity color system (PRD requires severity sort and new-vs-accepted distinction, `context/foundation/prd.md:74,87-91`), no focus styles, no breakpoints, no visual distinction between resolution states (`web/app/lib/patterns.ts:82-106`).

## Detailed Findings

### Styling stack and tokens (observed)
- Tailwind CSS v4 via `tailwindcss` + `@tailwindcss/vite` (`web/package.json:22-23`); `@import "tailwindcss";` at `web/app/app.css:1`. Dependencies list only react, react-dom, react-router, isbot (`web/package.json:12-18`): no UI library, no icon package.
- Only customization: `@theme` block with `--font-sans` system stack (`web/app/app.css:3-6`); `html, body` background/`color-scheme` rule (`web/app/app.css:8-15`). No web font (removed on purpose: `context/archive/2026-09-26-authenticated-app-shell/plan.md:30`).
- Colors are default Tailwind: borders gray-200/800; primary `blue-600` (`web/app/routes/home.tsx:182`, `login.tsx:324`, `repo-details.tsx:311`); links blue-700/400; errors `text-red-600 dark:text-red-400` with `role="alert"`; danger button red (`repo-details.tsx:145-146`).
- Dark mode: follows OS via default `dark:` variant; no toggle (`app.css:10-14`, `root.tsx:37`). No document states whether dark mode is a requirement.
- Shape/spacing repeated, not tokenized: `container mx-auto p-4`, cards `rounded-2xl border shadow-sm`, controls `rounded-lg`, h1 `text-2xl font-semibold`, h2 `text-lg font-semibold`.

### Layout shell and screens (observed, from source; app not run)
- `root.tsx:28-44`: `<html lang="pl">`, viewport meta, body `font-sans`.
- Routes (`web/app/routes.ts:9-17`): `/login` standalone; pathless `app-layout.tsx` wraps index, `repos/:repoId`, `*`.
- `AppLayout` (`web/app/routes/app-layout.tsx:51-76`): top bordered header, text brand "SecurityCheck Portal" (not a link, `:55`), displayName + "Wyloguj" button; no sidebar or nav. Header row has no `flex-wrap` (`:54`).
- Login: centered `max-w-sm` card (`login.tsx:279-331`). Home: h1 duplicates the header brand (`home.tsx:145`), add-repo card, `divide-y` repository list, dashed empty state (`:189-217`). Repo details: back link, danger delete button, "Wzorce wersji" card with mono pattern rows and gray "nieaktywny" pill, "Historia zmian" list (`repo-details.tsx:204-343`). Not-found and root `ErrorBoundary` are bare `<main>` blocks with no way back (`not-found.tsx:9-14`, `root.tsx:60-82`).
- Resolution states (Resolved/NoMatch/Ambiguous/Error) render as uncolored text (`patterns.ts:82-106`, `repo-details.tsx:164`).

### Accessibility and responsiveness (observed)
- Present in the inspected routes: `lang="pl"`, `label htmlFor` on inputs, `role="alert"`, `<time dateTime>`, semantic elements.
- Absent in the inspected files: `focus-visible` styling, `aria-live` for "Sprawdzanie…", `aria-invalid`/`aria-describedby`, breakpoint prefixes (`sm:`/`md:`). Destructive confirmation uses native `window.confirm` (`repo-details.tsx:137-141`). Contrast was not measured.

### Language and copy (settled)
- Polish, hard-coded, no i18n: `context/archive/2026-09-26-authenticated-app-shell/plan-brief.md:28,53`; `plan.md:42`. Dates via `toLocaleString("pl-PL")` (`patterns.ts:56`); Polish plural helper for pattern count (`home.tsx:128-136`). Other counts (e.g. "Pokaż nieaktywne (N)") are not pluralized.

### Constraints from product and infra docs
- Internal tool, small team, low load: `context/foundation/prd.md:7-11,95`; persona `:24-26`.
- Solo developer, after-hours, hard deadline 2026-11-04: `prd.md:12-15`, `shape-notes.md:35-38` → design effort must stay small.
- SPA (`ssr: false`), served from API `wwwroot`, one origin: `context/foundation/infrastructure.md:114,163,187`. No external CDN/fonts (rationale in the shell plan, `plan.md:12,30`).
- No public pages; login must show no data: `prd.md:83,39`.
- Trust guardrail: a failed/incomplete scan must never look like a clean result (`prd.md:38`; explicit scan-failure error `roadmap.md:123`; lockfile-less false "no results" risk `roadmap.md:130`).
- Functional UI needs from PRD: severity sort + accepted-risk filter (`prd.md:74`), new vs accepted visually distinct (`prd.md:87-91`), explicit "Brak wyników" (`prd.md:51-53`).
- `AGENTS.md`: UI code only in `web/` (`:12`); routes registered in `web/app/routes.ts` (`:19`); no dot in the last URL segment (`:8`); `npm run typecheck` (`:32`); no UI test runner (`:40`).

## Code References

- `web/app/app.css:1-15` — Tailwind import, `@theme` font, html/body dark rule
- `web/app/root.tsx:28-82` — Layout, HydrateFallback, ErrorBoundary
- `web/app/routes/app-layout.tsx:51-76` — the only shell (header)
- `web/app/routes/home.tsx:138-220` — repo list + add form
- `web/app/routes/repo-details.tsx:143-146` — only shared class constants (`secondaryButton`, `dangerButton`)
- `web/app/routes/login.tsx:279-331` — login card
- `web/app/lib/patterns.ts:82-106` — `describeResolution` (text only)

## Architecture Insights

- UI conventions live only in class strings inside route files; the sole reuse mechanisms are two constants and one local component. Any shared look needs either `@theme` tokens in `app.css` or a new components folder — neither exists yet.
- Constraints (no new dependencies, no external assets, small effort) favor a Tailwind-only, token-in-CSS approach; this is inference from the constraints above, not a stated decision.

## Historical Context (from prior changes)

- `context/archive/2026-09-26-authenticated-app-shell/plan-brief.md:28` — Polish UI, no i18n (supported).
- `context/archive/2026-09-26-authenticated-app-shell/plan.md:30` — Google Fonts/Inter removed for system stack (supported; rationale is external-request avoidance, not aesthetics).
- `context/archive/2026-09-26-repo-version-pattern/plan.md:413,449` — Polish texts, `window.confirm` for destructive actions as a deliberate simple choice.
- Reviews of both archived changes cover auth and git security; the agent did not open the UI item at `repo-version-pattern/reviews/impl-review.md:121` in detail.

## Related Research

- `context/archive/2026-09-26-repo-version-pattern/research.md` — about version-pattern resolution, not UI (not re-read here).

## Open Questions

Undecided anywhere in the inspected docs (each is a candidate decision for `/10x-plan`):
1. Visual identity: accent color (blue-600 is a template default), wordmark/logo, favicon (`web/public/favicon.ico` exists; whether it is the stock template icon is unverified).
2. Severity + status color system (critical/high/medium/low, new vs accepted-risk, resolution states, scan-failed vs empty) with a non-color cue.
3. Dark mode: explicit requirement (and toggle?) or accident.
4. Typography scale and monospace convention for CVE IDs/versions/SHAs.
5. Data-display pattern for vulnerability lists (table vs list), density.
6. Accessibility target (WCAG level, focus ring, in-page dialog instead of `window.confirm`).
7. Device/browser scope (desktop-only vs responsive); header overflow on narrow screens.
8. Component approach: shared components/tokens vs continued copy-paste.
9. Interaction patterns: loading/progress for scans, toasts vs inline, success feedback.
10. Navigation/IA beyond header + two routes; "client" as an entity is not yet in the UI.
11. Docs drift: `web/README.md` is untouched template text (SSR/Docker); `AGENTS.md` has no UI conventions section.

Not verified: the app was never run or screenshotted; `vite.config.ts` was not inspected; findings come from source reading by two read-only sub-agents plus the lead's file listing.
