/** Shapes of the /api/repos and /api/patterns responses (api/Repositories/RepositoryContracts.cs). */

export type RepositorySummary = {
  id: number;
  url: string;
  name: string | null;
  activePatternCount: number;
};

export type ResolutionState = "Resolved" | "NoMatch" | "Ambiguous" | "Error";

export type Resolution = {
  state: ResolutionState;
  tag: string | null;
  commit: string | null;
  resolvedAt: string | null;
};

export type VersionPattern = {
  id: number;
  repositoryId: number;
  pattern: string;
  isActive: boolean;
  createdAt: string;
  createdBy: string;
  lastResolution: Resolution | null;
};

export type RepositoryDetails = {
  id: number;
  url: string;
  name: string | null;
  createdAt: string;
  createdBy: string;
  patterns: VersionPattern[];
};

export type AuditAction =
  | "RepositoryAdded"
  | "RepositoryDeleted"
  | "PatternAdded"
  | "PatternDeleted"
  | "PatternActivated"
  | "PatternDeactivated";

export type AuditEvent = {
  id: number;
  occurredAt: string;
  actor: string;
  action: AuditAction;
  pattern: string | null;
};

/** Local date and time, e.g. "28.09.2026, 14:05:12". */
export function formatDateTime(value: string): string {
  return new Date(value).toLocaleString("pl-PL");
}

/**
 * Name shown for a repository: its name, else the URL path without host and
 * ".git" (e.g. "oe/zespol-dotnet/pineapple").
 */
export function repositoryDisplayName(repository: { url: string; name: string | null }): string {
  if (repository.name) return repository.name;
  try {
    const path = new URL(repository.url).pathname.replace(/^\/+/, "").replace(/\.git$/, "");
    return path || repository.url;
  } catch {
    return repository.url;
  }
}

export type ResolutionView = {
  /** Main line of the result. */
  text: string;
  /** Tooltip for the main line (the full commit SHA of a resolved pattern). */
  title?: string;
  /** Secondary line: when the pattern was last checked. */
  checkedAt?: string;
};

export function describeResolution(pattern: VersionPattern): ResolutionView {
  const resolution = pattern.lastResolution;
  if (!resolution) return { text: "Nie sprawdzono" };

  const checkedAt = resolution.resolvedAt ? `sprawdzono ${formatDateTime(resolution.resolvedAt)}` : undefined;

  switch (resolution.state) {
    case "Resolved": {
      const commit = resolution.commit ?? "";
      return {
        text: `${resolution.tag ?? ""} · commit ${commit.slice(0, 7)}`,
        title: commit || undefined,
        checkedAt,
      };
    }
    case "NoMatch":
      return { text: `Brak tagu pasującego do ${pattern.pattern}`, checkedAt };
    case "Ambiguous":
      return { text: "Wynik niejednoznaczny — sprawdź tagi w repozytorium", checkedAt };
    case "Error":
      return { text: "Nie udało się sprawdzić (serwer Git niedostępny lub przekroczony czas)", checkedAt };
    default:
      return { text: "Nie sprawdzono" };
  }
}

/** Polish description of a change log entry, e.g. "dodanie wzorca 2.1.*". */
export function describeEvent(event: AuditEvent): string {
  const pattern = event.pattern ?? "";
  switch (event.action) {
    case "RepositoryAdded":
      return "dodanie repozytorium";
    case "RepositoryDeleted":
      return "usunięcie repozytorium";
    case "PatternAdded":
      return `dodanie wzorca ${pattern}`;
    case "PatternDeleted":
      return `usunięcie wzorca ${pattern}`;
    case "PatternActivated":
      return `aktywacja wzorca ${pattern}`;
    case "PatternDeactivated":
      return `dezaktywacja wzorca ${pattern}`;
    default:
      return event.action;
  }
}
