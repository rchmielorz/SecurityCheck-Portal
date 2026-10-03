import { useEffect, useState } from "react";
import { data, Link, useRevalidator } from "react-router";

import type { Route } from "./+types/scan-details";
import { Alert } from "../components/alert";
import { Badge } from "../components/badge";
import { PageHeading, SectionHeading } from "../components/headings";
import { linkClass } from "../components/styles";
import { VulnerabilityList } from "../components/vulnerability-list";
import { apiFetch } from "../lib/api";
import { formatDateTime, repositoryDisplayName } from "../lib/patterns";
import {
  describeAgeDays,
  describeFailureReason,
  findingsCountLabel,
  isScanInProgress,
  QUEUED_WARNING_MS,
  scanStatusBadge,
  type ScanDetails,
} from "../lib/scan";

const REFRESH_INTERVAL_MS = 3000;

export function meta({ loaderData }: Route.MetaArgs) {
  const pattern = loaderData ? ` ${loaderData.scan.pattern}` : "";
  return [{ title: `Skan${pattern} – SecurityCheck Portal` }];
}

function notFound(): never {
  throw data("Nie znaleziono", { status: 404 });
}

export async function clientLoader({ params, request }: Route.ClientLoaderArgs) {
  // Only numeric IDs exist; anything else would not match the API route.
  if (!/^\d+$/.test(params.repoId) || !/^\d+$/.test(params.scanId)) notFound();

  // A 401 throws a redirect to /login (apiFetch), like in the other routes.
  const response = await apiFetch(`/api/scans/${params.scanId}`, { signal: request.signal }, request);
  if (response.status === 404) notFound();
  if (!response.ok) {
    throw new Error(`GET /api/scans/${params.scanId} failed with status ${response.status}`);
  }

  const scan = (await response.json()) as ScanDetails;
  // The scan belongs to one repository; a mismatching URL is not a valid address of it.
  if (String(scan.repositoryId) !== params.repoId) notFound();
  return { scan };
}

function Metadata({ scan }: { scan: ScanDetails }) {
  const dbAge = scan.trivyDbUpdatedAt ? describeAgeDays(scan.trivyDbUpdatedAt) : "";
  const items: Array<[string, string | null]> = [
    ["Przeskanowany tag", scan.scannedTag],
    ["Commit", scan.scannedCommit ? scan.scannedCommit.slice(0, 7) : null],
    ["Zakończono", scan.finishedAt ? formatDateTime(scan.finishedAt) : null],
    [
      "Baza Trivy z",
      scan.trivyDbUpdatedAt
        ? `${formatDateTime(scan.trivyDbUpdatedAt)}${dbAge ? ` (${dbAge})` : ""}`
        : null,
    ],
    ["Wersja Trivy", scan.trivyVersion],
  ];
  const known = items.filter((item): item is [string, string] => item[1] !== null);
  if (known.length === 0) return null;

  return (
    <dl className="grid gap-x-6 gap-y-2 text-sm sm:grid-cols-2 lg:grid-cols-3">
      {known.map(([label, value]) => (
        <div key={label}>
          <dt className="text-xs text-text-muted">{label}</dt>
          <dd className="break-all font-mono" title={label === "Commit" ? (scan.scannedCommit ?? undefined) : undefined}>
            {value}
          </dd>
        </div>
      ))}
    </dl>
  );
}

