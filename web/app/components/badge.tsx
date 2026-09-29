import type { ReactNode } from "react";

import { cx } from "./styles";

export type SeverityKind = "critical" | "high" | "medium" | "low";
export type StatusKind = "success" | "warning" | "danger" | "info" | "neutral" | "new" | "accepted-risk";
export type BadgeKind = SeverityKind | StatusKind;

export const BADGE_KINDS: BadgeKind[] = [
  "critical",
  "high",
  "medium",
  "low",
  "success",
  "warning",
  "danger",
  "info",
  "neutral",
  "new",
  "accepted-risk",
];

const CIRCLE = <circle cx="12" cy="12" r="9" />;

// Static map: Tailwind cannot see dynamically built class names. Each kind has
// its own icon shape, so color is never the only cue.
const STYLES: Record<BadgeKind, { classes: string; label: string; icon: ReactNode }> = {
  critical: {
    classes: "bg-critical-soft text-critical-fg",
    label: "Krytyczna",
    icon: <path d="M8 2h8l6 6v8l-6 6H8l-6-6V8l6-6zM12 8v5M12 16.5h.01" />,
  },
  high: {
    classes: "bg-high-soft text-high-fg",
    label: "Wysoka",
    icon: <path d="M12 3l10 18H2L12 3zM12 10v5M12 18h.01" />,
  },
  medium: {
    classes: "bg-medium-soft text-medium-fg",
    label: "Średnia",
    icon: <path d="M12 2l10 10-10 10L2 12 12 2zM12 8v5M12 16h.01" />,
  },
  low: {
    classes: "bg-low-soft text-low-fg",
    label: "Niska",
    icon: (
      <>
        {CIRCLE}
        <path d="M8 11l4 4 4-4" />
      </>
    ),
  },
  success: {
    classes: "bg-success-soft text-success-fg",
    label: "Sukces",
    icon: (
      <>
        {CIRCLE}
        <path d="M8 12.5l3 3 5-6" />
      </>
    ),
  },
  warning: {
    classes: "bg-warning-soft text-warning-fg",
    label: "Ostrzeżenie",
    icon: <path d="M12 3l10 18H2L12 3zM12 10v5M12 18h.01" />,
  },
  danger: {
    classes: "bg-danger-soft text-danger-fg",
    label: "Błąd",
    icon: (
      <>
        {CIRCLE}
        <path d="M15 9l-6 6M9 9l6 6" />
      </>
    ),
  },
  info: {
    classes: "bg-info-soft text-info-fg",
    label: "Informacja",
    icon: (
      <>
        {CIRCLE}
        <path d="M12 11v5M12 8h.01" />
      </>
    ),
  },
  neutral: {
    classes: "bg-neutral-soft text-neutral-fg",
    label: "Neutralny",
    icon: (
      <>
        {CIRCLE}
        <path d="M8 12h8" />
      </>
    ),
  },
  new: {
    classes: "bg-new-soft text-new-fg",
    label: "Nowa",
    icon: <path d="M12 2l2.9 6.6 7.1.7-5.3 4.7 1.6 7L12 17.3 5.7 21l1.6-7L2 9.3l7.1-.7L12 2z" />,
  },
  "accepted-risk": {
    classes: "bg-accepted-risk-soft text-accepted-risk-fg",
    label: "Zaakceptowane ryzyko",
    icon: <path d="M12 2l8 3v6c0 5-3.5 9-8 11-4.5-2-8-6-8-11V5l8-3zM8.5 12l2.5 2.5 4.5-5" />,
  },
};

// Always text + icon. Default text is the Polish label for the kind.
export function Badge({
  kind,
  children,
  className,
}: {
  kind: BadgeKind;
  children?: ReactNode;
  className?: string;
}) {
  const { classes, label, icon } = STYLES[kind];
  return (
    <span
      className={cx("inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium", classes, className)}
    >
      <svg
        width="12"
        height="12"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2.5"
        strokeLinecap="round"
        strokeLinejoin="round"
        aria-hidden="true"
        className="shrink-0"
      >
        {icon}
      </svg>
      {children ?? label}
    </span>
  );
}
