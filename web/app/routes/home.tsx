import { Form, Link, redirect, useNavigation } from "react-router";

import type { Route } from "./+types/home";
import { Alert } from "../components/alert";
import { Button } from "../components/button";
import { Card } from "../components/card";
import { Field, Input } from "../components/field";
import { cx, focusRing } from "../components/styles";
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
      <h1 className="sr-only">Panel repozytoriów</h1>
      <Card className="mb-8">
        <h2 className="mb-4 text-lg font-semibold">Dodaj repozytorium</h2>
        <Form method="post" className="space-y-4" noValidate>
          <Field label="Adres repozytorium (HTTPS)">
            {(control) => (
              <Input
                name="url"
                type="url"
                placeholder="https://gitlab-do.coig.app/grupa/projekt.git"
                {...control}
              />
            )}
          </Field>
          <Field label="Nazwa (opcjonalnie)">
            {(control) => <Input name="name" type="text" maxLength={200} {...control} />}
          </Field>
          {actionData?.error && <Alert>{actionData.error}</Alert>}
          <Button type="submit" disabled={submitting}>
            Dodaj
          </Button>
        </Form>
      </Card>

      <h2 className="mb-4 text-lg font-semibold">Repozytoria</h2>
      {repositories.length === 0 ? (
        <p className="rounded-2xl border border-dashed border-border p-8 text-center text-text-muted">
          Nie dodano jeszcze żadnego repozytorium.
        </p>
      ) : (
        <ul className="divide-y divide-border-subtle rounded-2xl border border-border-subtle bg-surface-raised">
          {repositories.map((repository) => (
            <li key={repository.id}>
              <Link
                to={`/repos/${repository.id}`}
                className={cx(
                  "flex flex-wrap items-center justify-between gap-2 p-4 hover:bg-surface-hover",
                  focusRing,
                )}
              >
                <span className="min-w-0">
                  <span className="block font-medium text-primary-text">{repositoryDisplayName(repository)}</span>
                  <span className="block break-all text-sm text-text-muted">{repository.url}</span>
                </span>
                <span className="text-sm text-text-muted">{patternCountLabel(repository.activePatternCount)}</span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </main>
  );
}
