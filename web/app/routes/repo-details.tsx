import { useEffect, useRef } from "react";
import { data, Form, Link, redirect, useNavigation, useSearchParams } from "react-router";

import type { Route } from "./+types/repo-details";
import { apiFetch } from "../lib/api";
import {
  describeEvent,
  describeResolution,
  formatDateTime,
  repositoryDisplayName,
  type AuditEvent,
  type RepositoryDetails,
  type VersionPattern,
} from "../lib/patterns";

const UNEXPECTED_ERROR = "Wystąpił nieoczekiwany błąd.";
const INVALID_PATTERN = "Wzorzec musi mieć postać X.Y.*, np. 2.1.*.";
const PATTERN_EXISTS = "Ten wzorzec już istnieje.";
const PATTERN_INACTIVE = "Ten wzorzec istnieje jako nieaktywny — pokaż nieaktywne i aktywuj go.";
const REPO_HAS_PATTERNS = "Najpierw usuń wszystkie wzorce tego repozytorium.";

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

function confirmSubmit(message: string) {
  return (event: React.FormEvent<HTMLFormElement>) => {
    if (!window.confirm(message)) event.preventDefault();
  };
}

const secondaryButton =
  "rounded-lg border border-gray-300 px-3 py-1.5 text-sm font-medium hover:bg-gray-100 disabled:opacity-60 dark:border-gray-700 dark:hover:bg-gray-900";
const dangerButton =
  "rounded-lg border border-red-300 px-3 py-1.5 text-sm font-medium text-red-700 hover:bg-red-50 disabled:opacity-60 dark:border-red-800 dark:text-red-400 dark:hover:bg-red-950";

function PatternRow({ pattern, busy }: { pattern: VersionPattern; busy: string | null }) {
  const result = describeResolution(pattern);
  const isBusy = (intent: Intent) => busy === `${intent}:${pattern.id}`;
  const disabled = busy !== null;

  return (
    <li className="flex flex-wrap items-start justify-between gap-4 p-4">
      <div className="min-w-0 space-y-1">
        <div className="flex items-center gap-2">
          <span className="font-mono font-medium">{pattern.pattern}</span>
          {!pattern.isActive && (
            <span className="rounded-full bg-gray-200 px-2 py-0.5 text-xs text-gray-700 dark:bg-gray-800 dark:text-gray-300">
              nieaktywny
            </span>
          )}
        </div>
        <p className="text-sm" title={result.title}>
          {result.text}
        </p>
        {result.checkedAt && <p className="text-xs text-gray-600 dark:text-gray-400">{result.checkedAt}</p>}
      </div>
      <div className="flex flex-wrap gap-2">
        {pattern.isActive ? (
          <>
            <Form method="post">
              <input type="hidden" name="patternId" value={pattern.id} />
              <button type="submit" name="intent" value="resolve" disabled={disabled} className={secondaryButton}>
                {isBusy("resolve") ? "Sprawdzanie…" : "Sprawdź"}
              </button>
            </Form>
            <Form method="post">
              <input type="hidden" name="patternId" value={pattern.id} />
              <button type="submit" name="intent" value="deactivate" disabled={disabled} className={secondaryButton}>
                Dezaktywuj
              </button>
            </Form>
          </>
        ) : (
          <Form method="post">
            <input type="hidden" name="patternId" value={pattern.id} />
            <button type="submit" name="intent" value="activate" disabled={disabled} className={secondaryButton}>
              Aktywuj
            </button>
          </Form>
        )}
        <Form method="post" onSubmit={confirmSubmit(`Usunąć wzorzec ${pattern.pattern}?`)}>
          <input type="hidden" name="patternId" value={pattern.id} />
          <button type="submit" name="intent" value="deletePattern" disabled={disabled} className={dangerButton}>
            Usuń
          </button>
        </Form>
      </div>
    </li>
  );
}

