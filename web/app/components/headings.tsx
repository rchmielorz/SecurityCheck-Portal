import type { ReactNode } from "react";

// Page title (h1) with an optional action on the right (e.g. a Button).
export function PageHeading({ children, action }: { children: ReactNode; action?: ReactNode }) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-2">
      <h1 className="text-2xl font-semibold">{children}</h1>
      {action}
    </div>
  );
}

// Section title (h2) inside a page.
export function SectionHeading({ children, id }: { children: ReactNode; id?: string }) {
  return (
    <h2 id={id} className="text-lg font-semibold">
      {children}
    </h2>
  );
}
