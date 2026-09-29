import { Link } from "react-router";

import { patternCountLabel, repositoryDisplayName, type RepositorySummary } from "../lib/patterns";
import { Badge } from "./badge";
import { cx, focusRing } from "./styles";

function Chevron() {
  return (
    <svg
      width="16"
      height="16"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      className="shrink-0 text-text-subtle"
    >
      <path d="M9 6l6 6-6 6" />
    </svg>
  );
}

// Presentational list of repositories (no data fetching); includes the empty state.
export function RepositoryList({ repositories }: { repositories: RepositorySummary[] }) {
  if (repositories.length === 0) {
    return (
      <p className="rounded-2xl border border-dashed border-border p-8 text-center text-text-muted">
        Nie dodano jeszcze żadnego repozytorium.
      </p>
    );
  }

  return (
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
            <span className="flex items-center gap-2">
              {repository.activePatternCount > 0 ? (
                <Badge kind="neutral">{patternCountLabel(repository.activePatternCount)}</Badge>
              ) : (
                <Badge kind="warning">Brak aktywnych wzorców</Badge>
              )}
              <Chevron />
            </span>
          </Link>
        </li>
      ))}
    </ul>
  );
}
