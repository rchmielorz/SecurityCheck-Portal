import type { ComponentProps } from "react";

import { cx, focusRing } from "./styles";

export type ButtonVariant = "primary" | "secondary" | "danger";

const VARIANTS: Record<ButtonVariant, string> = {
  primary: "bg-primary px-4 py-2 font-medium text-primary-fg hover:bg-primary-hover",
  secondary: "border border-border px-3 py-1.5 text-sm font-medium text-text hover:bg-surface-hover",
  danger: "border border-danger px-3 py-1.5 text-sm font-medium text-danger-fg hover:bg-danger-soft",
};

type ButtonProps = ComponentProps<"button"> & { variant?: ButtonVariant };

// Native <button>; `type` defaults to "button"; `ref` and all other props are forwarded.
export function Button({ variant = "primary", type = "button", className, ...props }: ButtonProps) {
  return (
    <button
      type={type}
      className={cx("rounded-lg disabled:opacity-60", VARIANTS[variant], focusRing, className)}
      {...props}
    />
  );
}