function Results({ scan }: { scan: ScanDetails }) {
  const { status, findings } = scan;

  if (isScanInProgress(status)) {
    return (
      <p className="text-sm text-text-muted">
        {status === "Queued"
          ? "Skan czeka w kolejce na worker."
          : "Skan jest w trakcie. Wyniki pojawią się po jego zakończeniu."}
      </p>
    );
  }

  if (status === "Failed") {
    return (
      <Alert>
        <span>
          <span className="block font-medium">Skan zakończył się błędem.</span>
          <span className="block">{describeFailureReason(scan.failureReason)}</span>
          {scan.failureDetail && <span className="mt-1 block break-all font-mono text-xs">{scan.failureDetail}</span>}
        </span>
      </Alert>
    );
  }

  if (status === "Incomplete") {
    return (
      <div className="space-y-4">
        <Alert variant="warning">
          <span>
            <span className="block font-medium">Skan niepełny.</span>
            <span className="block">
              {findings.length === 0
                ? "Nie znaleziono podatności w sprawdzonych plikach, ale nie można potwierdzić ich braku w całym repozytorium."
                : "Poniższa lista może być niekompletna."}{" "}
              {scan.missingLockFiles.length === 0
                ? "Trivy nie znalazł żadnego pliku z zależnościami (NuGet/npm), więc brak podatności nie może być potwierdzony."
                : "Brakuje plików lock (npm) lub nie udało się ich wygenerować (NuGet restore zakończył się błędem), więc nie wszystkie zależności zostały sprawdzone."}
            </span>
          </span>
        </Alert>
        {scan.missingLockFiles.length > 0 && (
          <div className="space-y-2">
            <SectionHeading>Brakujące lub niewygenerowane pliki lock</SectionHeading>
            <ul className="list-inside list-disc break-all font-mono text-sm">
              {scan.missingLockFiles.map((file) => (
                <li key={file}>{file}</li>
              ))}
            </ul>
          </div>
        )}
        {findings.length > 0 && (
          <div className="space-y-2">
            <SectionHeading>Znalezione podatności ({findingsCountLabel(findings.length)})</SectionHeading>
            <VulnerabilityList findings={findings} />
          </div>
        )}
      </div>
    );
  }

  // "No results" is true only for Completed.
  if (status === "Completed") {
    return (
      <div className="space-y-2">
        <SectionHeading>Podatności ({findingsCountLabel(findings.length)})</SectionHeading>
        <VulnerabilityList
          findings={findings}
          emptyText="Brak wyników — nie znaleziono podatności w plikach lock NuGet i npm."
        />
      </div>
    );
  }

  return <Alert variant="info">Nieznany status skanu</Alert>;
}

export default function ScanDetailsPage({ loaderData }: Route.ComponentProps) {
  const { scan } = loaderData;
  const revalidator = useRevalidator();
  const inProgress = isScanInProgress(scan.status);
  const [now, setNow] = useState(() => Date.now());

  // Refresh while the scan is queued or running; stops once it finishes.
  useEffect(() => {
    if (!inProgress) return;
    const timer = window.setInterval(() => {
      setNow(Date.now());
      if (revalidator.state === "idle") revalidator.revalidate();
    }, REFRESH_INTERVAL_MS);
    return () => window.clearInterval(timer);
  }, [inProgress, revalidator]);

  const queuedTooLong = scan.status === "Queued" && now - new Date(scan.requestedAt).getTime() > QUEUED_WARNING_MS;
  const badge = scanStatusBadge(scan.status, scan.findings.length);
  const repositoryName = repositoryDisplayName({ url: scan.repositoryUrl, name: null });

  return (
    <main className="container mx-auto space-y-6 p-4">
      <div className="space-y-2">
        <Link to={`/repos/${scan.repositoryId}`} className={`text-sm ${linkClass}`}>
          ← {repositoryName}
        </Link>
        <PageHeading action={<Badge kind={badge.kind}>{badge.label}</Badge>}>
          Skan wzorca <span className="font-mono">{scan.pattern}</span>
        </PageHeading>
        <p className="text-xs text-text-muted">
          Zlecono {formatDateTime(scan.requestedAt)} przez {scan.requestedBy}
        </p>
      </div>

      {queuedTooLong && (
        <Alert variant="warning">
          Worker może nie działać — skan czeka w kolejce dłużej niż 2 minuty. Skontaktuj się z administratorem.
        </Alert>
      )}

      <Metadata scan={scan} />

      <div aria-live="polite">
        <Results scan={scan} />
      </div>
    </main>
  );
}
