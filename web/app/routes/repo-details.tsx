import { useEffect, useRef, useState } from "react";
import { data, Form, Link, redirect, useNavigation, useSearchParams, useSubmit } from "react-router";

import type { Route } from "./+types/repo-details";
import { Alert } from "../components/alert";
import { Badge } from "../components/badge";
import { Button } from "../components/button";
import { ConfirmDialog } from "../components/confirm-dialog";
import { Field, Input } from "../components/field";
import { linkClass } from "../components/styles";
import { apiFetch } from "../lib/api";
import {
  describeEvent,
  describeResolution,
  formatDateTime,
  repositoryDisplayName,
  resolutionBadge,
  type AuditEvent,
  type RepositoryDetails,
  type VersionPattern,
} from "../lib/patterns";

const UNEXPECTED_ERROR = "Wystąpił nieoczekiwany błąd.";
const INVALID_PATTERN = "Wzorzec musi mieć postać X.Y.*, np. 2.1.*.";
const PATTERN_EXISTS = "Ten wzorzec już istnieje.";
const PATTERN_INACTIVE = "Ten wzorzec istnieje jako nieaktywny — pokaż nieaktywne i aktywuj go.";
const REPO_HAS_PATTERNS = "Najpierw usuń wszystkie wzorce tego repozytorium.";
const RESOLVE_INACTIVE = "Wzorzec jest nieaktywny — aktywuj go, aby sprawdzić.";

const SHOW_INACTIVE_PARAM = "nieaktywne";

type Intent = "addPattern" | "resolve" | "deactivate" | "activate" | "deletePattern" | "deleteRepo";

type ActionResult = { intent: Intent; error?: string };

export function meta({ loaderData }: Route.MetaArgs) {
  const title = loaderData ? repositoryDisplayName(loaderData.repository) : "Repozytorium";
  return [{ title: `${title} – SecurityCheck Portal` }];
}

function notFound(): never {
  throw data("Nie znaleziono", { status: 404 });
}

export async function clientLoader({ params, request }: Route.ClientLoaderArgs) {
  // Only numeric IDs exist; anything else would not match the API route.
  if (!/^\d+$/.test(params.repoId)) notFound();

  const init = { signal: request.signal };
  const [repositoryResponse, eventsResponse] = await Promise.all([
    apiFetch(`/api/repos/${params.repoId}`, init, request),
    apiFetch(`/api/repos/${params.repoId}/events`, init, request),
  ]);

  if (repositoryResponse.status === 404 || eventsResponse.status === 404) notFound();
  if (!repositoryResponse.ok) {
    throw new Error(`GET /api/repos/${params.repoId} failed with status ${repositoryResponse.status}`);
  }
  if (!eventsResponse.ok) {
    throw new Error(`GET /api/repos/${params.repoId}/events failed with status ${eventsResponse.status}`);
  }

  return {
    repository: (await repositoryResponse.json()) as RepositoryDetails,
    events: (await eventsResponse.json()) as AuditEvent[],
  };
}

async function readConflict(response: Response): Promise<string | undefined> {
  try {
    const body = (await response.json()) as { conflict?: string };
    return body.conflict;
  } catch {
    return undefined;
  }
}

async function errorMessage(intent: Intent, response: Response): Promise<string> {
  if (intent === "addPattern") {
    if (response.status === 400) return INVALID_PATTERN;
    if (response.status === 409) {
      return (await readConflict(response)) === "inactive" ? PATTERN_INACTIVE : PATTERN_EXISTS;
    }
  }
  if (intent === "deleteRepo" && response.status === 409) return REPO_HAS_PATTERNS;
  if (intent === "resolve" && response.status === 409) return RESOLVE_INACTIVE;
  return UNEXPECTED_ERROR;
}

function patternRequest(intent: Intent, patternId: string): [string, RequestInit] {
  const path = `/api/patterns/${encodeURIComponent(patternId)}`;
  switch (intent) {
    case "resolve":
    case "deactivate":
    case "activate":
      return [`${path}/${intent}`, { method: "POST" }];
    default:
      return [path, { method: "DELETE" }];
  }
}

export async function clientAction({ params, request }: Route.ClientActionArgs): Promise<ActionResult | Response> {
  const formData = await request.formData();
  const intent = String(formData.get("intent") ?? "") as Intent;
  const repoPath = `/api/repos/${encodeURIComponent(params.repoId)}`;

  let path: string;
  let init: RequestInit;
  switch (intent) {
    case "addPattern":
      path = `${repoPath}/patterns`;
      init = { method: "POST", body: JSON.stringify({ pattern: String(formData.get("pattern") ?? "").trim() }) };
      break;
    case "resolve":
    case "deactivate":
    case "activate":
    case "deletePattern":
      [path, init] = patternRequest(intent, String(formData.get("patternId") ?? ""));
      break;
    case "deleteRepo":
      path = repoPath;
      init = { method: "DELETE" };
      break;
    default:
      return { intent, error: UNEXPECTED_ERROR };
  }

  let response: Response;
  try {
    response = await apiFetch(path, init, request);
  } catch (error) {
    // A thrown Response is the login redirect after a 401.
    if (error instanceof Response) throw error;
    return { intent, error: UNEXPECTED_ERROR };
  }

  if (response.ok) {
    return intent === "deleteRepo" ? redirect("/") : { intent };
  }

  return { intent, error: await errorMessage(intent, response) };
}

