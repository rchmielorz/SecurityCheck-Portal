# Review fixes: follow-ups

Source: `reviews/impl-review.md` (triage 2026-09-27). The code fixes are applied; these checks and doc updates are still open.

- [ ] **F2**: Re-run manual check 1.5 on real AD. The search now requires the `userPrincipalName` attribute to literally equal `{login}@{UpnSuffix}`; otherwise login fails closed with 403.
- [ ] **F1**: Optionally verify the LDAP timeout against a silent server (a TCP listener on 636 that accepts and never replies); expect 503 after about `ConnectTimeoutSeconds`.
- [ ] **F3**: Update `context/foundation/infrastructure.md` Risk Register ("zgadywanie haseł blokuje konta AD"): the mitigation is now per-IP (5/min) plus per-account (5 failed per 15 min), and should stay below the AD lockout threshold.
- [ ] **F6, F8**: Browser check: a session expiring during a loader redirects to `/login?next=…`; a failed logout (API stopped) shows "Nie udało się wylogować. Spróbuj ponownie." and stays on the page.
