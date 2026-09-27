import { useState } from "react";
import { Outlet, useNavigate } from "react-router";

import type { Route } from "./+types/app-layout";
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
      <header className="border-b border-gray-200 dark:border-gray-800">
        <div className="container mx-auto flex items-center justify-between gap-4 p-4">
          <span className="text-lg font-semibold">SecurityCheck Portal</span>
          <div className="flex items-center gap-4">
            {logoutFailed && (
              <p role="alert" className="text-sm text-red-600 dark:text-red-400">
                Nie udało się wylogować. Spróbuj ponownie.
              </p>
            )}
            <span className="text-sm text-gray-700 dark:text-gray-300">{user.displayName}</span>
            <button
              type="button"
              onClick={logout}
              disabled={loggingOut}
              className="rounded-lg border border-gray-300 px-3 py-1.5 text-sm font-medium hover:bg-gray-100 disabled:opacity-60 dark:border-gray-700 dark:hover:bg-gray-900"
            >
              Wyloguj
            </button>
          </div>
        </div>
      </header>
      <Outlet />
    </div>
  );
}