type PendingDelete = {
  invoker?: HTMLElement;
  intent: "deletePattern" | "deleteRepo";
  patternId?: number;
  title: string;
  description: string;
};

function PatternRow({
  pattern,
  busy,
  onRequestDelete,
}: {
  pattern: VersionPattern;
  busy: string | null;
  onRequestDelete: (pending: PendingDelete) => void;
}) {
  const result = describeResolution(pattern);
  const badge = resolutionBadge(pattern);
  const isBusy = (intent: Intent) => busy === `${intent}:${pattern.id}`;
  const disabled = busy !== null;

  return (
    <li className="flex flex-wrap items-start justify-between gap-4 p-4">
      <div className="min-w-0 space-y-1">
        <div className="flex items-center gap-2">
          <span className="font-mono font-medium">{pattern.pattern}</span>
          {!pattern.isActive && <Badge kind="neutral">nieaktywny</Badge>}
        </div>
        <div aria-live="polite" className="flex flex-wrap items-center gap-2 text-sm">
          <Badge kind={badge.kind}>{badge.label}</Badge>
          <span title={result.title}>
            {result.text}
            {result.title && <span className="sr-only"> (pełny commit: {result.title})</span>}
          </span>
          {isBusy("resolve") && <span className="text-text-muted">Sprawdzanie…</span>}
        </div>
        {result.checkedAt && <p className="text-xs text-text-muted">{result.checkedAt}</p>}
      </div>
      <div className="flex flex-wrap gap-2">
        {pattern.isActive ? (
          <>
            <Form method="post">
              <input type="hidden" name="patternId" value={pattern.id} />
              <Button type="submit" variant="secondary" name="intent" value="resolve" disabled={disabled}>
                {isBusy("resolve") ? "Sprawdzanie…" : "Sprawdź"}
              </Button>
            </Form>
            <Form method="post">
              <input type="hidden" name="patternId" value={pattern.id} />
              <Button type="submit" variant="secondary" name="intent" value="deactivate" disabled={disabled}>
                Dezaktywuj
              </Button>
            </Form>
          </>
        ) : (
          <Form method="post">
            <input type="hidden" name="patternId" value={pattern.id} />
            <Button type="submit" variant="secondary" name="intent" value="activate" disabled={disabled}>
              Aktywuj
            </Button>
          </Form>
        )}
        <Button
          variant="danger"
          disabled={disabled}
          onClick={(event) =>
            onRequestDelete({
              invoker: event.currentTarget,
              intent: "deletePattern",
              patternId: pattern.id,
              title: `Usunąć wzorzec ${pattern.pattern}?`,
              description: "Wzorzec zostanie trwale usunięty z repozytorium.",
            })
          }
        >
          Usuń
        </Button>
      </div>
    </li>
  );
}

