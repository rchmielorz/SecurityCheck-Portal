import { Form, redirect, replace, useNavigation, useSearchParams } from "react-router";

import type { Route } from "./+types/login";
import { Alert } from "../components/alert";
import { BrandMark } from "../components/brand-mark";
import { Button } from "../components/button";
import { Card } from "../components/card";
import { Field, Input } from "../components/field";
import { ThemeToggle } from "../components/theme-toggle";
import { apiFetch, safeNext } from "../lib/api";

const ERROR_MESSAGES: Record<number, string> = {
  400: "Podaj login i hasło.",
  401: "Nieprawidłowy login lub hasło.",
  403: "Twoje konto nie ma dostępu do portalu.",
  503: "Usługa logowania jest niedostępna. Spróbuj później.",
  429: "Zbyt wiele prób logowania. Spróbuj ponownie później.",
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
      <div className="absolute right-4 top-4">
        <ThemeToggle />
      </div>
      <Card className="w-full max-w-sm p-8">
        <h1 className="mb-6 flex items-center justify-center gap-2 text-center text-2xl font-semibold">
          <BrandMark size={28} className="text-primary" />
          <span>SecurityCheck Portal</span>
        </h1>
        <Form method="post" className="space-y-4" noValidate>
          {next !== null && <input type="hidden" name="next" value={next} />}
          <Field label="Login">
            {(control) => <Input name="userName" type="text" autoComplete="username" autoFocus {...control} />}
          </Field>
          <Field label="Hasło">
            {(control) => <Input name="password" type="password" autoComplete="current-password" {...control} />}
          </Field>
          {actionData?.error && <Alert>{actionData.error}</Alert>}
          <Button type="submit" disabled={submitting} className="w-full">
            Zaloguj
          </Button>
        </Form>
      </Card>
    </main>
  );
}
