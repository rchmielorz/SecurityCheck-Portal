import type { ReactNode } from "react";

import { cx } from "./styles";

export type AlertVariant = "error" | "warning" | "info" | "success";

const VARIANTS: Record<AlertVariant, { box: string; icon: ReactNode }> = {
  error: {
    box: "border-danger bg-danger-soft text-danger-fg",
    icon: (
      <>
        <circle cx="12" cy="12" r="9" />
        <path d="M15 9l-6 6M9 9l6 6" />
      </>
    ),
  },
  warning: {
    box: "border-warning bg-warning-soft text-warning-fg",
    icon: <path d="M12 3l10 18H2L12 3zM12 10v5M12 18h.01" />,
  },
  info: {
    box: "border-info bg-info-soft text-info-fg",
    icon: (
      <>
        <circle cx="12" cy="12" r="9" />
        <path d="M12 11v5M12 8h.01" />
      </>
    ),
  },
  success: {
    box: "border-success bg-success-soft text-success-fg",
    icon: (
      <>
        <circle cx="12" cy="12" r="9" />
        <path d="M8 12.5l3 3 5-6" />
      </>
    ),
  },
};

export function Alert({
  variant = "error",
  className,
  children,
}: {
  variant?: AlertVariant;
  className?: string;
  children: ReactNode;
}) {
  const { box, icon } = VARIANTS[variant];
  return (
    <div role="alert" className={cx("flex items-start gap-2 rounded-lg border px-3 py-2 text-sm", box, className)}>
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
        aria-hidden="true"
        className="mt-0.5 shrink-0"
      >
        {icon}
      </svg>
      <div className="min-w-0">{children}</div>
    </div>
  );
}
