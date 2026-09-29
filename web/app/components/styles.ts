// Shared focus ring (keyboard only). Reuse on every interactive element,
// including plain links: className={`... ${focusRing}`}.
export const focusRing =
  "focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring";

// Shared text-input look (used by Input in field.tsx).
export const inputBase =
  "w-full rounded-lg border border-border bg-surface-raised px-3 py-2 text-text placeholder:text-text-subtle disabled:opacity-60 aria-invalid:border-danger";

export function cx(...parts: Array<string | false | null | undefined>): string {
  return parts.filter(Boolean).join(" ");
}
