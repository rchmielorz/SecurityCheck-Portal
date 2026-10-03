/** Shapes of the /api/patterns/{id}/scans and /api/scans/{id} responses (api/Scans/ScanContracts.cs). */

import type { BadgeKind } from "../components/badge";
import { formatDateTime } from "./patterns";

export type ScanStatus = "Queued" | "Running" | "Completed" | "Incomplete" | "Failed";

export type ScanFailureReason =
  | "PatternNotResolved"
  | "GitFailed"
  | "CommitMismatch"
  | "ScannerUnavailable"
  | "DatabaseTooOld"
  | "Timeout"
  | "ScannerFailed"
  | "Interrupted";

export type FindingSeverity = "Critical" | "High" | "Medium" | "Low" | "Unknown";

/** ScanResponse (api/Scans/ScanContracts.cs): the body of 202 on requesting a scan. */
export type ScanRequest = {
  id: number;
  patternId: number;
  repositoryId: number;
  status: ScanStatus;
};

/** LatestScanResponse (api/Repositories/RepositoryContracts.cs): the newest scan of a pattern. */
export type LatestScan = {
  id: number;
  status: ScanStatus;
  finishedAt: string | null;
  findingsCount: number;
  scannedTag: string | null;
};

/** ScanFindingResponse (api/Scans/ScanContracts.cs). */
export type ScanFinding = {
  library: string;
  installedVersion: string;
  vulnerabilityId: string;
  severity: FindingSeverity;
  fixedVersion: string | null;
  title: string | null;
  targets: string[];
};

/** ScanDetails (api/Scans/ScanContracts.cs); findings arrive already sorted by severity. */
export type ScanDetails = {
  id: number;
  patternId: number;
  repositoryId: number;
  repositoryUrl: string;
  pattern: string;
  status: ScanStatus;
  failureReason: ScanFailureReason | null;
  failureDetail: string | null;
  requestedBy: string;
  requestedAt: string;
  startedAt: string | null;
  finishedAt: string | null;
  scannedTag: string | null;
  scannedCommit: string | null;
  trivyVersion: string | null;
  trivyDbUpdatedAt: string | null;
  missingLockFiles: string[];
  findings: ScanFinding[];
};

/** Body of a 409 on requesting a scan (ScanConflict in api/Scans/ScanContracts.cs). */
export type ScanConflict = {
  reason: "active" | "inactive";
  scanId?: number | null;
};

/** A queued or running scan is still in progress and should be refreshed. */
export function isScanInProgress(status: ScanStatus): boolean {
  return status === "Queued" || status === "Running";
}

/** A scan queued for longer than this suggests that the worker is not running. */
export const QUEUED_WARNING_MS = 2 * 60 * 1000;

/** Polish age of a timestamp in whole days: "dziś", "1 dzień temu", "N dni temu"; "" for an unparseable timestamp. */
export function describeAgeDays(iso: string, now = Date.now()): string {
  const rawDays = Math.floor((now - new Date(iso).getTime()) / (24 * 60 * 60 * 1000));
  if (!Number.isFinite(rawDays)) return "";
  const days = Math.max(0, rawDays);
  if (days === 0) return "dziś";
  if (days === 1) return "1 dzień temu";
  return `${days} dni temu`;
}

/** Polish count label for vulnerabilities, e.g. "1 podatność", "3 podatności", "5 podatności". */
export function findingsCountLabel(count: number): string {
  if (count === 1) return "1 podatność";
  return `${count} podatności`;
}

export type ScanBadge = { kind: BadgeKind; label: string };

/** Badge for the status of a scan; text and icon, not color alone. */
export function scanStatusBadge(status: ScanStatus, findingsCount = 0): ScanBadge {
  switch (status) {
    case "Queued":
      return { kind: "info", label: "W kolejce" };
    case "Running":
      return { kind: "info", label: "W trakcie" };
    case "Completed":
      return findingsCount === 0
        ? { kind: "success", label: "Brak podatności" }
        : { kind: "danger", label: findingsCountLabel(findingsCount) };
    case "Incomplete":
      return { kind: "warning", label: "Niepełny" };
    case "Failed":
      return { kind: "danger", label: "Błąd skanu" };
    default:
      return { kind: "neutral", label: status };
  }
}

/** One-line summary shown next to the badge on the pattern row. */
export function describeLatestScan(scan: LatestScan): string {
  const parts: string[] = [];
  if (scan.scannedTag) parts.push(`przeskanowano ${scan.scannedTag}`);
  if (scan.finishedAt) parts.push(formatDateTime(scan.finishedAt));
  if (scan.status === "Incomplete" && scan.findingsCount > 0) parts.push(findingsCountLabel(scan.findingsCount));
  return parts.join(" · ");
}

/** Polish explanation of why a scan failed. */
export function describeFailureReason(reason: ScanFailureReason | null): string {
  switch (reason) {
    case "PatternNotResolved":
      return "Nie udało się ustalić tagu pasującego do wzorca. Sprawdź wzorzec i dostępność serwera Git.";
    case "GitFailed":
      return "Nie udało się pobrać kodu z serwera Git (serwer niedostępny, brak uprawnień lub przekroczony czas).";
    case "CommitMismatch":
      return "Pobrany kod nie odpowiada ustalonemu commitowi (tag mógł zostać przesunięty). Spróbuj ponownie.";
    case "ScannerUnavailable":
      return "Skaner Trivy jest niedostępny lub ma niewłaściwą wersję. Skontaktuj się z administratorem.";
    case "DatabaseTooOld":
      return "Baza podatności Trivy jest zbyt stara i nie udało się jej zaktualizować. Skontaktuj się z administratorem.";
    case "Timeout":
      return "Skan przekroczył dopuszczalny czas i został przerwany.";
    case "ScannerFailed":
      return "Skaner Trivy zakończył się błędem.";
    case "Interrupted":
      return "Skan został przerwany (restart workera). Uruchom go ponownie.";
    default:
      return "Skan zakończył się błędem.";
  }
}