export default function RepoDetails({ loaderData, actionData }: Route.ComponentProps) {
  const { repository, events } = loaderData;
  const [searchParams] = useSearchParams();
  const navigation = useNavigation();
  const addPatternForm = useRef<HTMLFormElement>(null);

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

  const toggleParams = new URLSearchParams(searchParams);
  if (showInactive) toggleParams.delete(SHOW_INACTIVE_PARAM);
  else toggleParams.set(SHOW_INACTIVE_PARAM, "1");
  const toggleSearch = toggleParams.toString();

  return (
    <main className="container mx-auto space-y-8 p-4">
      <div>
        <Link to="/" className="text-sm text-blue-700 hover:underline dark:text-blue-400">
          ← Repozytoria
        </Link>
        <div className="mt-2 flex flex-wrap items-start justify-between gap-4">
          <div className="min-w-0">
            <h1 className="text-2xl font-semibold">{repositoryDisplayName(repository)}</h1>
            <p className="break-all text-sm text-gray-600 dark:text-gray-400">{repository.url}</p>
            <p className="text-xs text-gray-600 dark:text-gray-400">
              Dodano {formatDateTime(repository.createdAt)} przez {repository.createdBy}
            </p>
          </div>
          <Form
            method="post"
            onSubmit={confirmSubmit(`Usunąć repozytorium ${repositoryDisplayName(repository)}?`)}
          >
            <button type="submit" name="intent" value="deleteRepo" disabled={submitting} className={dangerButton}>
              Usuń repozytorium
            </button>
          </Form>
        </div>
      </div>

      {otherError && (
        <p role="alert" className="text-sm text-red-600 dark:text-red-400">
          {otherError}
        </p>
      )}

      <section className="rounded-2xl border border-gray-200 shadow-sm dark:border-gray-800">
        <div className="flex flex-wrap items-center justify-between gap-2 border-b border-gray-200 p-4 dark:border-gray-800">
          <h2 className="text-lg font-semibold">Wzorce wersji</h2>
          <Link
            to={{ search: toggleSearch ? `?${toggleSearch}` : "" }}
            replace
            className="text-sm text-blue-700 hover:underline dark:text-blue-400"
          >
            {showInactive ? "Ukryj nieaktywne" : `Pokaż nieaktywne (${inactiveCount})`}
          </Link>
        </div>

        {visiblePatterns.length === 0 ? (
          <p className="p-4 text-sm text-gray-600 dark:text-gray-400">
            {showInactive ? "To repozytorium nie ma jeszcze wzorców." : "Brak aktywnych wzorców."}
          </p>
        ) : (
          <ul className="divide-y divide-gray-200 dark:divide-gray-800">
            {visiblePatterns.map((pattern) => (
              <PatternRow key={pattern.id} pattern={pattern} busy={busy} />
            ))}
          </ul>
        )}

        <Form
          method="post"
          ref={addPatternForm}
          noValidate
          className="space-y-2 border-t border-gray-200 p-4 dark:border-gray-800"
        >
          <label htmlFor="pattern" className="block text-sm font-medium">
            Nowy wzorzec
          </label>
          <div className="flex flex-wrap gap-2">
            <input
              id="pattern"
              name="pattern"
              type="text"
              placeholder="2.1.*"
              className="w-40 rounded-lg border border-gray-300 bg-white px-3 py-2 font-mono dark:border-gray-700 dark:bg-gray-900"
            />
            <button
              type="submit"
              name="intent"
              value="addPattern"
              disabled={submitting}
              className="rounded-lg bg-blue-600 px-4 py-2 font-medium text-white hover:bg-blue-700 disabled:opacity-60"
            >
              {busy === "addPattern:" ? "Dodawanie…" : "Dodaj wzorzec"}
            </button>
          </div>
          {addPatternError && (
            <p role="alert" className="text-sm text-red-600 dark:text-red-400">
              {addPatternError}
            </p>
          )}
        </Form>
      </section>

      <section>
        <h2 className="mb-4 text-lg font-semibold">Historia zmian</h2>
        {events.length === 0 ? (
          <p className="text-sm text-gray-600 dark:text-gray-400">Brak zdarzeń.</p>
        ) : (
          <ul className="divide-y divide-gray-200 rounded-2xl border border-gray-200 dark:divide-gray-800 dark:border-gray-800">
            {events.map((event) => (
              <li key={event.id} className="flex flex-wrap gap-x-4 gap-y-1 p-3 text-sm">
                <time dateTime={event.occurredAt} className="text-gray-600 dark:text-gray-400">
                  {formatDateTime(event.occurredAt)}
                </time>
                <span className="font-medium">{event.actor}</span>
                <span>{describeEvent(event)}</span>
              </li>
            ))}
          </ul>
        )}
      </section>
    </main>
  );
}
