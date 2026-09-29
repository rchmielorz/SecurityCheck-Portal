import { Form, redirect, useNavigation } from "react-router";

import type { Route } from "./+types/home";
import { Alert } from "../components/alert";
import { Button } from "../components/button";
import { Card } from "../components/card";
import { Field, Input } from "../components/field";
import { PageHeading, SectionHeading } from "../components/headings";
import { RepositoryList } from "../components/repository-list";
import { linkClass } from "../components/styles";
import { apiFetch } from "../lib/api";
import { type RepositoryDetails, type RepositorySummary } from "../lib/patterns";

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
    // `nowy=1` makes the details page focus the new-pattern field once (see repo-details.tsx).
    return redirect(`/repos/${repository.id}?nowy=1`);
  }

  return { error: ERROR_MESSAGES[response.status] ?? UNEXPECTED_ERROR };
}

export default function Home({ loaderData, actionData }: Route.ComponentProps) {
  const { repositories } = loaderData;
  const navigation = useNavigation();
  const submitting = navigation.state !== "idle" && navigation.formMethod === "POST";

  return (
    <main className="container mx-auto space-y-8 p-4">
      <PageHeading
        action={
          <a href="#dodaj-repozytorium" className={linkClass}>
            Dodaj repozytorium
          </a>
        }
      >
        Repozytoria
      </PageHeading>

      <RepositoryList repositories={repositories} />

      <Card id="dodaj-repozytorium">
        <div className="mb-4">
          <SectionHeading>Dodaj repozytorium</SectionHeading>
        </div>
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
    </main>
  );
}
