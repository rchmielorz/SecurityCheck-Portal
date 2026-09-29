import {
  isRouteErrorResponse,
  Links,
  Meta,
  Outlet,
  Scripts,
  ScrollRestoration,
} from "react-router";

import type { Route } from "./+types/root";
import { Card } from "./components/card";
import { linkClass } from "./components/styles";
import "./app.css";

export const links: Route.LinksFunction = () => [
  { rel: "icon", type: "image/svg+xml", href: "/favicon.svg" },
];

// Stored `theme` ∈ system | light | dark; missing/invalid/blocked storage = system.
// Mirrors readStoredTheme/applyTheme in components/theme-toggle.tsx: change both together.
const THEME_INIT_SCRIPT = `(function(){var m=null;try{m=localStorage.getItem("theme")}catch(e){}var d=m==="dark"||(m!=="light"&&window.matchMedia&&window.matchMedia("(prefers-color-scheme: dark)").matches);document.documentElement.classList.toggle("dark",d)})();`;

export function Layout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="pl" suppressHydrationWarning>
      <head>
        <meta charSet="utf-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1" />
        {/* Runs synchronously before first paint to avoid a flash of the wrong theme. */}
        <script dangerouslySetInnerHTML={{ __html: THEME_INIT_SCRIPT }} />
        <Meta />
        <Links />
      </head>
      <body className="bg-surface font-sans text-text">
        {children}
        <ScrollRestoration />
        <Scripts />
      </body>
    </html>
  );
}

export default function App() {
  return <Outlet />;
}

// Rendered into build/client/index.html at build time (SPA mode) and shown
// until the client loaders of the first matched routes finish.
export function HydrateFallback() {
  return (
    <main className="flex min-h-screen items-center justify-center p-4">
      <title>Ładowanie… – SecurityCheck Portal</title>
      <p className="text-text-muted">Ładowanie…</p>
    </main>
  );
}

export function ErrorBoundary({ error }: Route.ErrorBoundaryProps) {
  let message = "Wystąpił nieoczekiwany błąd";
  let stack: string | undefined;

  if (isRouteErrorResponse(error)) {
    if (error.status === 404) {
      message = "Nie znaleziono strony";
    }
  } else if (import.meta.env.DEV && error && error instanceof Error) {
    stack = error.stack;
  }

  return (
    <main className="flex min-h-screen items-center justify-center p-4">
      <title>{`${message} – SecurityCheck Portal`}</title>
      <Card className="w-full max-w-2xl space-y-3">
        <h1 className="text-2xl font-semibold">{message}</h1>
        {stack && (
          <pre className="w-full overflow-x-auto rounded-lg bg-surface-muted p-4 text-sm">
            <code>{stack}</code>
          </pre>
        )}
        <a href="/" className={linkClass}>
          Wróć na stronę główną
        </a>
      </Card>
    </main>
  );
}
