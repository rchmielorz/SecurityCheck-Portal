import { Form, Link, redirect, useNavigation } from "react-router";

import type { Route } from "./+types/home";
import { apiFetch } from "../lib/api";
import { repositoryDisplayName, type RepositoryDetails, type RepositorySummary } from "../lib/patterns";

const ERROR_MESSAGES: Record<number, string> = {
  400: "Podaj poprawny adres HTTPS repozytorium z dozwolonego serwera Git.",
  409: "To repozytorium jest już dodane.",
};

const UNEXPECTED_ERROR = "Wystąpił nieoczekiwany błąd.";

export function meta({}: Route.MetaArgs) {
  return [{ title: "SecurityCheck Portal" }];
}

export async function clientLoader({ request }: Route.ClientLoaderArgs) {
  const response = await apiFetch("/api/repos", { signal: request.signal }, request);
  if (!response.ok) {
    throw new Error(`GET /api/repos failed with status ${response.status}`);
  }
  return { repositories: (await response.json()) as RepositorySummary[] };
}

export async function clientAction({ request }: Route.ClientActionArgs) {
  const formData = await request.formData();
  const url = String(formData.get("url") ?? "").trim();
  const name = String(formData.get("name") ?? "").trim();

  let response: Response;
  try {
    response = await apiFetch(
      "/api/repos",
      { method: "POST", body: JSON.stringify({ url, name: name || null }) },
      request,
    );
  } catch (error) {
    if (error instanceof Response) throw error;
    return { error: UNEXPECTED_ERROR };
  }

  if (response.status === 201) {
    const repository = (await response.json()) as RepositoryDetails;
    return redirect(`/repos/${repository.id}`);
  }

  return { error: ERROR_MESSAGES[response.status] ?? UNEXPECTED_ERROR };
}

function patternCountLabel(count: number): string {
  if (count === 1) return "1 aktywny wzorzec";
  const lastDigit = count % 10;
  const lastTwo = count % 100;
  if (lastDigit >= 2 && lastDigit <= 4 && (lastTwo < 12 || lastTwo > 14)) {
    return `${count} aktywne wzorce`;
  }
  return `${count} aktywnych wzorców`;
}

export default function Home({ loaderData, actionData }: Route.ComponentProps) {
  const { repositories } = loaderData;
  const navigation = useNavigation();
  const submitting = navigation.state !== "idle" && navigation.formMethod === "POST";

  return (
    <main className="container mx-auto p-4">
      <h1 className="mb-6 text-2xl font-semibold">SecurityCheck Portal</h1>

      <section className="mb-8 rounded-2xl border border-gray-200 p-6 shadow-sm dark:border-gray-800">
        <h2 className="mb-4 text-lg font-semibold">Dodaj repozytorium</h2>
        <Form method="post" className="space-y-4" noValidate>
          <div className="space-y-1">
            <label htmlFor="url" className="block text-sm font-medium">
              Adres repozytorium (HTTPS)
            </label>
            <input
              id="url"
              name="url"
              type="url"
              placeholder="https://gitlab-do.coig.app/grupa/projekt.git"
              className="w-full rounded-lg border border-gray-300 bg-white px-3 py-2 dark:border-gray-700 dark:bg-gray-900"
            />
          </div>
          <div className="space-y-1">
            <label htmlFor="name" className="block text-sm font-medium">
              Nazwa (opcjonalnie)
            </label>
            <input
              id="name"
              name="name"
              type="text"
              maxLength={200}
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
            className="rounded-lg bg-blue-600 px-4 py-2 font-medium text-white hover:bg-blue-700 disabled:opacity-60"
          >
            Dodaj
          </button>
        </Form>
      </section>

      <h2 className="mb-4 text-lg font-semibold">Repozytoria</h2>
      {repositories.length === 0 ? (
        <p className="rounded-2xl border border-dashed border-gray-300 p-8 text-center text-gray-600 dark:border-gray-700 dark:text-gray-400">
          Nie dodano jeszcze żadnego repozytorium.
        </p>
      ) : (
        <ul className="divide-y divide-gray-200 rounded-2xl border border-gray-200 dark:divide-gray-800 dark:border-gray-800">
          {repositories.map((repository) => (
            <li key={repository.id}>
              <Link
                to={`/repos/${repository.id}`}
                className="flex flex-wrap items-center justify-between gap-2 p-4 hover:bg-gray-50 dark:hover:bg-gray-900"
              >
                <span className="min-w-0">
                  <span className="block font-medium text-blue-700 dark:text-blue-400">
                    {repositoryDisplayName(repository)}
                  </span>
                  <span className="block break-all text-sm text-gray-600 dark:text-gray-400">
                    {repository.url}
                  </span>
                </span>
                <span className="text-sm text-gray-700 dark:text-gray-300">
                  {patternCountLabel(repository.activePatternCount)}
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </main>
  );
}
