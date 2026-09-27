# Review fixes: follow-ups

Source: `reviews/impl-review.md` (triage 2026-09-27). The code fixes are applied; these checks and doc updates are still open.

- [x] **F2**: Re-run manual check 1.5 on real AD. The search now requires the `userPrincipalName` attribute to literally equal `{login}@{UpnSuffix}`; otherwise login fails closed with 403. Verified 2026-09-27: `whoami /upn` matches `{login}@{UpnSuffix}`, and login on real AD returns 200.
- [ ] **F1**: Optionally verify the LDAP timeout against a silent server (a TCP listener on 636 that accepts and never replies); expect 503 after about `ConnectTimeoutSeconds`.
- [x] **F3**: Update `context/foundation/infrastructure.md` Risk Register ("zgadywanie haseł blokuje konta AD"): the mitigation is now per-IP (5/min) plus per-account (5 failed per 15 min), and should stay below the AD lockout threshold.
- [x] **F6, F8**: Browser check: a session expiring during a loader redirects to `/login?next=…`; a failed logout (API stopped) shows "Nie udało się wylogować. Spróbuj ponownie." and stays on the page. Verified 2026-09-27 against the built SPA served by the API on :5143:
  - F6: session cleared server-side, then `/cokolwiek` redirects to `/login?next=%2Fcokolwiek`; after login the user returns to `/cokolwiek` (404 inside the shell).
  - F8: with the API stopped, "Wyloguj" makes 2 attempts (`ERR_CONNECTION_REFUSED`), shows the alert and stays on the page with the button enabled. With the API running again, it goes to `/login` and `/api/me` returns 401.
