import { useState } from "react";
import { Link, Outlet, useNavigate } from "react-router";

import type { Route } from "./+types/app-layout";
import { Alert } from "../components/alert";
import { BrandMark } from "../components/brand-mark";
import { Button } from "../components/button";
import { cx, focusRing } from "../components/styles";
import { ThemeToggle } from "../components/theme-toggle";
import { apiFetch, requireUser } from "../lib/api";

export async function clientLoader({ request }: Route.ClientLoaderArgs) {
  const user = await requireUser(request);
  return { user };
}

// Re-check the session on every navigation inside the protected area, not only
// when this layout is first matched, so an expired or revoked session is
// caught on the next click.
export function shouldRevalidate() {
  return true;
}

export default function AppLayout({ loaderData }: Route.ComponentProps) {
  const { user } = loaderData;
  const navigate = useNavigate();
  const [loggingOut, setLoggingOut] = useState(false);
  const [logoutFailed, setLogoutFailed] = useState(false);

  // 204 means the cookie was cleared; 401 means there was no session anyway.
  async function tryLogout(): Promise<boolean> {
    try {
      const response = await apiFetch("/api/auth/logout", { method: "POST" });
      return response.ok || response.status === 401;
    } catch {
      return false;
    }
  }

  async function logout() {
    setLoggingOut(true);
    setLogoutFailed(false);
    // Navigating while the cookie survives would bounce straight back from /login,
    // silently leaving the user signed in. Retry once, then say so.
    if (!(await tryLogout()) && !(await tryLogout())) {
      setLogoutFailed(true);
      setLoggingOut(false);
      return;
    }
    // Replace the current protected entry. Going Back to any protected entry
    // re-runs this layout's clientLoader (it is a new match from /login), which
    // gets 401 from /api/me and redirects to /login before any data renders.
    navigate("/login", { replace: true });
  }

  return (
    <div className="min-h-screen">
      <header className="border-b border-border-subtle">
        <div className="container mx-auto flex flex-wrap items-center justify-between gap-x-4 gap-y-2 p-4">
          <Link
            to="/"
            className={cx("inline-flex items-center gap-2 rounded-lg text-lg font-semibold text-text", focusRing)}
          >
            <BrandMark className="text-primary" />
            <span>SecurityCheck Portal</span>
          </Link>
          <div className="flex flex-wrap items-center gap-3">
            {logoutFailed && <Alert>Nie udało się wylogować. Spróbuj ponownie.</Alert>}
            <span className="text-sm text-text-muted">{user.displayName}</span>
            <ThemeToggle />
            <Button variant="secondary" onClick={logout} disabled={loggingOut}>
              Wyloguj
            </Button>
          </div>
        </div>
      </header>
      <Outlet />
    </div>
  );
}
