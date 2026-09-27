import { redirect } from "react-router";

export type CurrentUser = {
  userName: string;
  displayName: string;
};

/**
 * Shared fetch for the portal API. The auth cookie (sc_auth) is HttpOnly and
 * same-origin: in dev Vite proxies /api to Kestrel, in production the API
 * serves the SPA itself. Loaders and actions pass their `request`: then a 401
 * (expired or missing session) throws a redirect to the login page carrying
 * the current path in `next`.
 */
export async function apiFetch(
  path: string,
  init: RequestInit = {},
  request?: Request,
): Promise<Response> {
  const headers = new Headers(init.headers);
  headers.set("Accept", "application/json");
  if (init.body != null && !headers.has("Content-Type")) {
    headers.set("Content-Type", "application/json");
  }

  const response = await fetch(path, {
    ...init,
    headers,
    credentials: "same-origin",
  });

  if (request && response.status === 401) {
    throw loginRedirect(request);
  }

  return response;
}

function loginRedirect(request: Request): Response {
  const url = new URL(request.url);
  return redirect("/login?next=" + encodeURIComponent(url.pathname + url.search));
}

/**
 * Returns the signed-in user or throws a redirect to the login page, carrying
 * the requested path (with search) in `next`. Takes the loader's `request`
 * because during a client navigation window.location still shows the
 * previous URL.
 */
export async function requireUser(request: Request): Promise<CurrentUser> {
  const response = await apiFetch("/api/me", { signal: request.signal }, request);

  if (!response.ok) {
    throw new Error(`GET /api/me failed with status ${response.status}`);
  }

  return (await response.json()) as CurrentUser;
}

const SAFE_NEXT_BASE = "http://safe-next.invalid";

function isSafeRelativePath(value: string): boolean {
  if (!value.startsWith("/")) return false;
  // "//host" and "/\host" are protocol-relative for browsers; a backslash
  // anywhere is normalised to "/" by URL parsers, so reject it outright.
  if (value.startsWith("//") || value.includes("\\")) return false;
  // Browsers strip tabs/newlines from URLs ("/\t/evil" -> "//evil").
  if (/[\u0000-\u001f\u007f]/.test(value)) return false;
  return true;
}

/**
 * Open-redirect guard for `?next=`. Lets through only same-origin relative
 * paths ("/..."); anything else (absolute URLs, "//host", "/\host",
 * "javascript:", empty, non-string) becomes "/".
 */
export function safeNext(next: unknown): string {
  if (typeof next !== "string" || !isSafeRelativePath(next)) return "/";

  // Also check the percent-decoded form, so encoded variants such as
  // "/%2F%2Fhost" or "/%5Chost" are rejected too.
  let decoded: string;
  try {
    decoded = decodeURIComponent(next);
  } catch {
    return "/";
  }
  if (!isSafeRelativePath(decoded)) return "/";

  // Final check: resolving against a dummy origin must keep that origin.
  try {
    if (new URL(next, SAFE_NEXT_BASE).origin !== SAFE_NEXT_BASE) return "/";
  } catch {
    return "/";
  }

  return next;
}
