import { Form, redirect, replace, useNavigation, useSearchParams } from "react-router";

import type { Route } from "./+types/login";
import { apiFetch, safeNext } from "../lib/api";

const ERROR_MESSAGES: Record<number, string> = {
  400: "Podaj login i hasło.",
  401: "Nieprawidłowy login lub hasło.",
  403: "Twoje konto nie ma dostępu do portalu.",
  503: "Usługa logowania jest niedostępna. Spróbuj później.",
  429: "Zbyt wiele prób logowania. Odczekaj minutę.",
};

const UNEXPECTED_ERROR = "Wystąpił nieoczekiwany błąd.";

export function meta({}: Route.MetaArgs) {
  return [{ title: "Logowanie – SecurityCheck Portal" }];
}

export async function clientLoader({ request }: Route.ClientLoaderArgs) {
  // An already signed-in user skips the form.
  let response: Response;
  try {
    response = await apiFetch("/api/me", { signal: request.signal });
  } catch {
    // API unreachable: still show the form; the login attempt will report it.
    return null;
  }
  if (response.ok) {
    throw redirect("/");
  }
  return null;
}

export async function clientAction({ request }: Route.ClientActionArgs) {
  const formData = await request.formData();
  const userName = String(formData.get("userName") ?? "");
  const password = String(formData.get("password") ?? "");
  const next = formData.get("next") ?? new URL(request.url).searchParams.get("next");

  let response: Response;
  try {
    response = await apiFetch("/api/auth/login", {
      method: "POST",
      body: JSON.stringify({ userName, password }),
    });
  } catch {
    return { error: UNEXPECTED_ERROR };
  }

  if (response.ok) {
    // Replace, so Back from the target page does not return to the form.
    return replace(safeNext(next));
  }

  return { error: ERROR_MESSAGES[response.status] ?? UNEXPECTED_ERROR };
}

export default function Login({ actionData }: Route.ComponentProps) {
  const [searchParams] = useSearchParams();
  const navigation = useNavigation();
  const submitting = navigation.state !== "idle" && navigation.formMethod === "POST";
  const next = searchParams.get("next");

  return (
    <main className="flex min-h-screen items-center justify-center p-4">
      <div className="w-full max-w-sm rounded-2xl border border-gray-200 p-8 shadow-sm dark:border-gray-800">
        <h1 className="mb-6 text-center text-2xl font-semibold">SecurityCheck Portal</h1>
        <Form method="post" className="space-y-4" noValidate>
          {next !== null && <input type="hidden" name="next" value={next} />}
          <div className="space-y-1">
            <label htmlFor="userName" className="block text-sm font-medium">
              Login
            </label>
            <input
              id="userName"
              name="userName"
              type="text"
              autoComplete="username"
              autoFocus
              className="w-full rounded-lg border border-gray-300 bg-white px-3 py-2 dark:border-gray-700 dark:bg-gray-900"
            />
          </div>
          <div className="space-y-1">
            <label htmlFor="password" className="block text-sm font-medium">
              Hasło
            </label>
            <input
              id="password"
              name="password"
              type="password"
              autoComplete="current-password"
              className="w-full rounded-lg border border-gray-300 bg-white px-3 py-2 dark:border-gray-700 dark:bg-gray-900"
            />
          </div>
          {actionData?.error && (
            <p role="alert" className="text-sm text-red-600 dark:text-red-400">
              {actionData.error}
            </p>
          )}
          <button
            type="submit"
            disabled={submitting}
            className="w-full rounded-lg bg-blue-600 px-4 py-2 font-medium text-white hover:bg-blue-700 disabled:opacity-60"
          >
            Zaloguj
          </button>
        </Form>
      </div>
    </main>
  );
}
