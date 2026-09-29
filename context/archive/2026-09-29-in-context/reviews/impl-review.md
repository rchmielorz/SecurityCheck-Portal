<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Podstawowy wygląd aplikacji

- **Plan**: context/changes/in-context/plan.md
- **Scope**: Full plan (5 of 5 phases)
- **Reviewed phases**: 1, 2, 3, 4, 5
- **Date**: 2026-09-29
- **Verdict**: APPROVED
- **Findings**: 0 critical, 2 warnings, 6 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

Automated criteria re-run during review: typecheck PASS, build PASS, no `styleguide` in `web/build/client`, no `window.confirm` and no `bg-blue-600` in `web/app`, `styleguide` present in `AGENTS.md`. No new dependencies (`web/package.json` and lockfile untouched), no `api/` or `core/` changes. All manual rows are user-confirmed.

## Findings

### F1 — Focus is lost after a confirmed delete

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality (Accessibility)
- **Location**: web/app/components/confirm-dialog.tsx:34-38, web/app/routes/repo-details.tsx:279-287
- **Detail**: On confirm, the dialog restores focus to the invoking button, but that button is about to be disabled by `busy` (and for a pattern delete the row is removed after revalidation). `focus()` on a disabled or unmounted element does nothing, so keyboard and screen-reader focus falls to `<body>` after every successful delete.
- **Fix**: Restore focus to the invoker only on cancel; on confirm move focus to a stable target (the "Wzorce wersji" heading with `tabIndex={-1}`, or the add-pattern input).
  - Strength: Keeps keyboard users oriented after deleting; small change in one component plus one prop.
  - Tradeoff: ConfirmDialog needs a way to be told where focus goes on confirm (prop or callback).
  - Confidence: HIGH — the code path is straightforward.
  - Blind spot: Not reproduced in a browser with a screen reader.
- **Decision**: FIXED — Fix now (ConfirmDialog `focusAfterConfirm`; focus goes to the "Wzorce wersji" heading after confirm, invoker after cancel)

### F2 — Dialog invoker is captured from document.activeElement

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality (Reliability)
- **Location**: web/app/components/confirm-dialog.tsx:29-40
- **Detail**: The invoker is read from `document.activeElement` inside the open effect. A mouse click does not focus a `<button>` in Safari/Firefox, so `activeElement` is `<body>` and nothing is restored there; the native dialog fallback is the only path. Anything that moves focus between click and effect would also remember the wrong element.
- **Fix**: Capture the invoker at request time (`event.currentTarget` in `requestDelete`) and pass it to the dialog, or document the limitation in a comment.
- **Decision**: FIXED — Fix now (invoker captured from the click via `event.currentTarget`, passed as `invoker` prop)

### F3 — Plan text differs from what was built (palette, Field API)

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: context/changes/in-context/plan.md (Implementation Approach, Faza 2 `Field`)
- **Detail**: The plan describes a marine/indigo primary and a `Field` that wraps a child input. The build uses an orange palette from the tweakcn "qrafthive" theme (approved at checkpoint 2.5, primary darkened for AA) and a render-prop `Field` with a separate `Input`. Both are deliberate and accepted, but plan.md was not updated.
- **Fix**: Add a short addendum to plan.md recording the palette change and the render-prop Field API.
- **Decision**: FIXED — Fix now (addendum added to plan.md before References)

### F4 — ThemeToggle label flickers on mount

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality (Reliability)
- **Location**: web/app/components/theme-toggle.tsx:81-93
- **Detail**: First render is always "system" and the stored value is applied after mount, so the label and icon change from "systemowy" to the stored mode on each mount. The page theme itself does not flash (head script). There is also no `storage` listener, so another tab's change is not reflected.
- **Fix**: Initialise state lazily from storage (`useState(readStoredTheme)`), which is safe because `ssr: false`.
- **Decision**: FIXED — Fix now (ThemeToggle state lazily initialised from storage)

### F5 — Theme key and logic duplicated, exports unused

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: web/app/components/theme-toggle.tsx:5,14,29, web/app/root.tsx:19-32
- **Detail**: `THEME_STORAGE_KEY`, `readStoredTheme` and `applyTheme` are exported but not imported anywhere; the inline script in root.tsx hard-codes `"theme"` and repeats the logic, so the two can drift.
- **Fix**: Add a comment tying the two together, or drop the unused `export`s.
- **Decision**: FIXED — Fix now (unused exports dropped; sync comments in theme-toggle.tsx and root.tsx)

### F6 — /styleguide exclusion relies on NODE_ENV at build time

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: web/app/routes.ts:12
- **Detail**: `react-router build` sets `NODE_ENV=production`, and the current build contains no styleguide. If a build were run with `NODE_ENV=development` set explicitly, the page would ship. It shows no data, so impact is low.
- **Fix**: Add a build-time check (e.g. in `build:api`) that `build/client` has no styleguide markup, or accept the risk.
- **Decision**: FIXED — Fix now (web/scripts/copy-to-api.mjs aborts if build/client contains "styleguide"; verified with a planted leak and a clean build)

### F7 — lib/patterns.ts imports a type from components

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Architecture
- **Location**: web/app/lib/patterns.ts (`resolutionBadge`)
- **Detail**: `lib/` now has a type-only dependency on `components/badge.tsx` (no runtime cycle). Minor layering note.
- **Fix**: Leave as is, or move `BadgeKind` to a small shared types module.
- **Decision**: SKIPPED — type-only import, no runtime cycle

### F8 — Duplicate heading name and dead anchor

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: web/app/routes/home.tsx:60-64,84, web/app/routes/styleguide.tsx:150
- **Detail**: The sr-only `<h1>` and the visible `<h2>` are both named "Repozytoria". The styleguide sample link `href="#typografia"` points to a missing anchor. Heading order itself is valid.
- **Fix**: Rename the h1 (e.g. "Panel repozytoriów") and fix or remove the dead anchor.
- **Decision**: FIXED — Fix now (sr-only h1 renamed "Panel repozytoriów"; styleguide Section gets id="typografia")
