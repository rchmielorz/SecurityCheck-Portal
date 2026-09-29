import type { ComponentProps } from "react";

import { cx } from "./styles";

export function Card({ className, ...props }: ComponentProps<"section">) {
  return (
    <section
      className={cx("rounded-2xl border border-border-subtle bg-surface-raised p-6 shadow-sm", className)}
      {...props}
    />
  );
}