export default function RepoDetails({ loaderData, actionData }: Route.ComponentProps) {
  const { repository, events } = loaderData;
  const [searchParams] = useSearchParams();
  const navigation = useNavigation();
  const submit = useSubmit();
  const addPatternForm = useRef<HTMLFormElement>(null);
  const patternsHeading = useRef<HTMLHeadingElement>(null);
  const [pendingDelete, setPendingDelete] = useState<PendingDelete | null>(null);
  // Keep the last dialog texts while the dialog closes.
  const [dialogTexts, setDialogTexts] = useState({ title: "", description: "" });

  const showInactive = searchParams.get(SHOW_INACTIVE_PARAM) === "1";
  const inactiveCount = repository.patterns.filter((p) => !p.isActive).length;
  const visiblePatterns = showInactive ? repository.patterns : repository.patterns.filter((p) => p.isActive);

  // "<intent>:<patternId>" of the submission in flight, to disable buttons and show progress.
  const submitting = navigation.state !== "idle" && navigation.formMethod === "POST";
  const busy = submitting
    ? `${navigation.formData?.get("intent") ?? ""}:${navigation.formData?.get("patternId") ?? ""}`
    : null;

  const addPatternError = actionData?.intent === "addPattern" ? actionData.error : undefined;
  const otherError = actionData && actionData.intent !== "addPattern" ? actionData.error : undefined;

  useEffect(() => {
    if (actionData?.intent === "addPattern" && !actionData.error) {
      addPatternForm.current?.reset();
    }
  }, [actionData]);

  function requestDelete(pending: PendingDelete) {
    setDialogTexts({ title: pending.title, description: pending.description });
    setPendingDelete(pending);
  }

  function confirmDelete() {
    const pending = pendingDelete;
    setPendingDelete(null);
    if (!pending) return;
    const fields: Record<string, string> = { intent: pending.intent };
    if (pending.patternId !== undefined) fields.patternId = String(pending.patternId);
    submit(fields, { method: "post" });
  }

  const toggleParams = new URLSearchParams(searchParams);
  if (showInactive) toggleParams.delete(SHOW_INACTIVE_PARAM);
  else toggleParams.set(SHOW_INACTIVE_PARAM, "1");
  const toggleSearch = toggleParams.toString();

  return (
    <main className="container mx-auto space-y-8 p-4">
      <div>
        <Link to="/" className={`text-sm ${linkClass}`}>
          ← Repozytoria
        </Link>
        <div className="mt-2 flex flex-wrap items-start justify-between gap-4">
          <div className="min-w-0">
            <h1 className="text-2xl font-semibold">{repositoryDisplayName(repository)}</h1>
            <p className="break-all text-sm text-text-muted">{repository.url}</p>
            <p className="text-xs text-text-muted">
              Dodano {formatDateTime(repository.createdAt)} przez {repository.createdBy}
            </p>
          </div>
          <Button
            variant="danger"
            disabled={submitting}
            onClick={(event) =>
              requestDelete({
                invoker: event.currentTarget,
                intent: "deleteRepo",
                title: `Usunąć repozytorium ${repositoryDisplayName(repository)}?`,
                description: "Repozytorium zostanie trwale usunięte.",
              })
            }
          >
            Usuń repozytorium
          </Button>
        </div>
      </div>

      {otherError && <Alert>{otherError}</Alert>}

      <section className="rounded-2xl border border-border-subtle bg-surface-raised shadow-sm">
        <div className="flex flex-wrap items-center justify-between gap-2 border-b border-border-subtle p-4">
          <h2 ref={patternsHeading} tabIndex={-1} className="text-lg font-semibold focus:outline-none">
            Wzorce wersji
          </h2>
          <Link to={{ search: toggleSearch ? `?${toggleSearch}` : "" }} replace className={`text-sm ${linkClass}`}>
            {showInactive ? "Ukryj nieaktywne" : `Pokaż nieaktywne (${inactiveCount})`}
          </Link>
        </div>

        {visiblePatterns.length === 0 ? (
          <p className="p-4 text-sm text-text-muted">
            {showInactive ? "To repozytorium nie ma jeszcze wzorców." : "Brak aktywnych wzorców."}
          </p>
        ) : (
          <ul className="divide-y divide-border-subtle">
            {visiblePatterns.map((pattern) => (
              <PatternRow key={pattern.id} pattern={pattern} busy={busy} onRequestDelete={requestDelete} />
            ))}
          </ul>
        )}

        <Form method="post" ref={addPatternForm} noValidate className="border-t border-border-subtle p-4">
          <Field label="Nowy wzorzec" error={addPatternError}>
            {(control) => (
              <div className="flex flex-wrap gap-2">
                <Input name="pattern" type="text" placeholder="2.1.*" mono className="w-40" {...control} />
                <Button type="submit" name="intent" value="addPattern" disabled={submitting}>
                  {busy === "addPattern:" ? "Dodawanie…" : "Dodaj wzorzec"}
                </Button>
              </div>
            )}
          </Field>
        </Form>
      </section>

      <section>
        <h2 className="mb-4 text-lg font-semibold">Historia zmian</h2>
        {events.length === 0 ? (
          <p className="text-sm text-text-muted">Brak zdarzeń.</p>
        ) : (
          <ul className="divide-y divide-border-subtle rounded-2xl border border-border-subtle bg-surface-raised">
            {events.map((event) => (
              <li key={event.id} className="flex flex-wrap gap-x-4 gap-y-1 p-3 text-sm">
                <time dateTime={event.occurredAt} className="text-text-muted">
                  {formatDateTime(event.occurredAt)}
                </time>
                <span className="font-medium">{event.actor}</span>
                <span>{describeEvent(event)}</span>
              </li>
            ))}
          </ul>
        )}
      </section>

      <ConfirmDialog
        open={pendingDelete !== null}
        title={dialogTexts.title}
        description={dialogTexts.description}
        confirmLabel="Usuń"
        onConfirm={confirmDelete}
        onCancel={() => setPendingDelete(null)}
        invoker={pendingDelete?.invoker}
        // The invoking button is disabled or removed once the delete runs; keep focus on a stable heading.
        focusAfterConfirm={() => patternsHeading.current}
      />
    </main>
  );
}
